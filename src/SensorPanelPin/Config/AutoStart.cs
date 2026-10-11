using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using SensorPanelPin.Core;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Localization;

namespace SensorPanelPin.Config;

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
///
/// The scheduled task is managed through the Task Scheduler COM API, in process. An earlier
/// version wrote the definition to a fixed file in %TEMP% and handed it to schtasks.exe, which
/// let unprivileged code swap the file between the write and the elevated read, and launched
/// schtasks.exe by bare name from a directory unprivileged code could write to.
/// </summary>
public static class AutoStart
{
    public const string TaskName = "SensorPanelPin";

    private const string ExecutableName = "SensorPanelPin.exe";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SensorPanelPin";

    // The names the program registered under before it was renamed from SensorPanelGlue. They count
    // as the current autostart until the user picks a mode, which then replaces or removes them.
    private const string LegacyTaskName = "SensorPanelGlue";
    private const string LegacyValueName = "SensorPanelGlue";

    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int ErrorFileNotFound = unchecked((int)0x80070002);
    private const int ErrorAccessDenied = unchecked((int)0x80070005);

    /// <summary>
    /// The path to register for autostart. Environment.ProcessPath alone is not enough: under
    /// `dotnet SensorPanelPin.dll` it is dotnet.exe, and under a test host it is the host — either
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


    /// <summary>
    /// Applies a mode, removing whatever the other mechanism had left behind. Returns false and a
    /// reason in the user's language when the change could not be made; the log gets its own entry.
    /// </summary>
    public static bool SetMode(AutoStartMode mode, Logger log, out string message)
    {
        message = string.Empty;

        var exe = ResolveExecutablePath();
        if (exe is null)
        {
            message = Strings.AutoStartNoExecutableFormat(ExecutableName);
            log.Error($"Could not determine the path to {ExecutableName}; autostart left unchanged. "
                      + "ProcessPath=" + (Environment.ProcessPath ?? "(none)"));
            return false;
        }

        switch (mode)
        {
            case AutoStartMode.Disabled:
                if (!RemoveScheduledTask(log, out message))
                {
                    return false;
                }

                RemoveRunKey(log);
                return true;

            case AutoStartMode.RegistryRun:
                if (!RemoveScheduledTask(log, out message))
                {
                    return false;
                }

                return WriteRunKey(exe, log, out message);

            case AutoStartMode.ScheduledTaskElevated:
                // Create first, clean up second: a refusal must not leave the user with no autostart.
                if (!CreateScheduledTask(exe, log, out message))
                {
                    return false;
                }

                RemoveRunKey(log);
                return true;

            default:
                message = Strings.AutoStartUnknownMode;
                return false;
        }
    }

    /// <summary>
    /// For the installer, which runs elevated from the downloaded copy: registers the logon task for
    /// the copy it has just installed, <paramref name="exe"/>, rather than for the running process.
    /// </summary>
    internal static bool EnableElevatedTaskFor(string exe, Logger log, out string message)
    {
        if (!CreateScheduledTask(exe, log, out message))
        {
            return false;
        }

        RemoveRunKey(log);
        return true;
    }

    /// <summary>Removes every autostart entry, under the current name and the old one.</summary>
    internal static bool DisableAll(Logger log, out string message)
    {
        if (!RemoveScheduledTask(log, out message))
        {
            return false;
        }

        RemoveRunKey(log);
        return true;
    }

    /// <summary>
    /// Starts the logon task now. It runs as the account it is registered for, with its rights, which
    /// an elevated installer cannot reproduce by starting the program itself.
    /// </summary>
    internal static bool RunTaskNow(Logger log)
    {
        try
        {
            dynamic folder = RootFolder();
            folder.GetTask(TaskName).Run(null);
            log.Info("Started the logon task.");
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Could not start the logon task.", ex);
            return false;
        }
    }

    /// <summary>Ends whatever the logon tasks started, under the current name and the old one.</summary>
    internal static void StopTasks(Logger log)
    {
        foreach (var name in new[] { TaskName, LegacyTaskName })
        {
            if (!TaskExists(name))
            {
                continue;
            }

            try
            {
                dynamic folder = RootFolder();
                folder.GetTask(name).Stop(0);
            }
            catch (Exception ex)
            {
                log.Warn($"Could not stop the logon task {name}: {ex.Message}");
            }
        }
    }
    // ------------------------------------------------------------ registry

