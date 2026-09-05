using System.Text;

namespace SensorPanelGlue.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Rotating file log. Deliberately dependency-free: one writer, one lock, 1 MB x 5 files.
/// </summary>
public sealed class Logger
{
    private const long MaxFileBytes = 1024 * 1024;
    private const int MaxFiles = 5;

    private readonly object _gate = new();
    private readonly string _logFile;
    private readonly Dictionary<string, DateTime> _throttle = new(StringComparer.Ordinal);

    public Logger(string logDirectory)
    {
        LogDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
        _logFile = Path.Combine(logDirectory, "sensorpanelglue.log");
    }

    public string LogDirectory { get; }

    public string LogFilePath => _logFile;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    public void Error(string message, Exception ex) => Write(LogLevel.Error, message + " :: " + ex);

    /// <summary>
    /// Writes at most once per <paramref name="period"/> for a given key. Used for states that
    /// persist for minutes (AIDA64 not running, target monitor absent) so the log stays readable.
    /// </summary>
    public void Throttled(string key, TimeSpan period, LogLevel level, string message)
    {
        lock (_gate)
        {
            if (_throttle.TryGetValue(key, out var last) && DateTime.UtcNow - last < period)
            {
                return;
            }

            _throttle[key] = DateTime.UtcNow;
        }

        Write(level, message);
    }

    /// <summary>Clears a throttle key so the next occurrence of that state is logged again.</summary>
    public void ResetThrottle(string key)
    {
        lock (_gate)
        {
            _throttle.Remove(key);
        }
    }

    private void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var line = string.Concat(
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            " [", level.ToString().ToUpperInvariant().PadRight(5), "] ",
            message);

        lock (_gate)
        {
            try
            {
                Rotate();
                File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take the application down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(_logFile);
        if (!info.Exists || info.Length < MaxFileBytes)
        {
            return;
        }

        var oldest = _logFile + "." + MaxFiles;
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxFiles - 1; i >= 1; i--)
        {
            var from = _logFile + "." + i;
            if (File.Exists(from))
            {
                File.Move(from, _logFile + "." + (i + 1), overwrite: true);
            }
        }

        File.Move(_logFile, _logFile + ".1", overwrite: true);
    }
}
