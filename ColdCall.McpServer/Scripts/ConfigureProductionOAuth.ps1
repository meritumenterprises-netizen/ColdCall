#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$SiteName = 'ColdCall',
    [string]$McpSiteName = 'ColdCall.McpServer',
    [uri]$Issuer = 'https://www.findmyclient.pl/',
    [uri]$Resource = 'https://mcp.findmyclient.pl/mcp',
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::Is64BitProcess) {
    throw 'Run in 64-bit Windows PowerShell 5.1 as Administrator.'
}
foreach ($address in @($Issuer, $Resource)) {
    if (-not $address.IsAbsoluteUri -or $address.Scheme -ne 'https' -or
        $address.UserInfo -or $address.Query -or $address.Fragment) {
        throw 'Issuer and Resource must be absolute HTTPS URLs without credentials, query or fragment.'
    }
}
if ($Issuer.AbsolutePath -ne '/' -or $Resource.AbsolutePath -ne '/mcp') {
    throw 'Issuer must be the ColdCall origin; Resource must end with /mcp.'
}

function Set-PoolVariable($Pool, [string]$Name, [string]$Value) {
    $variables = $Pool.GetCollection('environmentVariables')
    $entries = @($variables | Where-Object { $_['name'] -eq $Name })
    if ($entries.Count) { $entries[0]['value'] = $Value }
    else {
        $entry = $variables.CreateElement('add')
        $entry['name'] = $Name
        $entry['value'] = $Value
        $variables.Add($entry)
    }
}

