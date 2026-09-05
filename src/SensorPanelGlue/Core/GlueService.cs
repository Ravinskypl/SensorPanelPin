using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SensorPanelGlue.Config;
using SensorPanelGlue.Diagnostics;
using SensorPanelGlue.Monitors;
using SensorPanelGlue.Windows;
using static SensorPanelGlue.Interop.NativeMethods;
using Timer = System.Windows.Forms.Timer;

namespace SensorPanelGlue.Core;

/// <summary>
/// Owns the whole enforcement loop: WinEvent hooks feed a debounced queue, a low-frequency safety
/// poll acts as a net, and display/power/session events trigger a delayed full sweep.
/// </summary>
public sealed class GlueService : IDisposable
{
    private const int PanelPositionTolerance = 2;
    private const int SelfMoveSuppressionMs = 400;
    private const int PostMoveRecheckMs = 700;
    private const int PanelBackoffMoves = 12;
    private static readonly TimeSpan PanelBackoffDuration = TimeSpan.FromSeconds(30);

    private readonly Logger _log;
    private readonly ConfigStore _store;
    private readonly MonitorRegistry _monitors;
    private readonly WindowMover _mover;
    private readonly WindowFilter _filter;
    private readonly PingPongGuard _guard;

    private readonly HashSet<IntPtr> _pending = new();
    private readonly Dictionary<IntPtr, DateTime> _selfMoved = new();
    private readonly Timer _debounceTimer;
    private readonly Timer _safetyTimer;
    private readonly Timer _layoutDelayTimer;
    private readonly Timer _recheckTimer;
    private readonly List<IntPtr> _recheckQueue = new();

    private WinEventListener? _listener;
    private SystemEventWindow? _systemEvents;

    private AppConfig _config;
    private IntPtr _panelHwnd = IntPtr.Zero;
    private bool _paused;
    private bool _started;
    private bool _panelAccessDenied;
    private IntPtr _probedPanel = IntPtr.Zero;
    private int _panelMoveCount;
    private DateTime _panelMoveWindowStart = DateTime.UtcNow;
    private DateTime _panelBackoffUntil = DateTime.MinValue;
    private ServiceStatus _status = new(ServiceState.Stopped, "Nie uruchomiono.");

    public GlueService(Logger log, ConfigStore store, AppConfig config)
    {
        _log = log;
        _store = store;
        _config = config;

        _monitors = new MonitorRegistry(log);
        _mover = new WindowMover(log);
        _filter = new WindowFilter((uint)Environment.ProcessId);
        _guard = new PingPongGuard(log)
        {
            Limit = config.PingPongLimit,
            Window = TimeSpan.FromSeconds(config.PingPongWindowSeconds),
        };

        _debounceTimer = new Timer { Interval = config.EventDebounceMs };
        _debounceTimer.Tick += (_, _) => ProcessPending();

        _safetyTimer = new Timer { Interval = config.SafetyPollSeconds * 1000 };
        _safetyTimer.Tick += (_, _) => FullSweep("poll bezpieczeństwa", verbose: false);

        _layoutDelayTimer = new Timer { Interval = Math.Max(1, config.DisplayChangeDelayMs) };
        _layoutDelayTimer.Tick += (_, _) =>
        {
            _layoutDelayTimer.Stop();
            WindowQuery.InvalidateProcessCache();
            FullSweep("po zmianie układu ekranów", verbose: true);
        };

        _recheckTimer = new Timer { Interval = PostMoveRecheckMs };
        _recheckTimer.Tick += (_, _) =>
        {
            _recheckTimer.Stop();
            var handles = _recheckQueue.ToArray();
            _recheckQueue.Clear();
            foreach (var handle in handles)
            {
                HandleForeignWindow(handle);
            }
        };
    }

    public event EventHandler<ServiceStatus>? StatusChanged;

    public AppConfig Config => _config;

    public MonitorRegistry Monitors => _monitors;

    public PingPongGuard Guard => _guard;

    public ServiceStatus Status => _status;

    public bool IsPaused => _paused;

    /// <summary>
    /// True when Windows refuses to let this process reposition the SensorPanel, which happens when
    /// AIDA64 runs elevated and SensorPanelGlue does not.
    /// </summary>
    public bool PanelAccessDenied => _panelAccessDenied;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _paused = _config.StartPaused;

        _monitors.Refresh();
        LogMonitors();

        _systemEvents = new SystemEventWindow(_log, OnLayoutEvent);
        _listener = new WinEventListener(_log, OnWindowEvent);
        _listener.Start();

