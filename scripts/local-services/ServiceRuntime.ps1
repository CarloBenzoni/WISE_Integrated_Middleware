Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$serviceConfig = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'services.json') -Raw | ConvertFrom-Json

function Test-LocalPort([int]$Port) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $pending = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        if (-not $pending.AsyncWaitHandle.WaitOne(400)) { return $false }
        $client.EndConnect($pending)
        return $true
    } catch { return $false } finally { $client.Dispose() }
}

function New-ServiceState($Id, $State, $Ready, $Controllable, $Message, $Pids = @()) {
    return [ordered]@{ id = $Id; state = $State; ready = $Ready; controllable = $Controllable; message = $Message; pids = @($Pids) }
}

function Invoke-HiddenMongoScript([string]$ScriptPath) {
    # A scheduled status check runs frequently while System Status is open.
    # CreateNoWindow also applies to mongosh itself, preventing console flashes.
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $serviceConfig.mongoShell
    $startInfo.Arguments = '"' + $serviceConfig.mongoUri.Replace('"', '\"') + '" --quiet --file "' + $ScriptPath.Replace('"', '\"') + '"'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'Unable to start mongosh.' }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $standardOutput.Result
            Error = $standardError.Result
        }
    } finally {
        $process.Dispose()
    }
}

function Get-WiseProcesses {
    # Preserve an empty array as a value. Without the unary comma PowerShell
    # emits nothing, so callers receive $null and StrictMode rejects $_.ExecutablePath.
    return ,@(Get-CimInstance Win32_Process -Filter "Name = 'mongod.exe' OR Name = 'monstache.exe' OR Name = 'java.exe'")
}

