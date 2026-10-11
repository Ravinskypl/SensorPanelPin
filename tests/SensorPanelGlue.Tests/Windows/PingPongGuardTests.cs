using SensorPanelGlue.Tests.TestSupport;
using SensorPanelGlue.Windows;

namespace SensorPanelGlue.Tests.Windows;

/// <summary>
/// PingPongGuard reads DateTime.UtcNow directly (no clock seam), so the cool-down tests use a short
/// real Cooldown instead of a fake clock. Recommendation: inject a TimeProvider.
/// </summary>
public sealed class PingPongGuardTests : IDisposable
{
    // Mirrors the private PingPongGuard.MaxRounds constant (documented in the README as "three pauses").
    private const int MaxRounds = 3;

    private static readonly TimeSpan ShortCooldown = TimeSpan.FromMilliseconds(200);

    private readonly TempLog _log = new();

    public void Dispose() => _log.Dispose();

    private PingPongGuard CreateGuard(int limit = 3)
    {
        return new PingPongGuard(_log.Logger)
        {
            Limit = limit,
            Cooldown = ShortCooldown,
        };
    }

    private static void WaitForCooldownToElapse(PingPongGuard guard) =>
        Thread.Sleep(guard.Cooldown + TimeSpan.FromMilliseconds(150));

    /// <summary>Registers Limit moves (all allowed) followed by one more that trips the guard.</summary>
    private static bool TripTheLimit(PingPongGuard guard, WindowInfo window)
    {
        for (var i = 0; i < guard.Limit; i++)
        {
            Assert.True(guard.RegisterMove(window), $"move {i + 1} of {guard.Limit} should be allowed");
        }

        return guard.RegisterMove(window);
    }

