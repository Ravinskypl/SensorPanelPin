using System.Drawing;
using SensorPanelGlue.Monitors;
using SensorPanelGlue.Windows;

namespace SensorPanelGlue.Tests.TestSupport;

internal static class Factories
{
    public static MonitorInfo Monitor(int x, int y, int width, int height, string? id = null, bool primary = false)
    {
        var bounds = new Rectangle(x, y, width, height);
        return new MonitorInfo
        {
            StableId = id ?? $"MON@{x},{y}",
            FriendlyName = id ?? $"MON@{x},{y}",
            GdiDeviceName = @"\\.\DISPLAYX",
            Bounds = bounds,
            WorkArea = bounds,
            IsPrimary = primary,
            Dpi = 96,
        };
    }

    /// <summary>The layout of the machine the tool was built for: 3440x1440 primary plus a 1024x600 panel screen.</summary>
    public static MonitorInfo[] NormalTwoMonitorLayout() => new[]
    {
        Monitor(0, 0, 3440, 1440, "primary", primary: true),
        Monitor(3440, 0, 1024, 600, "panel"),
    };

    public static WindowInfo Window(
        long handle = 0x1000,
        string processName = "notepad",
        string className = "Notepad",
        string title = "Untitled - Notepad")
    {
        return new WindowInfo
        {
            Handle = new IntPtr(handle),
            ClassName = className,
            Title = title,
            ProcessId = 1234,
            ProcessName = processName,
            Style = 0,
            ExStyle = 0,
            Rect = new Rectangle(0, 0, 800, 600),
            IsVisible = true,
            IsMinimized = false,
            IsMaximized = false,
            Owner = IntPtr.Zero,
        };
    }
}