function Get-HdfsProcesses($Processes) {
    return @($Processes | Where-Object {
        $_.Name -eq 'java.exe' -and $_.CommandLine -and
        $_.CommandLine.IndexOf(($serviceConfig.hadoopHome.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $_.CommandLine -match 'org\.apache\.hadoop\.hdfs\.server\.(namenode\.NameNode|datanode\.DataNode)(\s|$)'
    })
}

function Get-MonstacheProcesses($Processes) {
    return @($Processes | Where-Object {
        $_.ExecutablePath -eq $serviceConfig.monstacheExe -and $_.CommandLine -and
        $_.CommandLine -match ('(?:^|\s)-f(?:=|\s+)"?' + [regex]::Escape($serviceConfig.monstacheConfig) + '"?(?:\s|$)')
    })
}

function Get-MongoState($Processes) {
    $matching = @($Processes | Where-Object {
        $_.ExecutablePath -eq $serviceConfig.mongoExe -and $_.CommandLine -and
        $_.CommandLine.IndexOf($serviceConfig.mongoData, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
    if (-not (Test-LocalPort $serviceConfig.mongoPort)) {
        if ($matching.Count) { return New-ServiceState 'mongodb' 'degraded' $false $false 'MongoDB process exists but is not responding; inspect its log. No duplicate will be started.' @($matching | ForEach-Object { $_.ProcessId }) }
        return New-ServiceState 'mongodb' 'stopped' $false $true 'Configured MongoDB is not listening.'
    }
    try {
        $probeResult = Invoke-HiddenMongoScript (Join-Path $PSScriptRoot 'mongo-probe.js')
        if ($probeResult.ExitCode -ne 0) { throw 'Probe failed' }
        $probeLine = @($probeResult.Output -split "`r?`n" | Where-Object { $_.Trim() }) | Select-Object -Last 1
        $probe = $probeLine | ConvertFrom-Json
        if ([IO.Path]::GetFullPath($probe.dbPath).TrimEnd('\') -ine [IO.Path]::GetFullPath($serviceConfig.mongoData).TrimEnd('\') -or $probe.replicaSet -ne $serviceConfig.mongoReplicaSet) {
            return New-ServiceState 'mongodb' 'conflict' $false $false "Port $($serviceConfig.mongoPort) belongs to a different MongoDB data directory or replica set. Expected: $($serviceConfig.mongoData). No action was taken."
        }
        $state = if ($probe.ready) { 'running' } else { 'degraded' }
        $message = if ($probe.ready) { "Replica set $($serviceConfig.mongoReplicaSet) is writable; data: $($serviceConfig.mongoData)." } else { 'MongoDB is responding but the replica set has no writable primary. Replica-set initialization is not performed automatically.' }
        return New-ServiceState 'mongodb' $state ([bool]$probe.ready) $true $message @($matching | ForEach-Object { $_.ProcessId })
    } catch {
        return New-ServiceState 'mongodb' 'unknown' $false $false 'Port is occupied but MongoDB identity could not be verified. Check mongosh, authentication and the configured data directory.'
    }
}

function Get-ElasticState {
    $service = Get-Service -Name $serviceConfig.elasticService -ErrorAction SilentlyContinue
    if (-not $service) { return New-ServiceState 'elasticsearch' 'unknown' $false $false 'Configured Elasticsearch Windows service was not found.' }
    if ($service.Status -eq 'Stopped') { return New-ServiceState 'elasticsearch' 'stopped' $false $true 'Windows service is stopped.' }
    try {
        # Reuse the local endpoint and credentials already supplied to Monstache.
        # Neither the TOML content nor credentials are returned to the WebApp.
        $toml = Get-Content -LiteralPath $serviceConfig.monstacheConfig -Raw
        $endpointMatch = [regex]::Match($toml, '(?m)^\s*elasticsearch-urls\s*=\s*\[\s*"([^"]+)"')
        if (-not $endpointMatch.Success) { throw 'Endpoint unavailable' }
        $endpoint = [Uri]$endpointMatch.Groups[1].Value
        if ($endpoint.Host -notin @('localhost','127.0.0.1','::1','[::1]')) { throw 'Only local endpoints are allowed' }
        $headers = @{}
        if ($endpoint.UserInfo) {
            $credential = [Uri]::UnescapeDataString($endpoint.UserInfo)
            $headers.Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($credential))
        }
        $url = $endpoint.GetLeftPart([UriPartial]::Authority) -replace '://[^/@]+@', '://'
        $health = Invoke-RestMethod -Uri ($url + '/_cluster/health') -Headers $headers -TimeoutSec 3
        $ready = $service.Status -eq 'Running' -and $health.status -in @('green', 'yellow')
        $state = if ($ready) { 'running' } else { 'degraded' }
        return New-ServiceState 'elasticsearch' $state $ready $true "Windows service: $($service.Status); cluster: $($health.status)."
    } catch {
        return New-ServiceState 'elasticsearch' 'degraded' $false $true "Windows service: $($service.Status). Cluster health is unavailable; check the local endpoint and credentials in the Monstache configuration."
    }
}

function Get-HdfsState($Processes) {
    $matching = @(Get-HdfsProcesses $Processes)
    $namenodes = @($matching | Where-Object { $_.CommandLine -match 'namenode\.NameNode(\s|$)' })
    $datanodes = @($matching | Where-Object { $_.CommandLine -match 'datanode\.DataNode(\s|$)' })
    if ($namenodes.Count -gt 1 -or $datanodes.Count -gt 1) { return New-ServiceState 'hdfs' 'conflict' $false $false 'Multiple matching HDFS daemons found; manual inspection is required.' }
    if (-not $matching.Count) {
        if (Test-LocalPort ([Uri]$serviceConfig.hdfsHealthUrl).Port) { return New-ServiceState 'hdfs' 'conflict' $false $false 'HDFS port is occupied by an unidentified instance.' }
        return New-ServiceState 'hdfs' 'stopped' $false $true 'NameNode and DataNode are stopped.'
    }
    try {
        $health = Invoke-RestMethod -Uri $serviceConfig.hdfsHealthUrl -TimeoutSec 3
        $bean = @($health.beans)[0]
        $nodes = $bean.LiveNodes | ConvertFrom-Json
        $liveCount = @($nodes.PSObject.Properties).Count
        $ready = $namenodes.Count -eq 1 -and $datanodes.Count -eq 1 -and $liveCount -gt 0 -and -not $bean.Safemode
        $state = if ($ready) { 'running' } else { 'degraded' }
        return New-ServiceState 'hdfs' $state $ready $true "NameNode: $($namenodes.Count); DataNode: $($datanodes.Count); live data nodes: $liveCount. $($bean.Safemode)" @($matching | ForEach-Object { $_.ProcessId })
    } catch { return New-ServiceState 'hdfs' 'degraded' $false $true 'HDFS daemons detected; NameNode health is not ready.' @($matching | ForEach-Object { $_.ProcessId }) }
}

function Get-MonstacheState($Processes) {
    $matching = @(Get-MonstacheProcesses $Processes)
    if ($matching.Count -gt 1) { return New-ServiceState 'monstache' 'conflict' $false $false 'Multiple Monstache processes use this configuration.' }
    if (-not $matching.Count) {
        $unidentified = @($Processes | Where-Object { $_.Name -eq 'monstache.exe' -and (-not $_.CommandLine -or -not $_.ExecutablePath) })
        if ($unidentified.Count) { return New-ServiceState 'monstache' 'unknown' $false $false 'A Monstache process exists but its identity cannot be read.' }
        return New-ServiceState 'monstache' 'stopped' $false $true 'Configured Monstache is stopped.'
    }
    $message = 'Process is running with the configured file. Indexing progress is not verified.'
    try {
        $health = Invoke-WebRequest -UseBasicParsing -Uri ('http://' + $serviceConfig.monstacheHealthAddress + '/healthz') -TimeoutSec 2
        $instance = Invoke-RestMethod -Uri ('http://' + $serviceConfig.monstacheHealthAddress + '/instance') -TimeoutSec 2
        if ($health.StatusCode -eq 200 -and $instance.pid -eq $matching[0].ProcessId) { $message = 'Process and local health endpoint are responding. Indexing progress is not verified.' }
    } catch { }
    return New-ServiceState 'monstache' 'running' $true $true $message @($matching | ForEach-Object { $_.ProcessId })
}

function Get-ServiceSnapshot {
    $processes = Get-WiseProcesses
    $mongo = Get-MongoState $processes
    $elastic = Get-ElasticState
    $hdfs = Get-HdfsState $processes
    $monstache = Get-MonstacheState $processes
    if ($monstache.state -eq 'running' -and (-not $mongo.ready -or -not $elastic.ready)) {
        $monstache.state = 'degraded'; $monstache.ready = $false
        $monstache.message = 'Process exists but MongoDB or Elasticsearch is not ready.'
    }
    return @{ installed = $true; checkedAt = [DateTime]::UtcNow.ToString('o'); services = @($mongo, $elastic, $hdfs, $monstache) }
}

function Send-GracefulConsoleStop($Target) {
    # Verify process identity again immediately before signalling the console.
    $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($Target.ProcessId)"
    if (-not $current) { return }
    if ($current.CreationDate -ne $Target.CreationDate -or $current.CommandLine -ne $Target.CommandLine) { throw 'Process identity changed; stop refused.' }
    if (-not ('WiseConsoleSignal' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WiseConsoleSignal {
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint pid);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] processes, uint count);
}
'@
    }
    [WiseConsoleSignal]::FreeConsole() | Out-Null
    if (-not [WiseConsoleSignal]::AttachConsole([uint32]$Target.ProcessId)) { throw 'The process has no accessible console. Stop this existing instance manually once, then start it from WISE.' }
    try {
        $consolePids = New-Object uint32[] 64
        $count = [WiseConsoleSignal]::GetConsoleProcessList($consolePids, 64)
        if ($count -eq 0 -or $count -gt 64) { throw 'Unable to verify console ownership. Stop this instance manually.' }
        foreach ($consolePid in $consolePids[0..($count - 1)]) {
            if ($consolePid -eq $PID -or $consolePid -eq $Target.ProcessId) { continue }
            $consoleProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $consolePid"
            if ($consoleProcess -and $consoleProcess.Name -eq 'cmd.exe' -and $consoleProcess.CommandLine -and
                $consoleProcess.CommandLine.IndexOf((Join-Path $serviceConfig.hadoopHome 'bin\hdfs.cmd'), [StringComparison]::OrdinalIgnoreCase) -ge 0) { continue }
            throw 'The console is shared with another program. Stop this existing instance manually once, then start it from WISE.'
        }
        [WiseConsoleSignal]::SetConsoleCtrlHandler([IntPtr]::Zero, $true) | Out-Null
        if (-not [WiseConsoleSignal]::GenerateConsoleCtrlEvent(0, 0)) { throw 'Console shutdown signal failed.' }
        Start-Sleep -Milliseconds 300
    } finally { [WiseConsoleSignal]::FreeConsole() | Out-Null }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = Get-CimInstance Win32_Process -Filter "ProcessId = $($Target.ProcessId)"
        if (-not $remaining -or $remaining.CreationDate -ne $Target.CreationDate) { return }
        Start-Sleep -Milliseconds 500
    }
    throw 'Graceful shutdown timed out. The process was not forcibly terminated.'
}

function Start-WiseService([string]$Id) {
    $snapshot = Get-ServiceSnapshot
    $state = @($snapshot.services | Where-Object { $_.id -eq $Id })[0]
    if ($state.ready) { return }
    if ($state.state -ne 'stopped') { throw $state.message }
    $logRoot = Join-Path $PSScriptRoot 'logs'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    switch ($Id) {
        'mongodb' {
            if (-not (Test-Path -LiteralPath $serviceConfig.mongoData -PathType Container)) { throw 'Configured MongoDB data directory does not exist. It will not be created automatically.' }
            if ($serviceConfig.mongoMode -eq 'windows-service') {
                # A stopped service must also be checked against its configured data directory.
                throw 'Use process mode for the explicitly selected db1 archive; no existing MongoDB Windows service is reconfigured.'
            }
            Start-Process -FilePath $serviceConfig.mongoExe -ArgumentList @('--port', $serviceConfig.mongoPort, '--dbpath', ('"' + $serviceConfig.mongoData + '"'), '--replSet', $serviceConfig.mongoReplicaSet, '--bind_ip', 'localhost') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot "mongodb-$stamp.out.log") -RedirectStandardError (Join-Path $logRoot "mongodb-$stamp.err.log") | Out-Null
        }
        'elasticsearch' { Start-Service -Name $serviceConfig.elasticService }
        'hdfs' {
            $env:HADOOP_HOME = $serviceConfig.hadoopHome
            $env:Path = (Join-Path $serviceConfig.hadoopHome 'bin') + ';' + $env:Path
            $hadoopCommand = Join-Path $serviceConfig.hadoopHome 'bin\hdfs.cmd'
            # Same NameNode/DataNode commands as start-dfs.cmd, with hidden consoles.
            # Never format NameNode or change data directories.
            foreach ($daemon in @('namenode', 'datanode')) {
                $arguments = '/d /s /c ""' + $hadoopCommand + '" ' + $daemon + '"'
                Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList $arguments -WorkingDirectory $serviceConfig.hadoopHome -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot "$daemon-$stamp.out.log") -RedirectStandardError (Join-Path $logRoot "$daemon-$stamp.err.log") | Out-Null
            }
        }
        'monstache' {
            if (-not $snapshot.services[0].ready -or -not $snapshot.services[1].ready) { throw 'MongoDB and Elasticsearch must be ready before starting Monstache.' }
            Start-Process -FilePath $serviceConfig.monstacheExe -ArgumentList @('-f', ('"' + $serviceConfig.monstacheConfig + '"'), '-enable-http-server', '-http-server-addr', $serviceConfig.monstacheHealthAddress) -WorkingDirectory (Split-Path $serviceConfig.monstacheExe) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot "monstache-$stamp.out.log") -RedirectStandardError (Join-Path $logRoot "monstache-$stamp.err.log") | Out-Null
        }
    }
}

