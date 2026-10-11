using System.Drawing;
using System.Reflection;
using SensorPanelGlue.Monitors;
using SensorPanelGlue.Tests.TestSupport;

namespace SensorPanelGlue.Tests.Monitors;

public sealed class MonitorRegistryTests : IDisposable
{
    private readonly TempLog _log = new();

    public void Dispose() => _log.Dispose();

    private static bool CheckConsistency(params MonitorInfo[] monitors) => MonitorRegistry.CheckConsistency(monitors);

    // Refresh() enumerates real displays; the internal constructor takes a fixed list instead.
    private MonitorRegistry RegistryWith(params MonitorInfo[] monitors) => new(_log.Logger, monitors);

    // ---- CheckConsistency -------------------------------------------------------------------

    [Fact]
    public void Normal_two_monitor_layout_is_consistent()
    {
        Assert.True(CheckConsistency(Factories.NormalTwoMonitorLayout()));
    }

    [Fact]
    public void Secondary_monitor_reported_at_origin_overlapping_primary_is_not_consistent()
    {
        // The real incident: a monitor being switched off was still enumerated, but at (0,0).
        var layout = new[]
        {
            Factories.Monitor(0, 0, 3440, 1440, "primary", primary: true),
            Factories.Monitor(0, 0, 1024, 600, "panel"),
        };

        Assert.False(CheckConsistency(layout));
    }

    [Fact]
    public void Overlap_is_detected_regardless_of_monitor_order()
    {
        var layout = new[]
        {
            Factories.Monitor(0, 0, 1024, 600, "panel"),
            Factories.Monitor(0, 0, 3440, 1440, "primary", primary: true),
        };

        Assert.False(CheckConsistency(layout));
    }

    [Fact]
    public void Cloned_monitors_sharing_an_identical_rectangle_are_consistent()
    {
        var layout = new[]
        {
            Factories.Monitor(0, 0, 1920, 1080, "a", primary: true),
            Factories.Monitor(0, 0, 1920, 1080, "b"),
        };

        Assert.True(CheckConsistency(layout));
    }

    [Fact]
    public void Clones_next_to_a_third_monitor_are_consistent()
    {
        var layout = new[]
        {
            Factories.Monitor(0, 0, 1920, 1080, "a", primary: true),
            Factories.Monitor(0, 0, 1920, 1080, "b"),
            Factories.Monitor(1920, 0, 1024, 600, "c"),
        };

        Assert.True(CheckConsistency(layout));
    }

