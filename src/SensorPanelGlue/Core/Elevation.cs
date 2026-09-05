using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using SensorPanelGlue.Diagnostics;

namespace SensorPanelGlue.Core;

/// <summary>
/// The application deliberately runs as the invoker. AIDA64 usually runs elevated (it needs it for
/// sensor access), and Windows UIPI then refuses SetWindowPos on its windows with ERROR_ACCESS_DENIED.
/// Elevation is therefore offered as an explicit user action, never forced at startup.
/// </summary>
public static class Elevation
{
    public const int ErrorAccessDenied = 5;
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
            message = "Nie udało się ustalić ścieżki pliku wykonywalnego.";
            log.Error(message);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            });

            log.Info("Uruchomiono ponownie z podniesionymi uprawnieniami.");
            message = string.Empty;
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            message = "Podniesienie uprawnień zostało anulowane.";
            log.Info(message);
            return false;
        }
        catch (Exception ex)
        {
            message = "Nie udało się uruchomić ponownie z podniesionymi uprawnieniami.";
            log.Error(message, ex);
            return false;
        }
    }
}
