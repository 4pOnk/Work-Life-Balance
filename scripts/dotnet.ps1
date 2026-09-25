$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$localSdk = Join-Path (Split-Path -Parent $PSScriptRoot) '.artifacts/dotnet10/dotnet.exe'
$script:Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
