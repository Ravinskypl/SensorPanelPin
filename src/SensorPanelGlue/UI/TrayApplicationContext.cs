using System.Diagnostics;
using System.Windows.Forms;
using SensorPanelGlue.Config;
using SensorPanelGlue.Core;
using SensorPanelGlue.Diagnostics;

namespace SensorPanelGlue.UI;

/// <summary>Tray-only shell around <see cref="GlueService"/>.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Logger _log;
    private readonly ConfigStore _store;
    private readonly GlueService _service;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _elevateItem;

    private ConfigForm? _configForm;
    private bool _elevationBalloonShown;

    public TrayApplicationContext(Logger log, ConfigStore store, AppConfig config)
    {
        _log = log;
        _store = store;

        _service = new GlueService(log, store, config);
        _service.StatusChanged += (_, status) => UpdateTray(status);

        _statusItem = new ToolStripMenuItem("Uruchamianie...") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Wstrzymaj", null, (_, _) => TogglePause())
        {
            CheckOnClick = false,
        };
        _autoStartItem = new ToolStripMenuItem("Uruchamiaj z Windows");
        foreach (var mode in Enum.GetValues<AutoStartMode>())
        {
            var captured = mode;
            _autoStartItem.DropDownItems.Add(new ToolStripMenuItem(
                AutoStart.Describe(mode), null, (_, _) => ApplyAutoStartMode(captured)));
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Konfiguracja...", null, (_, _) => ShowConfig()));
        menu.Items.Add(new ToolStripMenuItem("Wymuś teraz", null, (_, _) => _service.ForceNow()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Tryb diagnostyczny", null, (_, _) => RunDiagnostics()));
        menu.Items.Add(_autoStartItem);
        _elevateItem = new ToolStripMenuItem("Uruchom ponownie jako administrator", null, (_, _) => RestartElevated())
        {
            Visible = !Elevation.IsElevated(),
        };
        menu.Items.Add(_elevateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Otwórz folder logów", null, (_, _) => OpenPath(_log.LogDirectory)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Zamknij", null, (_, _) => ExitApplication()));

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Normal,
            Text = "SensorPanelGlue",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowConfig();

        _service.Start();
        RefreshAutoStartMenu();
        UpdateTray(_service.Status);
    }

    private void TogglePause()
    {
        _service.SetPaused(!_service.IsPaused);
        UpdateTray(_service.Status);
    }

    private void ApplyAutoStartMode(AutoStartMode mode)
    {
        if (mode != AutoStart.GetCurrentMode()
            && !AutoStart.SetMode(mode, _log, out var error)
            && error.Length > 0)
        {
            MessageBox.Show(error, "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshAutoStartMenu();
    }

    /// <summary>Ticks the entry matching what is actually registered in the system, not what was clicked.</summary>
    private void RefreshAutoStartMenu()
    {
        var current = AutoStart.GetCurrentMode();
        for (var i = 0; i < _autoStartItem.DropDownItems.Count; i++)
        {
            if (_autoStartItem.DropDownItems[i] is ToolStripMenuItem item)
            {
                item.Checked = i == (int)current;
            }
        }

        _autoStartItem.Text = "Uruchamiaj z Windows: " + AutoStart.Describe(current);
    }

    private void ShowConfig()
    {
        if (_configForm is { IsDisposed: false })
        {
            _configForm.Activate();
            return;
        }

        _configForm = new ConfigForm(_service, _store, _log);
        _configForm.FormClosed += (_, _) =>
        {
            _configForm = null;
            RefreshAutoStartMenu();
            UpdateTray(_service.Status);
        };
        _configForm.Show();
    }

    private void RunDiagnostics()
    {
        var report = _service.RunDiagnostics();
        using var dialog = new Form
        {
            Text = "Diagnostyka okien",
            Size = new System.Drawing.Size(900, 520),
            StartPosition = FormStartPosition.CenterScreen,
            Icon = AppIcon.Normal,
        };

        dialog.Controls.Add(new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9f),
            Text = report,
        });

        dialog.ShowDialog();
    }

    private void RestartElevated()
    {
        if (Elevation.IsElevated())
        {
            MessageBox.Show("SensorPanelGlue już działa z podniesionymi uprawnieniami.",
                "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (Elevation.TryRestartElevated(_log, out var message))
        {
            _tray.Visible = false;
            ExitThread();
            return;
        }

        if (message.Length > 0)
        {
            MessageBox.Show(message, "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateTray(ServiceStatus status)
    {
        _pauseItem.Text = _service.IsPaused ? "Wznów" : "Wstrzymaj";
        _statusItem.Text = status.ShortText + " — " + status.Message;
        _tray.Icon = _service.IsPaused ? AppIcon.Paused : AppIcon.Normal;

        if (status.State == ServiceState.ElevationRequired && !_elevationBalloonShown)
        {
            _elevationBalloonShown = true;
            _elevateItem.Visible = true;
            _tray.ShowBalloonTip(
                10000,
                "SensorPanelGlue — brak uprawnień",
                "AIDA64 działa jako administrator, więc Windows nie pozwala przesuwać jej okna. "
                + "Wybierz z menu ikony „Uruchom ponownie jako administrator”.",
                ToolTipIcon.Warning);
        }

        // NotifyIcon truncates the tooltip at 63 characters.
        var tooltip = "SensorPanelGlue: " + status.ShortText;
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się otworzyć ścieżki: " + path, ex);
        }
    }

    private void ExitApplication()
    {
        _log.Info("Zamykanie na żądanie użytkownika.");
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _configForm?.Dispose();
            _service.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
