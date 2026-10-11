using SensorPanelGlue.Diagnostics;

namespace SensorPanelGlue.Tests.TestSupport;

/// <summary>
/// A real <see cref="Logger"/> writing into a throw-away temp directory. The classes under test take
/// a concrete Logger (no interface seam), so tests give them a real one and can read back what it wrote.
/// </summary>
internal sealed class TempLog : IDisposable
{
    public TempLog()
    {
        Directory = Path.Combine(Path.GetTempPath(), "SensorPanelGlue.Tests", Guid.NewGuid().ToString("N"));
        Logger = new Logger(Directory);
    }

    public string Directory { get; }

    public Logger Logger { get; }

    /// <summary>
    /// Everything currently in the active log file, or empty when nothing was written. The logger keeps
    /// the file open for writing, so the read has to share write access.
    /// </summary>
    public string ReadAll()
    {
        if (!File.Exists(Logger.LogFilePath))
        {
            return string.Empty;
        }

        using var stream = new FileStream(Logger.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        Logger.Dispose();
        TestLinks.DeleteTree(Directory);
    }
}
