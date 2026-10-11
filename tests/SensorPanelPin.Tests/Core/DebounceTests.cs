using SensorPanelPin.Core;

namespace SensorPanelPin.Tests.Core;

public sealed class DebounceTests
{
    private static readonly DateTime T0 = new(2026, 9, 6, 11, 45, 47, DateTimeKind.Utc);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);

    [Fact]
    public void A_single_event_is_handled_once_the_quiet_period_has_passed()
    {
        Assert.Equal(T0 + Quiet, Debounce.DueAt(T0, T0, Quiet, MaxWait));
    }

    [Fact]
    public void A_later_event_in_the_burst_pushes_the_deadline_back()
    {
        var last = T0 + TimeSpan.FromMilliseconds(400);

        Assert.Equal(last + Quiet, Debounce.DueAt(T0, last, Quiet, MaxWait));
    }

    [Fact]
    public void A_never_ending_stream_is_still_handled_after_the_maximum_wait()
    {
        // Events every 10 ms for 5 s: without the cap the work would wait for the whole stream,
        // which is how the delayed sweep went missing during a game.
        DateTime due = default;
        for (var ms = 0; ms <= 5_000; ms += 10)
        {
            due = Debounce.DueAt(T0, T0 + TimeSpan.FromMilliseconds(ms), Quiet, MaxWait);
        }

        Assert.Equal(T0 + MaxWait, due);
    }

    [Fact]
    public void The_cap_wins_exactly_when_the_burst_would_run_past_it()
    {
        var justBefore = T0 + MaxWait - Quiet - TimeSpan.FromMilliseconds(1);
        var justAfter = T0 + MaxWait - Quiet + TimeSpan.FromMilliseconds(1);

        Assert.Equal(justBefore + Quiet, Debounce.DueAt(T0, justBefore, Quiet, MaxWait));
        Assert.Equal(T0 + MaxWait, Debounce.DueAt(T0, justAfter, Quiet, MaxWait));
    }
}
