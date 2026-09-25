param([switch]$DeleteData)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\WorkLifeBalance'))
$data = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'WorkLifeBalance'))
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\WorkLifeBalance'))
if ($destination -ne $expected -or $destination -eq [IO.Path]::GetPathRoot($destination)) { throw 'Unsafe uninstall path.' }
$marker = Join-Path $destination 'wlb-install.marker'
if (-not (Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw) -ne 'WorkLifeBalance-v1') { throw 'Installation marker missing.' }
if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to uninstall a linked directory.' }
if (Get-Process -Name 'WorkLifeBalance.Host','WorkLifeBalance.NativeHost' -ErrorAction SilentlyContinue) { throw 'Exit the tracker and close Firefox first.' }
$key = 'HKCU:\Software\Mozilla\NativeMessagingHosts\com.worklifebalance.tracker'
if (Test-Path -LiteralPath $key) {
    $registered = (Get-Item -LiteralPath $key).GetValue('')
    if ($registered -eq (Join-Path $destination 'firefox-native-host.json')) { Remove-Item -LiteralPath $key }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$run = Get-ItemProperty -LiteralPath $runKey -Name 'WorkLifeBalance' -ErrorAction SilentlyContinue
if ($run -and $run.WorkLifeBalance.StartsWith('"' + (Join-Path $destination 'WorkLifeBalance.Host.exe') + '"')) {
    Remove-ItemProperty -LiteralPath $runKey -Name 'WorkLifeBalance'
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Work Life Balance.lnk'
if (Test-Path -LiteralPath $shortcut) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $destination 'WorkLifeBalance.Host.exe')) { Remove-Item -LiteralPath $shortcut }
}
Remove-Item -LiteralPath $destination -Recurse -Force
if ($DeleteData) {
    if ((Read-Host 'Delete ALL local history, backups and settings? Type DELETE') -ne 'DELETE') { throw 'Data deletion cancelled. Application removed; data retained.' }
    if ($data -ne [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'WorkLifeBalance'))) { throw 'Unsafe data path.' }
    if ((Test-Path -LiteralPath $data) -and ((Get-Item -LiteralPath $data).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Refusing to delete a linked data directory.' }
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
}
Write-Host 'Application removed. Remove the extension in Firefox Add-ons. Data was kept unless explicitly confirmed.'
