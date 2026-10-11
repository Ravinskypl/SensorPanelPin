using System.Runtime.InteropServices;

namespace SensorPanelPin.Interop;

internal static class DwmInterop
{
    private const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>
    /// True for windows the shell keeps alive but hides (suspended UWP apps, virtual-desktop
    /// residents). They look visible to GetWindowRect but must never be relocated.
    /// </summary>
    public static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