    [Fact]
    public void Moves_up_to_the_limit_are_allowed()
    {
        var guard = CreateGuard(limit: 3);
        var window = Factories.Window();

        Assert.True(guard.RegisterMove(window));
        Assert.True(guard.RegisterMove(window));
        Assert.True(guard.RegisterMove(window));

        Assert.False(guard.IsBlocked(window.Handle));
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void Exceeding_the_limit_blocks_the_window_temporarily_without_listing_it_as_a_problem()
    {
        var guard = CreateGuard(limit: 3);
        var window = Factories.Window();

        var allowed = TripTheLimit(guard, window);

        Assert.False(allowed, "the move beyond the limit must be refused");
        Assert.True(guard.IsBlocked(window.Handle));
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void Steam_style_burst_of_three_moves_under_the_default_limit_is_never_blocked()
    {
        // Regression: Steam repositions its window three times within 1.4 s while launching.
        var guard = new PingPongGuard(_log.Logger);
        var steam = Factories.Window(handle: 0x5700, processName: "steam", className: "SDL_app", title: "Steam");

        Assert.Equal(5, guard.Limit);
        Assert.True(guard.RegisterMove(steam));
        Assert.True(guard.RegisterMove(steam));
        Assert.True(guard.RegisterMove(steam));
        Assert.False(guard.IsBlocked(steam.Handle));
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void Moves_during_the_cooldown_are_refused_and_do_not_escalate_towards_abandonment()
    {
        var guard = CreateGuard(limit: 2);
        var window = Factories.Window();

        Assert.False(TripTheLimit(guard, window));

        // Hammering a paused window must not count as additional rounds.
        for (var i = 0; i < 20; i++)
        {
            Assert.False(guard.RegisterMove(window));
        }

        WaitForCooldownToElapse(guard);

        Assert.True(guard.RegisterMove(window), "the window gets another chance once the cool-down is over");
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void After_the_cooldown_the_window_is_unblocked_and_gets_a_full_new_allowance()
    {
        var guard = CreateGuard(limit: 3);
        var window = Factories.Window();

        Assert.False(TripTheLimit(guard, window));
        Assert.True(guard.IsBlocked(window.Handle));

        WaitForCooldownToElapse(guard);

        Assert.False(guard.IsBlocked(window.Handle));
        Assert.True(guard.RegisterMove(window));
        Assert.True(guard.RegisterMove(window));
        Assert.True(guard.RegisterMove(window));
        Assert.False(guard.RegisterMove(window), "the fourth move of the new round trips the limit again");
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void A_window_is_abandoned_and_listed_only_after_the_maximum_number_of_cooldowns()
    {
        var guard = CreateGuard(limit: 2);
        var window = Factories.Window(handle: 0x2A, processName: "stubborn", className: "StubbornWnd", title: "Stubborn App");

        // Rounds 1 and 2 end in a cool-down: the window is paused but not on the Problems list.
        for (var round = 1; round < MaxRounds; round++)
        {
            Assert.False(TripTheLimit(guard, window));
            Assert.Empty(guard.Problems);
            WaitForCooldownToElapse(guard);
            Assert.False(guard.IsBlocked(window.Handle), $"round {round}: cool-down should have expired");
        }

        // Round 3 gives up for good.
        Assert.False(TripTheLimit(guard, window));

        var problem = Assert.Single(guard.Problems);
        Assert.Contains("stubborn", problem.Description);
        Assert.Contains("Stubborn App", problem.Description);
        Assert.Contains("StubbornWnd", problem.Description);
        Assert.True(guard.IsBlocked(window.Handle));
    }

    [Fact]
    public void An_abandoned_window_stays_blocked_after_the_cooldown_has_elapsed()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window();

        for (var round = 1; round <= MaxRounds; round++)
        {
            Assert.False(TripTheLimit(guard, window));
            WaitForCooldownToElapse(guard);
        }

        Assert.True(guard.IsBlocked(window.Handle));
        Assert.False(guard.RegisterMove(window));
        Assert.Single(guard.Problems);
    }

    [Fact]
    public void Abandonment_is_logged_as_a_warning_and_a_cooldown_as_info()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window();

        Assert.False(TripTheLimit(guard, window));
        var afterFirstRound = _log.ReadAll();
        Assert.Contains("[INFO ]", afterFirstRound);
        Assert.DoesNotContain("[WARN ]", afterFirstRound);

        WaitForCooldownToElapse(guard);
        Assert.False(TripTheLimit(guard, window));
        WaitForCooldownToElapse(guard);
        Assert.False(TripTheLimit(guard, window));

        Assert.Contains("[WARN ]", _log.ReadAll());
    }

    [Fact]
    public void Clear_releases_an_abandoned_window_and_empties_the_problem_list()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window();

        for (var round = 1; round <= MaxRounds; round++)
        {
            Assert.False(TripTheLimit(guard, window));
            WaitForCooldownToElapse(guard);
        }

        Assert.NotEmpty(guard.Problems);

        guard.Clear();

        Assert.Empty(guard.Problems);
        Assert.False(guard.IsBlocked(window.Handle));
        Assert.True(guard.RegisterMove(window));
    }

    [Fact]
    public void Clear_releases_a_window_that_is_in_the_middle_of_a_cooldown()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window();

        Assert.False(TripTheLimit(guard, window));
        Assert.True(guard.IsBlocked(window.Handle));

        guard.Clear();

        Assert.False(guard.IsBlocked(window.Handle));
        Assert.True(guard.RegisterMove(window));
    }

    [Fact]
    public void Clear_also_resets_the_round_counter()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window();

        // Two cool-downs used up, one short of abandonment.
        for (var round = 1; round < MaxRounds; round++)
        {
            Assert.False(TripTheLimit(guard, window));
            WaitForCooldownToElapse(guard);
        }

        guard.Clear();

        // A fresh start: one more trip is only round 1 again, not abandonment.
        Assert.False(TripTheLimit(guard, window));
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void Prune_drops_state_for_dead_handles_and_keeps_live_ones()
    {
        var guard = CreateGuard(limit: 1);
        var dead = Factories.Window(handle: 0xDEAD);
        var alive = Factories.Window(handle: 0xA11E);

        foreach (var window in new[] { dead, alive })
        {
            for (var round = 1; round <= MaxRounds; round++)
            {
                Assert.False(TripTheLimit(guard, window));
                WaitForCooldownToElapse(guard);
            }
        }

        Assert.Equal(2, guard.Problems.Count);

        guard.Prune(handle => handle == alive.Handle);

        var remaining = Assert.Single(guard.Problems);
        Assert.Contains("notepad", remaining.Description);
        Assert.False(guard.IsBlocked(dead.Handle), "a pruned handle has no state left, so it is no longer blocked");
        Assert.True(guard.IsBlocked(alive.Handle));
    }

    [Fact]
    public void Prune_with_every_handle_alive_changes_nothing()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window();

        Assert.False(TripTheLimit(guard, window));

        guard.Prune(_ => true);

        Assert.True(guard.IsBlocked(window.Handle));
    }

