param(
    [string]$OutputDirectory = 'C:\Deploy'
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot '..\ColdCall.McpServer.csproj'
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$zipPath = Join-Path $outputPath 'ColdCall.McpServer-Debug.zip'
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workPath = [System.IO.Path]::GetFullPath((Join-Path $tempRoot ('ColdCall.McpServer-Debug-' + [guid]::NewGuid().ToString('N'))))
$tempPrefix = $tempRoot + [System.IO.Path]::DirectorySeparatorChar
if (-not $workPath.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary deployment directory is outside the expected location.'
}
$publishPath = Join-Path $workPath 'publish'
$artifactsPath = Join-Path $workPath 'artifacts'
$pendingZipPath = Join-Path $outputPath ('.ColdCall.McpServer-Debug-' + [guid]::NewGuid().ToString('N') + '.zip')

try {
    New-Item -ItemType Directory -Path $workPath -Force | Out-Null
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

    dotnet publish $projectPath --configuration Debug --output $publishPath --artifacts-path $artifactsPath --self-contained false
    if ($LASTEXITCODE -ne 0) {
        throw "ColdCall.McpServer Debug publish failed (exit code $LASTEXITCODE)."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $publishPath,
        $pendingZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    Move-Item -LiteralPath $pendingZipPath -Destination $zipPath -Force
    Write-Host "Deployment created: $zipPath"
    Get-Item -LiteralPath $zipPath
}
finally {
    if (Test-Path -LiteralPath $pendingZipPath -PathType Leaf) {
        Remove-Item -LiteralPath $pendingZipPath -Force
    }
    if ($workPath.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $workPath -PathType Container)) {
        Remove-Item -LiteralPath $workPath -Recurse -Force
    }
}

