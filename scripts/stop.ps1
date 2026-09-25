$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot '.artifacts/release/WorkLifeBalance.Host.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Application has not been built.' }
Start-Process -FilePath $executable -ArgumentList '--shutdown' -WindowStyle Hidden -Wait
