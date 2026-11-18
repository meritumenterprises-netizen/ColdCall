#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Issuer = 'https://localhost:44345/',
    [ValidateRange(1024, 65535)]
    [int]$Port = 5086,
    [switch]$SkipDatabaseInitialization
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$address = $null
if (-not [Uri]::TryCreate($Issuer, [UriKind]::Absolute, [ref]$address) -or
    -not $address.IsLoopback -or $address.Scheme -ne 'https' -or $address.AbsolutePath -ne '/' -or
    $address.UserInfo -or $address.Query -or $address.Fragment) {
    throw 'Issuer must be a local HTTPS origin, e.g. https://localhost:44345/ (IIS Express) or https://localhost:5001/ (Kestrel).'
}
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\WebApp\ColdCall.csproj'))
$endpoint = "http://127.0.0.1:$Port/mcp"
# Both projects intentionally share this development User Secrets ID.
$values = [ordered]@{
    'Mcp:OAuth:Enabled' = 'true'
    'Mcp:OAuth:EnableDynamicRegistration' = 'true'
    'Mcp:OAuth:Issuer' = $address.AbsoluteUri
    'Mcp:OAuth:Resource' = $endpoint
    'Mcp:Endpoint' = $endpoint
}
foreach ($key in $values.Keys) {
    & dotnet user-secrets set $key $values[$key] --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not set $key." }
}
if (-not $SkipDatabaseInitialization) {
    Write-Host 'Initializing only the McpOAuth schema in the configured ConnectionStrings:Firmy database.'
    $oldAspNet = $env:ASPNETCORE_ENVIRONMENT
    $oldDotNet = $env:DOTNET_ENVIRONMENT
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:DOTNET_ENVIRONMENT = 'Development'
        $artifacts = Join-Path ([IO.Path]::GetTempPath()) ('ColdCall-OAuth-' + [guid]::NewGuid().ToString('N'))
        & dotnet run --project $project --no-launch-profile --artifacts-path $artifacts -- --initialize-mcp-oauth
        if ($LASTEXITCODE -ne 0) { throw 'OAuth database initialization failed. Check the configured SQL connection and schema permissions.' }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $oldAspNet
        $env:DOTNET_ENVIRONMENT = $oldDotNet
    }
}
Write-Host "OAuth issuer: $($address.AbsoluteUri)"
Write-Host "Local MCP: $endpoint"
Write-Host 'Restart both applications. Start MCP with the port below; start ColdCall with the matching IIS Express/Kestrel profile.'
Write-Host '$env:ASPNETCORE_ENVIRONMENT = ''Development'''
Write-Host "dotnet run --project ColdCall.McpServer --no-launch-profile -- --urls http://127.0.0.1:$Port"
Write-Host 'Development signing/encryption certificates are not production certificates.'
