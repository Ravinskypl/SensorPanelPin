using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SensorPanelPin.Config;
using SensorPanelPin.Core;
using SensorPanelPin.Diagnostics;

namespace SensorPanelPin.UI;

public sealed record MonitorChoice(string Id, string Label);

public sealed class ExceptionRow
{
    public ExceptionRow(WindowRule rule, ICommand remove)
    {
        Rule = rule;
        Remove = remove;
    }

    public WindowRule Rule { get; }

    public ICommand Remove { get; }

    public string Title => string.IsNullOrWhiteSpace(Rule.ProcessName) ? "Każdy program" : Rule.ProcessName;

    public string Detail => string.IsNullOrWhiteSpace(Rule.TitlePattern)
        ? "Wszystkie okna"
        : "Okna o tytule: " + Rule.TitlePattern;
}

/// <summary>
/// State of the settings window. Like Windows Settings there is no Save button: every change is
/// written to disk and applied to the running service the moment it is made. Text and numbers
/// apply when the field loses focus, so typing a pattern does not reconfigure the service per key.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PinService _service;
    private readonly ConfigStore _store;
    private readonly Logger _log;
    private AppConfig _config;

    private IReadOnlyList<MapMonitor> _mapMonitors = Array.Empty<MapMonitor>();
    private Rect? _panelActual;
    private Rect? _panelDesired;
    private MapPanelState _panelState;
    private string _statusMessage = string.Empty;
    private InfoBarSeverity _statusSeverity;
    private string? _statusActionLabel;
    private ICommand? _statusAction;
    private string _monitorSignature = string.Empty;
    private string _panelMessage = string.Empty;
    private InfoBarSeverity _panelMessageSeverity;
    private string _autoStartMessage = string.Empty;
    private InfoBarSeverity _autoStartMessageSeverity;
    private AutoStartMode _autoStartMode;
    private string _newExceptionProcess = string.Empty;
    private string _newExceptionTitle = string.Empty;
    private string _saveError = string.Empty;

    public SettingsViewModel(PinService service, ConfigStore store, Logger log)
    {
        _service = service;
        _store = store;
        _log = log;
        _config = service.Config;

        IdentifyCommand = new RelayCommand(() => IdentifyRequested?.Invoke(this, EventArgs.Empty));
        CapturePositionCommand = new RelayCommand(CapturePosition);
        DiagnosticsCommand = new RelayCommand(() => DiagnosticsRequested?.Invoke(this, _config.PanelRule));
        AddExceptionCommand = new RelayCommand(AddException,
            () => !string.IsNullOrWhiteSpace(NewExceptionProcess) || !string.IsNullOrWhiteSpace(NewExceptionTitle));
        ForgiveCommand = new RelayCommand(Forgive, () => Problems.Count > 0);
        OpenLogsCommand = new RelayCommand(() => OpenFolder(_log.LogDirectory));
        OpenConfigCommand = new RelayCommand(() => OpenFolder(_store.AppDataDirectory));
        RestartElevatedCommand = new RelayCommand(() => RestartElevatedRequested?.Invoke(this, EventArgs.Empty));
        ResumeCommand = new RelayCommand(() =>
        {
            _service.SetPaused(false);
            Refresh();
        });

        IsElevated = Elevation.IsElevated();
        var exe = Environment.ProcessPath ?? string.Empty;
        if (!ExecutableTrust.IsProtected(exe, out var reason))
        {
            LocationWarning = "Program działa z folderu, który mogą zmieniać zwykli użytkownicy, więc nie może "
                              + "startować z uprawnieniami administratora. Zainstaluj go skryptem "
                              + @"scripts\Install.ps1 (do C:\Program Files\SensorPanelPin) i włącz ten tryb "
                              + "z zainstalowanej kopii." + Environment.NewLine + reason;
        }

        _autoStartMode = AutoStart.GetCurrentMode();
        RebuildExceptions();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? IdentifyRequested;

    public event EventHandler<WindowRule>? DiagnosticsRequested;

    public event EventHandler? RestartElevatedRequested;

    // ------------------------------------------------------------------ commands

    public ICommand IdentifyCommand { get; }

    public ICommand CapturePositionCommand { get; }

    public ICommand DiagnosticsCommand { get; }

    public ICommand AddExceptionCommand { get; }

    public ICommand ForgiveCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public ICommand OpenConfigCommand { get; }

    public ICommand RestartElevatedCommand { get; }

    public ICommand ResumeCommand { get; }

    // ------------------------------------------------------------------ screens page

    public IReadOnlyList<MapMonitor> MapMonitors { get => _mapMonitors; private set => Set(ref _mapMonitors, value); }

    public Rect? PanelActual { get => _panelActual; private set => Set(ref _panelActual, value); }

    public Rect? PanelDesired { get => _panelDesired; private set => Set(ref _panelDesired, value); }

    public MapPanelState PanelState { get => _panelState; private set => Set(ref _panelState, value); }

    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    public InfoBarSeverity StatusSeverity { get => _statusSeverity; private set => Set(ref _statusSeverity, value); }

    public string? StatusActionLabel { get => _statusActionLabel; private set => Set(ref _statusActionLabel, value); }

    public ICommand? StatusAction { get => _statusAction; private set => Set(ref _statusAction, value); }

    public ObservableCollection<MonitorChoice> PanelScreenChoices { get; } = new();

    public ObservableCollection<MonitorChoice> HostScreenChoices { get; } = new();

    public string PanelMonitorId
    {
        get => _config.PanelMonitorId;
        set
        {
            if (value is null || value == _config.PanelMonitorId)
            {
                return;
            }

            if (value == _config.HostMonitorId)
            {
                // One screen cannot be both; the system's main screen takes over the host role.
                _config.HostMonitorId = string.Empty;
                OnPropertyChanged(nameof(HostMonitorId));
            }

            _config.PanelMonitorId = value;

            // The stored offset belonged to the previous screen; it is re-learned on the new one.
            _config.PanelGeometry = new PanelGeometry();
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(GeometryText));
            Refresh();
        }
    }

    public string HostMonitorId
    {
        get => _config.HostMonitorId;
        set
        {
            if (value is null || value == _config.HostMonitorId)
            {
                return;
            }

            _config.HostMonitorId = value;
            Commit();
            OnPropertyChanged();
            Refresh();
        }
    }

    public string SaveError { get => _saveError; private set => Set(ref _saveError, value); }

    // ------------------------------------------------------------------ panel page

    public bool KeepPanelPosition { get => _config.KeepPanelPosition; set => Change(v => _config.KeepPanelPosition = v, value, _config.KeepPanelPosition); }

    public bool RestorePanelSize { get => _config.RestorePanelSize; set => Change(v => _config.RestorePanelSize = v, value, _config.RestorePanelSize); }

    public bool KeepPanelTopmost { get => _config.KeepPanelTopmost; set => Change(v => _config.KeepPanelTopmost = v, value, _config.KeepPanelTopmost); }

    public bool HidePanelFromAltTab { get => _config.HidePanelFromAltTab; set => Change(v => _config.HidePanelFromAltTab = v, value, _config.HidePanelFromAltTab); }

    public string RuleProcess { get => _config.PanelRule.ProcessName; set => Change(v => _config.PanelRule.ProcessName = v.Trim(), value ?? string.Empty, _config.PanelRule.ProcessName); }

    public string RuleClass { get => _config.PanelRule.ClassPattern; set => Change(v => _config.PanelRule.ClassPattern = v.Trim(), value ?? string.Empty, _config.PanelRule.ClassPattern); }

    public string RuleTitle { get => _config.PanelRule.TitlePattern; set => Change(v => _config.PanelRule.TitlePattern = v.Trim(), value ?? string.Empty, _config.PanelRule.TitlePattern); }

    public string GeometryText => _config.PanelGeometry.IsSet
        ? $"{_config.PanelGeometry.OffsetX}, {_config.PanelGeometry.OffsetY} px od lewego górnego rogu ekranu, "
          + $"rozmiar {_config.PanelGeometry.Width} × {_config.PanelGeometry.Height}"
        : "Jeszcze nie zapamiętane — zapamięta się samo, gdy panel pojawi się na swoim ekranie";

    public string PanelMessage { get => _panelMessage; private set => Set(ref _panelMessage, value); }

    public InfoBarSeverity PanelMessageSeverity { get => _panelMessageSeverity; private set => Set(ref _panelMessageSeverity, value); }

    // ------------------------------------------------------------------ other windows page

    public bool RelocateForeignWindows { get => _config.RelocateForeignWindows; set => Change(v => _config.RelocateForeignWindows = v, value, _config.RelocateForeignWindows); }

    public ObservableCollection<ExceptionRow> Exceptions { get; } = new();

    public ObservableCollection<string> Problems { get; } = new();

    public string NewExceptionProcess { get => _newExceptionProcess; set => Set(ref _newExceptionProcess, value ?? string.Empty); }

    public string NewExceptionTitle { get => _newExceptionTitle; set => Set(ref _newExceptionTitle, value ?? string.Empty); }

    // ------------------------------------------------------------------ startup page

    public bool IsElevated { get; }

    public string? LocationWarning { get; }

    public bool AutoStartDisabled { get => _autoStartMode == AutoStartMode.Disabled; set => SelectAutoStart(value, AutoStartMode.Disabled); }

    public bool AutoStartRegistry { get => _autoStartMode == AutoStartMode.RegistryRun; set => SelectAutoStart(value, AutoStartMode.RegistryRun); }

    public bool AutoStartElevated { get => _autoStartMode == AutoStartMode.ScheduledTaskElevated; set => SelectAutoStart(value, AutoStartMode.ScheduledTaskElevated); }

    public string AutoStartMessage { get => _autoStartMessage; private set => Set(ref _autoStartMessage, value); }

    public InfoBarSeverity AutoStartMessageSeverity { get => _autoStartMessageSeverity; private set => Set(ref _autoStartMessageSeverity, value); }

    public bool StartPaused { get => _config.StartPaused; set => Change(v => _config.StartPaused = v, value, _config.StartPaused); }

    public bool VerboseLogging { get => _config.VerboseLogging; set => Change(v => _config.VerboseLogging = v, value, _config.VerboseLogging); }

    public int SafetyPollSeconds { get => _config.SafetyPollSeconds; set => Change(v => _config.SafetyPollSeconds = v, value, _config.SafetyPollSeconds); }

    public int EventDebounceMs { get => _config.EventDebounceMs; set => Change(v => _config.EventDebounceMs = v, value, _config.EventDebounceMs); }

    public int DisplayChangeDelayMs { get => _config.DisplayChangeDelayMs; set => Change(v => _config.DisplayChangeDelayMs = v, value, _config.DisplayChangeDelayMs); }

    public int PingPongLimit { get => _config.PingPongLimit; set => Change(v => _config.PingPongLimit = v, value, _config.PingPongLimit); }

    public int PingPongWindowSeconds { get => _config.PingPongWindowSeconds; set => Change(v => _config.PingPongWindowSeconds = v, value, _config.PingPongWindowSeconds); }

    public string LogFolder => _log.LogDirectory;

    public string Version => "Wersja " + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?");

    // ------------------------------------------------------------------ live state

    /// <summary>Re-reads the service. Called every second while the window is visible.</summary>
    public void Refresh()
    {
        var monitors = _service.Monitors.Monitors;

        var signature = string.Join("|", monitors.Select(m => m.StableId + m.Bounds)) + _config.PanelMonitorId + _config.HostMonitorId;
        if (signature != _monitorSignature)
        {
            _monitorSignature = signature;
            RebuildChoices(monitors);
        }

        MapMonitors = monitors
            .Select((m, i) => new MapMonitor(
                i + 1,
                m.FriendlyName,
                new Rect(m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height),
                m.StableId == _config.PanelMonitorId))
            .ToList();

        var snapshot = _service.GetPanelSnapshot();
        PanelActual = ToRect(snapshot.Actual);
        PanelDesired = ToRect(snapshot.Desired);
        PanelState = ClassifyPanel(snapshot);

        UpdateStatus(_service.Status);
        RefreshProblems();
    }

    private MapPanelState ClassifyPanel(PanelSnapshot snapshot)
    {
        if (snapshot.Actual is not { } actual)
        {
            return MapPanelState.Missing;
        }

        var actualScreen = _service.Monitors.FromRect(actual);
        return actualScreen is not null && actualScreen.StableId == snapshot.TargetMonitorId
            ? MapPanelState.InPlace
            : MapPanelState.Away;
    }

    private void UpdateStatus(ServiceStatus status)
    {
        var panelScreen = _service.Monitors.FindById(_config.PanelMonitorId);

        (string message, InfoBarSeverity severity, string? label, ICommand? action) = status.State switch
        {
            ServiceState.Active when PanelState == MapPanelState.InPlace =>
                ($"SensorPanel jest na ekranie {panelScreen?.FriendlyName}.", InfoBarSeverity.Success, null, null),
            ServiceState.Active =>
                ("SensorPanel wraca na swój ekran.", InfoBarSeverity.Informational, null, null),
            ServiceState.ElevationRequired =>
                ("AIDA64 działa jako administrator, więc Windows nie pozwala przesunąć panelu. "
                 + "Uruchom SensorPanelPin jako administrator.", InfoBarSeverity.Warning,
                    "Uruchom jako administrator", RestartElevatedCommand),
            ServiceState.WaitingForMonitor when string.IsNullOrEmpty(_config.PanelMonitorId) =>
                ("Wybierz poniżej, na którym ekranie ma być SensorPanel.", InfoBarSeverity.Informational, null, null),
            ServiceState.WaitingForMonitor =>
                (status.Message + " Panel zostaje tam, gdzie jest, dopóki ekran nie wróci.", InfoBarSeverity.Warning, null, null),
            ServiceState.WaitingForPanel =>
                ("Nie widzę okna SensorPanel. Włącz SensorPanel w AIDA64.", InfoBarSeverity.Informational, null, null),
            ServiceState.Paused =>
                ("Pilnowanie jest wstrzymane.", InfoBarSeverity.Informational, "Wznów", ResumeCommand),
            _ => ("Uruchamianie…", InfoBarSeverity.Informational, null, null),
        };

        StatusMessage = message;
        StatusSeverity = severity;
        StatusActionLabel = label;
        StatusAction = action;
    }

    private void RebuildChoices(IReadOnlyList<Monitors.MonitorInfo> monitors)
    {
        var panelId = _config.PanelMonitorId;
        var hostId = _config.HostMonitorId;

        PanelScreenChoices.Clear();
        HostScreenChoices.Clear();
        HostScreenChoices.Add(new MonitorChoice(string.Empty, "Główny ekran Windows"));

        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var label = $"{i + 1}. {m.FriendlyName} — {m.Bounds.Width} × {m.Bounds.Height}";
            PanelScreenChoices.Add(new MonitorChoice(m.StableId, label));
            HostScreenChoices.Add(new MonitorChoice(m.StableId, label));
        }

        // A remembered screen that is unplugged right now stays selectable, so the choice is not lost.
        if (!string.IsNullOrEmpty(panelId) && PanelScreenChoices.All(c => c.Id != panelId))
        {
            PanelScreenChoices.Add(new MonitorChoice(panelId, "Zapamiętany ekran (teraz niepodłączony)"));
        }

        if (!string.IsNullOrEmpty(hostId) && HostScreenChoices.All(c => c.Id != hostId))
        {
            HostScreenChoices.Add(new MonitorChoice(hostId, "Zapamiętany ekran (teraz niepodłączony)"));
        }

        OnPropertyChanged(nameof(PanelMonitorId));
        OnPropertyChanged(nameof(HostMonitorId));
    }

    private void RefreshProblems()
    {
        var current = _service.Guard.Problems.Select(p => p.Description).ToList();
        if (current.SequenceEqual(Problems))
        {
            return;
        }

        Problems.Clear();
        foreach (var problem in current)
        {
            Problems.Add(problem);
        }

        OnPropertyChanged(nameof(Problems));
    }

    // ------------------------------------------------------------------ actions

    private void CapturePosition()
    {
        if (_service.TryMeasurePanel(_config.PanelRule, out var monitor, out var geometry, out var message))
        {
            _config.PanelMonitorId = monitor!.StableId;
            _config.PanelGeometry = geometry;
            Commit();
            OnPropertyChanged(nameof(PanelMonitorId));
            OnPropertyChanged(nameof(GeometryText));
            PanelMessage = $"Zapamiętano miejsce panelu na ekranie {monitor.FriendlyName}.";
            PanelMessageSeverity = InfoBarSeverity.Success;
            Refresh();
        }
        else
        {
            PanelMessage = message;
            PanelMessageSeverity = InfoBarSeverity.Warning;
        }
    }

    private void AddException()
    {
        _config.Exceptions.Add(new WindowRule
        {
            ProcessName = NewExceptionProcess.Trim(),
            TitlePattern = NewExceptionTitle.Trim(),
        });
        NewExceptionProcess = string.Empty;
        NewExceptionTitle = string.Empty;
        Commit();
        RebuildExceptions();
    }

    private void RemoveException(WindowRule rule)
    {
        _config.Exceptions.Remove(rule);
        Commit();
        RebuildExceptions();
    }

    private void RebuildExceptions()
    {
        Exceptions.Clear();
        foreach (var rule in _config.Exceptions)
        {
            var captured = rule;
            Exceptions.Add(new ExceptionRow(rule, new RelayCommand(() => RemoveException(captured))));
        }
    }

    private void Forgive()
    {
        _service.ForgiveProblemWindows();
        RefreshProblems();
    }

    private void SelectAutoStart(bool selected, AutoStartMode mode)
    {
        if (!selected || mode == _autoStartMode)
        {
            return;
        }

        if (AutoStart.SetMode(mode, _log, out var error))
        {
            AutoStartMessage = string.Empty;
        }
        else
        {
            AutoStartMessage = error;
            AutoStartMessageSeverity = InfoBarSeverity.Warning;
        }

        // Show what is actually registered, not what was clicked.
        _autoStartMode = AutoStart.GetCurrentMode();
        OnPropertyChanged(nameof(AutoStartDisabled));
        OnPropertyChanged(nameof(AutoStartRegistry));
        OnPropertyChanged(nameof(AutoStartElevated));
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się otworzyć folderu: " + path, ex);
        }
    }

    // ------------------------------------------------------------------ plumbing

    private void Change<T>(Action<T> apply, T value, T current, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
        {
            return;
        }

        apply(value);
        Commit();

        // Normalize may have clamped the value; let the field show what was actually stored.
        OnPropertyChanged(name);
    }

    private void Commit()
    {
        _config.Normalize();
        SaveError = _store.Save(_config)
            ? string.Empty
            : "Nie udało się zapisać ustawień na dysku — działają do zamknięcia programu. Szczegóły w logu.";
        _service.ApplyConfig(_config);
    }

    private static Rect? ToRect(System.Drawing.Rectangle? r) =>
        r is { } v ? new Rect(v.X, v.Y, v.Width, v.Height) : null;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
