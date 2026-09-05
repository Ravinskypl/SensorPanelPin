using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SensorPanelGlue.UI;

/// <summary>
/// The tray icon is drawn at runtime so the project stays a pure source tree with no binary assets.
/// </summary>
public static class AppIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon? _normal;
    private static Icon? _paused;

    public static Icon Normal => _normal ??= Build(Color.FromArgb(64, 224, 132));

    public static Icon Paused => _paused ??= Build(Color.FromArgb(160, 160, 160));

    private static Icon Build(Color accent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Screen body.
            using var body = new SolidBrush(Color.FromArgb(32, 36, 44));
            using var frame = new Pen(Color.FromArgb(96, 104, 120), 2f);
            var screen = new Rectangle(3, 5, 26, 18);
            g.FillRectangle(body, screen);
            g.DrawRectangle(frame, screen);

            // Stand.
            using var stand = new SolidBrush(Color.FromArgb(96, 104, 120));
            g.FillRectangle(stand, 13, 23, 6, 3);
            g.FillRectangle(stand, 9, 26, 14, 3);

            // Sensor bars.
            using var bar = new SolidBrush(accent);
            g.FillRectangle(bar, 6, 17, 4, 4);
            g.FillRectangle(bar, 12, 13, 4, 8);
            g.FillRectangle(bar, 18, 9, 4, 12);
            g.FillRectangle(bar, 24, 15, 3, 6);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Clone so the icon survives destroying the temporary GDI handle.
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
