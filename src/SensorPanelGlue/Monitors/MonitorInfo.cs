using System.Drawing;

namespace SensorPanelGlue.Monitors;

/// <summary>
/// A physical display. <see cref="StableId"/> is the monitor device path from the CCD API, which
/// stays the same across reboots, hot-plug and reordering. Never identify a monitor by index.
/// </summary>
public sealed class MonitorInfo
{
    public required string StableId { get; init; }

    public required string FriendlyName { get; init; }

    /// <summary>GDI name such as \\.\DISPLAY1. Changes between sessions; display only.</summary>
    public required string GdiDeviceName { get; init; }

    public required Rectangle Bounds { get; init; }

    public required Rectangle WorkArea { get; init; }

    public required bool IsPrimary { get; init; }

    public required uint Dpi { get; init; }

    public IntPtr Handle { get; init; }

    public string DisplayLabel =>
        $"{FriendlyName} ({Bounds.Width}x{Bounds.Height} @ {Bounds.X},{Bounds.Y})"
        + (IsPrimary ? " [główny]" : string.Empty);

    public override string ToString() => $"{FriendlyName} [{StableId}] {Bounds}";
}