        _safetyTimer.Start();
        FullSweep("start aplikacji", verbose: true);

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
        _panelMoveCount = 0;
        FullSweep("wymuszenie ręczne", verbose: true);
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        _log.MinimumLevel = config.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        _guard.Limit = config.PingPongLimit;
        _guard.Window = TimeSpan.FromSeconds(config.PingPongWindowSeconds);

        _debounceTimer.Interval = config.EventDebounceMs;
        _safetyTimer.Interval = config.SafetyPollSeconds * 1000;
        _layoutDelayTimer.Interval = Math.Max(1, config.DisplayChangeDelayMs);

        _panelHwnd = IntPtr.Zero;
        _probedPanel = IntPtr.Zero;
        _log.ResetThrottle("panel-missing");
        _log.ResetThrottle("monitor-missing");

        FullSweep("zmiana konfiguracji", verbose: true);
    }

    /// <summary>
    /// Stores the panel's current position as the target, expressed relative to its monitor.
    /// </summary>
    public bool CaptureCurrentPanelGeometry(out string message)
    {
        _monitors.Refresh();
        var panel = ResolvePanel();
        if (panel is null)
        {
            message = "Nie znaleziono okna SensorPanel. Uruchom AIDA64 i włącz SensorPanel.";
            return false;
        }

        var monitor = _monitors.FromRect(panel.Rect);
        if (monitor is null)
        {
            message = "Nie udało się ustalić monitora, na którym znajduje się panel.";
            return false;
        }

        _config.PanelMonitorId = monitor.StableId;
        _config.PanelGeometry = new PanelGeometry
        {
            OffsetX = panel.Rect.X - monitor.Bounds.X,
            OffsetY = panel.Rect.Y - monitor.Bounds.Y,
            Width = panel.Rect.Width,
            Height = panel.Rect.Height,
        };

        _store.Save(_config);
        _log.Info($"Zapisano pozycję panelu: {_config.PanelGeometry.OffsetX},{_config.PanelGeometry.OffsetY} "
                  + $"({_config.PanelGeometry.Width}x{_config.PanelGeometry.Height}) na {monitor.FriendlyName}.");

        message = $"Zapisano pozycję panelu na ekranie „{monitor.FriendlyName}”.";
        return true;
    }

    /// <summary>Dumps every top-level window of the AIDA64 process to the log.</summary>
    public string RunDiagnostics()
    {
        _monitors.Refresh();
        var sb = new StringBuilder();
        sb.AppendLine("=== Diagnostyka SensorPanelGlue ===");

        sb.AppendLine("Monitory:");
        foreach (var monitor in _monitors.Monitors)
        {
            sb.AppendLine($"  {monitor.FriendlyName} | GDI={monitor.GdiDeviceName} | DPI={monitor.Dpi} "
                          + $"| bounds={monitor.Bounds} | work={monitor.WorkArea} "
                          + $"| primary={monitor.IsPrimary} | id={monitor.StableId}");
        }

        var processFilter = string.IsNullOrWhiteSpace(_config.PanelRule.ProcessName)
            ? "aida64"
            : _config.PanelRule.ProcessName.Trim();

        sb.AppendLine($"Okna procesu pasującego do „{processFilter}”:");

        var windows = WindowQuery.EnumerateTopLevel(info => Wildcard.IsMatch(info.ProcessName, processFilter));
        if (windows.Count == 0)
        {
            sb.AppendLine("  (brak — czy AIDA64 jest uruchomiona?)");
        }

        foreach (var info in windows)
        {
            var monitor = _monitors.FromRect(info.Rect);
            var matches = WindowQuery.Matches(info, _config.PanelRule) ? " <== PASUJE DO REGUŁY" : string.Empty;
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
        _debounceTimer.Stop();
        _safetyTimer.Stop();
        _layoutDelayTimer.Stop();
        _recheckTimer.Stop();

        _debounceTimer.Dispose();
        _safetyTimer.Dispose();
        _layoutDelayTimer.Dispose();
        _recheckTimer.Dispose();

        _listener?.Dispose();
        _systemEvents?.Dispose();
        _started = false;
    }

    // ---------------------------------------------------------------- events

    private void OnLayoutEvent(string reason)
    {
        _log.Info("Zdarzenie systemowe: " + reason + " — zaplanowano pełne wymuszenie.");
        _layoutDelayTimer.Stop();
        _layoutDelayTimer.Start();
    }

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

        _pending.Add(hwnd);

        // Restarting the timer coalesces bursts (a drag produces hundreds of events).
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ProcessPending()
    {
        _debounceTimer.Stop();

        if (_paused)
        {
            _pending.Clear();
            return;
        }

        var handles = _pending.ToArray();
        _pending.Clear();

        var panelTouched = false;

        foreach (var hwnd in handles)
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

            if (_panelHwnd == IntPtr.Zero && WindowQuery.Matches(info, _config.PanelRule))
            {
                panelTouched = true;
                continue;
            }

            HandleForeignWindow(hwnd);
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
            return;
        }

        try
        {
            _monitors.Refresh();
            _guard.Prune(IsWindow);

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
        }
        catch (Exception ex)
        {
            _log.Error("Błąd podczas wymuszania układu (" + reason + ").", ex);
        }
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

        ApplyPanelStyles(panel);

        var desired = ComputePanelTarget(panel, target);
        var needsMove = Math.Abs(panel.Rect.X - desired.X) > PanelPositionTolerance
                        || Math.Abs(panel.Rect.Y - desired.Y) > PanelPositionTolerance
                        || (_config.RestorePanelSize
                            && (Math.Abs(panel.Rect.Width - desired.Width) > PanelPositionTolerance
                                || Math.Abs(panel.Rect.Height - desired.Height) > PanelPositionTolerance));

        if (needsMove)
        {
            if (IsPanelBackedOff())
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
                    break;

                case MoveOutcome.AccessDenied:
                    SetPanelAccessDenied(true);

                    // Without this the log goes silent exactly when the user is watching: after a
                    // hot-plug the panel visibly stays put and nothing explains why.
                    _log.Throttled("panel-blocked", TimeSpan.FromMinutes(1), LogLevel.Warn,
                        $"Panel powinien wrócić na „{target.FriendlyName}” do {desired}, ale Windows "
                        + $"odmawia (obecnie {panel.Rect}). Uruchom SensorPanelGlue jako administrator.");
                    break;
            }
        }

        if (_panelAccessDenied)
        {
            UpdateStatus(ServiceState.ElevationRequired,
                "AIDA64 działa jako administrator — uruchom SensorPanelGlue jako administrator.");
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
            MarkSelfMoved(panel.Handle);
            SetWindowPos(panel.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _log.Debug("Ustawiono SensorPanel jako zawsze na wierzchu.");
        }

        if (_config.HidePanelFromAltTab && (panel.ExStyle & WS_EX_TOOLWINDOW) == 0)
        {
            var updated = (panel.ExStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            SetWindowLongPtr(panel.Handle, GWL_EXSTYLE, new IntPtr(updated));
            _log.Debug("Ukryto SensorPanel z Alt+Tab.");
        }
    }

    private void RelocateForeignWindows()
    {
        if (!_config.RelocateForeignWindows)
        {
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
                _log.Info($"Przeniesiono okno na „{host.FriendlyName}”: {info.Describe()}");
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

        var found = WindowQuery
            .EnumerateTopLevel(info => info.IsVisible && WindowQuery.Matches(info, _config.PanelRule))
            .FirstOrDefault();

        if (found is not null)
        {
            _panelHwnd = found.Handle;
            _log.Info("Znaleziono SensorPanel: " + found.Describe());
            ProbePanelAccess(found.Handle);
        }

        return found;
    }

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
                + "z wyższymi uprawnieniami niż SensorPanelGlue. Użyj „Uruchom ponownie jako "
                + "administrator” z menu ikony w zasobniku.");
        }
        else
        {
            _log.Info("Uzyskano dostęp do okna SensorPanela.");
        }
    }

    /// <summary>
    /// Guards against fighting AIDA64 itself: after too many corrections in a row, stand down
    /// briefly instead of looping.
    /// </summary>
    private bool IsPanelBackedOff()
    {
        var now = DateTime.UtcNow;
        if (now < _panelBackoffUntil)
        {
            return true;
        }

        if (now - _panelMoveWindowStart > TimeSpan.FromSeconds(10))
        {
            _panelMoveWindowStart = now;
            _panelMoveCount = 0;
        }

        _panelMoveCount++;
        if (_panelMoveCount <= PanelBackoffMoves)
        {
            return false;
        }

        _panelBackoffUntil = now + PanelBackoffDuration;
        _panelMoveCount = 0;
        _log.Warn($"SensorPanel wraca na złą pozycję mimo {PanelBackoffMoves} korekt — "
                  + $"przerwa {PanelBackoffDuration.TotalSeconds:0} s.");
        return true;
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

    /// <summary>Re-checks a moved window once the destination monitor's DPI has settled.</summary>
    private void QueueRecheck(IntPtr hwnd)
    {
        if (!_recheckQueue.Contains(hwnd))
        {
            _recheckQueue.Add(hwnd);
        }

        _recheckTimer.Stop();
        _recheckTimer.Start();
    }

    private void LogMonitors()
    {
        _log.Info($"Wykryto {_monitors.Monitors.Count} monitorów:");
        foreach (var monitor in _monitors.Monitors)
        {
            _log.Info("  " + monitor);
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
