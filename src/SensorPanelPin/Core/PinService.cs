using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using SensorPanelPin.Config;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Monitors;
using SensorPanelPin.Windows;
using static SensorPanelPin.Interop.NativeMethods;

namespace SensorPanelPin.Core;

/// <summary>
/// Owns the whole enforcement loop. WinEvent hooks feed a coalesced queue, display/power/session
/// events schedule a delayed full sweep, and a low-frequency safety poll catches anything missed.
/// All three are deadlines served by one <see cref="LoopClock"/>; a separate thread-pool watchdog
/// reports if the UI thread stops serving them at all.
/// </summary>
public sealed class PinService : IDisposable
{
    private const int PanelPositionTolerance = 2;
    private const int SelfMoveSuppressionMs = 400;
    private const int PanelBackoffCorrections = 12;
    private const int StyleMaxFailures = 3;
    private const int TopmostReapplyWarnAfter = 5;

    private static readonly TimeSpan PostMoveRecheck = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan MaxEventWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxLayoutDefer = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PanelBackoffWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PanelBackoffDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StyleRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumStall = TimeSpan.FromSeconds(30);

    private readonly Logger _log;
    private readonly ConfigStore _store;
    private readonly MonitorRegistry _monitors;
    private readonly WindowMover _mover;
    private readonly WindowFilter _filter;
    private readonly PingPongGuard _guard;

    private readonly HashSet<IntPtr> _pending = new();
    private readonly Dictionary<IntPtr, DateTime> _selfMoved = new();
    private readonly List<IntPtr> _recheckQueue = new();

    private LoopClock? _clock;
    private System.Threading.Timer? _watchdog;
    private WinEventListener? _listener;
    private SystemEventWindow? _systemEvents;

    private AppConfig _config;
    private bool _paused;
    private bool _started;

    // Deadlines, all UTC and all owned by the UI thread.
    private DateTime _pendingFirstUtc = DateTime.MaxValue;
    private DateTime _pendingLastUtc = DateTime.MaxValue;
    private DateTime _layoutFirstUtc = DateTime.MaxValue;
    private DateTime _sweepDueUtc = DateTime.MaxValue;
    private DateTime _recheckDueUtc = DateTime.MaxValue;
    private DateTime _pollDueUtc = DateTime.MaxValue;

    // Read by the watchdog thread.
    private long _lastTickUtcTicks = DateTime.UtcNow.Ticks;
    private long _pollIntervalTicks;
    private int _stallReported;

    private IntPtr _panelHwnd = IntPtr.Zero;
    private IntPtr _probedPanel = IntPtr.Zero;
    private bool _panelAccessDenied;
    private int _panelCorrections;
    private DateTime _panelCorrectionWindowStart = DateTime.UtcNow;
    private DateTime _panelBackoffUntil = DateTime.MinValue;
    private DateTime _lastHeartbeat = DateTime.MinValue;

    private DateTime _lastTopmostAttempt = DateTime.MinValue;
    private int _topmostFailures;
    private int _topmostReapplications;
    private bool _topmostGivenUp;
    private DateTime _lastAltTabAttempt = DateTime.MinValue;
    private int _altTabFailures;
    private bool _altTabGivenUp;

    private ServiceStatus _status = new(ServiceState.Stopped, "Nie uruchomiono.");

    public PinService(Logger log, ConfigStore store, AppConfig config)
    {
        _log = log;
        _store = store;
        _config = config.Clone();

        _monitors = new MonitorRegistry(log);
        _mover = new WindowMover(log);
        _filter = new WindowFilter((uint)Environment.ProcessId);
        _guard = new PingPongGuard(log)
        {
            Limit = _config.PingPongLimit,
            Window = TimeSpan.FromSeconds(_config.PingPongWindowSeconds),
        };

        _pollIntervalTicks = PollInterval.Ticks;
    }

    public event EventHandler<ServiceStatus>? StatusChanged;

    /// <summary>
    /// A copy. The configuration window used to write draft values straight into the running
    /// configuration, so pressing Cancel left an abandoned panel rule live - and sometimes on disk.
    /// </summary>
    public AppConfig Config => _config.Clone();

    public MonitorRegistry Monitors => _monitors;

    public PingPongGuard Guard => _guard;

    public ServiceStatus Status => _status;

    public bool IsPaused => _paused;

    /// <summary>
    /// True when Windows refuses to let this process reposition the SensorPanel, which happens when
    /// AIDA64 runs elevated and SensorPanelPin does not.
    /// </summary>
    public bool PanelAccessDenied => _panelAccessDenied;

    private TimeSpan PollInterval => TimeSpan.FromSeconds(_config.SafetyPollSeconds);

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _paused = _config.StartPaused;

