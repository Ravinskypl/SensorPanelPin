using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SensorPanelPin.UI;

/// <summary>
/// Icons used at runtime. The normal states come from the same app.ico that is embedded in the
/// executable, so the shell icon, the title bar and the tray icon are one mark; the paused variant
/// is rendered from <see cref="IconArtwork"/> because it exists only in the tray.
/// </summary>
public static class AppIcon
{
    private const string ResourceName = "SensorPanelPin.app.ico";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon? _window;
    private static Icon? _tray;
    private static Icon? _trayPaused;

    /// <summary>Icon for windows: title bar, Alt+Tab, taskbar.</summary>
    public static Icon Normal => _window ??= LoadEmbedded(32) ?? FromArtwork(32, IconArtwork.ActiveAccent);

    /// <summary>Tray icon at the size the shell actually asks for, so it stays crisp at any DPI.</summary>
    public static Icon Tray => _tray ??= LoadEmbedded(TraySize) ?? FromArtwork(TraySize, IconArtwork.ActiveAccent);

    public static Icon TrayPaused => _trayPaused ??= FromArtwork(TraySize, IconArtwork.PausedAccent);

    private static System.Windows.Media.ImageSource? _windowSource;

    /// <summary>The same app.ico as a WPF image, for window title bars and the navigation header.</summary>
    public static System.Windows.Media.ImageSource WindowSource => _windowSource ??= LoadWindowSource();

    private static System.Windows.Media.ImageSource LoadWindowSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException("Missing embedded resource " + ResourceName);

        var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

        // 32 px: crisp for a 20 px header glyph at up to 150 % scaling, and a fine title-bar icon.
        return decoder.Frames.FirstOrDefault(f => f.PixelWidth == 32) ?? decoder.Frames[0];
    }

    /// <summary>Kept for call sites that only distinguish running from paused.</summary>
    public static Icon Paused => TrayPaused;

    private static int TraySize
    {
        get
        {
            var size = SystemInformation.SmallIconSize.Width;
            return size > 0 ? size : 16;
        }
    }

    private static Icon? LoadEmbedded(int size)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            // This overload picks the closest available frame from the multi-size .ico.
            return new Icon(stream, new Size(size, size));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Icon FromArtwork(int size, Color accent)
    {
        using var bitmap = IconArtwork.Render(size, accent);
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
