param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
    Write-Warning 'This shell may have an isolated registry view. Run this script from a normal Windows PowerShell to register the host for your desktop Firefox.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyPath = 'HKCU:\Software\Mozilla\NativeMessagingHosts\com.worklifebalance.tracker'
if ($Unregister) {
    if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath }
    Write-Host 'Firefox native host registration removed.'
    return
}
$appDirectory = Join-Path $projectRoot '.artifacts/release'
$executable = Join-Path $appDirectory 'WorkLifeBalance.NativeHost.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Run scripts/build.ps1 first.' }
$manifestPath = Join-Path $appDirectory 'firefox-native-host.json'
$manifest = @{
    name = 'com.worklifebalance.tracker'
    description = 'Local Work Life Balance Firefox bridge'
    path = $executable
    type = 'stdio'
    allowed_extensions = @('work-life-balance@local.invalid')
}
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
New-Item -Path $keyPath -Force | Out-Null
Set-Item -LiteralPath $keyPath -Value $manifestPath
Write-Host 'Firefox native host registered for the current user.'
