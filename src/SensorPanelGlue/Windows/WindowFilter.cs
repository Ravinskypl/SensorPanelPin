using SensorPanelGlue.Config;
using SensorPanelGlue.Interop;
using SensorPanelGlue.Monitors;
using static SensorPanelGlue.Interop.NativeMethods;

namespace SensorPanelGlue.Windows;

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
            return FilterVerdict.Deny("okno własne");
        }

        if (!info.IsVisible)
        {
            return FilterVerdict.Deny("niewidoczne");
        }

        if (info.IsMinimized)
        {
            return FilterVerdict.Deny("zminimalizowane");
        }

        if ((info.Style & WS_CHILD) != 0)
        {
            return FilterVerdict.Deny("okno potomne");
        }

        if (info.Owner != IntPtr.Zero)
        {
            return FilterVerdict.Deny("okno posiada właściciela");
        }

        if ((info.ExStyle & WS_EX_TOOLWINDOW) != 0)
        {
            return FilterVerdict.Deny("okno narzędziowe");
        }

        if (string.IsNullOrWhiteSpace(info.Title))
        {
            return FilterVerdict.Deny("pusty tytuł");
        }

        if (info.Rect.Width <= 0 || info.Rect.Height <= 0)
        {
            return FilterVerdict.Deny("zerowy rozmiar");
        }

        if (info.Handle == GetShellWindow())
        {
            return FilterVerdict.Deny("okno powłoki");
        }

        if (BlockedClasses.Contains(info.ClassName))
        {
            return FilterVerdict.Deny("klasa systemowa: " + info.ClassName);
        }

        if (string.IsNullOrEmpty(info.ProcessName))
        {
            // Typically an elevated process this app may not query; never guess.
            return FilterVerdict.Deny("proces niedostępny (prawdopodobnie podniesione uprawnienia)");
        }

        if (BlockedProcesses.Contains(info.ProcessName))
        {
            return FilterVerdict.Deny("proces systemowy: " + info.ProcessName);
        }

        if (WindowQuery.Matches(info, config.PanelRule))
        {
            return FilterVerdict.Deny("to jest SensorPanel");
        }

        if (string.Equals(info.ProcessName, "aida64", StringComparison.OrdinalIgnoreCase))
        {
            return FilterVerdict.Deny("okno AIDA64");
        }

        if (DwmInterop.IsCloaked(info.Handle))
        {
            return FilterVerdict.Deny("okno ukryte przez DWM");
        }

        if (IsFullscreenOn(info, sourceMonitor))
        {
            return FilterVerdict.Deny("okno pełnoekranowe");
        }

        foreach (var exception in config.Exceptions)
        {
            if (WindowQuery.Matches(info, exception))
            {
                return FilterVerdict.Deny("wyjątek użytkownika: " + exception);
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
