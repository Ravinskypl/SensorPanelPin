using System.Drawing;
using System.Windows.Forms;
using SensorPanelGlue.Monitors;
using Timer = System.Windows.Forms.Timer;

namespace SensorPanelGlue.UI;

/// <summary>Shows a large number on every physical screen, like the Windows display settings do.</summary>
public static class MonitorIdentifyOverlay
{
    public static void Show(IReadOnlyList<MonitorInfo> monitors, int seconds = 3)
    {
        var forms = new List<Form>();

        for (var i = 0; i < monitors.Count; i++)
        {
            var form = BuildOverlay(monitors[i], i + 1);
            forms.Add(form);
            form.Show();
        }

        if (forms.Count == 0)
        {
            return;
        }

        var timer = new Timer { Interval = Math.Max(1, seconds) * 1000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach (var form in forms)
            {
                form.Close();
                form.Dispose();
            }
        };
        timer.Start();
    }

    private static Form BuildOverlay(MonitorInfo monitor, int number)
    {
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Color.FromArgb(16, 18, 24),
            Opacity = 0.88,
            Bounds = new Rectangle(
                monitor.Bounds.X + (monitor.Bounds.Width / 2) - 180,
                monitor.Bounds.Y + (monitor.Bounds.Height / 2) - 130,
                360,
                260),
        };

        var numberLabel = new Label
        {
            Text = number.ToString(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = UiFonts.OverlayNumber,
        };

        var nameLabel = new Label
        {
            Text = monitor.DisplayLabel,
            Dock = DockStyle.Bottom,
            Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(190, 200, 215),
            Font = UiFonts.OverlayCaption,
        };

        form.Controls.Add(numberLabel);
        form.Controls.Add(nameLabel);
        return form;
    }
}
