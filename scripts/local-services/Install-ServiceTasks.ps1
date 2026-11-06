# Run once from an elevated PowerShell under the same Windows account that runs WISE.
[CmdletBinding(SupportsShouldProcess)]
param([string]$ConfigPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Resolve script-relative defaults after parameter binding (Windows PowerShell 5.1).
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'services.example.json'
}
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
foreach ($required in @($config.mongoExe, $config.mongoShell, $config.monstacheExe, $config.monstacheConfig, (Join-Path $config.hadoopHome 'bin\hdfs.cmd'), "$env:SystemRoot\System32\wscript.exe")) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file not found: $required" }
}
if (-not (Test-Path -LiteralPath $config.mongoData -PathType Container)) { throw 'MongoDB data directory must already exist.' }
if ($config.mongoMode -ne 'process') { throw 'This installer manages the existing db1 process; it does not modify the MongoDB Windows service.' }
if (-not (Get-Service -Name $config.elasticService -ErrorAction SilentlyContinue)) { throw 'Elasticsearch Windows service was not found.' }
foreach ($value in @($config.mongoExe, $config.mongoShell, $config.mongoData, $config.monstacheExe, $config.monstacheConfig, $config.hadoopHome)) {
    if (-not [IO.Path]::IsPathRooted($value) -or $value -match '["\r\n%&|<>^!]') { throw 'Service paths must be absolute local Windows paths without shell metacharacters.' }
}
foreach ($endpoint in @($config.mongoUri, $config.hdfsHealthUrl, ('http://' + $config.monstacheHealthAddress))) {
    if (([Uri]$endpoint).Host -notin @('localhost','127.0.0.1','::1','[::1]')) { throw 'Service endpoints must be loopback addresses.' }
}
$installRoot = Join-Path $env:ProgramFiles 'WISE-LocalServices'
if (-not $PSCmdlet.ShouldProcess($installRoot, 'Install fixed service-control tasks; no services will be started or stopped')) { return }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Open PowerShell as administrator under the WISE Windows account, then run this script again.' }
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
if ((Get-Item -LiteralPath $installRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installation directory must not be a link.' }
# Protect executable scripts/configuration from modifications by the normal WebApp process.
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$administrators = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$system = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
$user = $identity.User
$acl.SetOwner($administrators)
foreach ($sid in @($administrators, $system)) {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($user, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -LiteralPath $installRoot -AclObject $acl
foreach ($directory in @('results','logs')) { New-Item -ItemType Directory -Path (Join-Path $installRoot $directory) -Force | Out-Null }
foreach ($file in @('ServiceRuntime.ps1','ServiceTask.ps1','ServiceTaskLauncher.vbs','mongo-probe.js','mongo-stop.js')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $installRoot $file) -Force
}
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installRoot 'services.json') -Encoding UTF8
# An unelevated process of the same user must not gain WriteDAC through file ownership.
foreach ($file in @('ServiceRuntime.ps1','ServiceTask.ps1','ServiceTaskLauncher.vbs','mongo-probe.js','mongo-stop.js','services.json')) {
    $filePath = Join-Path $installRoot $file
    $fileAcl = New-Object Security.AccessControl.FileSecurity
    $fileAcl.SetAccessRuleProtection($true, $false)
    $fileAcl.SetOwner($administrators)
    foreach ($sid in @($administrators, $system)) {
        $fileAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')))
    }
    $fileAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($user, 'ReadAndExecute', 'Allow')))
    Set-Acl -LiteralPath $filePath -AclObject $fileAcl
}
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$root = $scheduler.GetFolder('\')
$sddl = "O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;$($user.Value))"
try { $folder = $scheduler.GetFolder('\WISE-LocalServices') }
catch { $folder = $root.CreateFolder('WISE-LocalServices', $sddl) }
$folder.SetSecurityDescriptor($sddl, 0)
$pairs = @(@{ Service = 'all'; Action = 'status' })
foreach ($service in @('mongodb','elasticsearch','hdfs','monstache')) {
    foreach ($action in @('start','stop')) { $pairs += @{ Service = $service; Action = $action } }
}
foreach ($pair in $pairs) {
    $definition = $scheduler.NewTask(0)
    $definition.RegistrationInfo.Description = 'WISE local service control. Fixed action; configuration requires administrator rights.'
    $definition.Principal.UserId = $user.Value
    $definition.Principal.LogonType = 3 # Interactive token: no password stored; user must be logged in.
    $definition.Principal.RunLevel = 1 # Highest available privileges.
    $definition.Settings.Enabled = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    $definition.Settings.MultipleInstances = 2 # Ignore duplicate requests.
    $action = $definition.Actions.Create(0)
    $action.Path = "$env:SystemRoot\System32\wscript.exe"
    $action.WorkingDirectory = $installRoot
    $action.Arguments = '//B //Nologo "' + (Join-Path $installRoot 'ServiceTaskLauncher.vbs') + '" ' + $pair.Service + ' ' + $pair.Action
    $folder.RegisterTaskDefinition(($pair.Service + '-' + $pair.Action), $definition, 6, $user.Value, $null, 3, $sddl) | Out-Null
}
Write-Host 'Installed 9 WISE tasks. No services were started, stopped, or reconfigured.'
Write-Host 'Restart Middleware.Api or open System Status > Local Services in the WebApp.'

