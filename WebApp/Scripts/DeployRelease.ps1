cd C:\projects\ColdCall

$ErrorActionPreference = 'Stop'

dotnet publish C:\projects\ColdCall\WebApp\ColdCall.csproj -c Release -o C:\Deploy\ColdCall-Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Invoke-Item -LiteralPath 'C:\Deploy\ColdCall-Release'

$zipPath = 'C:\Deploy\ColdCall-Release.zip'
if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    'C:\Deploy\ColdCall-Release',
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

& explorer.exe "/select,`"$zipPath`""

cd 'C:\projects\ColdCall\WebApp\Scripts'