    [Fact]
    public void Prune_discards_the_round_history_of_a_dead_handle_that_is_reused()
    {
        var guard = CreateGuard(limit: 1);
        var window = Factories.Window(handle: 0x77);

        // Two cool-downs used up on the old window.
        for (var round = 1; round < MaxRounds; round++)
        {
            Assert.False(TripTheLimit(guard, window));
            WaitForCooldownToElapse(guard);
        }

        guard.Prune(_ => false);

        // The OS recycled the handle for a new window: it must start from round 1, not be abandoned.
        Assert.False(TripTheLimit(guard, window));
        Assert.Empty(guard.Problems);
    }

    [Fact]
    public void Windows_are_tracked_independently()
    {
        var guard = CreateGuard(limit: 2);
        var noisy = Factories.Window(handle: 0x1);
        var quiet = Factories.Window(handle: 0x2);

        Assert.False(TripTheLimit(guard, noisy));

        Assert.True(guard.IsBlocked(noisy.Handle));
        Assert.False(guard.IsBlocked(quiet.Handle));
        Assert.True(guard.RegisterMove(quiet));
    }

    [Fact]
    public void Moves_spread_beyond_the_window_do_not_accumulate()
    {
        var guard = CreateGuard(limit: 2);
        guard.Window = TimeSpan.FromMilliseconds(200);
        var window = Factories.Window();

        Assert.True(guard.RegisterMove(window));
        Assert.True(guard.RegisterMove(window));

        Thread.Sleep(350); // longer than the counting window: the count starts over

        Assert.True(guard.RegisterMove(window));
        Assert.True(guard.RegisterMove(window));
        Assert.False(guard.IsBlocked(window.Handle));
    }

    [Fact]
    public void An_unknown_handle_is_not_blocked()
    {
        var guard = CreateGuard();

        Assert.False(guard.IsBlocked(new IntPtr(0x9999)));
    }

    [Fact]
    public void Default_cooldown_is_thirty_seconds_for_the_default_window()
    {
        var guard = new PingPongGuard(_log.Logger);

        Assert.Equal(TimeSpan.FromSeconds(30), guard.Cooldown);
    }

    [Fact]
    public void Default_cooldown_never_drops_below_fifteen_seconds()
    {
        var guard = new PingPongGuard(_log.Logger) { Window = TimeSpan.FromSeconds(1) };

        Assert.Equal(TimeSpan.FromSeconds(15), guard.Cooldown);
    }

    [Fact]
    public void An_explicit_cooldown_overrides_the_derived_default()
    {
        var guard = new PingPongGuard(_log.Logger) { Window = TimeSpan.FromSeconds(100) };

        guard.Cooldown = TimeSpan.FromSeconds(7);

        Assert.Equal(TimeSpan.FromSeconds(7), guard.Cooldown);
    }
}
