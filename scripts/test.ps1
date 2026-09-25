$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dotnet.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $Dotnet restore WorkLifeBalance.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $Dotnet test WorkLifeBalance.sln --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    node --test extensions/tests/*.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Firefox collector tests failed.' }
    Push-Location (Join-Path $projectRoot 'src/web')
    try {
        npm run lint
        if ($LASTEXITCODE -ne 0) { throw 'Lint failed.' }
        npm run format:check
        if ($LASTEXITCODE -ne 0) { throw 'Formatting check failed.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }
} finally { Pop-Location }
