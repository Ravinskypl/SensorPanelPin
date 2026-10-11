#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs SensorPanelPin into Program Files and sets it to start at logon with administrator rights.

.DESCRIPTION
    The logon task runs SensorPanelPin with administrator rights and no UAC prompt. That is only safe
    if nobody but administrators can change the files it starts - otherwise any program running as
    the user could replace the executable, or drop a DLL beside it, and be handed administrator
    rights at the next logon. A download or build folder fails that test.

    This script copies the program into %ProgramFiles%\SensorPanelPin, makes Administrators the
    owner, resets the permissions to the protected ones inherited from Program Files, creates the
    logon task (or repoints an existing one at the installed copy) and starts it.

    It also finishes the rename from SensorPanelGlue: stops the old program, carries its logon task
    over under the new name and removes its folder from Program Files. Settings move over on their
    own at the first start of SensorPanelPin.

    From a release ZIP, run Install.cmd, which asks for administrator rights and runs this script.
    From source, build first:
        powershell -ExecutionPolicy Bypass -File scripts\Build-Release.ps1

    Deliberately WITHOUT IncludeNativeLibrariesForSelfExtract: with it, the native libraries are
    unpacked at startup into the user's %TEMP%, which the user controls, and the elevated process
    loads them from there.

.PARAMETER Source
    Folder with the program. Defaults to the folder of this script when SensorPanelPin.exe is there
    (a release ZIP), otherwise ..\publish next to it (a source checkout).

.PARAMETER Destination
    Installation folder. Defaults to %ProgramFiles%\SensorPanelPin.

.PARAMETER NoAutoStart
    Install only: create no logon task. An existing task is still repointed at the installed copy.
#>
param(
    [string]$Source,
    [string]$Destination = (Join-Path $env:ProgramFiles 'SensorPanelPin'),
    [switch]$NoAutoStart
)

$ErrorActionPreference = 'Stop'
$taskName = 'SensorPanelPin'
$exeName = 'SensorPanelPin.exe'
$legacyName = 'SensorPanelGlue'
$legacyDestination = Join-Path $env:ProgramFiles $legacyName

if (-not $Source) {
    $Source = if (Test-Path (Join-Path $PSScriptRoot $exeName)) { $PSScriptRoot } else { Join-Path $PSScriptRoot '..\publish' }
}
$Source = (Resolve-Path $Source).Path
$sourceExe = Join-Path $Source $exeName
if (-not (Test-Path $sourceExe)) {
    throw "$sourceExe not found. From source, run scripts\Build-Release.ps1 first."
}

# The account the task is for: the user signed in at the console. When a standard user approves the
# UAC prompt with an administrator's password, this script runs as that administrator, and a task
# registered for the current account would start for the wrong person.
function Get-InteractiveUser {
    $console = (Get-CimInstance Win32_ComputerSystem).UserName
    if ($console) { return $console }
    return [Security.Principal.WindowsIdentity]::GetCurrent().Name
}

# 1. Stop whatever is running, task-started or not: its files are about to be replaced.
#    The old name too: two copies would fight over the same windows.
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
$legacyTask = Get-ScheduledTask -TaskName $legacyName -ErrorAction SilentlyContinue
foreach ($name in @($taskName, $legacyName)) {
    Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
}
Start-Sleep -Milliseconds 800

# 2. Replace the installation completely, so no file from an older build lingers beside the new one.
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $Destination -Force | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force

# 3. Administrators own everything, and only the protected permissions of Program Files apply.
& icacls $Destination /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls /setowner failed with exit code $LASTEXITCODE" }
& icacls $Destination /reset /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls /reset failed with exit code $LASTEXITCODE" }

$exe = Join-Path $Destination $exeName
Write-Host "Installed: $exe"

# 3a. The task of the old name becomes the task of the new one, with its trigger and account intact.
#     The definition goes from Export to Register in memory, never through a file. New first, old
#     second: a failure must not leave the user with no autostart at all.
if (-not $task -and $legacyTask) {
    $xml = (Export-ScheduledTask -TaskName $legacyName).Replace($legacyName, 'SensorPanelPin')
    $task = Register-ScheduledTask -TaskName $taskName -Xml $xml
    Write-Host "Logon task '$legacyName' carried over as '$taskName'."
}
if ($task -and $legacyTask) {
    Unregister-ScheduledTask -TaskName $legacyName -Confirm:$false
    Write-Host "Removed the old logon task '$legacyName'."
}

# 3b. The old installation. Only a folder that really holds the old program is removed.
if ((Test-Path (Join-Path $legacyDestination "$legacyName.exe")) -and
    ((Resolve-Path $legacyDestination).Path -ne (Resolve-Path $Destination).Path)) {
    Remove-Item -LiteralPath $legacyDestination -Recurse -Force
    Write-Host "Removed the old installation: $legacyDestination"
}

# 4. The logon task, created or repointed through the Task Scheduler API directly - no definition
#    file passes through a user-writable folder on the way. Same settings as the program's own
#    "start with administrator rights" option: at logon with a 15 s delay, so AIDA64 is up first.
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $Destination
if ($task) {
    Set-ScheduledTask -TaskName $taskName -Action $action | Out-Null
    Write-Host "Logon task '$taskName' now starts the installed copy."
}
elseif (-not $NoAutoStart) {
    $user = Get-InteractiveUser
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $trigger.Delay = 'PT15S'
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    $task = Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings `
        -Description 'Keeps the AIDA64 SensorPanel on its chosen monitor.'
    Write-Host "Created logon task '$taskName' for $user, with administrator rights."
}

if ($task) {
    Start-ScheduledTask -TaskName $taskName
    Write-Host 'SensorPanelPin is running. Its icon is in the notification area; click it to open the settings.'
}
else {
    Write-Host "No logon task was created. Start $exe yourself, or choose autostart in its settings."
}
