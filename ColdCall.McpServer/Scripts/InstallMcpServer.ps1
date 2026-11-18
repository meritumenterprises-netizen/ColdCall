#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [string]$InstallDirectory = 'C:\Sites\ColdCall.McpServer',
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$SiteName = 'ColdCall.McpServer',
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$AppPoolName = 'ColdCall.McpServer',
    [ValidateRange(1024, 65535)]
    [int]$Port = 5086,
    [switch]$PublicHttps,
    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$HostName = 'mcp.findmyclient.pl',
    [string]$CertificateThumbprint,
    [Security.SecureString]$ConnectionString,
    [Security.SecureString]$SigningKey
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertFrom-ProtectedValue([Security.SecureString]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Resolve-PoolSecret($Pool, [string]$Name, [Security.SecureString]$ProvidedValue, [string]$Prompt) {
    if ($ProvidedValue) { return ConvertFrom-ProtectedValue $ProvidedValue }
    if ($Pool) {
        $existing = @($Pool.GetCollection('environmentVariables') | Where-Object { $_['name'] -eq $Name })
        if ($existing.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$existing[0]['value'])) {
            return [string]$existing[0]['value']
        }
    }
    $secret = Read-Host $Prompt -AsSecureString
    return ConvertFrom-ProtectedValue $secret
}

function Set-PoolVariable($Pool, [string]$Name, [string]$Value) {
    $variables = $Pool.GetCollection('environmentVariables')
    $entry = @($variables | Where-Object { $_['name'] -eq $Name })
    if ($entry.Count -eq 0) {
        $variable = $variables.CreateElement('add')
        $variable['name'] = $Name
        $variable['value'] = $Value
        $variables.Add($variable)
    }
    else { $entry[0]['value'] = $Value }
}

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this installer with 64-bit Windows PowerShell (powershell.exe), not pwsh.exe.'
}
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }

$archivePath = (Resolve-Path -LiteralPath $ZipPath).ProviderPath
if ([IO.Path]::GetExtension($archivePath) -ne '.zip') { throw 'ZipPath must point to a deployment ZIP.' }
$root = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\', '/')
if ($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\', '/')) {
    throw 'InstallDirectory cannot be a drive root.'
}
$iisDirectory = Join-Path $env:windir 'System32\inetsrv'
$administrationDll = Join-Path $iisDirectory 'Microsoft.Web.Administration.dll'
$appcmd = Join-Path $iisDirectory 'appcmd.exe'
if (-not (Test-Path -LiteralPath $administrationDll)) { throw 'Install IIS before running this script.' }
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Install the .NET 10 ASP.NET Core Hosting Bundle first.' }
$runtimes = & $dotnet --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.AspNetCore.App 10\.') -or
    -not ($runtimes -match '^Microsoft.NETCore.App 10\.')) {
    throw 'Install the .NET 10 ASP.NET Core Hosting Bundle first.'
}

