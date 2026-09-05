using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using SensorPanelGlue.Core;
using SensorPanelGlue.Diagnostics;

namespace SensorPanelGlue.Config;

public enum AutoStartMode
{
    /// <summary>No autostart entry of any kind.</summary>
    Disabled,

    /// <summary>HKCU\...\Run. Simple, but cannot elevate — the panel stays unmanaged if AIDA64 is elevated.</summary>
    RegistryRun,

    /// <summary>Scheduled task with HighestAvailable. Starts elevated at logon without a UAC prompt.</summary>
    ScheduledTaskElevated,
}

/// <summary>
/// The only system-configuration surface this application touches, and only when the user picks a
/// mode in the UI. Two mechanisms, because the registry Run key physically cannot start a process
/// elevated — that is a Windows rule, not a limitation of this code.
/// </summary>
public static class AutoStart
{
    public const string TaskName = "SensorPanelGlue";

    private const string ExecutableName = "SensorPanelGlue.exe";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SensorPanelGlue";

    /// <summary>
    /// The path to register for autostart. Environment.ProcessPath alone is not enough: under
    /// `dotnet SensorPanelGlue.dll` it is dotnet.exe, and under a test host it is the host — either
    /// way the registered entry would silently start the wrong program at logon. Returns null
    /// rather than guessing, so the caller can refuse the change instead of writing a bad entry.
    /// </summary>
    private static string? ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath)
            && string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var beside = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        return File.Exists(beside) ? beside : null;
    }

    public static AutoStartMode GetCurrentMode()
    {
        if (ScheduledTaskExists())
        {
            return AutoStartMode.ScheduledTaskElevated;
        }

        return RunKeyExists() ? AutoStartMode.RegistryRun : AutoStartMode.Disabled;
    }

    public static string Describe(AutoStartMode mode) => mode switch
    {
        AutoStartMode.Disabled => "Wyłączony",
        AutoStartMode.RegistryRun => "Rejestr (HKCU\\...\\Run) — bez uprawnień administratora",
        AutoStartMode.ScheduledTaskElevated => "Harmonogram zadań — z uprawnieniami administratora",
        _ => mode.ToString(),
    };

    /// <summary>
    /// Applies a mode, removing whatever the other mechanism had left behind. Returns false and a
    /// human-readable reason when the change could not be made (usually a dismissed UAC prompt).
    /// </summary>
    public static bool SetMode(AutoStartMode mode, Logger log, out string message)
    {
        message = string.Empty;

        var exe = ResolveExecutablePath();
        if (exe is null)
        {
            message = $"Nie udało się ustalić ścieżki do {ExecutableName}. "
                      + "Autostart nie został zmieniony, żeby nie zarejestrować błędnego wpisu.";
            log.Error(message + " ProcessPath=" + (Environment.ProcessPath ?? "(brak)"));
            return false;
        }

        switch (mode)
        {
            case AutoStartMode.Disabled:
                RemoveRunKey(log);
                return RemoveScheduledTask(log, out message);

            case AutoStartMode.RegistryRun:
                if (!RemoveScheduledTask(log, out message))
                {
                    return false;
                }

                return WriteRunKey(exe, log, out message);

            case AutoStartMode.ScheduledTaskElevated:
                RemoveRunKey(log);
                return CreateScheduledTask(exe, log, out message);

            default:
                message = "Nieznany tryb autostartu.";
                return false;
        }
    }

    // ------------------------------------------------------------ registry

    private static bool RunKeyExists()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool WriteRunKey(string exe, Logger log, out string message)
    {
        message = string.Empty;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                message = "Nie udało się otworzyć klucza autostartu w rejestrze.";
                log.Error(message);
                return false;
            }

            key.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
            log.Info("Autostart przez rejestr włączony: " + exe);
            return true;
        }
        catch (Exception ex)
        {
            message = "Nie udało się zapisać wpisu autostartu w rejestrze.";
            log.Error(message, ex);
            return false;
        }
    }

    private static void RemoveRunKey(Logger log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                log.Info("Usunięto wpis autostartu z rejestru.");
            }
        }
        catch (Exception ex)
        {
            log.Error("Nie udało się usunąć wpisu autostartu z rejestru.", ex);
        }
    }

    // ------------------------------------------------------- scheduled task

    private static bool ScheduledTaskExists()
    {
        try
        {
            return RunSchtasks($"/Query /TN \"{TaskName}\"", elevate: false, out _) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool CreateScheduledTask(string exe, Logger log, out string message)
    {
        message = string.Empty;
        var xmlPath = Path.Combine(Path.GetTempPath(), "SensorPanelGlue.task.xml");

        try
        {
            // schtasks /XML expects UTF-16; anything else fails with a misleading parse error.
            File.WriteAllText(xmlPath, BuildTaskXml(exe), Encoding.Unicode);

            var exitCode = RunSchtasks(
                $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F",
                elevate: !Elevation.IsElevated(),
                out var output);

            if (exitCode != 0)
            {
                message = "Nie udało się utworzyć zadania w Harmonogramie zadań "
                          + $"(kod {exitCode}). Najczęstsza przyczyna to anulowany monit UAC.";
                log.Error(message + " " + output);
                return false;
            }

            if (!ScheduledTaskExists())
            {
                message = "Zadanie zostało zgłoszone jako utworzone, ale nie widać go w Harmonogramie zadań.";
                log.Error(message);
                return false;
            }

            log.Info("Utworzono zadanie autostartu z podniesionymi uprawnieniami: " + exe);
            return true;
        }
        catch (Exception ex)
        {
            message = "Nie udało się utworzyć zadania w Harmonogramie zadań.";
            log.Error(message, ex);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                }
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing the operation over.
            }
        }
    }

    private static bool RemoveScheduledTask(Logger log, out string message)
    {
        message = string.Empty;

        if (!ScheduledTaskExists())
        {
            return true;
        }

        var exitCode = RunSchtasks(
            $"/Delete /TN \"{TaskName}\" /F",
            elevate: !Elevation.IsElevated(),
            out var output);

        if (exitCode == 0)
        {
            log.Info("Usunięto zadanie autostartu z Harmonogramu zadań.");
            return true;
        }

        message = $"Nie udało się usunąć zadania z Harmonogramu zadań (kod {exitCode}).";
        log.Error(message + " " + output);
        return false;
    }

    private static int RunSchtasks(string arguments, bool elevate, out string output)
    {
        output = string.Empty;

        var info = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
        };

        if (elevate)
        {
            // A single UAC prompt for the task change; the application itself stays as-invoker.
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WindowStyle = ProcessWindowStyle.Hidden;
        }
        else
        {
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
        }

        using var process = Process.Start(info);
        if (process is null)
        {
            return -1;
        }

        if (!elevate)
        {
            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        }

        process.WaitForExit(30000);
        return process.HasExited ? process.ExitCode : -1;
    }

    private static string BuildTaskXml(string exe)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        var workingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;

        return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Pilnuje, aby SensorPanel z AIDA64 pozostawał na wybranym monitorze.</Description>
                    <URI>\{TaskName}</URI>
                  </RegistrationInfo>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <UserId>{Escape(user)}</UserId>
                      <Delay>PT15S</Delay>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{Escape(user)}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <WakeToRun>false</WakeToRun>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{Escape(exe)}</Command>
                      <WorkingDirectory>{Escape(workingDirectory)}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
