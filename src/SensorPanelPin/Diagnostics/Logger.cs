using System.Text;

namespace SensorPanelPin.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Rotating file log. Deliberately dependency-free: one writer, one lock, 1 MB x 5 files.
///
/// Two properties matter more than they look. A missing line must not be mistaken for an event
/// that never happened, so lines that could not be written are counted and the gap is reported
/// as soon as writing works again. And the content includes window titles, which any program can
/// set to anything, so every line break inside a message is indented: a title cannot forge a line
/// that starts with a timestamp.
/// </summary>
public sealed class Logger : IDisposable
{
    internal const long MaxFileBytes = 1024 * 1024;
    internal const int MaxFiles = 5;
    private const string FileName = "sensorpanelpin.log";
    private const string ContinuationIndent = "    ";

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _throttle = new(StringComparer.Ordinal);
    private readonly SafeDirectory? _directory;

    private FileStream? _stream;
    private StreamWriter? _writer;
    private long _droppedTotal;
    private long _droppedSinceLastWrite;
    private bool _disposed;

    public Logger(string logDirectory)
    {
        LogDirectory = logDirectory;
        LogFilePath = Path.Combine(logDirectory, FileName);

        try
        {
            // Two levels: the logs directory and the application directory above it are both ours.
            _directory = SafeDirectory.Open(logDirectory, levels: 2);
        }
        catch (Exception ex)
        {
            DisabledReason = ex.Message;
        }
    }

    public string LogDirectory { get; }

    public string LogFilePath { get; }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Set when file logging was switched off because the target looked tampered with.</summary>
    public string? DisabledReason { get; private set; }

    /// <summary>Lines that could not be written since start-up.</summary>
    public long DroppedLines
    {
        get
        {
            lock (_gate)
            {
                return _droppedTotal;
            }
        }
    }

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

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseStream();
        }
    }

    /// <summary>Indents every line after the first so no part of a message can pose as a log line.</summary>
    internal static string NeutraliseLineBreaks(string message)
    {
        if (message.IndexOfAny(new[] { '\r', '\n' }) < 0)
        {
            return message;
        }

        var lines = message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join(Environment.NewLine + ContinuationIndent, lines);
    }

    private void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var line = Format(level, NeutraliseLineBreaks(message));

        lock (_gate)
        {
            if (_disposed || _directory is null)
            {
                _droppedTotal++;
                return;
            }

            try
            {
                EnsureOpen();

                // Rotate before writing, so a full file never receives one more line.
                if (_stream!.Length >= MaxFileBytes)
                {
                    Rotate();
                    EnsureOpen();
                }

                if (_droppedSinceLastWrite > 0)
                {
                    _writer!.WriteLine(Format(LogLevel.Warn,
                        $"Lost {_droppedSinceLastWrite} log lines while the file was unavailable."));
                    _droppedSinceLastWrite = 0;
                }

                _writer!.WriteLine(line);
                _writer.Flush();
            }
            catch (UnsafePathException ex)
            {
                // Refusing beats following a link with elevated rights; logging stays off.
                DisabledReason = ex.Message;
                Drop();
            }
            catch (IOException)
            {
                // Usually another program holding the file; try again on the next line.
                Drop();
            }
            catch (UnauthorizedAccessException)
            {
                Drop();
            }
        }
    }

    private static string Format(LogLevel level, string message) => string.Concat(
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        " [", level.ToString().ToUpperInvariant().PadRight(5), "] ",
        message);

    private void Drop()
    {
        _droppedTotal++;
        _droppedSinceLastWrite++;
        CloseStream();

        if (DisabledReason is not null)
        {
            // Nothing more will be written; stop pretending the next attempt might succeed.
            _disposed = true;
        }
    }

    private void EnsureOpen()
    {
        if (_writer is not null)
        {
            return;
        }

        // Kept open for the process lifetime: opening per line widened every window in which another
        // program could make a line vanish, and put a file open on the UI thread for every message.
        _stream = _directory!.OpenFile(FileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void CloseStream()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
        }

        _writer = null;
        _stream = null;
    }

    private void Rotate()
    {
        CloseStream();

        var oldest = LogFilePath + "." + MaxFiles;
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxFiles - 1; i >= 1; i--)
        {
            var from = LogFilePath + "." + i;
            if (File.Exists(from))
            {
                File.Move(from, LogFilePath + "." + (i + 1), overwrite: true);
            }
        }

        // Moving replaces directory entries and never writes through a link, so rotation itself
        // needs no handle verification; the next EnsureOpen verifies the fresh file.
        File.Move(LogFilePath, LogFilePath + ".1", overwrite: true);
    }
}
