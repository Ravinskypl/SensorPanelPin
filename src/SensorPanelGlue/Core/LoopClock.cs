namespace SensorPanelGlue.Core;

/// <summary>
/// Wakes the enforcement loop on the UI thread at the earliest deadline it was asked for.
///
/// This replaces three System.Windows.Forms.Timer instances that were restarted on every window
/// event. Measured on this machine, Timer.Stop() destroys the timer's hidden window and Start()
/// creates a new one - 208 microseconds per restart, about 150 times the cost of the work around
/// it. Worse, WM_TIMER is only synthesised while the message queue holds nothing else, so a burst of
/// window events silenced all three timers at once, including the safety poll that was meant to be
/// the backstop. That matches an incident where three display changes were logged and the delayed
/// sweep never ran.
///
/// Here a thread-pool timer posts to the UI thread instead. A posted message is queued in order with
/// everything else rather than generated only when the queue is idle, so it cannot be starved the
/// way WM_TIMER can, and re-arming is a cheap Change() call rather than a window create/destroy.
/// </summary>
internal sealed class LoopClock : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly Action _onTick;
    private readonly System.Threading.Timer _timer;

    private long _armedForTicks = long.MaxValue;
    private int _posted;
    private bool _disposed;

    public LoopClock(SynchronizationContext ui, Action onTick)
    {
        _ui = ui;
        _onTick = onTick;
        _timer = new System.Threading.Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Ensures a tick happens no later than <paramref name="dueUtc"/>. Cheap when an earlier tick is
    /// already armed, which is the common case during a burst of events. UI thread only.
    /// </summary>
    public void WakeAt(DateTime dueUtc)
    {
        if (_disposed || dueUtc.Ticks >= _armedForTicks)
        {
            return;
        }

        _armedForTicks = dueUtc.Ticks;
        var delay = (dueUtc - DateTime.UtcNow).TotalMilliseconds;
        _timer.Change((long)Math.Clamp(delay, 0, int.MaxValue - 1), Timeout.Infinite);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private void Fire(object? state)
    {
        // One outstanding post at a time; a slow tick must not stack up copies of itself.
        if (Interlocked.Exchange(ref _posted, 1) == 1)
        {
            return;
        }

        _ui.Post(
            _ =>
            {
                Volatile.Write(ref _posted, 0);
                _armedForTicks = long.MaxValue;
                if (!_disposed)
                {
                    _onTick();
                }
            },
            null);
    }
}

/// <summary>Deadline arithmetic for coalescing bursts, kept pure so it can be tested.</summary>
internal static class Debounce
{
    /// <summary>
    /// When a burst should be handled: once it has been quiet for <paramref name="quiet"/>, but
    /// never later than <paramref name="maxWait"/> after it began. Without the cap, a steady stream
    /// of events - a window being dragged, a game resizing - would postpone the work for as long as
    /// the stream lasted.
    /// </summary>
    public static DateTime DueAt(DateTime firstUtc, DateTime lastUtc, TimeSpan quiet, TimeSpan maxWait)
    {
        var settled = lastUtc + quiet;
        var capped = firstUtc + maxWait;
        return settled < capped ? settled : capped;
    }
}
