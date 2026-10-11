using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Localization;

namespace SensorPanelPin.Core;

/// <summary>
/// The application deliberately runs as the invoker. AIDA64 usually runs elevated (it needs it for
/// sensor access), and Windows UIPI then refuses SetWindowPos on its windows with ERROR_ACCESS_DENIED.
/// Elevation is therefore offered as an explicit user action, never forced at startup.
/// </summary>
public static class Elevation
{
    public const int ErrorAccessDenied = 5;
    public const string AfterRestartArgument = "--after-restart";
    private const int ErrorCancelled = 1223;

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunches this executable with the runas verb. Returns true when the elevated instance
    /// started and the caller should shut down; false when the user dismissed the UAC prompt.
    /// </summary>
    public static bool TryRestartElevated(Logger log, out string message)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            message = Strings.ElevationNoExecutable;
            log.Error("Could not determine the executable's path for the restart.");
            return false;
        }

        try
        {
            // The new instance would otherwise race this one for the single-instance mutex and,
            // losing, exit with "already running"; the flag tells it to wait for us to go.
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = AfterRestartArgument,
                UseShellExecute = true,
                Verb = "runas",
            });

            log.Info("Restarted with administrator rights.");
            message = string.Empty;
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            message = Strings.ElevationCancelled;
            log.Info("The administrator prompt was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            message = Strings.ElevationFailed;
            log.Error("Could not restart with administrator rights.", ex);
            return false;
        }
    }
}