    private static bool RunKeyExists()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string { Length: > 0 }
                   || key?.GetValue(LegacyValueName) is string { Length: > 0 };
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
                message = Strings.AutoStartRegistryOpenFailed;
                log.Error("Could not open the registry autostart key.");
                return false;
            }

            key.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            log.Info("Registry autostart turned on: " + exe);
            return true;
        }
        catch (Exception ex)
        {
            message = Strings.AutoStartRegistryWriteFailed;
            log.Error("Could not write the registry autostart entry.", ex);
            return false;
        }
    }

    private static void RemoveRunKey(Logger log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            foreach (var name in new[] { ValueName, LegacyValueName })
            {
                if (key?.GetValue(name) is not null)
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                    log.Info("Removed registry autostart entry: " + name);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Could not remove the registry autostart entry.", ex);
        }
    }

    // ------------------------------------------------------- scheduled task

    private static bool ScheduledTaskExists() => TaskExists(TaskName) || TaskExists(LegacyTaskName);

    private static bool TaskExists(string name)
    {
        try
        {
            dynamic folder = RootFolder();
            folder.GetTask(name);
            return true;
        }
        catch (Exception ex) when (HResultOf(ex) == ErrorFileNotFound)
        {
            return false;
        }
        catch (Exception ex) when (HResultOf(ex) == ErrorAccessDenied)
        {
            // The task belongs to an administrator context: it exists, this process just may not read it.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool CreateScheduledTask(string exe, Logger log, out string message)
    {
        if (!Elevation.IsElevated())
        {
            message = Strings.AutoStartNeedsElevation;
            return false;
        }

        // The task starts this file with administrator rights and no UAC prompt at every logon.
        // If anyone else can change the file, that is a free elevation for any program they run.
        if (!ExecutableTrust.IsProtected(exe, out var reason))
        {
            message = Strings.AutoStartUntrustedFormat(reason);
            log.Warn("Refused to create the elevated logon task: non-administrators can change " + exe
                     + " or the folders leading to it.");
            return false;
        }

        try
        {
            dynamic folder = RootFolder();
            folder.RegisterTask(TaskName, BuildTaskXml(exe), TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);

            if (!TaskExists(TaskName))
            {
                message = Strings.AutoStartTaskNotVisible;
                log.Error("The logon task was reported as created but cannot be found in Task Scheduler.");
                return false;
            }

            message = string.Empty;
            log.Info("Created the elevated logon task: " + exe);
            DeleteLegacyTask(folder, log);
            return true;
        }
        catch (Exception ex)
        {
            message = Strings.AutoStartTaskCreateFailed;
            log.Error("Could not create the logon task in Task Scheduler.", ex);
            return false;
        }
    }

    private static bool RemoveScheduledTask(Logger log, out string message)
    {
        message = string.Empty;

        if (!ScheduledTaskExists())
        {
            return true;
        }

        if (!Elevation.IsElevated())
        {
            message = Strings.AutoStartRemoveNeedsElevation;
            return false;
        }

        try
        {
            dynamic folder = RootFolder();
            if (TaskExists(TaskName))
            {
                folder.DeleteTask(TaskName, 0);
                log.Info("Removed the logon task from Task Scheduler.");
            }

            DeleteLegacyTask(folder, log);
            return true;
        }
        catch (Exception ex)
        {
            message = Strings.AutoStartTaskRemoveFailed;
            log.Error("Could not remove the logon task from Task Scheduler.", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes the task left by the program under its old name. Failure is only logged: the new task
    /// is already in place, and the old one at worst starts a program that is no longer there.
    /// </summary>
    private static void DeleteLegacyTask(dynamic folder, Logger log)
    {
        if (!TaskExists(LegacyTaskName))
        {
            return;
        }

        try
        {
            folder.DeleteTask(LegacyTaskName, 0);
            log.Info("Removed the logon task left under the old program name: " + LegacyTaskName);
        }
        catch (Exception ex)
        {
            log.Error("Could not remove the old logon task " + LegacyTaskName + " from Task Scheduler.", ex);
        }
    }

    private static object RootFolder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    private static int HResultOf(Exception ex)
    {
        var current = ex;
        while (current is TargetInvocationException { InnerException: { } inner })
        {
            current = inner;
        }

        return current is COMException com ? com.ErrorCode : current.HResult;
    }

    private static string BuildTaskXml(string exe)
    {
        // The signed-in user, not necessarily the account this elevated process runs as.
        var user = Elevation.InteractiveUserName();
        var workingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;

        return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Keeps the AIDA64 SensorPanel on its chosen monitor.</Description>
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
