param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$DataDirectory, [int]$Seconds = 300)
$ErrorActionPreference = 'Stop'
if ($Seconds -lt 10 -or $Seconds -gt 86400) { throw 'Duration must be 10..86400 seconds.' }
function DatabaseBytes {
    $sum = 0L
    foreach ($name in @('activity.db','activity.db-wal','activity.db-shm')) {
        $path = Join-Path $DataDirectory $name
        if (Test-Path -LiteralPath $path) { $sum += (Get-Item -LiteralPath $path).Length }
    }
    return $sum
}
$process = Get-Process -Id $ProcessId
if ($process.ProcessName -ne 'WorkLifeBalance.Host') { throw 'Expected a Work Life Balance host process.' }
$started = Get-Date
$cpu = $process.TotalProcessorTime.TotalSeconds
$initialBytes = DatabaseBytes
$samples = @()
$remote = @()
$networkAvailable = $true
do {
    $process.Refresh()
    if ($process.HasExited) { throw 'Tracker exited during measurement.' }
    $samples += $process.WorkingSet64
    try {
        $remote += Get-NetTCPConnection -OwningProcess $ProcessId -ErrorAction Stop | Where-Object { $_.RemoteAddress -notin @('0.0.0.0','127.0.0.1','::','::1') } | Select-Object -ExpandProperty RemoteAddress
    } catch { $networkAvailable = $false }
    Start-Sleep -Seconds 5
} while (((Get-Date) - $started).TotalSeconds -lt $Seconds)
$process.Refresh()
$elapsed = ((Get-Date) - $started).TotalSeconds
$peers = @($remote | Sort-Object -Unique)
[pscustomobject]@{
    Started = $started.ToString('o'); ElapsedSeconds = [math]::Round($elapsed, 2)
    CpuSeconds = [math]::Round($process.TotalProcessorTime.TotalSeconds - $cpu, 3)
    CpuPercentOneCore = [math]::Round(($process.TotalProcessorTime.TotalSeconds - $cpu) / $elapsed * 100, 3)
    WorkingSetMinMiB = [math]::Round(($samples | Measure-Object -Minimum).Minimum / 1MB, 2)
    WorkingSetMaxMiB = [math]::Round(($samples | Measure-Object -Maximum).Maximum / 1MB, 2)
    DatabaseStartBytes = $initialBytes; DatabaseEndBytes = DatabaseBytes
    NetworkInspectionAvailable = $networkAvailable
    NonLoopbackTcpPeers = $peers
} | ConvertTo-Json
