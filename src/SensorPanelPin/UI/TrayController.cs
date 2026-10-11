using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using SensorPanelPin.Config;
using SensorPanelPin.Core;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Localization;
using Forms = System.Windows.Forms;

namespace SensorPanelPin.UI;

/// <summary>
/// The tray icon and its menu. The icon is WinForms' NotifyIcon (WPF has none); the menu is a WPF
/// ContextMenu, so it gets the Windows 11 look and follows the light/dark setting like the rest.
/// The menu keeps to the few things done from the tray; everything else lives in the window.
/// </summary>
public sealed class TrayController : IDisposable
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Logger _log;
    private readonly ConfigStore _store;
    private readonly PinService _service;
    private readonly Forms.NotifyIcon _tray;
    private readonly ContextMenu _menu;
    private readonly MenuItem _statusItem;
    private readonly MenuItem _pauseItem;
    private readonly Window _menuHost;

    private SettingsWindow? _settings;
    private bool _elevationNoticeShown;

    public TrayController(Logger log, ConfigStore store, AppConfig config)
    {
        _log = log;
        _store = store;
        _service = new PinService(log, store, config);
        _service.StatusChanged += (_, status) => UpdateTray(status);

        _statusItem = new MenuItem { IsEnabled = false };
        _pauseItem = new MenuItem();
        _pauseItem.Click += (_, _) => TogglePause();

        var open = new MenuItem { Header = Strings.TrayOpenSettings, FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => OpenSettings();
        var fix = new MenuItem { Header = Strings.TrayFixNow };
        fix.Click += (_, _) => _service.ForceNow();
        var exit = new MenuItem { Header = Strings.TrayExit };
        exit.Click += (_, _) => Exit();

        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(open);
        _menu.Items.Add(fix);
        _menu.Items.Add(_pauseItem);
        if (!Elevation.IsElevated())
        {
            var elevate = new MenuItem { Header = Strings.TrayRestartElevated };
            elevate.Click += (_, _) => RestartElevated();
            _menu.Items.Add(elevate);
        }

        _menu.Items.Add(new Separator());
        _menu.Items.Add(exit);

        // A context menu opened from the tray only closes on an outside click if its window owns the
        // foreground; this invisible host takes the foreground for it.
        _menuHost = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Width = 0,
            Height = 0,
            Left = -10000,
            Top = -10000,
        };
        _menu.Closed += (_, _) => _menuHost.Hide();

        _tray = new Forms.NotifyIcon { Icon = AppIcon.Tray, Text = "SensorPanelPin", Visible = true };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                ShowMenu();
            }
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenSettings();
            }
        };

        _service.Start();
        UpdateTray(_service.Status);

        // If the log refused to write through a planted link, the log itself cannot say so.
        if (_log.DisabledReason is not null)
        {
            _tray.ShowBalloonTip(15000, Strings.TrayLogDisabledTitle,
                Strings.TrayLogDisabledFormat(_log.DisabledReason), Forms.ToolTipIcon.Warning);
        }
    }

    public void OpenSettings()
    {
        if (_settings is not null)
        {
            if (_settings.WindowState == WindowState.Minimized)
            {
                _settings.WindowState = WindowState.Normal;
            }

            _settings.Activate();
            return;
        }

        var model = new SettingsViewModel(_service, _store, _log);
        model.IdentifyRequested += (_, _) => IdentifyWindow.ShowOnAll(_service.Monitors.Monitors);
        model.RestartElevatedRequested += (_, _) => RestartElevated();
        model.DiagnosticsRequested += (_, rule) =>
        {
            var report = _service.RunDiagnostics(rule);
            new DiagnosticsWindow(report) { Owner = _settings }.ShowDialog();
        };

        _settings = new SettingsWindow(model);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _service.Dispose();
        _menuHost.Close();
    }

    private void ShowMenu()
    {
        _menuHost.Show();
        SetForegroundWindow(new WindowInteropHelper(_menuHost).Handle);
        _menu.PlacementTarget = _menuHost;
        _menu.IsOpen = true;
    }

    private void TogglePause()
    {
        _service.SetPaused(!_service.IsPaused);
        UpdateTray(_service.Status);
    }

    private void RestartElevated()
    {
        if (Elevation.TryRestartElevated(_log, out var message))
        {
            Exit();
            return;
        }

        if (message.Length > 0)
        {
            _tray.ShowBalloonTip(8000, "SensorPanelPin", message, Forms.ToolTipIcon.Warning);
        }
    }

    private void Exit()
    {
        _log.Info("Closing at the user's request.");
        _settings?.Close();
        _tray.Visible = false;
        Application.Current.Shutdown();
    }

    private void UpdateTray(ServiceStatus status)
    {
        _statusItem.Header = status.ShortText + " — " + status.Message;
        _pauseItem.Header = _service.IsPaused ? Strings.TrayResume : Strings.TrayPause;
        _tray.Icon = _service.IsPaused ? AppIcon.TrayPaused : AppIcon.Tray;

        // NotifyIcon truncates the tooltip at 63 characters.
        var tooltip = "SensorPanelPin: " + status.ShortText;
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (status.State == ServiceState.ElevationRequired && !_elevationNoticeShown)
        {
            _elevationNoticeShown = true;
            _tray.ShowBalloonTip(
                10000,
                Strings.TrayElevationTitle,
                Strings.TrayElevationText,
                Forms.ToolTipIcon.Warning);
        }
    }
}
