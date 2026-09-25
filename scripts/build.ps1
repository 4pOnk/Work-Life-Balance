param([string]$Output = '.artifacts/release')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dotnet.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $Dotnet restore WorkLifeBalance.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    Push-Location (Join-Path $projectRoot 'src/web')
    try {
        npm ci --no-fund --no-audit
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }
    & $Dotnet publish src/WorkLifeBalance.Host -c Release --no-restore -r win-x64 --self-contained true -o $Output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Exit the running application before rebuilding.' }
    & $Dotnet publish src/WorkLifeBalance.NativeHost -c Release --no-restore -r win-x64 --self-contained true -o $Output
    if ($LASTEXITCODE -ne 0) { throw 'Native messaging bridge publish failed.' }
    & (Join-Path $PSScriptRoot 'package-firefox.ps1')
    Copy-Item -LiteralPath '.artifacts/work-life-balance-firefox-0.2.0.zip' -Destination (Join-Path $Output 'firefox-extension.zip') -Force
    if (Test-Path -LiteralPath '.artifacts/work-life-balance-firefox-0.2.0-signed.xpi') {
        Copy-Item -LiteralPath '.artifacts/work-life-balance-firefox-0.2.0-signed.xpi' -Destination (Join-Path $Output 'firefox-extension-signed.xpi') -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $Output -Force
    Copy-Item -LiteralPath 'docs/user-guide.md' -Destination $Output -Force
    Write-Host "Built self-contained win-x64: $Output/WorkLifeBalance.Host.exe"
} finally { Pop-Location }
