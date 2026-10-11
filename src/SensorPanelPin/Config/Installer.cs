using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using SensorPanelPin.Core;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Interop;
using SensorPanelPin.Localization;

namespace SensorPanelPin.Config;

/// <summary>
/// Installing and uninstalling, done by the program itself. The settings window starts this
/// executable again with administrator rights and <see cref="InstallArgument"/> or
/// <see cref="UninstallArgument"/>: one UAC prompt, and nothing in between.
///
/// Release 2.0.0 shipped Install.cmd, which asked for rights through PowerShell and then ran
/// Install.ps1 with the execution policy bypassed. Antivirus behaviour engines (Bitdefender Advanced
/// Threat Defense, for one) stop that chain as malicious before anything is installed, and they are
/// right to be suspicious of it. No script host or shell takes part here.
/// </summary>
public static class Installer
{
    public const string InstallArgument = "--install";
    public const string UninstallArgument = "--uninstall";
    public const string KeepSettingsArgument = "--keep-settings";

    private const string ProgramName = "SensorPanelPin";
    private const string LegacyName = "SensorPanelGlue";
    private const string ExecutableName = ProgramName + ".exe";

    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProgramName);

    private static string LegacyInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), LegacyName);

    /// <summary>True when this process runs from the installed copy in Program Files.</summary>
    public static bool IsInstalledCopy => IsSameDirectory(AppContext.BaseDirectory, InstallDirectory);

    public static bool IsInstallerRun(string[] args) =>
        args.Contains(InstallArgument, StringComparer.OrdinalIgnoreCase)
        || args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase);

    /// <summary>The elevated half: called from Main before the single-instance check.</summary>
    public static int Run(string[] args)
    {
        var install = args.Contains(InstallArgument, StringComparer.OrdinalIgnoreCase);

        if (!Elevation.IsElevated())
        {
            MessageBox.Show(Strings.InstallNeedsAdministrator, ProgramName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return 2;
        }

        // Disposing twice is harmless, and lines written after disposal are only counted as lost.
        using var log = new Logger(Path.Combine(ConfigStore.DefaultAppDataDirectory, "logs"));
        bool succeeded;
        string message;
        try
        {
            log.Info(install ? "=== Install ===" : "=== Uninstall ===");
            succeeded = install
                ? Install(log, out message)
                : Uninstall(log, !args.Contains(KeepSettingsArgument, StringComparer.OrdinalIgnoreCase), out message);
        }
        catch (Exception ex)
        {
            log.Error(install ? "Installation failed." : "Uninstallation failed.", ex);
            message = install ? Strings.InstallFailedFormat(ex.Message) : Strings.UninstallFailedFormat(ex.Message);
            succeeded = false;
        }

        MessageBox.Show(message, ProgramName, MessageBoxButton.OK, succeeded ? MessageBoxImage.Information : MessageBoxImage.Error);
        return succeeded ? 0 : 1;
    }

    private static bool Install(Logger log, out string message)
    {
        var source = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var destination = InstallDirectory;

        StopRunningCopies(log);

        if (!IsSameDirectory(source, destination))
        {
            ReplaceDirectory(source, destination);
            log.Info($"Copied the program from {source} to {destination}.");
        }

        // The logon task starts this file with administrator rights and no prompt. Program Files only
        // lets administrators change it; check that it really is so before registering anything.
        var exe = Path.Combine(destination, ExecutableName);
        if (!ExecutableTrust.IsProtected(exe, out var reason))
        {
            log.Warn("The installed copy is not protected; no logon task was created.");
            message = Strings.InstallUntrustedFormat(reason);
            return false;
        }

        RemoveLegacyInstallation(destination, log);

        if (!AutoStart.EnableElevatedTaskFor(exe, log, out var taskMessage))
        {
            message = Strings.InstallFailedFormat(taskMessage);
            return false;
        }

        AutoStart.RunTaskNow(log);
        message = Strings.InstallDoneFormat(destination);
        return true;
    }

    private static bool Uninstall(Logger log, bool removeSettings, out string message)
    {
        StopRunningCopies(log);

        if (!AutoStart.DisableAll(log, out var taskMessage))
        {
            message = Strings.UninstallFailedFormat(taskMessage);
            return false;
        }

        var later = false;
        foreach (var folder in new[] { InstallDirectory, LegacyInstallDirectory })
        {
            if (File.Exists(Path.Combine(folder, ExecutableName)) || File.Exists(Path.Combine(folder, LegacyName + ".exe")))
            {
                later |= DeleteDirectory(folder, log);
            }
        }

        if (removeSettings)
        {
            // The log lives in the folder about to go; it cannot stay open while it is deleted.
            log.Info("Removing settings and logs.");
            log.Dispose();
            RemoveSettings();
        }

        message = later ? Strings.UninstallDoneAfterRestart : Strings.UninstallDone;
        return true;
    }

    /// <summary>
    /// Ends every other copy, under either name: the one the logon task started and the one that
    /// launched this installer. They would hold files that are about to be replaced, and two copies
    /// would fight over the same windows.
    /// </summary>
    private static void StopRunningCopies(Logger log)
    {
        AutoStart.StopTasks(log);

        var self = Environment.ProcessId;
        foreach (var name in new[] { ProgramName, LegacyName })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id == self)
                    {
                        continue;
                    }

                    try
                    {
                        // The copy that launched this installer closes on its own; give it a moment.
                        if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
                        {
                            process.Kill();
                            process.WaitForExit(TimeSpan.FromSeconds(5));
                        }

                        log.Info($"Stopped {name} (process {process.Id}).");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Already gone.
                    }
                }
            }
        }
    }

    /// <summary>
    /// Puts exactly the program's files into <paramref name="destination"/>: whatever an older
    /// version left there goes first, so nothing stale lies beside the new executable. The folder is
    /// created fresh under Program Files and inherits its protected permissions; files and folder are
    /// owned by Administrators. Scripts from an old release package are not carried over.
    /// </summary>
    internal static void ReplaceDirectory(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);
        SetOwnerToAdministrators(new DirectoryInfo(destination));

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target);
            SetOwnerToAdministrators(new FileInfo(target));
        }
    }

    private static void SetOwnerToAdministrators(FileSystemInfo item)
    {
        if (!Elevation.IsElevated())
        {
            // Only an elevated process may hand ownership to the group; tests copy into %TEMP%.
            return;
        }

        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        switch (item)
        {
            case DirectoryInfo directory:
                var directorySecurity = directory.GetAccessControl(AccessControlSections.Owner);
                directorySecurity.SetOwner(administrators);
                directory.SetAccessControl(directorySecurity);
                break;
            case FileInfo file:
                var fileSecurity = file.GetAccessControl(AccessControlSections.Owner);
                fileSecurity.SetOwner(administrators);
                file.SetAccessControl(fileSecurity);
                break;
        }
    }

    private static void RemoveLegacyInstallation(string destination, Logger log)
    {
        var legacy = LegacyInstallDirectory;
        if (!File.Exists(Path.Combine(legacy, LegacyName + ".exe")) || IsSameDirectory(legacy, destination))
        {
            return;
        }

        if (DeleteDirectory(legacy, log))
        {
            log.Info("Parts of the old installation are removed at the next restart: " + legacy);
        }
    }

    /// <summary>
    /// Deletes a program folder. Files in use - the running uninstaller itself, and the native
    /// libraries it has loaded - are queued for deletion at the next restart instead. Returns true
    /// when anything had to wait for the restart.
    /// </summary>
    private static bool DeleteDirectory(string folder, Logger log)
    {
        var later = false;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NativeMethods.MoveFileEx(file, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
                later = true;
            }
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NativeMethods.MoveFileEx(folder, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
            later = true;
        }

        log.Info(later ? $"Removed {folder}; files in use go at the next restart." : $"Removed {folder}.");
        return later;
    }

    /// <summary>
    /// %APPDATA%\SensorPanelPin. The folder sits in the user's profile, where any program running as
    /// the user can swap it for a junction to somewhere else, and this runs as administrator: a
    /// folder that is a link is left alone. Directory.Delete removes links met inside the tree
    /// without descending into them.
    /// </summary>
    private static void RemoveSettings()
    {
        var folder = new DirectoryInfo(ConfigStore.DefaultAppDataDirectory);
        if (!folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        folder.Delete(recursive: true);
    }

    internal static bool IsSameDirectory(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
