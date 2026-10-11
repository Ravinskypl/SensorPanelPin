#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes SensorPanelPin: the running program, its autostart entries, its Program Files folder and,
    unless -KeepSettings is given, its settings and logs.

.DESCRIPTION
    Run Uninstall.cmd, which asks for administrator rights and runs this script. It cleans up after
    the old name, SensorPanelGlue, as well.

.PARAMETER Destination
    Installation folder. Defaults to %ProgramFiles%\SensorPanelPin.

.PARAMETER KeepSettings
    Leave %APPDATA%\SensorPanelPin (settings and logs) in place.
#>
param(
    [string]$Destination = (Join-Path $env:ProgramFiles 'SensorPanelPin'),
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$names = @('SensorPanelPin', 'SensorPanelGlue')

# The user signed in at the console, whose autostart entry and settings these are. This script may
# run as a different administrator account when a standard user approved the UAC prompt.
$user = (Get-CimInstance Win32_ComputerSystem).UserName
if (-not $user) { $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name }
$sid = ([Security.Principal.NTAccount]$user).Translate([Security.Principal.SecurityIdentifier]).Value

# 1. Stop the program and remove its logon tasks.
foreach ($name in $names) {
    if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $name -Confirm:$false
        Write-Host "Removed logon task '$name'."
    }
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
}
Start-Sleep -Milliseconds 800

# 2. The registry autostart entry the program writes in "start without administrator rights" mode.
$runKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run"
foreach ($name in $names) {
    if (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $name
        Write-Host "Removed registry autostart entry '$name'."
    }
}

# 3. The program files. Only folders that really hold the program are removed.
foreach ($folder in @($Destination, (Join-Path $env:ProgramFiles 'SensorPanelGlue'))) {
    $holdsProgram = $names | Where-Object { Test-Path (Join-Path $folder "$_.exe") }
    if ($holdsProgram) {
        Remove-Item -LiteralPath $folder -Recurse -Force
        Write-Host "Removed $folder"
    }
}

# 4. Settings and logs. The folder sits in the user's profile, where any program running as that
#    user can swap it for a junction to somewhere else. A recursive delete running as administrator
#    must never follow one, so a folder that is a link is left alone, and rmdir - which removes
#    junctions inside the tree instead of descending into them - does the deleting.
if (-not $KeepSettings) {
    $profilePath = (Get-CimInstance Win32_UserProfile -Filter "SID='$sid'").LocalPath
    foreach ($name in $names) {
        $folder = Join-Path $profilePath "AppData\Roaming\$name"
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        if ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            Write-Warning "$folder is a link, not a folder; left as it is."
            continue
        }
        & cmd.exe /d /c rmdir /s /q "$folder"
        Write-Host "Removed $folder"
    }
}

Write-Host 'SensorPanelPin is uninstalled.'
