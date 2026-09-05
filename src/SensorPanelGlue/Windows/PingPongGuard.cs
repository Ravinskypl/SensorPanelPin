using SensorPanelGlue.Diagnostics;

namespace SensorPanelGlue.Windows;

public sealed record ProblemWindow(string Description, DateTime BlockedAt);

/// <summary>
/// Stops the tug-of-war with applications that reposition themselves. After N moves inside the
/// configured window the handle is abandoned for the rest of the session and reported once.
/// </summary>
public sealed class PingPongGuard
{
    private sealed class Entry
    {
        public int Count;
        public DateTime WindowStart;
        public bool Blocked;
    }

    private readonly Dictionary<IntPtr, Entry> _entries = new();
    private readonly Dictionary<IntPtr, ProblemWindow> _problems = new();
    private readonly Logger _log;

    public PingPongGuard(Logger log)
    {
        _log = log;
    }

    public int Limit { get; set; } = 3;

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    public IReadOnlyCollection<ProblemWindow> Problems => _problems.Values;

    public bool IsBlocked(IntPtr hwnd) =>
        _entries.TryGetValue(hwnd, out var entry) && entry.Blocked;

    /// <summary>Records a move. Returns false once the window has been given up on.</summary>
    public bool RegisterMove(WindowInfo info)
    {
        var now = DateTime.UtcNow;

        if (!_entries.TryGetValue(info.Handle, out var entry))
        {
            entry = new Entry { WindowStart = now };
            _entries[info.Handle] = entry;
        }

        if (entry.Blocked)
        {
            return false;
        }

        if (now - entry.WindowStart > Window)
        {
            entry.WindowStart = now;
            entry.Count = 0;
        }

        entry.Count++;

        if (entry.Count <= Limit)
        {
            return true;
        }

        entry.Blocked = true;
        var description = $"{info.ProcessName} — {info.Title} (klasa {info.ClassName})";
        _problems[info.Handle] = new ProblemWindow(description, DateTime.Now);
        _log.Warn(
            $"Okno wraca na zakazany ekran mimo {Limit} przeniesień w ciągu {Window.TotalSeconds:0} s — "
            + "przestaję je przenosić: " + info.Describe());
        return false;
    }

    /// <summary>Drops entries whose handles no longer exist so the maps do not grow forever.</summary>
    public void Prune(Func<IntPtr, bool> isAlive)
    {
        foreach (var handle in _entries.Keys.ToList())
        {
            if (!isAlive(handle))
            {
                _entries.Remove(handle);
                _problems.Remove(handle);
            }
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _problems.Clear();
        _log.Info("Wyczyszczono listę problematycznych okien.");
    }
}
