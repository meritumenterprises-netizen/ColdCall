param(
    [string]$StripeCli = 'stripe',
    [string]$ForwardTo = 'https://localhost:44345/api/plans/stripe-webhook'
)

$ErrorActionPreference = 'Stop'
$secretId = 'ColdCall-LocalDevelopment'
$secretPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretId\secrets.json"
$secrets = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
$key = $secrets.'Stripe:SecretKey'
if (-not $key -or -not $key.StartsWith('sk_test_')) {
    throw 'Configure a test Stripe:SecretKey in dotnet user-secrets first.'
}
$logFolder = Join-Path $PSScriptRoot '..\App_Data\Stripe'
New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
$logName = [Guid]::NewGuid().ToString('N')
$outputLog = Join-Path $logFolder "$logName.log"
$errorLog = Join-Path $logFolder "$logName.err.log"
$oldKey = $env:STRIPE_API_KEY
try {
    $env:STRIPE_API_KEY = $key
    $listener = Start-Process -FilePath $StripeCli -ArgumentList @(
        'listen', '--forward-to', $ForwardTo,
        '--events', 'checkout.session.completed,checkout.session.async_payment_succeeded', '--skip-verify'
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog
}
finally { $env:STRIPE_API_KEY = $oldKey }
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Seconds 1
    $text = (Get-Content -LiteralPath $outputLog, $errorLog -Raw -ErrorAction SilentlyContinue) -join "`n"
    $secret = [regex]::Match($text, 'whsec_[A-Za-z0-9]+')
    if ($secret.Success) {
        & dotnet user-secrets set 'Stripe:WebhookSecret' $secret.Value --id $secretId
        if ($LASTEXITCODE -ne 0) { throw 'Could not save webhook signing secret.' }
        Write-Output "Stripe test listener started (PID $($listener.Id)). Restart the backend if its configuration was already loaded."
        return
    }
    $listener.Refresh()
    if ($listener.HasExited) { throw 'Stripe listener exited. Check the private App_Data/Stripe logs.' }
}
throw 'Stripe listener startup timed out. Check the private App_Data/Stripe logs.'