    [Fact]
    public void Empty_monitor_list_is_not_consistent()
    {
        Assert.False(CheckConsistency());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 600)]
    [InlineData(1024, 0)]
    [InlineData(-1024, 600)]
    public void A_monitor_with_zero_or_negative_size_is_not_consistent(int width, int height)
    {
        var layout = new[]
        {
            Factories.Monitor(0, 0, 3440, 1440, "primary", primary: true),
            Factories.Monitor(3440, 0, width, height, "broken"),
        };

        Assert.False(CheckConsistency(layout));
    }

    [Fact]
    public void A_single_zero_size_monitor_is_not_consistent()
    {
        Assert.False(CheckConsistency(Factories.Monitor(0, 0, 0, 0, "ghost", primary: true)));
    }

    [Fact]
    public void A_single_monitor_alone_at_the_origin_is_consistent()
    {
        Assert.True(CheckConsistency(Factories.Monitor(0, 0, 1920, 1080, "only", primary: true)));
    }

    [Fact]
    public void Monitor_left_of_the_primary_with_negative_coordinates_is_consistent()
    {
        var layout = new[]
        {
            Factories.Monitor(-1920, 0, 1920, 1080, "left"),
            Factories.Monitor(0, 0, 3440, 1440, "primary", primary: true),
        };

        Assert.True(CheckConsistency(layout));
    }

    [Fact]
    public void Monitors_stacked_vertically_and_touching_are_consistent()
    {
        var layout = new[]
        {
            Factories.Monitor(0, -1080, 1920, 1080, "above"),
            Factories.Monitor(0, 0, 1920, 1080, "primary", primary: true),
        };

        Assert.True(CheckConsistency(layout));
    }

    [Fact]
    public void Partially_overlapping_monitors_are_not_consistent()
    {
        var layout = new[]
        {
            Factories.Monitor(0, 0, 1920, 1080, "a", primary: true),
            Factories.Monitor(1900, 0, 1920, 1080, "b"),
        };

        Assert.False(CheckConsistency(layout));
    }

    [Fact]
    public void An_overlap_between_non_adjacent_entries_is_detected()
    {
        // The overlapping pair is (first, third); a check that only compares neighbours would miss it.
        var layout = new[]
        {
            Factories.Monitor(0, 0, 3440, 1440, "primary", primary: true),
            Factories.Monitor(3440, 0, 1024, 600, "panel"),
            Factories.Monitor(100, 100, 500, 500, "ghost"),
        };

        Assert.False(CheckConsistency(layout));
    }

    // ---- FromRect ---------------------------------------------------------------------------

    [Fact]
    public void FromRect_picks_the_monitor_containing_the_rectangle()
    {
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        Assert.Equal("panel", registry.FromRect(new Rectangle(3500, 50, 300, 200))?.StableId);
        Assert.Equal("primary", registry.FromRect(new Rectangle(100, 100, 300, 200))?.StableId);
    }

    [Fact]
    public void FromRect_picks_the_monitor_with_the_largest_intersection()
    {
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        // 1000 wide, starting 100 px left of the border: 100 px on primary, 900 px on the panel screen.
        var mostlyOnPanel = new Rectangle(3340, 0, 1000, 500);
        Assert.Equal("panel", registry.FromRect(mostlyOnPanel)?.StableId);

        // 1000 wide, starting 900 px left of the border: 900 px on primary, 100 px on the panel screen.
        var mostlyOnPrimary = new Rectangle(2540, 0, 1000, 500);
        Assert.Equal("primary", registry.FromRect(mostlyOnPrimary)?.StableId);
    }

    [Fact]
    public void FromRect_uses_intersection_area_not_the_position_of_the_top_left_corner()
    {
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        // The top-left corner is on the primary, but almost the whole window is on the panel screen.
        var rect = new Rectangle(3430, 0, 1000, 600);

        Assert.Equal("panel", registry.FromRect(rect)?.StableId);
    }

    [Theory]
    [InlineData(10000, 10000, "panel")]   // far to the lower right: the panel screen is nearer
    [InlineData(-5000, 100, "primary")]   // far to the left of everything
    [InlineData(5000, 100, "panel")]      // just right of the panel screen, entirely off-screen
    public void FromRect_falls_back_to_the_nearest_centre_for_an_off_screen_rectangle(int x, int y, string expectedId)
    {
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        var monitor = registry.FromRect(new Rectangle(x, y, 100, 100));

        Assert.NotNull(monitor);
        Assert.Equal(expectedId, monitor!.StableId);
    }

    [Fact]
    public void FromRect_with_an_empty_rectangle_inside_a_monitor_still_resolves_that_monitor()
    {
        // A zero-size rectangle has zero intersection area everywhere, so it goes through the
        // nearest-centre path; it must still land on the monitor it sits on.
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        Assert.Equal("panel", registry.FromRect(new Rectangle(3500, 100, 0, 0))?.StableId);
    }

    [Fact]
    public void FromRect_returns_null_when_there_are_no_monitors()
    {
        var registry = RegistryWith();

        Assert.Null(registry.FromRect(new Rectangle(0, 0, 100, 100)));
    }

    [Fact]
    public void FromRect_returns_null_for_off_screen_rectangle_when_there_are_no_monitors()
    {
        var registry = RegistryWith();

        Assert.Null(registry.FromRect(new Rectangle(9000, 9000, 100, 100)));
    }

    // ---- Lookup helpers that sit on the same list -------------------------------------------

    [Fact]
    public void FindById_is_case_insensitive_and_null_safe()
    {
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        Assert.Equal("panel", registry.FindById("PANEL")?.StableId);
        Assert.Null(registry.FindById("missing"));
        Assert.Null(registry.FindById(null));
        Assert.Null(registry.FindById(string.Empty));
    }

    [Fact]
    public void ResolveHost_falls_back_to_the_primary_when_the_configured_monitor_is_absent()
    {
        var registry = RegistryWith(Factories.NormalTwoMonitorLayout());

        Assert.Equal("panel", registry.ResolveHost("panel")?.StableId);
        Assert.Equal("primary", registry.ResolveHost("unplugged")?.StableId);
        Assert.Equal("primary", registry.ResolveHost(string.Empty)?.StableId);
    }

    [Fact]
    public void Primary_falls_back_to_the_first_monitor_when_none_is_flagged_primary()
    {
        var registry = RegistryWith(
            Factories.Monitor(0, 0, 1920, 1080, "first"),
            Factories.Monitor(1920, 0, 1920, 1080, "second"));

        Assert.Equal("first", registry.Primary?.StableId);
    }
}
