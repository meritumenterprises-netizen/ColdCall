cd C:\projects\ColdCall\ImportCEIDGByDate\bin\Debug\net10.0

$env:DOTNET_PROCESSOR_COUNT = '4'
$env:DOTNET_GCHeapHardLimit = '0x400000000'

$p = Start-Process `
    -FilePath '.\ImportCEIDGByDate.exe' `
    -ArgumentList '2026-11-03'`
    -NoNewWindow -PassThru

$p.ProcessorAffinity = [IntPtr]15
$p.WaitForExit()