using System.Drawing;

namespace SensorPanelGlue.Windows;

public sealed class WindowInfo
{
    public required IntPtr Handle { get; init; }

    public required string ClassName { get; init; }

    public required string Title { get; init; }

    public required uint ProcessId { get; init; }

    /// <summary>Lower-case executable name without extension; empty when the process cannot be queried.</summary>
    public required string ProcessName { get; init; }

    public required long Style { get; init; }

    public required long ExStyle { get; init; }

    public required Rectangle Rect { get; init; }

    public required bool IsVisible { get; init; }

    public required bool IsMinimized { get; init; }

    public required bool IsMaximized { get; init; }

    public required IntPtr Owner { get; init; }

    public string Describe() =>
        $"hwnd=0x{Handle.ToInt64():X} proces='{ProcessName}' klasa='{ClassName}' tytuł='{Title}' rect={Rect}";
}