Add-Type -Path $administrationDll
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manager = New-Object Microsoft.Web.Administration.ServerManager
$archive = $null
$plainConnection = $null
$plainKey = $null
$backupName = $null
$releasePath = Join-Path $root ('releases\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))

try {
    $globalModules = $manager.GetApplicationHostConfiguration().GetSection('system.webServer/globalModules').GetCollection()
    if (-not @($globalModules | Where-Object { $_['name'] -eq 'AspNetCoreModuleV2' }).Count) {
        throw 'ASP.NET Core IIS module is missing. Install/repair the .NET 10 Hosting Bundle after installing IIS.'
    }
    $binding = '127.0.0.1:' + $Port + ':'
    $httpsBinding = '*:443:' + $HostName
    $certificate = $null
    if ($PublicHttps) {
        if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
            throw 'Public HTTPS requires -CertificateThumbprint for a certificate in Cert:\LocalMachine\My.'
        }
        $thumbprint = $CertificateThumbprint.Replace(' ', '')
        if ($thumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Invalid certificate thumbprint.' }
        $certificate = Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $thumbprint)
        if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
            throw 'The HTTPS certificate must be valid and have a private key.'
        }
        $matchingNames = @($certificate.DnsNameList | ForEach-Object { $_.Unicode })
        $wildcardName = '*.' + ($HostName -replace '^[^.]+\.', '')
        if ($matchingNames -notcontains $HostName -and $matchingNames -notcontains $wildcardName) {
            throw 'The HTTPS certificate does not cover HostName.'
        }
    }
    $site = $manager.Sites[$SiteName]
    $pool = $manager.ApplicationPools[$AppPoolName]
    foreach ($otherSite in $manager.Sites) {
        foreach ($application in $otherSite.Applications) {
            if ($application.ApplicationPoolName -eq $AppPoolName -and
                ($otherSite.Name -ne $SiteName -or $application.Path -ne '/')) {
                throw 'The selected application pool is shared. Choose a dedicated AppPoolName.'
            }
        }
        foreach ($otherBinding in $otherSite.Bindings) {
            if ($PublicHttps -and $otherSite.Name -ne $SiteName -and
                $otherBinding.Protocol -eq 'https' -and $otherBinding.Host -eq $HostName) {
                throw 'The HTTPS hostname is already assigned to another IIS site.'
            }
            if ($otherSite.Name -ne $SiteName -and $otherBinding.EndPoint -and $otherBinding.EndPoint.Port -eq $Port) {
                throw "Port $Port is already assigned to another IIS site. Choose another Port."
            }
        }
    }
    if ($site) {
        $existingPath = [IO.Path]::GetFullPath($site.Applications['/'].VirtualDirectories['/'].PhysicalPath)
        if (-not $existingPath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The existing site is outside InstallDirectory. Refusing to replace another installation.'
        }
        $unexpectedBindings = @($site.Bindings | Where-Object {
            -not (($_.Protocol -eq 'http' -and $_.BindingInformation -eq $binding) -or
                ($_.Protocol -eq 'https' -and $_.BindingInformation -eq $httpsBinding))
        })
        if ($unexpectedBindings.Count -gt 0 -or $site.Applications.Count -ne 1) {
            throw 'The existing site contains unexpected bindings or applications. Refusing to replace it.'
        }
    }
    elseif ($pool) { throw 'The application pool already exists without this site. Choose another AppPoolName.' }

    $plainConnection = Resolve-PoolSecret $pool 'ConnectionStrings__Firmy' $ConnectionString 'SQL connection string (ConnectionStrings:Firmy)'
    $plainKey = Resolve-PoolSecret $pool 'Mcp__SigningKey' $SigningKey 'Mcp:SigningKey (the SAME value as in ColdCall)'
    if ([string]::IsNullOrWhiteSpace($plainConnection)) { throw 'The SQL connection string cannot be empty.' }
    if ([string]::IsNullOrWhiteSpace($plainKey) -or [Text.Encoding]::UTF8.GetByteCount($plainKey) -lt 32) {
        throw 'Mcp:SigningKey must contain at least 32 UTF-8 bytes.'
    }

    # Validate every entry before extraction, including ZIP directory traversal attempts.
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    foreach ($required in @('ColdCall.McpServer.dll', 'ColdCall.McpServer.runtimeconfig.json', 'web.config')) {
        if (-not $archive.GetEntry($required)) { throw "Deployment ZIP is missing $required at its root." }
    }
    foreach ($entry in $archive.Entries) {
        $destination = [IO.Path]::GetFullPath((Join-Path $releasePath $entry.FullName))
        if (-not $destination.StartsWith($releasePath + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $entry.FullName.Contains(':')) { throw 'Deployment ZIP contains an unsafe path.' }
    }
    $archive.Dispose()
    $archive = $null
    New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $releasePath)
    [xml]$webConfig = Get-Content -LiteralPath (Join-Path $releasePath 'web.config') -Raw
    $aspNetCore = $webConfig.SelectSingleNode('//aspNetCore')
    if (-not $aspNetCore -or [string]$aspNetCore.arguments -notmatch 'ColdCall\.McpServer\.dll') {
        throw 'The deployment web.config does not launch ColdCall.McpServer.dll.'
    }

    $backupName = 'ColdCall-McpServer-' + [guid]::NewGuid().ToString('N')
    & $appcmd add backup $backupName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not back up IIS configuration.' }
    if (-not $pool) { $pool = $manager.ApplicationPools.Add($AppPoolName) }
    $pool.ManagedRuntimeVersion = ''
    $pool.ManagedPipelineMode = 'Integrated'
    $pool.Enable32BitAppOnWin64 = $false
    $pool.ProcessModel.IdentityType = 'ApplicationPoolIdentity'
    $pool.ProcessModel.IdleTimeout = [TimeSpan]::Zero
    $pool.StartMode = 'AlwaysRunning'
    Set-PoolVariable $pool 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-PoolVariable $pool 'DOTNET_ENVIRONMENT' 'Production'
    Set-PoolVariable $pool 'ConnectionStrings__Firmy' $plainConnection
    Set-PoolVariable $pool 'Mcp__SigningKey' $plainKey
    Set-PoolVariable $pool 'Mcp__ListenUrl' ("http://127.0.0.1:$Port")
    if (-not $site) { $site = $manager.Sites.Add($SiteName, 'http', $binding, $releasePath) }
    else { $site.Applications['/'].VirtualDirectories['/'].PhysicalPath = $releasePath }
    if ($PublicHttps) {
        $https = @($site.Bindings | Where-Object { $_.Protocol -eq 'https' -and $_.BindingInformation -eq $httpsBinding })
        if ($https.Count -eq 0) {
            $httpsBindingEntry = $site.Bindings.Add($httpsBinding, $certificate.GetCertHash(), 'My')
        }
        else { $httpsBindingEntry = $https[0] }
        $httpsBindingEntry.CertificateHash = $certificate.GetCertHash()
        $httpsBindingEntry.CertificateStoreName = 'My'
        $httpsBindingEntry.SslFlags = [Microsoft.Web.Administration.SslFlags]::Sni
    }
    $site.Applications['/'].ApplicationPoolName = $AppPoolName
    $site.ServerAutoStart = $true
    $manager.CommitChanges()

    & icacls.exe $root /grant ("IIS AppPool\${AppPoolName}:(OI)(CI)(RX)") /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the application pool read/execute access to the installation.' }
    if ($pool.State.ToString() -eq 'Started') { $pool.Recycle() | Out-Null }
    else { $pool.Start() | Out-Null }
    if ($site.State.ToString() -ne 'Started') { $site.Start() | Out-Null }

    $healthy = $false
    for ($attempt = 0; $attempt -lt 12; $attempt++) {
        try {
            $response = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 5
            if ($response.status -eq 'ok') { $healthy = $true; break }
        }
        catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $healthy) { throw 'MCP health check failed. Check IIS and Windows Application event logs.' }

    Write-Host "Installed: $releasePath"
    Write-Host "MCP endpoint: http://127.0.0.1:$Port/mcp (loopback only)"
    if ($PublicHttps) {
        Write-Host "Public MCP endpoint: https://$HostName/mcp"
        Write-Host 'DNS must point to this VPS and inbound TCP 443 must be allowed. External reachability was not verified.'
        Write-Warning 'This deployment still requires authenticated MCP tokens. Public OAuth client support must be configured separately.'
    }
    Write-Host "IIS configuration backup: $backupName"
    Write-Host 'In the MAIN ColdCall app, set Mcp__Endpoint to this endpoint and Mcp__SigningKey to the SAME key, then recycle its pool.'
    Write-Host 'The health check verifies startup only, not SQL connectivity or authenticated searches.'
}
catch {
    if ($backupName) {
        Write-Warning "Installation did not complete. IIS configuration backup: $backupName. Previous release folders were retained."
    }
    throw
}
finally {
    if ($archive) { $archive.Dispose() }
    $manager.Dispose()
    $plainConnection = $null
    $plainKey = $null
}
