using System.Drawing;
using System.Drawing.Drawing2D;

namespace SensorPanelPin.UI;

/// <summary>
/// The application mark: a small screen showing sensor bars. Drawn proportionally so the same
/// artwork serves the 16 px tray icon and the 256 px shell icon without a second definition.
/// </summary>
public static class IconArtwork
{
    public static readonly Color ActiveAccent = Color.FromArgb(64, 224, 132);
    public static readonly Color PausedAccent = Color.FromArgb(150, 156, 168);

    private static readonly Color ScreenFill = Color.FromArgb(28, 32, 40);
    private static readonly Color ScreenFrame = Color.FromArgb(120, 130, 148);

    /// <summary>Draws the mark filling a <paramref name="size"/> x <paramref name="size"/> box.</summary>
    public static void Draw(Graphics g, int size, Color accent)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // All coordinates below are expressed on a 32-unit grid and scaled to the requested size.
        var s = size / 32f;

        using var body = new SolidBrush(ScreenFill);
        using var frame = new Pen(ScreenFrame, Math.Max(1f, 2f * s));
        using var stand = new SolidBrush(ScreenFrame);
        using var bar = new SolidBrush(accent);

        var screen = new RectangleF(2.5f * s, 4.5f * s, 27f * s, 19f * s);
        var radius = 3f * s;

        using (var path = RoundedRect(screen, radius))
        {
            g.FillPath(body, path);
            g.DrawPath(frame, path);
        }

        // Stand.
        g.FillRectangle(stand, 13.5f * s, 23.5f * s, 5f * s, 3.5f * s);
        g.FillRectangle(stand, 8.5f * s, 27f * s, 15f * s, 2.5f * s);

        // Sensor bars, tallest in the middle so the shape reads even at 16 px.
        g.FillRectangle(bar, 6f * s, 16f * s, 3.5f * s, 5f * s);
        g.FillRectangle(bar, 11f * s, 12.5f * s, 3.5f * s, 8.5f * s);
        g.FillRectangle(bar, 16f * s, 8.5f * s, 3.5f * s, 12.5f * s);
        g.FillRectangle(bar, 21f * s, 14f * s, 3.5f * s, 7f * s);
    }

    /// <summary>Renders the mark onto a transparent bitmap.</summary>
    public static Bitmap Render(int size, Color accent)
    {
        var bitmap = new Bitmap(size, size);
        bitmap.SetResolution(96, 96);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        Draw(g, size, accent);
        return bitmap;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

        if (d <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
