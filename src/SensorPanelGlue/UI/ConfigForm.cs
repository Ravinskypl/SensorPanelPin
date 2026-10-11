using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using SensorPanelGlue.Config;
using SensorPanelGlue.Core;
using SensorPanelGlue.Diagnostics;
using SensorPanelGlue.Monitors;

namespace SensorPanelGlue.UI;

/// <summary>Configuration window. Built in code; no designer files, no resx.</summary>
public sealed class ConfigForm : Form
{
    private readonly GlueService _service;
    private readonly ConfigStore _store;
    private readonly Logger _log;
    private readonly AppConfig _draft;

    private readonly ListView _monitorList = new();
    private readonly Label _panelMonitorLabel = new();
    private readonly Label _hostMonitorLabel = new();

    private readonly TextBox _ruleProcess = new();
    private readonly TextBox _ruleClass = new();
    private readonly TextBox _ruleTitle = new();
    private readonly Label _geometryLabel = new();

    private readonly CheckBox _keepPosition = new();
    private readonly CheckBox _restoreSize = new();
    private readonly CheckBox _topmost = new();
    private readonly CheckBox _hideAltTab = new();

    private readonly CheckBox _relocateForeign = new();
    private readonly ListBox _exceptionList = new();
    private readonly TextBox _exceptionProcess = new();
    private readonly TextBox _exceptionTitle = new();
    private readonly ListBox _problemList = new();

    private readonly NumericUpDown _safetyPoll = new();
    private readonly NumericUpDown _debounce = new();
    private readonly NumericUpDown _displayDelay = new();
    private readonly NumericUpDown _pingPongLimit = new();
    private readonly NumericUpDown _pingPongWindow = new();
    private readonly CheckBox _verbose = new();
    private readonly CheckBox _startPaused = new();
    private readonly ComboBox _autoStartMode = new();

