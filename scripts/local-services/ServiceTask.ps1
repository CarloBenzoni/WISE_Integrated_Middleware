param(
    [ValidateSet('status','start','stop')][string]$Action,
    [ValidateSet('all','mongodb','elasticsearch','hdfs','monstache')][string]$Service = 'all'
)
$ErrorActionPreference = 'Stop'
$started = [DateTime]::UtcNow.ToString('o')
$controlMutex = $null
$lockHeld = $false
try {
    if ($Action -ne 'status') {
        $controlMutex = New-Object Threading.Mutex($false, 'Local\WISE-LocalServices-Control')
        try { $lockHeld = $controlMutex.WaitOne(120000) } catch [Threading.AbandonedMutexException] { $lockHeld = $true }
        if (-not $lockHeld) { throw 'Another Windows service task is still running.' }
    }
    . (Join-Path $PSScriptRoot 'ServiceRuntime.ps1')
    switch ($Action) {
        'status' { $result = Get-ServiceSnapshot }
        'start' { Start-WiseService $Service; $result = @{ ok = $true } }
        'stop' { Stop-WiseService $Service; $result = @{ ok = $true } }
    }
} catch {
    # Only controlled messages are returned; never include full command lines or config values.
    $result = @{ error = $_.Exception.Message }
} finally {
    if ($lockHeld) { $controlMutex.ReleaseMutex() }
    if ($controlMutex) { $controlMutex.Dispose() }
}
$result.startedAt = $started
$result.finishedAt = [DateTime]::UtcNow.ToString('o')
$destination = Join-Path $PSScriptRoot "results\$Service-$Action.json"
$temporary = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 10 -Compress), (New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath $temporary -Destination $destination -Force
if ($result.ContainsKey('error')) { exit 1 }

