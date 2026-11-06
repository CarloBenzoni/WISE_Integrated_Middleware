param(
    [ValidateSet('status','start','stop')][string]$Action = 'status',
    [ValidateSet('all','mongodb','elasticsearch','hdfs','monstache')][string]$Service = 'all'
)
$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:ProgramFiles 'WISE-LocalServices'
try {
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $folder = $scheduler.GetFolder('\WISE-LocalServices')
    $task = $folder.GetTask("$Service-$Action")
} catch {
    if ($Action -eq 'status') {
        @{ installed = $false; checkedAt = [DateTime]::UtcNow.ToString('o'); services = @(); message = 'Run scripts\local-services\Install-ServiceTasks.ps1 once from an administrator PowerShell.' } | ConvertTo-Json -Compress
        exit 0
    }
    @{ error = 'Windows service tasks are not installed or this account cannot execute them.' } | ConvertTo-Json -Compress
    exit 1
}
try {
    $requestedAt = [DateTime]::UtcNow
    $instance = $task.Run($null)
    $resultPath = Join-Path $installRoot "results\$Service-$Action.json"
    $deadline = [DateTime]::UtcNow.AddSeconds($(if ($Action -eq 'status') { 35 } else { 150 }))
    do {
        if (Test-Path -LiteralPath $resultPath) {
            try {
                $raw = [IO.File]::ReadAllText($resultPath)
                $result = $raw | ConvertFrom-Json
                if ([DateTime]::Parse($result.startedAt).ToUniversalTime() -ge $requestedAt.AddMilliseconds(-500)) {
                    Write-Output $raw
                    if ($result.PSObject.Properties['error']) { exit 1 }
                    exit 0
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Windows service task did not complete in time. Check Task Scheduler and local service logs before retrying.'
} catch {
    @{ error = $_.Exception.Message } | ConvertTo-Json -Compress
    exit 1
}

