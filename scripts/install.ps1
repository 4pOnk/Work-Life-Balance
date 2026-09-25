param([string]$Source = $PSScriptRoot, [switch]$Launch)
$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destination = Join-Path $env:LOCALAPPDATA 'Programs\WorkLifeBalance'
$hostExe = Join-Path $destination 'WorkLifeBalance.Host.exe'
$nativeExe = Join-Path $destination 'WorkLifeBalance.NativeHost.exe'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'WorkLifeBalance.Host.exe'))) { throw 'Run install.ps1 from the built package, or specify -Source.' }
if ([IO.Path]::GetFullPath($sourcePath) -eq [IO.Path]::GetFullPath($destination)) { throw 'Run the installer from the unpacked new release, not from the installed directory.' }
$running = Get-Process -Name 'WorkLifeBalance.Host','WorkLifeBalance.NativeHost' -ErrorAction SilentlyContinue
if ($running) { throw 'Exit Work Life Balance from the tray and close Firefox before installing/updating. No processes were terminated.' }
if ((Test-Path -LiteralPath $destination) -and -not (Test-Path -LiteralPath (Join-Path $destination 'wlb-install.marker'))) {
    throw 'Destination already exists without an installation marker; refusing to overwrite it.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $sourcePath -Force | Copy-Item -Destination $destination -Recurse -Force
[IO.File]::WriteAllText((Join-Path $destination 'wlb-install.marker'), 'WorkLifeBalance-v1')
$manifestPath = Join-Path $destination 'firefox-native-host.json'
$manifest = @{name='com.worklifebalance.tracker'; description='Local Work Life Balance Firefox bridge'; path=$nativeExe; type='stdio'; allowed_extensions=@('work-life-balance@local.invalid')}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$key = 'HKCU:\Software\Mozilla\NativeMessagingHosts\com.worklifebalance.tracker'
New-Item -Path $key -Force | Out-Null
Set-Item -LiteralPath $key -Value $manifestPath
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Work Life Balance.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $hostExe
$shortcut.Arguments = '--open'
$shortcut.WorkingDirectory = $destination
$shortcut.Save()
Write-Host "Installed: $destination"
Write-Host 'Data preserved in LOCALAPPDATA\WorkLifeBalance. Startup is opt-in in the web settings.'
Write-Host 'Install firefox-extension-signed.xpi in Firefox if the extension is not already installed.'
if ($Launch) { Start-Process -FilePath $hostExe -ArgumentList '--open' -WindowStyle Hidden }
