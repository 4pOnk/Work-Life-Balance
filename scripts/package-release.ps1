$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root '.artifacts/release'
$archive = Join-Path $root '.artifacts/WorkLifeBalance-0.4.0-win-x64.zip'
foreach ($required in @('WorkLifeBalance.Host.exe','WorkLifeBalance.NativeHost.exe','coreclr.dll','install.ps1','uninstall.ps1','user-guide.md','firefox-extension-signed.xpi')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing release file: $required. Build first; include the approved signed XPI." }
}
if (Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Name -match '\.(db|db-wal|db-shm|bak|log)$' }) {
    throw 'Refusing to package data or diagnostic files.'
}
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256
