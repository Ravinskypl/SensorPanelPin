using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SensorPanelGlue.Tests.TestSupport;

/// <summary>
/// Creates the filesystem tricks an unprivileged attacker would use. Neither needs administrator
/// rights, which is exactly why the production code has to defend against them.
/// </summary>
internal static class TestLinks
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public static bool CreateHardLink(string link, string existing) =>
        CreateHardLinkNative(link, existing, IntPtr.Zero);

    /// <summary>Directory junction via mklink /J, which any user may create.</summary>
    public static bool CreateJunction(string junction, string target)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ArgumentList = { "/c", "mklink", "/J", junction, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)!;
        process.WaitForExit(10_000);
        return process.ExitCode == 0 && Directory.Exists(junction);
    }

    /// <summary>
    /// Best-effort removal of a test directory that may contain junctions. Junctions are removed as
    /// links first, so the recursive delete never walks into - or is refused by - their targets.
    /// </summary>
    public static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                         .ToList())
            {
                Directory.Delete(dir);
            }

            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>Holds the file the way an editor or antivirus scanner might: nobody else gets in.</summary>
    public static IDisposable LockExclusively(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
