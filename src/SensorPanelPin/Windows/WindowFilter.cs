using SensorPanelPin.Config;
using SensorPanelPin.Interop;
using SensorPanelPin.Monitors;
using static SensorPanelPin.Interop.NativeMethods;

namespace SensorPanelPin.Windows;

public readonly record struct FilterVerdict(bool Allowed, string Reason)
{
    public static FilterVerdict Allow() => new(true, string.Empty);

    public static FilterVerdict Deny(string reason) => new(false, reason);
}

/// <summary>
/// Decides whether a foreign window may be relocated. Everything shell-owned, owned, tool,
/// cloaked, elevated-opaque or fullscreen is left alone.
/// </summary>
public sealed class WindowFilter
{
    private static readonly HashSet<string> BlockedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "SysListView32",
        "Windows.UI.Core.CoreWindow",
        "Xaml_WindowedPopupClass",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "TaskListThumbnailWnd",
        "DV2ControlHost",
        "EdgeUiInputTopWndClass",
        "NativeHWNDHost",
        "Touch Tooltip Window",
        "tooltips_class32",
        "#32770.UAC",
    };

    private static readonly HashSet<string> BlockedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "consent",
        "lockapp",
        "searchhost",
        "startmenuexperiencehost",
        "shellexperiencehost",
        "textinputhost",
        "systemsettingsbroker",
    };

    private readonly uint _ownProcessId;

    public WindowFilter(uint ownProcessId)
    {
        _ownProcessId = ownProcessId;
    }

    /// <summary>
    /// True while a game or presentation owns the screen. In that state no window is touched at all.
    /// </summary>
    public static bool IsScreenCaptured()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) != 0)
            {
                return false;
            }

            return state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public FilterVerdict CanRelocate(WindowInfo info, AppConfig config, MonitorInfo sourceMonitor)
    {
        if (info.ProcessId == _ownProcessId)
        {
            return FilterVerdict.Deny("own window");
        }

        if (!info.IsVisible)
        {
            return FilterVerdict.Deny("invisible");
        }

        if (info.IsMinimized)
        {
            return FilterVerdict.Deny("minimised");
        }

        if ((info.Style & WS_CHILD) != 0)
        {
            return FilterVerdict.Deny("child window");
        }

        if (info.Owner != IntPtr.Zero)
        {
            return FilterVerdict.Deny("owned window");
        }

        if ((info.ExStyle & WS_EX_TOOLWINDOW) != 0)
        {
            return FilterVerdict.Deny("tool window");
        }

        if (string.IsNullOrWhiteSpace(info.Title))
        {
            return FilterVerdict.Deny("empty title");
        }

        if (info.Rect.Width <= 0 || info.Rect.Height <= 0)
        {
            return FilterVerdict.Deny("zero size");
        }

        if (info.Handle == GetShellWindow())
        {
            return FilterVerdict.Deny("shell window");
        }

        if (BlockedClasses.Contains(info.ClassName))
        {
            return FilterVerdict.Deny("system class: " + info.ClassName);
        }

        if (string.IsNullOrEmpty(info.ProcessName))
        {
            // Typically an elevated process this app may not query; never guess.
            return FilterVerdict.Deny("process not accessible (probably elevated)");
        }

        if (BlockedProcesses.Contains(info.ProcessName))
        {
            return FilterVerdict.Deny("system process: " + info.ProcessName);
        }

        if (WindowQuery.Matches(info, config.PanelRule))
        {
            return FilterVerdict.Deny("this is the SensorPanel");
        }

        if (string.Equals(info.ProcessName, "aida64", StringComparison.OrdinalIgnoreCase))
        {
            return FilterVerdict.Deny("AIDA64 window");
        }

        if (DwmInterop.IsCloaked(info.Handle))
        {
            return FilterVerdict.Deny("cloaked by DWM");
        }

        if (IsFullscreenOn(info, sourceMonitor))
        {
            return FilterVerdict.Deny("full-screen window");
        }

        foreach (var exception in config.Exceptions)
        {
            if (WindowQuery.Matches(info, exception))
            {
                return FilterVerdict.Deny("user exception: " + exception);
            }
        }

        return FilterVerdict.Allow();
    }

    private static bool IsFullscreenOn(WindowInfo info, MonitorInfo monitor)
    {
        var bounds = monitor.Bounds;
        var coversMonitor = info.Rect.Left <= bounds.Left
                            && info.Rect.Top <= bounds.Top
                            && info.Rect.Right >= bounds.Right
                            && info.Rect.Bottom >= bounds.Bottom;

        if (!coversMonitor)
        {
            return false;
        }

        var borderless = (info.Style & WS_CAPTION) == 0 && (info.Style & WS_THICKFRAME) == 0;
        return borderless && info.Handle == GetForegroundWindow();
    }
}
