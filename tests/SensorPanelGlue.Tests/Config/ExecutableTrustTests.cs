using SensorPanelGlue.Config;

namespace SensorPanelGlue.Tests.Config;

/// <summary>
/// The check that stops an elevated logon task from pointing at a file ordinary users can replace.
/// These run against real ACLs, so they rely on stock Windows permissions.
/// </summary>
public sealed class ExecutableTrustTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SensorPanelGlue.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_program_in_the_users_temp_folder_is_not_trusted()
    {
        // The user owns %TEMP%; any program running as that user could swap this file.
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, "SensorPanelGlue.exe");
        File.WriteAllText(exe, "not really an exe");

        var trusted = ExecutableTrust.IsProtected(exe, out var reason);

        Assert.False(trusted);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void A_program_installed_under_Program_Files_is_trusted()
    {
        // The .NET host is installed exactly the way this application should be: under Program Files,
        // owned and writable by administrators only. (System32 is a poor example: some of its files
        // do not let ordinary users read their permissions, and the check fails closed on those.)
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        Assert.True(File.Exists(dotnet), "the test needs the .NET runtime installed under Program Files");

        var trusted = ExecutableTrust.IsProtected(dotnet, out var reason);

        Assert.True(trusted, reason);
    }

    [Fact]
    public void A_missing_file_is_not_trusted()
    {
        var trusted = ExecutableTrust.IsProtected(Path.Combine(_dir, "missing.exe"), out var reason);

        Assert.False(trusted);
        Assert.Contains("nie istnieje", reason);
    }
}
