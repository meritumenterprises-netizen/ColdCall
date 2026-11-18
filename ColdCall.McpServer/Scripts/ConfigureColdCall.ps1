#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$SiteName = 'ColdCall',
    [string]$McpAppPoolName = 'ColdCall.McpServer',
    [string]$McpEndpoint = 'http://127.0.0.1:5086/mcp',
    [Security.SecureString]$SigningKey,
    [Security.SecureString]$OllamaApiKey,
    [string]$OllamaModel,
    [switch]$ConfigureMcpDatabase,
    [Security.SecureString]$ConnectionString,
    [switch]$SkipRecycle
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertFrom-ProtectedValue([Security.SecureString]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Set-PoolVariable($Pool, [string]$Name, [string]$Value) {
    $variables = $Pool.GetCollection('environmentVariables')
    $existing = @($variables | Where-Object { $_['name'] -eq $Name })
    if ($existing.Count -gt 0) { $existing[0]['value'] = $Value }
    else {
        $entry = $variables.CreateElement('add')
        $entry['name'] = $Name
        $entry['value'] = $Value
        $variables.Add($entry)
    }
}

if ($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::Is64BitProcess) {
    throw 'Run with 64-bit Windows PowerShell 5.1 (powershell.exe) as Administrator.'
}
$endpoint = $null
if (-not [Uri]::TryCreate($McpEndpoint, [UriKind]::Absolute, [ref]$endpoint) -or
    ($endpoint.Scheme -ne 'https' -and -not ($endpoint.Scheme -eq 'http' -and $endpoint.IsLoopback)) -or
    $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment) {
    throw 'McpEndpoint must be HTTPS or loopback HTTP, without credentials, query or fragment.'
}
$iisDirectory = Join-Path $env:windir 'System32\inetsrv'
$assemblyPath = Join-Path $iisDirectory 'Microsoft.Web.Administration.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) { throw 'IIS is not installed.' }
Add-Type -Path $assemblyPath
$manager = New-Object Microsoft.Web.Administration.ServerManager
$plainSigningKey = $null
$plainOllamaKey = $null
$plainConnection = $null
$configureDatabase = $ConfigureMcpDatabase -or $PSBoundParameters.ContainsKey('ConnectionString')
$backupName = $null

try {
    $site = $manager.Sites[$SiteName]
    if (-not $site) { throw "IIS site '$SiteName' does not exist. Specify -SiteName." }
    $poolName = $site.Applications['/'].ApplicationPoolName
    if ($poolName -eq $McpAppPoolName) { throw 'ColdCall and MCP must use separate application pools.' }
    $pool = $manager.ApplicationPools[$poolName]
    if (-not $pool) { throw 'The ColdCall application pool does not exist.' }
    foreach ($otherSite in $manager.Sites) {
        foreach ($application in $otherSite.Applications) {
            if ($application.ApplicationPoolName -eq $poolName -and
                ($otherSite.Name -ne $SiteName -or $application.Path -ne '/')) {
                throw 'The ColdCall pool is shared with another application. Assign a dedicated pool first.'
            }
        }
    }

    # Reuse the installed MCP key without displaying it or requiring it in command history.
    $mcpPool = $manager.ApplicationPools[$McpAppPoolName]
    if ($configureDatabase -and -not $mcpPool) {
        throw 'The MCP application pool does not exist. Install MCP first or specify -McpAppPoolName.'
    }
    if ($configureDatabase) {
        $mcpApplications = @(
            foreach ($configuredSite in $manager.Sites) {
                foreach ($application in $configuredSite.Applications) {
                    if ($application.ApplicationPoolName -eq $McpAppPoolName) { $application }
                }
            }
        )
        if ($mcpApplications.Count -ne 1) { throw 'The MCP pool must belong to exactly one dedicated application.' }
        if (-not $ConnectionString) {
            $ConnectionString = Read-Host 'MCP SQL connection string (use single backslashes, NOT JSON escaping)' -AsSecureString
        }
        $plainConnection = ConvertFrom-ProtectedValue $ConnectionString
        if ([string]::IsNullOrWhiteSpace($plainConnection)) { throw 'The SQL connection string cannot be empty.' }
        Add-Type -AssemblyName System.Data
        $connectionBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
        try { $connectionBuilder.set_ConnectionString($plainConnection) }
        catch { throw 'Invalid SQL connection string syntax. Enter the connection string value, not a JSON property.' }
        if ([string]::IsNullOrWhiteSpace($connectionBuilder.get_DataSource())) { throw 'SQL Server/Data Source is required.' }
        if ($connectionBuilder.get_DataSource().Contains('\\')) {
            throw 'SQL Server name contains a double backslash. Use a single backslash (not JSON escaping).'
        }
        $connectionBuilder.Clear()
    }
    $mcpKey = $null
    if ($mcpPool) {
        $variable = @($mcpPool.GetCollection('environmentVariables') | Where-Object { $_['name'] -eq 'Mcp__SigningKey' })
        if ($variable.Count -gt 0) { $mcpKey = [string]$variable[0]['value'] }
    }
    if ($SigningKey) { $plainSigningKey = ConvertFrom-ProtectedValue $SigningKey }
    elseif (-not [string]::IsNullOrWhiteSpace($mcpKey)) { $plainSigningKey = $mcpKey }
    else {
        $SigningKey = Read-Host 'Mcp:SigningKey (the SAME value as in MCP)' -AsSecureString
        $plainSigningKey = ConvertFrom-ProtectedValue $SigningKey
    }
    if ([string]::IsNullOrWhiteSpace($plainSigningKey) -or [Text.Encoding]::UTF8.GetByteCount($plainSigningKey) -lt 32) {
        throw 'Mcp:SigningKey must contain at least 32 UTF-8 bytes.'
    }
    if (-not [string]::IsNullOrWhiteSpace($mcpKey) -and $plainSigningKey -cne $mcpKey) {
        throw 'The supplied signing key does not match the MCP application pool signing key.'
    }
    if (-not $OllamaApiKey) { $OllamaApiKey = Read-Host 'Ollama API key' -AsSecureString }
    $plainOllamaKey = ConvertFrom-ProtectedValue $OllamaApiKey
    if ([string]::IsNullOrWhiteSpace($plainOllamaKey)) { throw 'Ollama API key cannot be empty.' }
    if ([string]::IsNullOrWhiteSpace($OllamaModel)) { $OllamaModel = Read-Host 'Ollama Cloud model (exact model name)' }
    if ([string]::IsNullOrWhiteSpace($OllamaModel)) { throw 'Ollama model cannot be empty.' }

    $backupName = 'ColdCall-AI-Config-' + [guid]::NewGuid().ToString('N')
    & (Join-Path $iisDirectory 'appcmd.exe') add backup $backupName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not back up IIS configuration.' }
    Set-PoolVariable $pool 'Mcp__Endpoint' $endpoint.AbsoluteUri
    Set-PoolVariable $pool 'Mcp__SigningKey' $plainSigningKey
    Set-PoolVariable $pool 'Ollama__ApiKey' $plainOllamaKey
    Set-PoolVariable $pool 'Ollama__Model' $OllamaModel.Trim()
    if ($configureDatabase) { Set-PoolVariable $mcpPool 'ConnectionStrings__Firmy' $plainConnection }
    $manager.CommitChanges()
    if (-not $SkipRecycle) {
        if ($pool.State.ToString() -eq 'Started') { $pool.Recycle() | Out-Null }
        else { $pool.Start() | Out-Null }
        if ($configureDatabase) {
            if ($mcpPool.State.ToString() -eq 'Started') { $mcpPool.Recycle() | Out-Null }
            else { $mcpPool.Start() | Out-Null }
        }
    }
    Write-Host "Configured IIS pool '$poolName' for site '$SiteName'."
    Write-Host "Mcp__Endpoint: $($endpoint.AbsoluteUri)"
    Write-Host "Ollama__Model: $($OllamaModel.Trim())"
    Write-Host 'Mcp__SigningKey and Ollama__ApiKey: configured (values hidden).'
    if ($configureDatabase) { Write-Host "ConnectionStrings__Firmy: configured in MCP pool '$McpAppPoolName' (value hidden)." }
    Write-Host "IIS configuration backup: $backupName"
    if ($SkipRecycle) { Write-Warning 'Recycle ColdCall and, if its database configuration changed, MCP to apply the configuration.' }
}
catch {
    if ($backupName) { Write-Warning "IIS backup: $backupName. Configuration may have been applied; check pool state." }
    throw
}
finally {
    $manager.Dispose()
    $plainSigningKey = $null
    $plainOllamaKey = $null
    $plainConnection = $null
    $mcpKey = $null
}