function Stop-WiseService([string]$Id) {
    $snapshot = Get-ServiceSnapshot
    $state = @($snapshot.services | Where-Object { $_.id -eq $Id })[0]
    if ($state.state -eq 'stopped') { return }
    if (-not $state.controllable -or $state.state -in @('unknown','conflict')) { throw $state.message }
    if ($Id -in @('mongodb','elasticsearch') -and $snapshot.services[3].state -ne 'stopped') { throw 'Stop Monstache before stopping its dependencies.' }
    switch ($Id) {
        'mongodb' {
            $env:WISE_EXPECTED_MONGO_DATA = $serviceConfig.mongoData
            $env:WISE_EXPECTED_MONGO_REPLICA = $serviceConfig.mongoReplicaSet
            # A successful MongoDB shutdown normally closes the shell connection.
            $null = Invoke-HiddenMongoScript (Join-Path $PSScriptRoot 'mongo-stop.js')
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            while ((Test-LocalPort $serviceConfig.mongoPort) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
            if (Test-LocalPort $serviceConfig.mongoPort) { throw 'MongoDB did not shut down gracefully. No forced termination was attempted.' }
        }
        'elasticsearch' { Stop-Service -Name $serviceConfig.elasticService -ErrorAction Stop }
        'hdfs' {
            $targets = @(Get-HdfsProcesses (Get-WiseProcesses) | Sort-Object { if ($_.CommandLine -match 'datanode\.DataNode') { 0 } else { 1 } })
            foreach ($target in $targets) { Send-GracefulConsoleStop $target }
        }
        'monstache' { foreach ($target in @(Get-MonstacheProcesses (Get-WiseProcesses))) { Send-GracefulConsoleStop $target } }
    }
}

