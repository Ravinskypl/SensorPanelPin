using System.Drawing;
using System.Runtime.InteropServices;
using SensorPanelGlue.Diagnostics;
using SensorPanelGlue.Monitors;
using static SensorPanelGlue.Interop.NativeMethods;

namespace SensorPanelGlue.Windows;

public enum MoveOutcome
{
    Success,

    /// <summary>The call failed for an ordinary reason (window vanished, driver refused).</summary>
    Failed,

    /// <summary>UIPI blocked the call: the target window belongs to a higher-integrity process.</summary>
    AccessDenied,
}

/// <summary>
/// Moves windows without stealing focus. Maximized windows go through SetWindowPlacement, which
/// re-maximizes on the destination monitor without activating the window.
/// </summary>
public sealed class WindowMover
{
    private const int ErrorAccessDenied = 5;

    private readonly Logger _log;

    public WindowMover(Logger log)
    {
        _log = log;
    }

    /// <summary>Moves a window from one monitor to another, keeping its relative position.</summary>
    public MoveOutcome MoveToMonitor(WindowInfo info, MonitorInfo from, MonitorInfo to)
    {
        var delta = new Size(
            to.WorkArea.X - from.WorkArea.X,
            to.WorkArea.Y - from.WorkArea.Y);

        if (info.IsMaximized)
        {
            return MoveMaximized(info, delta, to);
        }

        var desired = ClampInto(new Rectangle(info.Rect.Location + delta, info.Rect.Size), to.WorkArea);

        var flags = SWP_NOACTIVATE | SWP_NOZORDER;
        if (desired.Size == info.Rect.Size)
        {
            flags |= SWP_NOSIZE;
        }

        if (SetWindowPos(info.Handle, HWND_TOP, desired.X, desired.Y, desired.Width, desired.Height, flags))
        {
            return MoveOutcome.Success;
        }

        return Report("SetWindowPos", info.Describe());
    }

    /// <summary>Places a window at an exact rectangle (used by the SensorPanel watchdog).</summary>
    public MoveOutcome PlaceAt(IntPtr hwnd, Rectangle target, bool resize, bool topmost, bool changeZOrder)
    {
        var flags = SWP_NOACTIVATE;
        if (!resize)
        {
            flags |= SWP_NOSIZE;
        }

        if (!changeZOrder)
        {
            flags |= SWP_NOZORDER;
        }

        var insertAfter = changeZOrder
            ? (topmost ? HWND_TOPMOST : HWND_NOTOPMOST)
            : HWND_TOP;

        if (SetWindowPos(hwnd, insertAfter, target.X, target.Y, target.Width, target.Height, flags))
        {
            return MoveOutcome.Success;
        }

        return Report("SetWindowPos", $"okno 0x{hwnd.ToInt64():X}");
    }

    /// <summary>
    /// Harmless no-op SetWindowPos used to learn up front whether this process is allowed to
    /// reposition the window at all, without actually moving anything.
    /// </summary>
    public MoveOutcome ProbeAccess(IntPtr hwnd)
    {
        if (SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
        {
            return MoveOutcome.Success;
        }

        return Marshal.GetLastWin32Error() == ErrorAccessDenied ? MoveOutcome.AccessDenied : MoveOutcome.Failed;
    }

    private MoveOutcome MoveMaximized(WindowInfo info, Size delta, MonitorInfo to)
    {
        var placement = new WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<WINDOWPLACEMENT>(),
        };

        if (!GetWindowPlacement(info.Handle, ref placement))
        {
            return Report("GetWindowPlacement", info.Describe());
        }

        // rcNormalPosition uses workspace coordinates; translating by a delta is safe regardless
        // of the coordinate base, unlike computing an absolute position.
        var normal = placement.rcNormalPosition.ToRectangle();
        var moved = ClampInto(new Rectangle(normal.Location + delta, normal.Size), to.WorkArea);

        placement.rcNormalPosition = RECT.FromRectangle(moved);
        placement.showCmd = SW_SHOWMAXIMIZED;

        if (SetWindowPlacement(info.Handle, ref placement))
        {
            return MoveOutcome.Success;
        }

        return Report("SetWindowPlacement", info.Describe());
    }

    private MoveOutcome Report(string api, string subject)
    {
        var error = Marshal.GetLastWin32Error();
        if (error == ErrorAccessDenied)
        {
            _log.Debug($"{api}: odmowa dostępu (okno procesu z wyższymi uprawnieniami) — {subject}");
            return MoveOutcome.AccessDenied;
        }

        _log.Warn($"{api} nie powiodło się (błąd {error}) — {subject}");
        return MoveOutcome.Failed;
    }

    /// <summary>Shrinks and shifts a rectangle so it fits entirely inside <paramref name="area"/>.</summary>
    private static Rectangle ClampInto(Rectangle rect, Rectangle area)
    {
        var width = Math.Min(rect.Width, area.Width);
        var height = Math.Min(rect.Height, area.Height);
        var x = Math.Clamp(rect.X, area.Left, Math.Max(area.Left, area.Right - width));
        var y = Math.Clamp(rect.Y, area.Top, Math.Max(area.Top, area.Bottom - height));
        return new Rectangle(x, y, width, height);
    }
}