$iis = Join-Path $env:windir 'System32\inetsrv'
Add-Type -Path (Join-Path $iis 'Microsoft.Web.Administration.dll')
$manager = New-Object Microsoft.Web.Administration.ServerManager
$originalEnvironment = @{}
$backup = $null
$committed = $false
try {
    $site = $manager.Sites[$SiteName]
    $mcpSite = $manager.Sites[$McpSiteName]
    if (-not $site -or -not $mcpSite) { throw 'Both IIS sites must already be installed.' }
    $pool = $manager.ApplicationPools[$site.Applications['/'].ApplicationPoolName]
    $mcpPool = $manager.ApplicationPools[$mcpSite.Applications['/'].ApplicationPoolName]
    if (-not $pool -or -not $mcpPool -or $pool.Name -eq $mcpPool.Name) {
        throw 'ColdCall and MCP require separate application pools.'
    }
    foreach ($target in @($pool, $mcpPool)) {
        $applications = @(
            foreach ($installedSite in $manager.Sites) {
                foreach ($app in $installedSite.Applications) {
                    if ($app.ApplicationPoolName -eq $target.Name) { $app }
                }
            }
        )
        if ($applications.Count -ne 1) { throw "Pool '$($target.Name)' must be dedicated to one application." }
    }
    $root = [Environment]::ExpandEnvironmentVariables($site.Applications['/'].VirtualDirectories['/'].PhysicalPath)
    $dll = Join-Path $root 'ColdCall.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'Published ColdCall.dll not found in the IIS site directory.' }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'OpenIddict.Server.dll'))) {
        throw 'Deploy the updated ColdCall application with OAuth support first.'
    }
    $certificate = Get-Item -LiteralPath ('Cert:\LocalMachine\My\' + $CertificateThumbprint)
    if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
        throw 'OAuth certificate must be currently valid and have a private key.'
    }
    if ($pool.ProcessModel.IdentityType.ToString() -ne 'ApplicationPoolIdentity') {
        throw 'This script expects ColdCall to use ApplicationPoolIdentity. Grant the custom identity key access manually first.'
    }
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    try {
        if ($rsa -is [Security.Cryptography.RSACng]) {
            $keyPath = Join-Path "$env:ProgramData\Microsoft\Crypto\Keys" $rsa.Key.UniqueName
        }
        elseif ($rsa -is [Security.Cryptography.RSACryptoServiceProvider]) {
            $keyPath = Join-Path "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" $rsa.CspKeyContainerInfo.UniqueKeyContainerName
        }
        else { throw 'OAuth requires a persisted RSA certificate key.' }
        $acl = Get-Acl -LiteralPath $keyPath
        $rule = New-Object Security.AccessControl.FileSystemAccessRule(
            ('IIS AppPool\' + $pool.Name), 'Read', 'Allow')
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $keyPath -AclObject $acl
    }
    finally { if ($rsa) { $rsa.Dispose() } }

    $settings = @{
        Mcp__OAuth__Enabled = 'true'
        Mcp__OAuth__Issuer = $Issuer.AbsoluteUri
        Mcp__OAuth__Resource = $Resource.AbsoluteUri
    }
    # web.config overrides pool variables; reject conflicting settings instead of silently enabling a different issuer.
    $webEnvironment = @{}
    foreach ($installedSite in @($site, $mcpSite)) {
        $path = [Environment]::ExpandEnvironmentVariables($installedSite.Applications['/'].VirtualDirectories['/'].PhysicalPath)
        $config = Join-Path $path 'web.config'
        if (Test-Path -LiteralPath $config) {
            $xml = New-Object Xml.XmlDocument
            $xml.Load($config)
            foreach ($entry in $xml.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) {
                $name = $entry.GetAttribute('name')
                $value = $entry.GetAttribute('value')
                $expected = $null
                if ($settings.ContainsKey($name)) { $expected = $settings[$name] }
                if ($installedSite.Name -eq $SiteName) {
                    $webEnvironment[$name] = $value
                    if ($name -eq 'Mcp__OAuth__CertificateThumbprint') { $expected = $CertificateThumbprint }
                    if ($name -eq 'Mcp__OAuth__EnableDynamicRegistration') { $expected = 'true' }
                }
                if ($null -ne $expected -and $value -cne $expected) {
                    throw "Conflicting '$name' in $config. Remove that override and run again."
                }
                if ($name -in @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT') -and $value -ne 'Production') {
                    throw "Non-production environment in $config. Correct it before continuing."
                }
            }
        }
    }

    # The maintenance process does not inherit IIS pool variables automatically.
    $environment = @{}
    foreach ($entry in $pool.GetCollection('environmentVariables')) { $environment[[string]$entry['name']] = [string]$entry['value'] }
    foreach ($name in $webEnvironment.Keys) { $environment[$name] = $webEnvironment[$name] }
    foreach ($name in $settings.Keys) { $environment[$name] = $settings[$name] }
    $environment['Mcp__OAuth__CertificateThumbprint'] = $CertificateThumbprint
    $environment['Mcp__OAuth__EnableDynamicRegistration'] = 'true'
    $environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $environment['DOTNET_ENVIRONMENT'] = 'Production'
    foreach ($name in $environment.Keys) {
        $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
    }
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    Push-Location -LiteralPath $root
    try {
        & $dotnet $dll --initialize-mcp-oauth
        if ($LASTEXITCODE -ne 0) { throw 'OAuth database initialization failed. IIS OAuth settings were not changed.' }
    }
    finally { Pop-Location }

    $backup = 'ColdCall-OAuth-' + [guid]::NewGuid().ToString('N')
    & (Join-Path $iis 'appcmd.exe') add backup $backup | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not back up IIS configuration.' }
    foreach ($target in @($pool, $mcpPool)) {
        foreach ($name in $settings.Keys) { Set-PoolVariable $target $name $settings[$name] }
        Set-PoolVariable $target 'ASPNETCORE_ENVIRONMENT' 'Production'
        Set-PoolVariable $target 'DOTNET_ENVIRONMENT' 'Production'
    }
    Set-PoolVariable $pool 'Mcp__OAuth__CertificateThumbprint' $CertificateThumbprint
    Set-PoolVariable $pool 'Mcp__OAuth__EnableDynamicRegistration' 'true'
    $manager.CommitChanges()
    $committed = $true
    foreach ($target in @($pool, $mcpPool)) {
        if ($target.State.ToString() -eq 'Started') { $target.Recycle() | Out-Null }
        else { $target.Start() | Out-Null }
    }
    Write-Host "OAuth configured. Issuer: $($Issuer.AbsoluteUri)"
    Write-Host "Resource: $($Resource.AbsoluteUri)"
    Write-Host "IIS backup: $backup"
    Write-Host 'Existing SQL credentials, internal MCP signing key, endpoint and Ollama settings were preserved.'
}
catch {
    if ($committed) { Write-Warning "IIS configuration was applied. Backup: $backup. Check application pool state." }
    throw
}
finally {
    foreach ($name in $originalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process')
    }
    $manager.Dispose()
    $environment = $null
    $webEnvironment = $null
}
