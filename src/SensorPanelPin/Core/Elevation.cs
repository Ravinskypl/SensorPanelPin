using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Interop;
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
    public static bool TryRestartElevated(Logger log, out string message) =>
        // The new instance would otherwise race this one for the single-instance mutex and,
        // losing, exit with "already running"; the flag tells it to wait for us to go.
        TryStartElevated(AfterRestartArgument, log, out message);

    /// <summary>
    /// Starts this executable again with administrator rights and the given arguments: one UAC
    /// prompt, no script host or shell in between. Returns false when the prompt was declined.
    /// </summary>
    public static bool TryStartElevated(string arguments, Logger log, out string message)
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
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
            });

            log.Info("Started with administrator rights: " + arguments);
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

    /// <summary>
    /// The user signed in to this session, as DOMAIN\name. Not the same as the account this process
    /// runs as: when a standard user approves a UAC prompt with an administrator's password, the
    /// elevated process runs as that administrator, and a logon task registered for it would start
    /// for the wrong person.
    /// </summary>
    public static string InteractiveUserName()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var name = QuerySession(session, NativeMethods.WTSUserName);
        var domain = QuerySession(session, NativeMethods.WTSDomainName);
        if (!string.IsNullOrEmpty(name))
        {
            return string.IsNullOrEmpty(domain) ? name : domain + "\\" + name;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    private static string? QuerySession(int session, int infoClass)
    {
        if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
    }
}
