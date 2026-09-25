$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot '.artifacts/release/WorkLifeBalance.Host.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Run scripts/build.ps1 first.' }
Start-Process -FilePath $executable -ArgumentList '--open' -WindowStyle Hidden
