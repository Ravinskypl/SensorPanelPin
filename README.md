# SensorPanelPin

A Windows 11 tray app that keeps the AIDA64 **SensorPanel** on the monitor you choose, and moves
windows of other programs off that monitor onto your main one.

Made for small secondary screens (USB or HDMI sensor displays) that exist only to show the panel:
the panel stays there through reboots, games, monitor power-offs and cable replugs, and nothing
else is allowed to open on top of it.

> Not affiliated with FinalWire Ltd., the maker of AIDA64. "AIDA64" and "SensorPanel" are used here
> only to describe what the program works with.

The interface is in English, or in Polish when Windows is set to Polish. The log is always in English, so it can be shared in an issue.

---

## What this app does NOT do

**It does not prevent programs from opening on the chosen screen.** Windows has no API, policy or
setting that forbids a process from opening a window on a particular monitor.

What can be done without tampering with other processes is to **notice windows that appear there
and move them away at once**. That is what this app does. In practice a window flashes on the panel
screen and lands on the main monitor about 0.2 s later.

No DLL injection, no global CBT hooks in other processes, no drivers, no kernel changes: only
documented user-mode Win32 APIs.

---

## Install

1. Download `SensorPanelPin-<version>-win-x64.zip` from
   [Releases](https://github.com/Ravinskypl/SensorPanelPin/releases) and extract it anywhere.
2. Double-click **`Install.cmd`** and approve the administrator prompt.
3. Click the tray icon, and on the first page (**Screens**) pick the screen the SensorPanel
   belongs on.

`Install.cmd` copies the program to `C:\Program Files\SensorPanelPin`, creates a logon task that
starts it with administrator rights, and starts it. Nothing needs to be installed first: the .NET
runtime is inside the executable. The extracted folder is not needed afterwards.

**SmartScreen.** The executable is not code-signed, so Windows may show "Windows protected your PC".
Choose **More info → Run anyway**. Each release is built by GitHub Actions from the tagged commit,
lists its SHA-256 checksum, and carries a provenance attestation you can check:

```bash
gh attestation verify SensorPanelPin-2.0.0-win-x64.zip --repo Ravinskypl/SensorPanelPin
```

**Why administrator rights.** AIDA64 runs as administrator to read its sensors, and Windows does not
let a normal program move the windows of an elevated one (see
[the main limitation](#the-main-limitation-aida64-runs-as-administrator)). The logon task gives
SensorPanelPin the same rights without a UAC prompt at every logon. That is only safe when nobody
but administrators can change the program's files, which is why it installs into `Program Files`.

**Without installing**, `SensorPanelPin.exe` also runs straight from the extracted folder, without
administrator rights. It will then manage other windows but cannot move an elevated SensorPanel.

### Uninstall

Run `Uninstall.cmd` (from the ZIP, or from `C:\Program Files\SensorPanelPin`) and approve the
prompt. It stops the program and removes its logon task, its registry autostart entry, its
`Program Files` folder and its settings and logs. `Uninstall.ps1 -KeepSettings` keeps the settings.

### First run

1. Start AIDA64 and turn on the SensorPanel.
2. Click the tray icon. The settings open on **Screens**, with a map of your monitors.
3. In **SensorPanel screen**, pick the screen for the panel. **Identify** shows each monitor's
   number from the map on that monitor.
4. If the panel is not exactly where you want it yet, place it by hand and on the **SensorPanel**
   page press **Remember current**. Otherwise the position is learned the first time the panel
   appears on its screen.

Changes apply at once, as in Windows Settings: there is no Save button.

---

## Interface

The settings window follows the Windows 11 look: light or dark with the system, the system accent
colour, Mica background. Four pages:

* **Screens**: a to-scale map of the monitors with the SensorPanel drawn where it really
  is: in the accent colour when in place, in the warning colour when on another screen, with a
  dashed outline where it should be. The map refreshes every second while the window is open.
  Below it, one sentence about the state and the choice of screen roles.
* **SensorPanel**: keep position and size, remember the current position, window behaviour. The
  rule that recognises the panel window is in a collapsed **Advanced** card; the
  default fits AIDA64.
* **Other windows**: moving windows off the panel screen, exceptions, windows that kept
  coming back.
* **Startup**: autostart, logging and, collapsed, the reaction timings.

The tray menu holds only what belongs in a tray: state, open settings, fix the position now, pause,
and quit.

---

## The main limitation: AIDA64 runs as administrator

AIDA64 almost always runs elevated. Windows UIPI does not let a process at a lower integrity level
call `SetWindowPos` on a window of a higher one: the call fails with `ERROR_ACCESS_DENIED (5)`.

**So SensorPanelPin started normally cannot move the AIDA64 panel.**

The app detects this at start with a harmless `SetWindowPos` call that moves nothing, and says so:
a warning in the log, a notification at the tray icon, and on the **Screens** page a yellow bar with
a **Run as administrator** button. The tray menu has the same command.

Elevation is never forced at start: the manifest declares `asInvoker`, and UAC appears only when
you click that command.

### Autostart

A `HKCU\...\Run` entry **cannot start a program elevated**; that is a Windows rule. So the
**Startup** page offers three choices:

| Choice | What it does |
|---|---|
| Do not start | Removes both entries |
| Start without administrator rights | A `HKCU\...\Run` entry. The panel is not managed if AIDA64 runs as administrator |
| Start with administrator rights | The `SensorPanelPin` scheduled task, `RunLevel = HighestAvailable`, logon trigger with a 15 s delay. Starts elevated **with no UAC prompt at logon** |

`Install.cmd` sets up the third one. AIDA64 starts the same way, through its `\AIDA64 AutoStart`
task, for the same reason. The 15 s delay lets the panel exist before SensorPanelPin looks for it;
if AIDA64 is slower, the app waits and picks the window up when it appears.

The task is created and removed through the Task Scheduler COM API, in process: no `schtasks.exe`,
no temporary file. Creating or removing it requires the program to be elevated, and is **refused
when the program sits where ordinary users can change it** (see below).

Moving windows of *other* programs works without elevation, as long as those programs are not
elevated themselves.

---

## Security

The program normally runs with an administrator token, started at logon with no UAC prompt. The
security boundary that matters is the one between it and ordinary code running on the same account
without those rights: anything that code can change, and that the elevated program then reads,
runs or writes, has to be checked.

* **The file started at logon must be protected.** Before creating the task the program checks the
  ACLs of the executable, the files beside it, its folder and every parent folder: only `SYSTEM`,
  Administrators and `TrustedInstaller` may write, delete or change permissions, and one of them
  must be the owner. An early version registered a task for a file on a data drive, where inherited
  permissions let every signed-in user modify it; any program on the account could have replaced
  the file and received administrator rights at the next logon. Hence `Program Files`.
* **No libraries from `%TEMP%`.** The single-file publish is built *without*
  `IncludeNativeLibrariesForSelfExtract`. With it, the native WPF libraries would be unpacked at
  every start into `%TEMP%\.net\…`, a folder the user fully controls, and the elevated program would
  load them from there. They ship beside the exe instead.
* **The task through the API, not through a file.** An early version wrote the task definition to a
  fixed file in `%TEMP%` and passed it to `schtasks.exe`, run elevated and without a full path. That
  allowed swapping the file between write and read, and planting a `schtasks.exe`.
* **Writes into the user profile through a verified handle.** The log and settings live in
  `%APPDATA%`, which ordinary code can replace with a link (junctions and hardlinks need no
  privileges). The folder is checked for links, and every opened file must, through its handle,
  have exactly one link and lie where it should. If not, logging turns off (with a notification)
  and settings are not saved. `Uninstall.ps1` refuses to delete through a link for the same reason.
* **Content controlled by others.** Window titles are set by whichever program owns the window.
  Every line break in a log message is indented, so a title cannot forge a log entry; titles are
  read up to 512 characters and shortened to 60 in the log; pattern matching has a timeout.

See [SECURITY.md](SECURITY.md) to report a vulnerability.

---

## How it works

```
WinEventHook (OUTOFCONTEXT)          system events                   safety poll
  SHOW / LOCATIONCHANGE                WM_DISPLAYCHANGE                  every 5 s
  FOREGROUND / MOVESIZEEND             WM_DPICHANGED
  MINIMIZEEND                          WM_POWERBROADCAST
        |                              WM_WTSSESSION_CHANGE                  |
        v                                     |                              |
   deadlines: events 150 ms (max 1 s)   screen change 2 s (max 10 s)         |
        |                                     |                              |
        +------------> LoopClock (one clock, posts to the UI queue) <--------+
                                  |
                             PinService                 thread-pool watchdog:
                                  |                     "loop not responding for N s"
                 +----------------+----------------+
                 v                                 v
        SensorPanel watchdog              foreign window relocator
        (position / size /                (filter -> anti-ping-pong ->
         topmost / Alt+Tab)                WindowMover)
```

Decisions that matter:

* **Monitors are identified by device path.** `QueryDisplayConfig` returns a `monitorDevicePath`
  (e.g. `\\?\DISPLAY#EYA713A#5&3af5c87a&0&UID4352#{...}`) that survives reboots and replugs. When
  the target monitor is missing, the app **waits**; it never parks the panel on a substitute screen.
* **One clock, driven by deadlines.** An early version had three WinForms timers restarted on every
  window event. In WinForms `Timer.Stop()` destroys the timer's hidden window and `Start()` creates
  a new one: measured at 208 µs per restart, about 150 times the rest of the work. Worse,
  `WM_TIMER` is generated only when the message queue is empty, so a flood of events silenced all
  three timers at once, including the poll meant as the safety net. That matched an incident where
  three screen changes were logged and the delayed enforcement never came. Now one thread-pool
  clock posts a message to the UI queue (a posted message is not starved like `WM_TIMER`), events
  only move deadlines, and every burst has a cap on how long it can defer. A separate thread-pool
  watchdog logs it if the loop stops responding.
* **No trust in coordinates while the layout changes.** A layout where monitor rectangles partly
  overlap (clones with identical rectangles are fine) counts as transitional, and nothing happens;
  and every computed target position of the panel must actually lie on the target monitor. The same
  guard applies to moving other windows.
* **A pulse in the log.** Every 5 minutes, a state line, also while paused, with the number of log
  lines that could not be written. Layout changes are logged with full coordinates.
* **Moves are verified, not assumed.** After each operation the app reads the window rectangle back
  and checks that it really is on the target screen. The log shows where the window landed.
* **Maximised windows**: restore on the target monitor, move, maximise again. If maximising fails,
  the window goes back to its original state.
* **Anti-ping-pong with breaks, not a life sentence.** A window may be moved 5 times in 10 s
  (`PingPongLimit` / `PingPongWindowSeconds`); the next move in that time gives it a break (30 s by
  default, `max(15 s, 3 × window)`), after which it gets another chance. Only the third burst, after
  two breaks, ends with the window being left alone for the session. A separate fuse protects
  against fighting AIDA64 itself: after 12 successful panel corrections in 10 s comes a 30 s pause.
* **Never touched:** owned windows, `WS_EX_TOOLWINDOW`, untitled, minimised, DWM-cloaked
  (`DWMWA_CLOAKED`), shell windows, UAC/`consent.exe` windows, windows of processes that cannot be
  queried, the SensorPanel itself, anything from the AIDA64 process, full-screen windows, and
  everything while `SHQueryUserNotificationState` reports full-screen D3D or presentation mode.

---

## Configuration

`%APPDATA%\SensorPanelPin\config.json`, editable by hand; written atomically, through a random
temporary file and a verified handle. Hand-written `null`s are cleaned up on load.

| Key | Meaning | Default |
|---|---|---|
| `PanelMonitorId` | Device path of the SensorPanel's monitor | (empty) |
| `HostMonitorId` | Target monitor for other windows; empty = the Windows main display | (empty) |
| `PanelRule` | How to recognise the panel window: `ProcessName`, `ClassPattern`, `TitlePattern` (`*` and `?` wildcards) | `ProcessName` = `aida64`, `ClassPattern` = `*SensorPanel*`, `TitlePattern` empty |
| `PanelGeometry` | Offset from the monitor's top-left corner + size | learned |
| `KeepPanelPosition` | Restore the saved position; off = only keep it on the right screen | `true` |
| `RestorePanelSize` | Restore the size too | `false` |
| `KeepPanelTopmost` | `WS_EX_TOPMOST` on the panel (AIDA64 may remove it) | `false` |
| `HidePanelFromAltTab` | Add `WS_EX_TOOLWINDOW` | `false` |
| `RelocateForeignWindows` | Move other windows off the panel screen | `true` |
| `Exceptions` | Rules for windows left alone | `[]` |
| `SafetyPollSeconds` | Safety poll interval | `5` |
| `EventDebounceMs` | Wait after a window event | `150` |
| `DisplayChangeDelayMs` | Wait after a screen layout change or wake-up | `2000` |
| `PingPongLimit` / `PingPongWindowSeconds` | Moves allowed within the time window; the next one gives the window a break | `5` / `10` |
| `VerboseLogging` | `Debug` level in the log | `false` |
| `StartPaused` | Start paused | `false` |

With AIDA64 Extreme v8.35 the panel window has class `TForm_HWMonitoringSensorPanel` and title
`SensorPanel`, which the default pattern matches. If it ever stops matching,
**SensorPanel → Advanced → Show AIDA64 windows** lists every window of the
process and marks the one that matches.

Logs: `%APPDATA%\SensorPanelPin\logs\sensorpanelpin.log` (rotated at 1 MB × 5 files). The log
records shortened (60 characters) titles of windows the program moves or, with verbose logging,
skips.

### Coming from SensorPanelGlue

Before 2.0 the program was called SensorPanelGlue. `Install.cmd` stops the old version, carries its
logon task over under the new name and removes `C:\Program Files\SensorPanelGlue`. On its first
start SensorPanelPin takes over `%APPDATA%\SensorPanelGlue\config.json` (only reading it). A running
old version stops the new one from starting beside it, since two copies would fight over the same
windows.

---

## Building from source

Requires the .NET 10 SDK.

```bash
dotnet test tests/SensorPanelPin.Tests/SensorPanelPin.Tests.csproj
```

```bash
powershell -ExecutionPolicy Bypass -File scripts\Build-Release.ps1
```

`Build-Release.ps1` runs the tests, publishes a self-contained single-file build and packs it with
the install scripts into `artifacts\SensorPanelPin-<version>-win-x64.zip`, with `SHA256SUMS.txt`.
The release workflow runs the same script when a `v*` tag is pushed. The package is the 62 MB
executable plus five native WPF libraries, 70 MB in all.

To install a local build, run `Install.cmd` from the folder inside `artifacts`.

### Stack

C# / .NET 10 (LTS): the engine on P/Invoke into Win32 (`SetWinEventHook`, `SetWindowPos`,
`SetWindowPlacement`, `EnumWindows`, `EnumDisplayMonitors`, CCD's `QueryDisplayConfig`), the
interface in WPF with the built-in Fluent theme. WinForms remains for the two things WPF lacks: the
tray icon (`NotifyIcon`) and a hidden window receiving `WM_DISPLAYCHANGE`, `WM_POWERBROADCAST` and
session change broadcasts.

The manifest declares **Per-Monitor DPI Awareness V2**, deliberately not through
`Application.SetHighDpiMode`: the manifest applies before any managed code runs, and a program that
measures and moves other programs' windows must be DPI-aware from its first instruction. The
WinForms analyzer that expects the API route (`WFAC010`, `WFO0003` in .NET 10) is silenced in the
`.csproj`.

### Tests

392 xUnit tests that need no desktop: monitor layout consistency (including an incident with a
monitor reported at (0,0)), anti-ping-pong with breaks (an incident with Steam), clock deadline
arithmetic, refusal to write through junctions and hardlinks, ACL checks (a file in `%TEMP%`
rejected, `dotnet.exe` in `Program Files` accepted), wildcards, configuration, logging, taking over
settings from the old name, runtime switches (WPF cannot run with `InvariantGlobalization`: every
control template then throws `XamlParseException`), and the two interface languages: every text
exists in both with the same placeholders, and no Polish text is left in the code.

### Measured on real hardware

Windows 11 Pro 26220, AW3423DWF 3440×1440 at 0,0 (main) + eM713A 1024×600 at 3440,0 (panel screen),
AIDA64 Extreme v8.35.8400.

| Test | Result |
|---|---|
| Monitor detection by device path | 2/2, with stable IDs and DPI |
| Finding the SensorPanel window | within 100 ms of start |
| Detecting missing rights over the AIDA64 window | reported at start, no forced UAC |
| Panel back in place after a manual move | **186 ms** on average (197 / 180 / 182) |
| Notepad moved off the panel screen | **185–234 ms**, relative position kept |
| Moving a maximised window (Steam) | **254 ms**, still maximised |
| Window on the exception list | stayed on the panel screen |
| Target monitor unavailable | "waiting" state, panel untouched, no crash |
| Physically unplugging and replugging the panel monitor | both `WM_DISPLAYCHANGE` seen, monitor recognised without choosing it again, panel restored |
| Full Windows restart | task started the app 37 s after boot, elevated; panel on the eM713A |
| Idle CPU (90 s) | **0.12% of one core** |
| Idle memory | 69 MB working set / 28 MB private |

---

## Known limitations

1. **Windows of elevated processes** (AIDA64 included) cannot be moved without elevation. That is
   Windows UIPI.
2. **Exclusive full-screen games** are skipped on purpose.
3. **A window flashes before it is moved**: the reaction takes about 0.2 s.
4. **Programs that insist on their own position** are left alone after three bursts of moves; they
   are listed on the **Other windows** page.
5. **Autostart without administrator rights does not manage the panel** if AIDA64 runs as
   administrator.
6. **"Always on top" may be removed by AIDA64**; turning it on in AIDA64 itself is more reliable.
7. **Cloned monitors** count as one screen.

---

## Code layout

```
src/SensorPanelPin/
  Program.cs              single instance, log, WPF loop with the Fluent theme
  app.manifest            PerMonitorV2 DPI, asInvoker
  app.ico                 icon (16-256 px), generated from UI/IconArtwork.cs
  Interop/                P/Invoke: NativeMethods, DwmInterop, DisplayConfigInterop (CCD), FileInterop
  Monitors/               MonitorRegistry (stable IDs, geometry, layout consistency), MonitorInfo
  Windows/                WindowQuery/WindowInfo, WindowFilter, WindowMover, PingPongGuard, Wildcard
  Core/                   PinService (engine), LoopClock (deadline clock), WinEventListener,
                          SystemEventWindow, Elevation, ServiceStatus
  Config/                 AppConfig, ConfigStore, AutoStart (Task Scheduler COM API), ExecutableTrust (ACL)
  Diagnostics/            Logger, SafeDirectory (link-resistant writes)
  Localization/           Strings.resx (English), Strings.pl.resx (Polish), Strings (typed access)
  UI/                     SettingsWindow + Pages/, SettingsViewModel, MonitorMap, TrayController,
                          IdentifyWindow, DiagnosticsWindow, Theme.xaml, AppIcon, IconArtwork
tests/SensorPanelPin.Tests/   xUnit
scripts/                  Install / Uninstall (.cmd + .ps1), Build-Release.ps1, README-release.txt
.github/workflows/        CI (tests on every push) and Release (package on a v* tag)
```

The icon is defined once, in `UI/IconArtwork.cs`, as a drawing proportional to its size; `app.ico`
is generated from it and serves as both the executable's icon and the resource the windows and
tray icon load. After changing the drawing, regenerate `app.ico`.

---

## How this was made

The code, tests and documentation were written by an AI coding assistant (Claude, through Claude
Code), directed, reviewed and tested on real hardware by the repository owner. Issues and pull
requests are welcome.

## License

[MIT](LICENSE).