    public ConfigForm(GlueService service, ConfigStore store, Logger log)
    {
        _service = service;
        _store = store;
        _log = log;
        _draft = service.Config.Clone();

        Text = "SensorPanelGlue — konfiguracja";
        Icon = AppIcon.Normal;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 620);
        Size = new Size(820, 660);
        AutoScaleMode = AutoScaleMode.Font;
        Font = UiFonts.Body;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildMonitorsTab());
        tabs.TabPages.Add(BuildPanelTab());
        tabs.TabPages.Add(BuildForeignTab());
        tabs.TabPages.Add(BuildAdvancedTab());

        Controls.Add(tabs);
        Controls.Add(BuildButtonBar());
        Controls.Add(BuildElevationBanner());

        LoadDraftIntoControls();
        RefreshMonitors();
        RefreshProblems();
    }

    // ------------------------------------------------------------- tabs

    private TabPage BuildMonitorsTab()
    {
        var page = new TabPage("Ekrany") { Padding = new Padding(12) };

        _monitorList.View = View.Details;
        _monitorList.FullRowSelect = true;
        _monitorList.MultiSelect = false;
        _monitorList.HideSelection = false;
        _monitorList.Dock = DockStyle.Fill;
        _monitorList.Columns.Add("#", 40);
        _monitorList.Columns.Add("Nazwa", 220);
        _monitorList.Columns.Add("Rozdzielczość", 110);
        _monitorList.Columns.Add("Pozycja", 100);
        _monitorList.Columns.Add("DPI", 55);
        _monitorList.Columns.Add("Rola", 190);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
        };
        buttons.Controls.Add(MakeButton("Odśwież", (_, _) => RefreshMonitors(), 90));
        buttons.Controls.Add(MakeButton("Identyfikuj ekrany", (_, _) => IdentifyMonitors(), 140));
        buttons.Controls.Add(MakeButton("Ekran SensorPanela", (_, _) => AssignSelected(panel: true), 150));
        buttons.Controls.Add(MakeButton("Ekran główny", (_, _) => AssignSelected(panel: false), 120));

        var info = new Panel { Dock = DockStyle.Top, Height = 60 };
        _panelMonitorLabel.Location = new Point(0, 4);
        _panelMonitorLabel.AutoSize = true;
        _hostMonitorLabel.Location = new Point(0, 30);
        _hostMonitorLabel.AutoSize = true;
        info.Controls.Add(_panelMonitorLabel);
        info.Controls.Add(_hostMonitorLabel);

        page.Controls.Add(_monitorList);
        page.Controls.Add(buttons);
        page.Controls.Add(info);
        return page;
    }

    private TabPage BuildPanelTab()
    {
        var page = new TabPage("SensorPanel") { Padding = new Padding(12), AutoScroll = true };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(MakeHeader("Reguła rozpoznawania okna panelu (dozwolone * i ?)"), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        AddRow(layout, 1, "Nazwa procesu:", _ruleProcess);
        AddRow(layout, 2, "Klasa okna:", _ruleClass);
        AddRow(layout, 3, "Tytuł okna:", _ruleTitle);

        var diagButton = MakeButton("Diagnostyka okien AIDA64", (_, _) => ShowDiagnostics(), 200);
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 4);
        layout.Controls.Add(diagButton, 1, 4);

        layout.Controls.Add(MakeHeader("Zachowanie panelu"), 0, 5);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 5)!, 2);

        _keepPosition.Text = "Przywracaj zapisaną pozycję panelu";
        _restoreSize.Text = "Przywracaj także rozmiar panelu";
        _topmost.Text = "Trzymaj panel zawsze na wierzchu";
        _hideAltTab.Text = "Ukryj panel z Alt+Tab";

        AddCheckRow(layout, 6, _keepPosition);
        AddCheckRow(layout, 7, _restoreSize);
        AddCheckRow(layout, 8, _topmost);
        AddCheckRow(layout, 9, _hideAltTab);

        _geometryLabel.AutoSize = true;
        layout.Controls.Add(new Label { Text = "Zapisana pozycja:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 10);
        layout.Controls.Add(_geometryLabel, 1, 10);

        var captureButton = MakeButton("Zapisz bieżącą pozycję panelu jako docelową", (_, _) => CaptureGeometry(), 320);
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 11);
        layout.Controls.Add(captureButton, 1, 11);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildForeignTab()
    {
        var page = new TabPage("Inne okna") { Padding = new Padding(12) };

        _relocateForeign.Text = "Przenoś okna innych aplikacji z ekranu SensorPanela na ekran główny";
        _relocateForeign.AutoSize = true;
        _relocateForeign.Margin = new Padding(0, 4, 0, 4);

        var note = new Label
        {
            Text = "Windows nie pozwala zabronić aplikacji uruchamiania się na wybranym ekranie."
                   + Environment.NewLine
                   + "Aplikacja wykrywa okna, które się tam pojawiają, i je przenosi.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        };

        // Stacked explicitly: docking both to the top made them overlap.
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 4),
        };
        header.Controls.Add(_relocateForeign);
        header.Controls.Add(note);

        var exceptionsBox = new GroupBox
        {
            Text = "Wyjątki — te okna zostają na miejscu",
            Dock = DockStyle.Top,
            Height = 220,
            Padding = new Padding(10),
        };

        _exceptionList.Dock = DockStyle.Fill;
        _exceptionList.IntegralHeight = false;

        var exceptionEditor = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
        exceptionEditor.Controls.Add(new Label { Text = "Proces:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        _exceptionProcess.Width = 130;
        exceptionEditor.Controls.Add(_exceptionProcess);
        exceptionEditor.Controls.Add(new Label { Text = "Tytuł:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        _exceptionTitle.Width = 200;
        exceptionEditor.Controls.Add(_exceptionTitle);
        exceptionEditor.Controls.Add(MakeButton("Dodaj", (_, _) => AddException(), 70));
        exceptionEditor.Controls.Add(MakeButton("Usuń", (_, _) => RemoveException(), 70));

        exceptionsBox.Controls.Add(_exceptionList);
        exceptionsBox.Controls.Add(exceptionEditor);

        var problemsBox = new GroupBox
        {
            Text = "Okna, które wracały na ekran panelu — przeniesienie wstrzymane",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };
        _problemList.Dock = DockStyle.Fill;
        _problemList.IntegralHeight = false;
        var problemButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        problemButtons.Controls.Add(MakeButton("Odśwież", (_, _) => RefreshProblems(), 80));
        problemButtons.Controls.Add(MakeButton("Wyczyść", (_, _) =>
        {
            _service.Guard.Clear();
            RefreshProblems();
        }, 80));
        problemsBox.Controls.Add(_problemList);
        problemsBox.Controls.Add(problemButtons);

        page.Controls.Add(problemsBox);
        page.Controls.Add(exceptionsBox);
        page.Controls.Add(header);
        return page;
    }

    private TabPage BuildAdvancedTab()
    {
        var page = new TabPage("Zaawansowane") { Padding = new Padding(12), AutoScroll = true };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureNumeric(_safetyPoll, 1, 600);
        ConfigureNumeric(_debounce, 50, 5000);
        ConfigureNumeric(_displayDelay, 0, 60000);
        ConfigureNumeric(_pingPongLimit, 1, 50);
        ConfigureNumeric(_pingPongWindow, 1, 600);

        AddRow(layout, 0, "Poll bezpieczeństwa (s):", _safetyPoll);
        AddRow(layout, 1, "Opóźnienie reakcji na zdarzenia (ms):", _debounce);
        AddRow(layout, 2, "Opóźnienie po zmianie ekranów (ms):", _displayDelay);
        AddRow(layout, 3, "Limit przeniesień jednego okna:", _pingPongLimit);
        AddRow(layout, 4, "Okno czasowe limitu (s):", _pingPongWindow);

        _verbose.Text = "Szczegółowe logowanie (poziom Debug)";
        _startPaused.Text = "Startuj w trybie wstrzymanym";

        _autoStartMode.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var mode in Enum.GetValues<AutoStartMode>())
        {
            _autoStartMode.Items.Add(AutoStart.Describe(mode));
        }

        AddCheckRow(layout, 5, _verbose);
        AddCheckRow(layout, 6, _startPaused);
        AddRow(layout, 7, "Uruchamianie razem z Windows:", _autoStartMode);

        var autoStartNote = new Label
        {
            Text = "Wpis w rejestrze nie potrafi podnieść uprawnień. Jeśli AIDA64 działa jako "
                   + "administrator, tylko zadanie w Harmonogramie zadań sprawi, że po restarcie "
                   + "systemu panel będzie faktycznie pilnowany.",
            AutoSize = false,
            Height = 50,
            Width = 420,
            ForeColor = SystemColors.GrayText,
        };
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 8);
        layout.Controls.Add(autoStartNote, 1, 8);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
        buttons.Controls.Add(MakeButton("Otwórz folder logów", (_, _) => OpenPath(_log.LogDirectory), 150));
        buttons.Controls.Add(MakeButton("Otwórz plik konfiguracji", (_, _) => OpenPath(_store.ConfigPath), 170));

        page.Controls.Add(layout);
        page.Controls.Add(buttons);
        return page;
    }

    /// <summary>
    /// Shown only when Windows has actually refused a move; the application never demands elevation
    /// up front, it just tells the user what is blocking it.
    /// </summary>
    private Control BuildElevationBanner()
    {
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(255, 244, 206),
            Visible = _service.PanelAccessDenied,
            Padding = new Padding(10, 0, 10, 0),
        };

        var text = new Label
        {
            Text = "AIDA64 działa z wyższymi uprawnieniami — Windows blokuje przesuwanie jej okna. "
                   + "Uruchom SensorPanelGlue jako administrator.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(80, 60, 0),
        };

        var button = MakeButton("Uruchom jako administrator", (_, _) =>
        {
            if (Elevation.TryRestartElevated(_log, out var message))
            {
                Application.Exit();
            }
            else if (message.Length > 0)
            {
                MessageBox.Show(this, message, "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, 200);
        button.Dock = DockStyle.Right;

        banner.Controls.Add(text);
        banner.Controls.Add(button);
        return banner;
    }

    private Control BuildButtonBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 8, 10, 8),
        };

        var cancel = MakeButton("Anuluj", (_, _) => Close(), 100);
        var save = MakeButton("Zapisz", (_, _) => SaveAndClose(), 100);
        save.Font = UiFonts.Bold;

        bar.Controls.Add(cancel);
        bar.Controls.Add(save);
        CancelButton = cancel;
        AcceptButton = save;
        return bar;
    }

    // ------------------------------------------------------------- actions

    private void RefreshMonitors()
    {
        _service.Monitors.Refresh();
        _monitorList.BeginUpdate();
        _monitorList.Items.Clear();

        var index = 1;
        foreach (var monitor in _service.Monitors.Monitors)
        {
            var roles = new List<string>();
            if (monitor.StableId == _draft.PanelMonitorId)
            {
                roles.Add("SensorPanel");
            }

            if (monitor.StableId == _draft.HostMonitorId)
            {
                roles.Add("główny (docelowy)");
            }

            if (monitor.IsPrimary)
            {
                roles.Add("systemowy główny");
            }

            var item = new ListViewItem(index.ToString())
            {
                Tag = monitor,
            };
            item.SubItems.Add(monitor.FriendlyName);
            item.SubItems.Add($"{monitor.Bounds.Width}x{monitor.Bounds.Height}");
            item.SubItems.Add($"{monitor.Bounds.X},{monitor.Bounds.Y}");
            item.SubItems.Add(monitor.Dpi.ToString());
            item.SubItems.Add(string.Join(", ", roles));
            _monitorList.Items.Add(item);
            index++;
        }

        _monitorList.EndUpdate();
        UpdateAssignmentLabels();
    }

    private void UpdateAssignmentLabels()
    {
        var panel = _service.Monitors.FindById(_draft.PanelMonitorId);
        var host = _service.Monitors.FindById(_draft.HostMonitorId);

        _panelMonitorLabel.Text = "Ekran SensorPanela: " + (panel?.DisplayLabel
            ?? (string.IsNullOrEmpty(_draft.PanelMonitorId)
                ? "nie wybrano"
                : "niedostępny (zapisany, czeka na podłączenie)"));

        _hostMonitorLabel.Text = "Ekran dla pozostałych okien: " + (host?.DisplayLabel
            ?? (string.IsNullOrEmpty(_draft.HostMonitorId)
                ? "systemowy główny"
                : "niedostępny (zapisany, czeka na podłączenie)"));
    }

    private void IdentifyMonitors()
    {
        _service.Monitors.Refresh();
        MonitorIdentifyOverlay.Show(_service.Monitors.Monitors);
    }

    private void AssignSelected(bool panel)
    {
        if (_monitorList.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "Zaznacz najpierw ekran na liście.", "SensorPanelGlue",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_monitorList.SelectedItems[0].Tag is not MonitorInfo monitor)
        {
            return;
        }

        if (panel)
        {
            if (monitor.StableId == _draft.HostMonitorId)
            {
                _draft.HostMonitorId = string.Empty;
            }

            _draft.PanelMonitorId = monitor.StableId;

            // The stored offset belongs to the previous screen; drop it so it is re-learned.
            _draft.PanelGeometry = new PanelGeometry();
            UpdateGeometryLabel();
        }
        else
        {
            if (monitor.StableId == _draft.PanelMonitorId)
            {
                MessageBox.Show(this, "Ekran główny nie może być tym samym ekranem co SensorPanel.",
                    "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _draft.HostMonitorId = monitor.StableId;
        }

        RefreshMonitors();
    }

    private void CaptureGeometry()
    {
        // Measures with the rule currently typed in the form, but only into the draft: nothing
        // takes effect until Save, and Cancel discards it. This used to write into the running
        // configuration and save it, so an abandoned rule stayed live after Cancel.
        ReadControlsIntoDraft();

        if (_service.TryMeasurePanel(_draft.PanelRule, out var monitor, out var geometry, out var message))
        {
            _draft.PanelMonitorId = monitor!.StableId;
            _draft.PanelGeometry = geometry;
            UpdateGeometryLabel();
            RefreshMonitors();
            MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine
                                  + "Pozycja zostanie użyta po naciśnięciu „Zapisz”.",
                "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, message, "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowDiagnostics()
    {
        ReadControlsIntoDraft();

        var report = _service.RunDiagnostics(_draft.PanelRule);
        using var dialog = new Form
        {
            Text = "Diagnostyka okien",
            Size = new Size(900, 520),
            StartPosition = FormStartPosition.CenterParent,
            Icon = AppIcon.Normal,
        };

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = UiFonts.Mono,
            Text = report,
        };

        dialog.Controls.Add(box);
        dialog.ShowDialog(this);
    }

    private void AddException()
    {
        var process = _exceptionProcess.Text.Trim();
        var title = _exceptionTitle.Text.Trim();

        if (process.Length == 0 && title.Length == 0)
        {
            MessageBox.Show(this, "Podaj nazwę procesu lub wzorzec tytułu.", "SensorPanelGlue",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _draft.Exceptions.Add(new WindowRule { ProcessName = process, TitlePattern = title });
        _exceptionProcess.Clear();
        _exceptionTitle.Clear();
        RefreshExceptions();
    }

    private void RemoveException()
    {
        var index = _exceptionList.SelectedIndex;
        if (index < 0 || index >= _draft.Exceptions.Count)
        {
            return;
        }

        _draft.Exceptions.RemoveAt(index);
        RefreshExceptions();
    }

    private void RefreshExceptions()
    {
        _exceptionList.BeginUpdate();
        _exceptionList.Items.Clear();
        foreach (var rule in _draft.Exceptions)
        {
            _exceptionList.Items.Add(rule.ToString());
        }

        _exceptionList.EndUpdate();
    }

    private void RefreshProblems()
    {
        _problemList.BeginUpdate();
        _problemList.Items.Clear();
        foreach (var problem in _service.Guard.Problems)
        {
            _problemList.Items.Add($"{problem.BlockedAt:HH:mm:ss} — {problem.Description}");
        }

        if (_problemList.Items.Count == 0)
        {
            _problemList.Items.Add("(brak)");
        }

        _problemList.EndUpdate();
    }

    private void SaveAndClose()
    {
        ReadControlsIntoDraft();

        if (!string.IsNullOrEmpty(_draft.PanelMonitorId)
            && _draft.PanelMonitorId == _draft.HostMonitorId)
        {
            MessageBox.Show(this, "Ekran SensorPanela i ekran główny nie mogą być tym samym ekranem.",
                "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedMode = (AutoStartMode)Math.Max(0, _autoStartMode.SelectedIndex);
        if (selectedMode != AutoStart.GetCurrentMode()
            && !AutoStart.SetMode(selectedMode, _log, out var autoStartError))
        {
            MessageBox.Show(this,
                "Konfiguracja zostanie zapisana, ale nie udało się zmienić autostartu:"
                + Environment.NewLine + Environment.NewLine + autoStartError,
                "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!_store.Save(_draft))
        {
            MessageBox.Show(this, "Nie udało się zapisać konfiguracji — szczegóły w logu.",
                "SensorPanelGlue", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _service.ApplyConfig(_draft);
        Close();
    }

    private void LoadDraftIntoControls()
    {
        _ruleProcess.Text = _draft.PanelRule.ProcessName;
        _ruleClass.Text = _draft.PanelRule.ClassPattern;
        _ruleTitle.Text = _draft.PanelRule.TitlePattern;

        _keepPosition.Checked = _draft.KeepPanelPosition;
        _restoreSize.Checked = _draft.RestorePanelSize;
        _topmost.Checked = _draft.KeepPanelTopmost;
        _hideAltTab.Checked = _draft.HidePanelFromAltTab;

        _relocateForeign.Checked = _draft.RelocateForeignWindows;

        _safetyPoll.Value = _draft.SafetyPollSeconds;
        _debounce.Value = _draft.EventDebounceMs;
        _displayDelay.Value = _draft.DisplayChangeDelayMs;
        _pingPongLimit.Value = _draft.PingPongLimit;
        _pingPongWindow.Value = _draft.PingPongWindowSeconds;
        _verbose.Checked = _draft.VerboseLogging;
        _startPaused.Checked = _draft.StartPaused;
        _autoStartMode.SelectedIndex = (int)AutoStart.GetCurrentMode();

        UpdateGeometryLabel();
        RefreshExceptions();
    }

    private void ReadControlsIntoDraft()
    {
        _draft.PanelRule = new WindowRule
        {
            ProcessName = _ruleProcess.Text.Trim(),
            ClassPattern = _ruleClass.Text.Trim(),
            TitlePattern = _ruleTitle.Text.Trim(),
        };

        _draft.KeepPanelPosition = _keepPosition.Checked;
        _draft.RestorePanelSize = _restoreSize.Checked;
        _draft.KeepPanelTopmost = _topmost.Checked;
        _draft.HidePanelFromAltTab = _hideAltTab.Checked;

        _draft.RelocateForeignWindows = _relocateForeign.Checked;

        _draft.SafetyPollSeconds = (int)_safetyPoll.Value;
        _draft.EventDebounceMs = (int)_debounce.Value;
        _draft.DisplayChangeDelayMs = (int)_displayDelay.Value;
        _draft.PingPongLimit = (int)_pingPongLimit.Value;
        _draft.PingPongWindowSeconds = (int)_pingPongWindow.Value;
        _draft.VerboseLogging = _verbose.Checked;
        _draft.StartPaused = _startPaused.Checked;

        _draft.Normalize();
    }

    private void UpdateGeometryLabel()
    {
        _geometryLabel.Text = _draft.PanelGeometry.IsSet
            ? $"offset {_draft.PanelGeometry.OffsetX},{_draft.PanelGeometry.OffsetY} "
              + $"rozmiar {_draft.PanelGeometry.Width}x{_draft.PanelGeometry.Height}"
            : "nie zapisano — zostanie zapamiętana automatycznie";
    }

    // ------------------------------------------------------------- helpers

    private static Button MakeButton(string text, EventHandler onClick, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            Margin = new Padding(4),
        };
        button.Click += onClick;
        return button;
    }

    private static Label MakeHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiFonts.Bold,
        Margin = new Padding(0, 12, 0, 6),
    };

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) }, 0, row);
        control.Width = control is NumericUpDown ? 90 : 320;
        control.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(control, 1, row);
    }

    private static void AddCheckRow(TableLayoutPanel layout, int row, CheckBox check)
    {
        check.AutoSize = true;
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, row);
        layout.Controls.Add(check, 1, row);
    }

    private static void ConfigureNumeric(NumericUpDown numeric, int min, int max)
    {
        numeric.Minimum = min;
        numeric.Maximum = max;
        numeric.Increment = 1;
    }

    private void OpenPath(string path)
    {
        try
        {
            var target = File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się otworzyć ścieżki: " + path, ex);
        }
    }
}
