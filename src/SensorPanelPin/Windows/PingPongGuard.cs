using SensorPanelPin.Diagnostics;

namespace SensorPanelPin.Windows;

public sealed record ProblemWindow(string Description, DateTime BlockedAt);

/// <summary>
/// Stops the tug-of-war with applications that reposition themselves, without punishing the ones
/// that merely repositioned themselves a few times while starting up.
///
/// A burst of moves pauses the window for a cool-down instead of abandoning it: applications like
/// Steam place their window several times within a second or two during launch and then settle, so
/// a permanent give-up after three moves in 1.4 s leaves the window stranded on the wrong screen.
/// Only a window that keeps coming back across several cool-downs is given up on for good.
/// </summary>
public sealed class PingPongGuard
{
    private sealed class Entry
    {
        public int Count;
        public DateTime WindowStart;
        public DateTime CooldownUntil;
        public int Rounds;
        public bool Abandoned;
    }

    internal const int MaxRounds = 3;

    private readonly Dictionary<IntPtr, Entry> _entries = new();
    private readonly Dictionary<IntPtr, ProblemWindow> _problems = new();
    private readonly Logger _log;

    public PingPongGuard(Logger log)
    {
        _log = log;
    }

    public int Limit { get; set; } = 5;

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    private TimeSpan? _cooldown;

    /// <summary>How long a window is left alone after a burst before it gets another chance.</summary>
    public TimeSpan Cooldown
    {
        get => _cooldown ?? TimeSpan.FromSeconds(Math.Max(15, Window.TotalSeconds * 3));
        set => _cooldown = value;
    }

    public IReadOnlyCollection<ProblemWindow> Problems => _problems.Values;

    public bool IsBlocked(IntPtr hwnd) =>
        _entries.TryGetValue(hwnd, out var entry)
        && (entry.Abandoned || DateTime.UtcNow < entry.CooldownUntil);

    /// <summary>Records a move. Returns false while the window is paused or has been given up on.</summary>
    public bool RegisterMove(WindowInfo info)
    {
        var now = DateTime.UtcNow;

        if (!_entries.TryGetValue(info.Handle, out var entry))
        {
            entry = new Entry { WindowStart = now };
            _entries[info.Handle] = entry;
        }

        if (entry.Abandoned)
        {
            return false;
        }

        if (now < entry.CooldownUntil)
        {
            return false;
        }

        if (entry.CooldownUntil != default)
        {
            // Cool-down just expired: give the window a clean slate for one more round.
            entry.CooldownUntil = default;
            entry.Count = 0;
            entry.WindowStart = now;
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

        entry.Rounds++;

        if (entry.Rounds < MaxRounds)
        {
            entry.CooldownUntil = now + Cooldown;
            _log.Info(
                $"Okno wraca na zakazany ekran mimo {Limit} przeniesień w ciągu "
                + $"{Window.TotalSeconds:0} s — przerwa {Cooldown.TotalSeconds:0} s "
                + $"(próba {entry.Rounds} z {MaxRounds}): {info.Describe()}");
            return false;
        }

        entry.Abandoned = true;
        var description = $"{info.ProcessName} — {WindowInfo.Shorten(info.Title)} (klasa {info.ClassName})";
        _problems[info.Handle] = new ProblemWindow(description, DateTime.Now);

        // The abandoning burst is the MaxRounds-th; the pauses happened between bursts.
        _log.Warn(
            $"Okno wraca na zakazany ekran po {MaxRounds - 1} przerwach — przestaję je przenosić "
            + "do końca sesji: " + info.Describe());
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
