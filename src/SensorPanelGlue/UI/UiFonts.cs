using System.Drawing;

namespace SensorPanelGlue.UI;

/// <summary>
/// Shared fonts. Disposing a control does not dispose the Font assigned to it, so creating a new
/// Font per window leaked a GDI object every time the configuration, diagnostics or identify
/// window was opened. These live for the process lifetime instead.
/// </summary>
internal static class UiFonts
{
    public static readonly Font Body = new("Segoe UI", 9f);

    public static readonly Font Bold = new("Segoe UI", 9f, FontStyle.Bold);

    public static readonly Font Mono = new("Consolas", 9f);

    public static readonly Font OverlayNumber = new("Segoe UI", 96f, FontStyle.Bold, GraphicsUnit.Point);

    public static readonly Font OverlayCaption = new("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
}
