using SensorPanelPin.Config;
using SensorPanelPin.Core;
using SensorPanelPin.Tests.TestSupport;

namespace SensorPanelPin.Tests.Config;

/// <summary>
/// The parts of the built-in installer that need no administrator rights: what gets copied, how
/// folders are compared, and whose name the logon task is registered for. The elevated steps
/// (Program Files, the task itself) are checked by installing for real.
/// </summary>
public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SensorPanelPin.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => TestLinks.DeleteTree(_root);

    [Fact]
    public void Installing_puts_exactly_the_program_files_in_place()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "download")).FullName;
        File.WriteAllText(Path.Combine(source, "SensorPanelPin.exe"), "exe");
        File.WriteAllText(Path.Combine(source, "wpfgfx_cor3.dll"), "dll");
        File.WriteAllText(Path.Combine(source, "LICENSE.txt"), "license");
        // Left over from the 2.0.0 package; never carried into Program Files.
        File.WriteAllText(Path.Combine(source, "Install.cmd"), "script");
        File.WriteAllText(Path.Combine(source, "Install.ps1"), "script");

        var destination = Path.Combine(_root, "Program Files", "SensorPanelPin");
        Directory.CreateDirectory(Path.Combine(destination, "old"));
        File.WriteAllText(Path.Combine(destination, "stale.dll"), "from an older version");

        Installer.ReplaceDirectory(source, destination);

        var installed = Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(new[] { "LICENSE.txt", "SensorPanelPin.exe", "wpfgfx_cor3.dll" }, installed);
        Assert.Equal("exe", File.ReadAllText(Path.Combine(destination, "SensorPanelPin.exe")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\SensorPanelPin", @"C:\Program Files\SensorPanelPin\", true)]
    [InlineData(@"C:\PROGRAM FILES\sensorpanelpin\", @"C:\Program Files\SensorPanelPin", true)]
    [InlineData(@"C:\Users\me\Downloads\SensorPanelPin-2.0.1-win-x64", @"C:\Program Files\SensorPanelPin", false)]
    [InlineData(@"C:\Program Files\SensorPanelPin2", @"C:\Program Files\SensorPanelPin", false)]
    public void Folders_compare_by_path_regardless_of_case_and_trailing_separator(string a, string b, bool same)
    {
        Assert.Equal(same, Installer.IsSameDirectory(a, b));
    }

    [Theory]
    [InlineData(new[] { "--install" }, true)]
    [InlineData(new[] { "--UNINSTALL", "--keep-settings" }, true)]
    [InlineData(new[] { "--after-restart" }, false)]
    [InlineData(new string[0], false)]
    public void Only_the_install_and_uninstall_arguments_start_the_installer(string[] args, bool expected)
    {
        Assert.Equal(expected, Installer.IsInstallerRun(args));
    }

    [Fact]
    public void The_logon_task_is_registered_for_a_named_account()
    {
        // DOMAIN\name of the signed-in user; falls back to this process's account outside a session.
        var user = Elevation.InteractiveUserName();

        Assert.Contains('\\', user);
        Assert.DoesNotContain(string.Empty, user.Split('\\'));
    }
}
