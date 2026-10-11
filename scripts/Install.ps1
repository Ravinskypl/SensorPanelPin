#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs SensorPanelPin into Program Files and points the logon task at that copy.

.DESCRIPTION
    The logon task runs SensorPanelPin with administrator rights and no UAC prompt. That is only
    safe if nobody but administrators can change the files it starts - otherwise any program running
    as the user could replace the executable, or drop a DLL beside it, and be handed administrator
    rights at the next logon. A build folder on a data drive fails that test: inherited permissions
    there give every authenticated user Modify.

    This script copies a published build into %ProgramFiles%\SensorPanelPin, makes Administrators
    the owner, resets the permissions to the protected ones inherited from Program Files, and - if
    the task already exists - repoints it at the installed copy and starts it.

    It also finishes the rename from SensorPanelGlue: stops the old program, carries its logon task
    over under the new name and removes its folder from Program Files. Settings move over on their
    own at the first start of SensorPanelPin.

    Build first:
        dotnet publish src/SensorPanelPin/SensorPanelPin.csproj -c Release -p:PublishSingleFile=true `
            -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish

    Deliberately WITHOUT IncludeNativeLibrariesForSelfExtract: with it, the native libraries are
    unpacked at startup into the user's %TEMP%, which the user controls, and the elevated process
    loads them from there.

.PARAMETER Source
    Folder with the published build. Defaults to ..\publish next to this script.

.PARAMETER Destination
    Installation folder. Defaults to %ProgramFiles%\SensorPanelPin.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\publish'),
    [string]$Destination = (Join-Path $env:ProgramFiles 'SensorPanelPin')
)

$ErrorActionPreference = 'Stop'
$taskName = 'SensorPanelPin'
$exeName = 'SensorPanelPin.exe'
$legacyName = 'SensorPanelGlue'
$legacyDestination = Join-Path $env:ProgramFiles $legacyName

$Source = (Resolve-Path $Source).Path
$sourceExe = Join-Path $Source $exeName
if (-not (Test-Path $sourceExe)) {
    throw "Brak $sourceExe - najpierw wykonaj dotnet publish (polecenie w opisie skryptu)."
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
if ($LASTEXITCODE -ne 0) { throw "icacls /setowner zakonczyl sie kodem $LASTEXITCODE" }
& icacls $Destination /reset /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls /reset zakonczyl sie kodem $LASTEXITCODE" }

$exe = Join-Path $Destination $exeName
Write-Host "Zainstalowano: $exe"

# 3a. The task of the old name becomes the task of the new one, with its trigger and account intact.
#     The definition goes from Export to Register in memory, never through a file. New first, old
#     second: a failure must not leave the user with no autostart at all.
if (-not $task -and $legacyTask) {
    $xml = (Export-ScheduledTask -TaskName $legacyName).Replace($legacyName, 'SensorPanelPin')
    $task = Register-ScheduledTask -TaskName $taskName -Xml $xml
    Write-Host "Zadanie '$legacyName' przeniesione pod nazwe '$taskName'."
}
if ($task -and $legacyTask) {
    Unregister-ScheduledTask -TaskName $legacyName -Confirm:$false
    Write-Host "Usunieto stare zadanie '$legacyName'."
}

# 3b. The old installation. Only a folder that really holds the old program is removed.
if ((Test-Path (Join-Path $legacyDestination "$legacyName.exe")) -and
    ((Resolve-Path $legacyDestination).Path -ne (Resolve-Path $Destination).Path)) {
    Remove-Item -LiteralPath $legacyDestination -Recurse -Force
    Write-Host "Usunieto stara instalacje: $legacyDestination"
}

# 4. Point the logon task at the installed copy. Set-ScheduledTask goes through the Task Scheduler
#    API directly - no definition file passes through a user-writable folder on the way.
if ($task) {
    $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $Destination
    Set-ScheduledTask -TaskName $taskName -Action $action | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Write-Host "Zadanie '$taskName' wskazuje teraz na zainstalowana kopie i zostalo uruchomione."
}
else {
    Write-Host "Zadanie '$taskName' nie istnieje. Uruchom $exe jako administrator i wybierz w menu ikony"
    Write-Host "'Uruchamiaj z Windows' -> 'Harmonogram zadan - z uprawnieniami administratora'."
}
