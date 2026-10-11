using System.Drawing;
using System.Runtime.InteropServices;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Monitors;
using static SensorPanelPin.Interop.NativeMethods;

namespace SensorPanelPin.Windows;

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

        var outcome = info.IsMaximized
            ? MoveMaximized(info, delta, to)
            : MoveRestored(info, delta, to);

        // A successful API call is not a moved window: SetWindowPlacement in particular can report
        // success and leave a maximized window exactly where it was. Check the result.
        return outcome == MoveOutcome.Success ? VerifyLanded(info, from, to) : outcome;
    }

    private MoveOutcome MoveRestored(WindowInfo info, Size delta, MonitorInfo to)
    {
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

    /// <summary>
    /// Confirms the window actually changed screens, comparing how much of it now sits on the
    /// destination against how much is still on the source.
    /// </summary>
    private MoveOutcome VerifyLanded(WindowInfo info, MonitorInfo from, MonitorInfo to)
    {
        if (!GetWindowRect(info.Handle, out var raw))
        {
            return MoveOutcome.Failed;
        }

        var rect = raw.ToRectangle();
        var onTarget = Area(Rectangle.Intersect(rect, to.Bounds));
        var onSource = Area(Rectangle.Intersect(rect, from.Bounds));

        // A tie still counts: half the window on each screen is not "unmoved".
        if (onTarget > 0 && onTarget >= onSource)
        {
            return MoveOutcome.Success;
        }

        _log.Warn(
            $"Wywołanie przeniesienia zakończyło się powodzeniem, ale okno nie zmieniło ekranu: "
            + $"{info.Describe()} — po operacji {rect}");
        return MoveOutcome.Failed;
    }

    private static long Area(Rectangle rect) => (long)rect.Width * rect.Height;

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

        var original = placement;

        // rcNormalPosition uses workspace coordinates; translating by a delta is safe regardless
        // of the coordinate base, unlike computing an absolute position. Only the position is
        // clamped: this is the size the user gets back when un-maximizing, and shrinking it to fit
        // the destination would change it for good.
        var normal = placement.rcNormalPosition.ToRectangle();
        var moved = ClampPosition(new Rectangle(normal.Location + delta, normal.Size), to.WorkArea);
        placement.rcNormalPosition = RECT.FromRectangle(moved);

        // Two steps on purpose. Handing SetWindowPlacement a translated rcNormalPosition together
        // with SW_SHOWMAXIMIZED leaves the window maximized on the screen it is already on - it
        // reports success and nothing moves. Restoring it onto the destination first makes the
        // second call maximize on the intended monitor.
        placement.showCmd = SW_SHOWNORMAL;
        if (!SetWindowPlacement(info.Handle, ref placement))
        {
            return Report("SetWindowPlacement (przywrócenie)", info.Describe());
        }

        placement.showCmd = SW_SHOWMAXIMIZED;
        if (SetWindowPlacement(info.Handle, ref placement))
        {
            return MoveOutcome.Success;
        }

        var outcome = Report("SetWindowPlacement (maksymalizacja)", info.Describe());

        // Never leave a maximized window un-maximized at a position it was never in.
        if (!SetWindowPlacement(info.Handle, ref original))
        {
            _log.Warn("Nie udało się przywrócić pierwotnego stanu okna po nieudanej maksymalizacji: " + info.Describe());
        }

        return outcome;
    }

    /// <summary>Shifts a rectangle as far into <paramref name="area"/> as it goes, without resizing it.</summary>
    private static Rectangle ClampPosition(Rectangle rect, Rectangle area)
    {
        var x = Math.Clamp(rect.X, area.Left, Math.Max(area.Left, area.Right - rect.Width));
        var y = Math.Clamp(rect.Y, area.Top, Math.Max(area.Top, area.Bottom - rect.Height));
        return new Rectangle(x, y, rect.Width, rect.Height);
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