        // Posting needs a context bound to this (the UI) thread: WPF's dispatcher context in the app,
        // anything installed by a host otherwise.
        var ui = SynchronizationContext.Current
                 ?? throw new InvalidOperationException("PinService.Start must run on a UI thread with a SynchronizationContext.");
        _clock = new LoopClock(ui, OnTick);

        // MonitorRegistry logs the layout itself whenever it changes, including this first read.
        _monitors.Refresh();

        _systemEvents = new SystemEventWindow(_log, OnLayoutEvent);
        _listener = new WinEventListener(_log, OnWindowEvent);
        _listener.Start();

        Volatile.Write(ref _lastTickUtcTicks, DateTime.UtcNow.Ticks);
        _watchdog = new System.Threading.Timer(Watch, null, WatchdogInterval, WatchdogInterval);

        FullSweep("start aplikacji", verbose: true);
        _pollDueUtc = DateTime.UtcNow + PollInterval;
        ScheduleNext();

        _log.Info(_paused ? "Uruchomiono w trybie wstrzymanym." : "Uruchomiono.");
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        _log.Info(paused ? "Wstrzymano pilnowanie." : "Wznowiono pilnowanie.");

        if (paused)
        {
            ClearPending();
            UpdateStatus(ServiceState.Paused, "Wstrzymane przez użytkownika.");
        }
        else
        {
            FullSweep("wznowienie", verbose: true);
        }
    }

    public void ForceNow()
    {
        WindowQuery.InvalidateProcessCache();
        _panelBackoffUntil = DateTime.MinValue;
        _panelCorrections = 0;
        FullSweep("wymuszenie ręczne", verbose: true);
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config.Clone();
        _log.MinimumLevel = _config.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        _guard.Limit = _config.PingPongLimit;
        _guard.Window = TimeSpan.FromSeconds(_config.PingPongWindowSeconds);
        Volatile.Write(ref _pollIntervalTicks, PollInterval.Ticks);

        _panelHwnd = IntPtr.Zero;
        _probedPanel = IntPtr.Zero;
        ResetStyleState();
        _log.ResetThrottle("panel-missing");
        _log.ResetThrottle("monitor-missing");

        FullSweep("zmiana konfiguracji", verbose: true);
        _pollDueUtc = DateTime.UtcNow + PollInterval;
        ScheduleNext();
    }

    /// <summary>
    /// Measures where the panel currently sits, relative to its monitor, using
    /// <paramref name="rule"/> to find it. Changes nothing - neither the running configuration nor
    /// the file; the caller decides whether to keep the result.
    /// </summary>
    public bool TryMeasurePanel(WindowRule rule, out MonitorInfo? monitor, out PanelGeometry geometry, out string message)
    {
        _monitors.Refresh();
        monitor = null;
        geometry = new PanelGeometry();

        var panel = FindPanel(rule);
        if (panel is null)
        {
            message = "Nie znaleziono okna SensorPanel. Uruchom AIDA64 i włącz SensorPanel.";
            return false;
        }

        monitor = _monitors.FromRect(panel.Rect);
        if (monitor is null)
        {
            message = "Nie udało się ustalić monitora, na którym znajduje się panel.";
            return false;
        }

        geometry = new PanelGeometry
        {
            OffsetX = panel.Rect.X - monitor.Bounds.X,
            OffsetY = panel.Rect.Y - monitor.Bounds.Y,
            Width = panel.Rect.Width,
            Height = panel.Rect.Height,
        };

        message = $"Zmierzono pozycję panelu na ekranie „{monitor.FriendlyName}”.";
        return true;
    }

    /// <summary>
    /// Where the panel is and where it should be, for drawing. Reads the cached handle only - no
    /// enumeration - so a settings window can refresh this every second at negligible cost.
    /// </summary>
    public PanelSnapshot GetPanelSnapshot()
    {
        var target = _monitors.FindById(_config.PanelMonitorId);
        var panel = _panelHwnd != IntPtr.Zero ? WindowQuery.Get(_panelHwnd) : null;

        Rectangle? desired = null;
        if (panel is not null && target is not null)
        {
            desired = ComputePanelTarget(panel, target);
        }

        return new PanelSnapshot(panel?.Rect, desired, target?.StableId);
    }

    /// <summary>Lets windows given up on by the ping-pong guard be moved again.</summary>
    public void ForgiveProblemWindows() => _guard.Clear();

    /// <summary>Dumps every top-level window of the process named by <paramref name="rule"/> to the log.</summary>
    public string RunDiagnostics(WindowRule rule)
    {
        _monitors.Refresh();
        var sb = new StringBuilder();
        sb.AppendLine("=== Diagnostyka SensorPanelPin ===");

        sb.AppendLine("Monitory:");
        foreach (var monitor in _monitors.Monitors)
        {
            sb.AppendLine($"  {monitor.FriendlyName} | GDI={monitor.GdiDeviceName} | DPI={monitor.Dpi} "
                          + $"| bounds={monitor.Bounds} | work={monitor.WorkArea} "
                          + $"| primary={monitor.IsPrimary} | id={monitor.StableId}");
        }

        var processFilter = string.IsNullOrWhiteSpace(rule.ProcessName) ? "aida64" : rule.ProcessName.Trim();
        sb.AppendLine($"Okna procesu pasującego do „{processFilter}”:");

        var windows = WindowQuery.EnumerateTopLevel(info => Wildcard.IsMatch(info.ProcessName, processFilter));
        if (windows.Count == 0)
        {
            sb.AppendLine("  (brak — czy AIDA64 jest uruchomiona?)");
        }

        foreach (var info in windows)
        {
            var monitor = _monitors.FromRect(info.Rect);
            var matches = WindowQuery.Matches(info, rule) ? " <== PASUJE DO REGUŁY" : string.Empty;
            sb.AppendLine(
                $"  hwnd=0x{info.Handle.ToInt64():X} vis={info.IsVisible} owner=0x{info.Owner.ToInt64():X} "
                + $"style=0x{info.Style:X8} ex=0x{info.ExStyle:X8} rect={info.Rect} "
                + $"monitor={monitor?.FriendlyName ?? "?"} klasa='{info.ClassName}' tytuł='{info.Title}'{matches}");
        }

        var report = sb.ToString();
        foreach (var line in report.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            _log.Info(line);
        }

        return report;
    }

    public void Dispose()
    {
        // Stop the sources first, so nothing can schedule work on a clock being torn down.
        _listener?.Dispose();
        _systemEvents?.Dispose();
        _watchdog?.Dispose();
        _clock?.Dispose();
        _started = false;
    }

    // ---------------------------------------------------------------- scheduling

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        Volatile.Write(ref _lastTickUtcTicks, now.Ticks);

        if (Interlocked.Exchange(ref _stallReported, 0) == 1)
        {
            _log.Info("Pętla główna znów reaguje.");
        }

        try
        {
            if (_pending.Count > 0 && now >= PendingDue())
            {
                ProcessPending();
            }

            if (now >= _sweepDueUtc)
            {
                _sweepDueUtc = DateTime.MaxValue;
                _layoutFirstUtc = DateTime.MaxValue;
                WindowQuery.InvalidateProcessCache();
                FullSweep("po zmianie układu ekranów", verbose: true);
            }

            if (now >= _recheckDueUtc)
            {
                _recheckDueUtc = DateTime.MaxValue;
                RunRechecks();
            }

            if (now >= _pollDueUtc)
            {
                _pollDueUtc = now + PollInterval;
                FullSweep("poll bezpieczeństwa", verbose: false);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Błąd w pętli głównej.", ex);
        }
        finally
        {
            // Whatever happened, the next deadline must stay armed: a lost tick must not be lost forever.
            ScheduleNext();
        }
    }

    private void ScheduleNext()
    {
        var next = _pollDueUtc;
        if (_pending.Count > 0)
        {
            next = Earlier(next, PendingDue());
        }

        next = Earlier(next, _sweepDueUtc);
        next = Earlier(next, _recheckDueUtc);
        _clock?.WakeAt(next);
    }

    private DateTime PendingDue() =>
        Debounce.DueAt(_pendingFirstUtc, _pendingLastUtc, TimeSpan.FromMilliseconds(_config.EventDebounceMs), MaxEventWait);

    private static DateTime Earlier(DateTime a, DateTime b) => a <= b ? a : b;

    /// <summary>
    /// Runs on a thread-pool thread, outside the UI message queue, so it keeps working when the
    /// loop itself is starved or stuck - the one situation the loop cannot report on its own.
    /// </summary>
    private void Watch(object? state)
    {
        try
        {
            var last = new DateTime(Volatile.Read(ref _lastTickUtcTicks), DateTimeKind.Utc);
            var silent = DateTime.UtcNow - last;
            var limit = TimeSpan.FromTicks(Math.Max(MinimumStall.Ticks, 3 * Volatile.Read(ref _pollIntervalTicks)));

            if (silent > limit && Interlocked.Exchange(ref _stallReported, 1) == 0)
            {
                _log.Warn($"Pętla główna nie odpowiada od {silent.TotalSeconds:0} s — zdarzenia okien "
                          + "i zmiany ekranów nie są w tym czasie obsługiwane.");
            }
        }
        catch (Exception)
        {
            // The watchdog must never take the process down.
        }
    }

    // ---------------------------------------------------------------- events

    private void OnLayoutEvent(string reason)
    {
        try
        {
            if (_paused)
            {
                _log.Info("Zdarzenie systemowe: " + reason + " — pilnowanie wstrzymane, pomijam.");
                return;
            }

            var now = DateTime.UtcNow;
            if (_layoutFirstUtc == DateTime.MaxValue)
            {
                _layoutFirstUtc = now;
            }

            // A burst of display changes is handled once it settles, but never deferred indefinitely.
            _sweepDueUtc = Debounce.DueAt(
                _layoutFirstUtc, now, TimeSpan.FromMilliseconds(_config.DisplayChangeDelayMs), MaxLayoutDefer);

            _log.Info($"Zdarzenie systemowe: {reason} — pełne wymuszenie o {_sweepDueUtc.ToLocalTime():HH:mm:ss.fff}.");
            _clock?.WakeAt(_sweepDueUtc);
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się zaplanować wymuszenia po zdarzeniu systemowym.", ex);
        }
    }

    /// <summary>
    /// Hook callback. Runs once per window event, possibly thousands of times a second, so it does
    /// nothing but record the handle and make sure a tick is armed.
    /// </summary>
    private void OnWindowEvent(uint eventType, IntPtr hwnd)
    {
        if (_paused)
        {
            return;
        }

        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && IsSelfMoved(hwnd))
        {
            return;
        }

        var now = DateTime.UtcNow;
        _pending.Add(hwnd);
        if (_pendingFirstUtc == DateTime.MaxValue)
        {
            _pendingFirstUtc = now;
        }

        _pendingLastUtc = now;
        _clock?.WakeAt(PendingDue());
    }

    private void ClearPending()
    {
        _pending.Clear();
        _pendingFirstUtc = DateTime.MaxValue;
        _pendingLastUtc = DateTime.MaxValue;
    }

    private void ProcessPending()
    {
        if (_paused)
        {
            ClearPending();
            return;
        }

        var handles = _pending.ToArray();
        ClearPending();

        var panelTouched = false;

        foreach (var hwnd in handles)
        {
            // One bad window must not cost the rest of the batch - or the panel check after it.
            try
            {
                if (hwnd == _panelHwnd)
                {
                    panelTouched = true;
                    continue;
                }

                var info = WindowQuery.Get(hwnd);
                if (info is null)
                {
                    continue;
                }

                // Checked whatever the cached handle says: when AIDA64 recreates the panel, the cache
                // still holds the dead handle, and the new window would wait for the next poll.
                if (WindowQuery.Matches(info, _config.PanelRule))
                {
                    panelTouched = true;
                    continue;
                }

                HandleForeignWindow(hwnd);
            }
            catch (Exception ex)
            {
                _log.Error($"Błąd przy obsłudze okna 0x{hwnd.ToInt64():X}.", ex);
            }
        }

        if (panelTouched || _panelHwnd == IntPtr.Zero)
        {
            EnforcePanel();
        }
    }

    // ---------------------------------------------------------------- core

    private void FullSweep(string reason, bool verbose)
    {
        if (!_started)
        {
            return;
        }

        if (_paused)
        {
            UpdateStatus(ServiceState.Paused, "Wstrzymane przez użytkownika.");
            _log.Debug("Pominięto wymuszenie układu (" + reason + ") — pilnowanie wstrzymane.");

            // The pulse matters most here: a paused service is otherwise completely silent.
            Heartbeat();
            return;
        }

        try
        {
            _monitors.Refresh();
            _guard.Prune(IsWindow);
            PruneSelfMoved();

            if (verbose)
            {
                _log.Info("Pełne wymuszenie układu: " + reason);
            }
            else
            {
                _log.Debug("Pełne wymuszenie układu: " + reason);
            }

            EnforcePanel();
            RelocateForeignWindows();
            Heartbeat();
        }
        catch (Exception ex)
        {
            _log.Error("Błąd podczas wymuszania układu (" + reason + ").", ex);
        }
    }

    /// <summary>
    /// Periodic proof of life. Without it the log falls silent whenever nothing needs correcting,
    /// and an incident cannot be told apart from a stalled loop after the fact.
    /// </summary>
    private void Heartbeat()
    {
        var now = DateTime.UtcNow;
        if (now - _lastHeartbeat < HeartbeatInterval)
        {
            return;
        }

        _lastHeartbeat = now;

        var panel = _panelHwnd != IntPtr.Zero ? WindowQuery.Get(_panelHwnd) : null;
        var target = _monitors.FindById(_config.PanelMonitorId);

        _log.Info(
            $"Puls: monitory={_monitors.Monitors.Count} spójny={_monitors.LayoutIsConsistent} "
            + $"ekran docelowy={(target is null ? "brak" : target.Bounds.ToString())} "
            + $"panel={(panel is null ? "nie znaleziony" : panel.Rect.ToString())} "
            + $"stan={_status.ShortText} utracone wpisy logu={_log.DroppedLines}");
    }

    private void EnforcePanel()
    {
        var panel = ResolvePanel();
        if (panel is null)
        {
            _panelHwnd = IntPtr.Zero;
            _log.Throttled("panel-missing", TimeSpan.FromMinutes(5), LogLevel.Info,
                "Nie znaleziono okna SensorPanel (reguła: " + _config.PanelRule + "). Czekam.");
            UpdateStatus(ServiceState.WaitingForPanel, "Nie znaleziono okna SensorPanel.");
            return;
        }

        _log.ResetThrottle("panel-missing");

        var target = _monitors.FindById(_config.PanelMonitorId);
        if (target is null)
        {
            // Never park the panel on a substitute screen; wait for the real one to come back.
            _log.Throttled("monitor-missing", TimeSpan.FromMinutes(5), LogLevel.Warn,
                "Docelowy monitor panelu jest niedostępny — nie przenoszę panelu. Id: "
                + (string.IsNullOrEmpty(_config.PanelMonitorId) ? "(nie wybrano)" : _config.PanelMonitorId));

            // So the first blocked correction after the monitor returns is reported immediately.
            _log.ResetThrottle("panel-blocked");
            UpdateStatus(ServiceState.WaitingForMonitor,
                string.IsNullOrEmpty(_config.PanelMonitorId)
                    ? "Nie wybrano ekranu dla SensorPanela."
                    : "Docelowy ekran jest niedostępny.");
            return;
        }

        _log.ResetThrottle("monitor-missing");

        if (!_config.PanelGeometry.IsSet)
        {
            var current = _monitors.FromRect(panel.Rect);
            if (current is not null && current.StableId == target.StableId)
            {
                _config.PanelGeometry = new PanelGeometry
                {
                    OffsetX = panel.Rect.X - target.Bounds.X,
                    OffsetY = panel.Rect.Y - target.Bounds.Y,
                    Width = panel.Rect.Width,
                    Height = panel.Rect.Height,
                };
                _store.Save(_config);
                _log.Info("Zapamiętano bieżącą pozycję panelu jako docelową.");
            }
        }

        if (!_monitors.LayoutIsConsistent)
        {
            // Windows is mid-transition and the coordinates cannot be trusted; the next sweep
            // will act on a settled layout.
            _log.Throttled("layout-unstable", TimeSpan.FromMinutes(1), LogLevel.Warn,
                "Układ ekranów jest w trakcie zmiany (monitory nachodzą na siebie) — "
                + "nie ruszam panelu do czasu ustabilizowania.");
            UpdateStatus(ServiceState.WaitingForMonitor, "Układ ekranów się zmienia.");
            return;
        }

        _log.ResetThrottle("layout-unstable");

        ApplyPanelStyles(panel);

        var desired = ComputePanelTarget(panel, target);

        // Hard invariant: the panel must never be sent anywhere but its own monitor. A powered-off
        // display can still be enumerated while reporting bogus bounds at the origin, and without
        // this check the stored offset lands the panel on the main screen - on top of whatever is
        // running there.
        var landing = _monitors.FromRect(desired);
        if (landing is null || landing.StableId != target.StableId)
        {
            _log.Throttled("bad-destination", TimeSpan.FromMinutes(1), LogLevel.Warn,
                $"Odrzucam przeniesienie panelu do {desired}: to miejsce leży na "
                + $"„{landing?.FriendlyName ?? "nieznanym ekranie"}”, a nie na „{target.FriendlyName}” "
                + $"({target.Bounds}). Prawdopodobnie Windows raportuje monitor w trakcie zmiany.");
            UpdateStatus(ServiceState.WaitingForMonitor, "Współrzędne ekranu docelowego są niewiarygodne.");
            return;
        }

        _log.ResetThrottle("bad-destination");
        var needsMove = Math.Abs(panel.Rect.X - desired.X) > PanelPositionTolerance
                        || Math.Abs(panel.Rect.Y - desired.Y) > PanelPositionTolerance
                        || (_config.RestorePanelSize
                            && (Math.Abs(panel.Rect.Width - desired.Width) > PanelPositionTolerance
                                || Math.Abs(panel.Rect.Height - desired.Height) > PanelPositionTolerance));

        if (needsMove)
        {
            if (DateTime.UtcNow < _panelBackoffUntil)
            {
                UpdateStatus(ServiceState.Active, "Panel wraca sam — chwilowa przerwa w korygowaniu.");
                return;
            }

            MarkSelfMoved(panel.Handle);
            var outcome = _mover.PlaceAt(
                panel.Handle, desired, _config.RestorePanelSize, _config.KeepPanelTopmost, changeZOrder: false);

            switch (outcome)
            {
                case MoveOutcome.Success:
                    SetPanelAccessDenied(false);
                    _log.Info($"Przywrócono SensorPanel na „{target.FriendlyName}” do {desired}.");
                    RecordPanelCorrection();
                    break;

                case MoveOutcome.AccessDenied:
                    SetPanelAccessDenied(true);

                    // Without this the log goes silent exactly when the user is watching: after a
                    // hot-plug the panel visibly stays put and nothing explains why.
                    _log.Throttled("panel-blocked", TimeSpan.FromMinutes(1), LogLevel.Warn,
                        $"Panel powinien wrócić na „{target.FriendlyName}” do {desired}, ale Windows "
                        + $"odmawia (obecnie {panel.Rect}). Uruchom SensorPanelPin jako administrator.");
                    break;
            }
        }

        if (_panelAccessDenied)
        {
            UpdateStatus(ServiceState.ElevationRequired,
                "AIDA64 działa jako administrator — uruchom SensorPanelPin jako administrator.");
            return;
        }

        UpdateStatus(ServiceState.Active, $"SensorPanel pilnowany na „{target.FriendlyName}”.");
    }

    private Rectangle ComputePanelTarget(WindowInfo panel, MonitorInfo target)
    {
        if (_config.KeepPanelPosition && _config.PanelGeometry.IsSet)
        {
            var width = _config.RestorePanelSize ? _config.PanelGeometry.Width : panel.Rect.Width;
            var height = _config.RestorePanelSize ? _config.PanelGeometry.Height : panel.Rect.Height;
            return new Rectangle(
                target.Bounds.X + _config.PanelGeometry.OffsetX,
                target.Bounds.Y + _config.PanelGeometry.OffsetY,
                width,
                height);
        }

        // Position not pinned: only guarantee the panel sits on the correct monitor.
        var source = _monitors.FromRect(panel.Rect);
        if (source is null || source.StableId == target.StableId)
        {
            return panel.Rect;
        }

        return new Rectangle(
            target.Bounds.X + (panel.Rect.X - source.Bounds.X),
            target.Bounds.Y + (panel.Rect.Y - source.Bounds.Y),
            panel.Rect.Width,
            panel.Rect.Height);
    }

    private void ApplyPanelStyles(WindowInfo panel)
    {
        if (_config.KeepPanelTopmost && (panel.ExStyle & WS_EX_TOPMOST) == 0)
        {
            TrySetPanelTopmost(panel);
        }

        if (_config.HidePanelFromAltTab && (panel.ExStyle & WS_EX_TOOLWINDOW) == 0)
        {
            TryHidePanelFromAltTab(panel);
        }
    }

    /// <summary>
    /// Sets the topmost flag and checks whether it actually stuck. AIDA64 owns this window and can
    /// clear the flag straight away; without this check the service re-applied it on every sweep
    /// forever, filling the log and achieving nothing.
    /// </summary>
    private void TrySetPanelTopmost(WindowInfo panel)
    {
        if (_topmostGivenUp || DateTime.UtcNow - _lastTopmostAttempt < StyleRetryInterval)
        {
            return;
        }

        _lastTopmostAttempt = DateTime.UtcNow;
        MarkSelfMoved(panel.Handle);

        if (!SetWindowPos(panel.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
        {
            _log.Warn($"Nie udało się ustawić panelu na wierzchu (błąd {Marshal.GetLastWin32Error()}).");
        }

        var applied = (GetWindowLongPtr(panel.Handle, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
        if (applied)
        {
            _topmostFailures = 0;
            _topmostReapplications++;
            _log.Debug("Ustawiono SensorPanel jako zawsze na wierzchu.");

            // The call works, yet the flag is gone again by the next sweep: AIDA64 owns this window
            // and keeps clearing it. Say so once instead of quietly fighting forever.
            if (_topmostReapplications == TopmostReapplyWarnAfter)
            {
                _log.Warn(
                    $"Flaga „zawsze na wierzchu” była ustawiana {_topmostReapplications} razy i za "
                    + "każdym razem znika — AIDA64 sama zarządza kolejnością okna. Włącz tę opcję "
                    + "w ustawieniach SensorPanel w AIDA64; tutaj daje tylko chwilowy efekt.");
            }

            return;
        }

        _topmostFailures++;
        if (_topmostFailures < StyleMaxFailures)
        {
            return;
        }

        _topmostGivenUp = true;
        _log.Warn(
            "AIDA64 natychmiast zdejmuje z panelu flagę „zawsze na wierzchu”, więc przestaję ją "
            + $"ustawiać (nieudanych prób: {_topmostFailures}). Włącz tę opcję w samej AIDA64 "
            + "(ustawienia SensorPanel), albo odznacz ją w SensorPanelPin.");
    }

    /// <summary>
    /// Same verify-and-give-up discipline as the topmost flag. This call is refused outright when
    /// AIDA64 runs elevated and this process does not, and it used to log a success it never had,
    /// every five seconds, forever.
    /// </summary>
    private void TryHidePanelFromAltTab(WindowInfo panel)
    {
        if (_altTabGivenUp || DateTime.UtcNow - _lastAltTabAttempt < StyleRetryInterval)
        {
            return;
        }

        _lastAltTabAttempt = DateTime.UtcNow;

        var updated = (panel.ExStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(panel.Handle, GWL_EXSTYLE, new IntPtr(updated));
        var error = Marshal.GetLastWin32Error();

        var applied = (GetWindowLongPtr(panel.Handle, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;
        if (applied)
        {
            _altTabFailures = 0;
            _log.Debug("Ukryto SensorPanel z Alt+Tab.");
            return;
        }

        _altTabFailures++;
        if (_altTabFailures < StyleMaxFailures)
        {
            return;
        }

        _altTabGivenUp = true;
        _log.Warn($"Nie udało się ukryć panelu z Alt+Tab (błąd {error}, prób: {_altTabFailures}) — "
                  + "przestaję próbować. Przy AIDA64 działającej jako administrator wymaga to uruchomienia "
                  + "SensorPanelPin jako administrator.");
    }

    private void ResetStyleState()
    {
        _topmostGivenUp = false;
        _topmostFailures = 0;
        _lastTopmostAttempt = DateTime.MinValue;
        _altTabGivenUp = false;
        _altTabFailures = 0;
        _lastAltTabAttempt = DateTime.MinValue;
    }

    private void RelocateForeignWindows()
    {
        if (!_config.RelocateForeignWindows)
        {
            return;
        }

        if (!_monitors.LayoutIsConsistent)
        {
            // Same reason as for the panel: moving windows by a delta between monitors whose
            // reported positions are mid-transition scatters them onto the wrong screens.
            return;
        }

        var panelMonitor = _monitors.FindById(_config.PanelMonitorId);
        if (panelMonitor is null)
        {
            return;
        }

        if (WindowFilter.IsScreenCaptured())
        {
            _log.Throttled("fullscreen", TimeSpan.FromMinutes(1), LogLevel.Debug,
                "Tryb pełnoekranowy/prezentacji — nie ruszam okien.");
            return;
        }

        foreach (var info in WindowQuery.EnumerateTopLevel(i => i.IsVisible && !i.IsMinimized))
        {
            TryRelocate(info, panelMonitor);
        }
    }

    private void HandleForeignWindow(IntPtr hwnd)
    {
        if (_paused || !_config.RelocateForeignWindows)
        {
            return;
        }

        var panelMonitor = _monitors.FindById(_config.PanelMonitorId);
        if (panelMonitor is null)
        {
            return;
        }

        if (!_monitors.LayoutIsConsistent)
        {
            return;
        }

        var info = WindowQuery.Get(hwnd);
        if (info is null)
        {
            return;
        }

        if (WindowFilter.IsScreenCaptured())
        {
            return;
        }

        TryRelocate(info, panelMonitor);
    }

    private void TryRelocate(WindowInfo info, MonitorInfo panelMonitor)
    {
        var current = _monitors.FromRect(info.Rect);
        if (current is null || current.StableId != panelMonitor.StableId)
        {
            return;
        }

        if (_guard.IsBlocked(info.Handle))
        {
            return;
        }

        var verdict = _filter.CanRelocate(info, _config, current);
        if (!verdict.Allowed)
        {
            _log.Debug($"Pomijam okno ({verdict.Reason}): {info.Describe()}");
            return;
        }

        var host = _monitors.ResolveHost(_config.HostMonitorId);
        if (host is null || host.StableId == panelMonitor.StableId)
        {
            _log.Throttled("host-invalid", TimeSpan.FromMinutes(5), LogLevel.Warn,
                "Ekran główny jest ten sam co ekran SensorPanela albo niedostępny — nie przenoszę okien.");
            return;
        }

        if (!_guard.RegisterMove(info))
        {
            return;
        }

        MarkSelfMoved(info.Handle);
        switch (_mover.MoveToMonitor(info, current, host))
        {
            case MoveOutcome.Success:
                // Log where the window ended up, not where it was: the old rect made a move that
                // never happened look like a success.
                var after = WindowQuery.Get(info.Handle);
                _log.Info($"Przeniesiono okno na „{host.FriendlyName}”: {info.Describe()} "
                          + $"— nowa pozycja {after?.Rect.ToString() ?? "?"}");
                QueueRecheck(info.Handle);
                break;

            case MoveOutcome.AccessDenied:
                _log.Throttled("foreign-access-denied", TimeSpan.FromMinutes(10), LogLevel.Warn,
                    "Brak uprawnień do przeniesienia okna procesu z wyższymi uprawnieniami: "
                    + info.Describe());
                break;
        }
    }

    // ---------------------------------------------------------------- helpers

    private WindowInfo? ResolvePanel()
    {
        if (_panelHwnd != IntPtr.Zero)
        {
            var cached = WindowQuery.Get(_panelHwnd);
            if (cached is not null && cached.IsVisible && WindowQuery.Matches(cached, _config.PanelRule))
            {
                return cached;
            }

            _panelHwnd = IntPtr.Zero;
        }

        var found = FindPanel(_config.PanelRule);
        if (found is not null)
        {
            _panelHwnd = found.Handle;
            _log.Info("Znaleziono SensorPanel: " + found.Describe());

            // A recreated panel window deserves a fresh attempt at the styles.
            ResetStyleState();
            ProbePanelAccess(found.Handle);
        }

        return found;
    }

    private static WindowInfo? FindPanel(WindowRule rule) =>
        WindowQuery
            .EnumerateTopLevel(info => info.IsVisible && WindowQuery.Matches(info, rule))
            .FirstOrDefault();

    /// <summary>
    /// Checks once per panel handle whether this process may reposition it. Done with a no-op
    /// SetWindowPos so the user learns about the elevation problem before anything needs moving.
    /// </summary>
    private void ProbePanelAccess(IntPtr hwnd)
    {
        if (_probedPanel == hwnd)
        {
            return;
        }

        _probedPanel = hwnd;
        var denied = _mover.ProbeAccess(hwnd) == MoveOutcome.AccessDenied;
        SetPanelAccessDenied(denied);
    }

    private void SetPanelAccessDenied(bool denied)
    {
        if (_panelAccessDenied == denied)
        {
            return;
        }

        _panelAccessDenied = denied;

        if (denied)
        {
            _log.Warn(
                "Windows nie pozwala tej aplikacji przesuwać okna SensorPanela — AIDA64 działa "
                + "z wyższymi uprawnieniami niż SensorPanelPin. Użyj „Uruchom ponownie jako "
                + "administrator” z menu ikony w zasobniku.");
        }
        else
        {
            _log.Info("Uzyskano dostęp do okna SensorPanela.");
        }
    }

    /// <summary>
    /// Guards against fighting AIDA64 itself. Only corrections that actually happened count: when
    /// every attempt is refused, a message about "corrections" being undone would be false.
    /// </summary>
    private void RecordPanelCorrection()
    {
        var now = DateTime.UtcNow;
        if (now - _panelCorrectionWindowStart > PanelBackoffWindow)
        {
            _panelCorrectionWindowStart = now;
            _panelCorrections = 0;
        }

        _panelCorrections++;
        if (_panelCorrections < PanelBackoffCorrections)
        {
            return;
        }

        _panelBackoffUntil = now + PanelBackoffDuration;
        _panelCorrections = 0;
        _log.Warn($"SensorPanel wraca na złą pozycję mimo {PanelBackoffCorrections} udanych korekt "
                  + $"w ciągu {PanelBackoffWindow.TotalSeconds:0} s — przerwa {PanelBackoffDuration.TotalSeconds:0} s.");
    }

    private void MarkSelfMoved(IntPtr hwnd)
    {
        _selfMoved[hwnd] = DateTime.UtcNow.AddMilliseconds(SelfMoveSuppressionMs);
    }

    private bool IsSelfMoved(IntPtr hwnd)
    {
        if (!_selfMoved.TryGetValue(hwnd, out var until))
        {
            return false;
        }

        if (DateTime.UtcNow <= until)
        {
            return true;
        }

        _selfMoved.Remove(hwnd);
        return false;
    }

    /// <summary>Entries are otherwise only removed when the same window moves again.</summary>
    private void PruneSelfMoved()
    {
        var now = DateTime.UtcNow;
        foreach (var handle in _selfMoved.Where(e => e.Value < now).Select(e => e.Key).ToList())
        {
            _selfMoved.Remove(handle);
        }
    }

    /// <summary>Re-checks a moved window once the destination monitor's DPI has settled.</summary>
    private void QueueRecheck(IntPtr hwnd)
    {
        if (!_recheckQueue.Contains(hwnd))
        {
            _recheckQueue.Add(hwnd);
        }

        _recheckDueUtc = Earlier(_recheckDueUtc, DateTime.UtcNow + PostMoveRecheck);
        _clock?.WakeAt(_recheckDueUtc);
    }

    private void RunRechecks()
    {
        var handles = _recheckQueue.ToArray();
        _recheckQueue.Clear();
        foreach (var handle in handles)
        {
            try
            {
                HandleForeignWindow(handle);
            }
            catch (Exception ex)
            {
                _log.Error($"Błąd przy ponownym sprawdzeniu okna 0x{handle.ToInt64():X}.", ex);
            }
        }
    }

    private void UpdateStatus(ServiceState state, string message)
    {
        if (_status.State == state && _status.Message == message)
        {
            return;
        }

        _status = new ServiceStatus(state, message);
        StatusChanged?.Invoke(this, _status);
    }
}
