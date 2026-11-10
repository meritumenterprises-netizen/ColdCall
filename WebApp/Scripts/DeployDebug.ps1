cd C:\projects\ColdCall

$ErrorActionPreference = 'Stop'

dotnet publish C:\projects\ColdCall\WebApp\ColdCall.csproj -c Debug -o C:\Deploy\ColdCall-Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Invoke-Item -LiteralPath 'C:\Deploy\ColdCall-Debug'

$zipPath = 'C:\Deploy\ColdCall-Debug.zip'
if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    'C:\Deploy\ColdCall-Debug',
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

& explorer.exe "/select,`"$zipPath`""

cd 'C:\projects\ColdCall\WebApp\Scripts'