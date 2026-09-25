$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'extensions/firefox'
$artifacts = Join-Path $projectRoot '.artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$archive = Join-Path $artifacts 'work-life-balance-firefox-0.2.0.zip'
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $archive -Force
Write-Host "Unsigned Firefox package: $archive"
