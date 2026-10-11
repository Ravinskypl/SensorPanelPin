using System.Text.Json;
using SensorPanelPin.Config;

namespace SensorPanelPin.Tests.Config;

public sealed class AppConfigTests
{
    // ---- Normalize: numeric clamping --------------------------------------------------------

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    [InlineData(int.MaxValue, 600)]
    [InlineData(int.MinValue, 1)]
    public void Normalize_clamps_SafetyPollSeconds(int input, int expected)
    {
        var config = new AppConfig { SafetyPollSeconds = input };

        config.Normalize();

        Assert.Equal(expected, config.SafetyPollSeconds);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 50)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(150, 150)]
    [InlineData(5000, 5000)]
    [InlineData(5001, 5000)]
    [InlineData(int.MaxValue, 5000)]
    public void Normalize_clamps_EventDebounceMs(int input, int expected)
    {
        var config = new AppConfig { EventDebounceMs = input };

        config.Normalize();

        Assert.Equal(expected, config.EventDebounceMs);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(0, 0)]
    [InlineData(2000, 2000)]
    [InlineData(60000, 60000)]
    [InlineData(60001, 60000)]
    [InlineData(int.MaxValue, 60000)]
    public void Normalize_clamps_DisplayChangeDelayMs(int input, int expected)
    {
        var config = new AppConfig { DisplayChangeDelayMs = input };

        config.Normalize();

        Assert.Equal(expected, config.DisplayChangeDelayMs);
    }

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    [InlineData(int.MaxValue, 50)]
    public void Normalize_clamps_PingPongLimit(int input, int expected)
    {
        var config = new AppConfig { PingPongLimit = input };

        config.Normalize();

        Assert.Equal(expected, config.PingPongLimit);
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    [InlineData(int.MaxValue, 600)]
    public void Normalize_clamps_PingPongWindowSeconds(int input, int expected)
    {
        var config = new AppConfig { PingPongWindowSeconds = input };

        config.Normalize();

        Assert.Equal(expected, config.PingPongWindowSeconds);
    }

    [Fact]
    public void Normalize_leaves_the_defaults_unchanged()
    {
        var config = new AppConfig();

        config.Normalize();

        Assert.Equal(5, config.SafetyPollSeconds);
        Assert.Equal(150, config.EventDebounceMs);
        Assert.Equal(2000, config.DisplayChangeDelayMs);
        Assert.Equal(5, config.PingPongLimit);
        Assert.Equal(10, config.PingPongWindowSeconds);
    }

    [Fact]
    public void Normalize_does_not_touch_non_numeric_settings()
    {
        var config = new AppConfig
        {
            PanelMonitorId = "panel-id",
            HostMonitorId = "host-id",
            KeepPanelPosition = false,
            RestorePanelSize = true,
            KeepPanelTopmost = true,
            HidePanelFromAltTab = true,
            RelocateForeignWindows = false,
            VerboseLogging = true,
            StartPaused = true,
            PanelGeometry = new PanelGeometry { OffsetX = -5, OffsetY = 7, Width = 1024, Height = 600 },
        };

        config.Normalize();

        Assert.Equal("panel-id", config.PanelMonitorId);
        Assert.Equal("host-id", config.HostMonitorId);
        Assert.False(config.KeepPanelPosition);
        Assert.True(config.RestorePanelSize);
        Assert.True(config.KeepPanelTopmost);
        Assert.True(config.HidePanelFromAltTab);
        Assert.False(config.RelocateForeignWindows);
        Assert.True(config.VerboseLogging);
        Assert.True(config.StartPaused);
        Assert.Equal(-5, config.PanelGeometry.OffsetX);
        Assert.Equal(7, config.PanelGeometry.OffsetY);
        Assert.Equal(1024, config.PanelGeometry.Width);
        Assert.Equal(600, config.PanelGeometry.Height);
    }

    // ---- Normalize: null handling -----------------------------------------------------------

    [Fact]
    public void Normalize_replaces_a_null_PanelRule_with_an_empty_rule()
    {
        var config = new AppConfig { PanelRule = null! };

        config.Normalize();

        Assert.NotNull(config.PanelRule);
        Assert.Equal(string.Empty, config.PanelRule.ProcessName);
        Assert.Equal(string.Empty, config.PanelRule.ClassPattern);
        Assert.Equal(string.Empty, config.PanelRule.TitlePattern);
    }

    [Fact]
    public void Normalize_replaces_a_null_PanelGeometry_with_an_unset_geometry()
    {
        var config = new AppConfig { PanelGeometry = null! };

        config.Normalize();

        Assert.NotNull(config.PanelGeometry);
        Assert.False(config.PanelGeometry.IsSet);
    }

    [Fact]
    public void Normalize_replaces_a_null_Exceptions_list_with_an_empty_list()
    {
        var config = new AppConfig { Exceptions = null! };

        config.Normalize();

        Assert.NotNull(config.Exceptions);
        Assert.Empty(config.Exceptions);
    }

    [Fact]
    public void Normalize_keeps_existing_non_null_objects_instead_of_replacing_them()
    {
        var rule = new WindowRule { ProcessName = "aida64" };
        var geometry = new PanelGeometry { Width = 10, Height = 20 };
        var exceptions = new List<WindowRule> { new() { ProcessName = "steam" } };
        var config = new AppConfig { PanelRule = rule, PanelGeometry = geometry, Exceptions = exceptions };

        config.Normalize();

        Assert.Same(rule, config.PanelRule);
        Assert.Same(geometry, config.PanelGeometry);
        Assert.Same(exceptions, config.Exceptions);
    }

    [Fact]
    public void Normalize_repairs_a_config_deserialized_from_json_with_explicit_nulls_and_wild_numbers()
    {
        const string json = """
            {
              "PanelRule": null,
              "PanelGeometry": null,
              "Exceptions": null,
              "SafetyPollSeconds": 0,
              "EventDebounceMs": 1,
              "DisplayChangeDelayMs": -100,
              "PingPongLimit": 9999,
              "PingPongWindowSeconds": 0
            }
            """;

        var config = JsonSerializer.Deserialize<AppConfig>(json)!;
        config.Normalize();

        Assert.NotNull(config.PanelRule);
        Assert.NotNull(config.PanelGeometry);
        Assert.NotNull(config.Exceptions);
        Assert.Equal(1, config.SafetyPollSeconds);
        Assert.Equal(50, config.EventDebounceMs);
        Assert.Equal(0, config.DisplayChangeDelayMs);
        Assert.Equal(50, config.PingPongLimit);
        Assert.Equal(1, config.PingPongWindowSeconds);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var config = new AppConfig { SafetyPollSeconds = 99999, EventDebounceMs = -4, PingPongLimit = 0 };

        config.Normalize();
        var once = JsonSerializer.Serialize(config);
        config.Normalize();

        Assert.Equal(once, JsonSerializer.Serialize(config));
    }

    // ---- Clone ------------------------------------------------------------------------------

    private static AppConfig FullyPopulated() => new()
    {
        PanelMonitorId = @"\\?\DISPLAY#EYA713A#panel",
        HostMonitorId = @"\\?\DISPLAY#AW3423DWF#host",
        PanelRule = new WindowRule { ProcessName = "aida64", ClassPattern = "*SensorPanel*", TitlePattern = "Sensor*" },
        PanelGeometry = new PanelGeometry { OffsetX = -5, OffsetY = 3, Width = 1024, Height = 600 },
        KeepPanelPosition = false,
        RestorePanelSize = true,
        KeepPanelTopmost = true,
        HidePanelFromAltTab = true,
        RelocateForeignWindows = false,
        Exceptions = new List<WindowRule>
        {
            new() { ProcessName = "steam", ClassPattern = "SDL_app", TitlePattern = "Steam" },
            new() { ProcessName = "obs64", ClassPattern = string.Empty, TitlePattern = string.Empty },
        },
        SafetyPollSeconds = 11,
        EventDebounceMs = 222,
        DisplayChangeDelayMs = 3333,
        PingPongLimit = 7,
        PingPongWindowSeconds = 13,
        VerboseLogging = true,
        StartPaused = true,
    };

    [Fact]
    public void Clone_copies_every_setting()
    {
        var original = FullyPopulated();

        var clone = original.Clone();

        // Serialized form covers every public property, including ones added later and set here.
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(clone));
    }

    [Fact]
    public void Clone_returns_a_new_instance()
    {
        var original = FullyPopulated();

        Assert.NotSame(original, original.Clone());
    }

    [Fact]
    public void Clone_does_not_share_the_PanelRule_instance()
    {
        var original = FullyPopulated();
        var clone = original.Clone();

        Assert.NotSame(original.PanelRule, clone.PanelRule);

        clone.PanelRule.ProcessName = "changed";
        clone.PanelRule.ClassPattern = "changed";
        clone.PanelRule.TitlePattern = "changed";

        Assert.Equal("aida64", original.PanelRule.ProcessName);
        Assert.Equal("*SensorPanel*", original.PanelRule.ClassPattern);
        Assert.Equal("Sensor*", original.PanelRule.TitlePattern);
    }

    [Fact]
    public void Clone_does_not_share_the_PanelGeometry_instance()
    {
        var original = FullyPopulated();
        var clone = original.Clone();

        Assert.NotSame(original.PanelGeometry, clone.PanelGeometry);

        clone.PanelGeometry.OffsetX = 100;
        clone.PanelGeometry.OffsetY = 100;
        clone.PanelGeometry.Width = 1;
        clone.PanelGeometry.Height = 1;

        Assert.Equal(-5, original.PanelGeometry.OffsetX);
        Assert.Equal(3, original.PanelGeometry.OffsetY);
        Assert.Equal(1024, original.PanelGeometry.Width);
        Assert.Equal(600, original.PanelGeometry.Height);
    }

    [Fact]
    public void Clone_does_not_share_the_Exceptions_list_or_its_rules()
    {
        var original = FullyPopulated();
        var clone = original.Clone();

        Assert.NotSame(original.Exceptions, clone.Exceptions);
        Assert.NotSame(original.Exceptions[0], clone.Exceptions[0]);

        clone.Exceptions[0].ProcessName = "changed";
        clone.Exceptions[0].ClassPattern = "changed";
        clone.Exceptions[0].TitlePattern = "changed";
        clone.Exceptions.Add(new WindowRule { ProcessName = "added" });
        clone.Exceptions.RemoveAt(1);

        Assert.Equal(2, original.Exceptions.Count);
        Assert.Equal("steam", original.Exceptions[0].ProcessName);
        Assert.Equal("SDL_app", original.Exceptions[0].ClassPattern);
        Assert.Equal("Steam", original.Exceptions[0].TitlePattern);
        Assert.Equal("obs64", original.Exceptions[1].ProcessName);
    }

    [Fact]
    public void Mutating_the_original_after_cloning_does_not_affect_the_clone()
    {
        var original = FullyPopulated();
        var clone = original.Clone();

        original.PanelRule.ProcessName = "changed";
        original.PanelGeometry.Width = 1;
        original.Exceptions.Clear();
        original.SafetyPollSeconds = 99;

        Assert.Equal("aida64", clone.PanelRule.ProcessName);
        Assert.Equal(1024, clone.PanelGeometry.Width);
        Assert.Equal(2, clone.Exceptions.Count);
        Assert.Equal(11, clone.SafetyPollSeconds);
    }

    [Fact]
    public void Clone_of_a_default_config_has_an_empty_independent_exception_list()
    {
        var original = new AppConfig();
        var clone = original.Clone();

        Assert.Empty(clone.Exceptions);

        clone.Exceptions.Add(new WindowRule { ProcessName = "x" });

        Assert.Empty(original.Exceptions);
    }

    // ---- PanelGeometry / WindowRule helpers that the config depends on ----------------------

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1024, 0, false)]
    [InlineData(0, 600, false)]
    [InlineData(-1, 600, false)]
    [InlineData(1024, 600, true)]
    public void PanelGeometry_IsSet_requires_a_positive_width_and_height(int width, int height, bool expected)
    {
        var geometry = new PanelGeometry { Width = width, Height = height };

        Assert.Equal(expected, geometry.IsSet);
    }

    [Fact]
    public void PanelGeometry_IsSet_is_not_serialized()
    {
        var json = JsonSerializer.Serialize(new PanelGeometry { Width = 10, Height = 10 });

        Assert.DoesNotContain("IsSet", json);
    }

    // ---- Hand-edited files (regression: [null] made the configuration window refuse to open) ----

    [Fact]
    public void Null_entries_in_the_exception_list_are_dropped_so_Clone_cannot_fail()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """{ "Exceptions": [ null, { "ProcessName": "steam" }, null ] }""")!;

        config.Normalize();

        Assert.Single(config.Exceptions);
        Assert.Equal("steam", config.Exceptions[0].ProcessName);
        Assert.NotNull(config.Clone());
    }

    [Fact]
    public void Null_strings_become_empty_everywhere()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """
            {
              "PanelMonitorId": null,
              "HostMonitorId": null,
              "PanelRule": { "ProcessName": null, "ClassPattern": null, "TitlePattern": null },
              "Exceptions": [ { "ProcessName": null, "ClassPattern": null, "TitlePattern": null } ]
            }
            """)!;

        config.Normalize();

        Assert.Equal(string.Empty, config.PanelMonitorId);
        Assert.Equal(string.Empty, config.HostMonitorId);
        Assert.Equal(string.Empty, config.PanelRule.ProcessName);
        Assert.Equal(string.Empty, config.PanelRule.ClassPattern);
        Assert.Equal(string.Empty, config.PanelRule.TitlePattern);
        Assert.Equal(string.Empty, config.Exceptions[0].ProcessName);
        Assert.Equal(string.Empty, config.Exceptions[0].TitlePattern);
    }

    [Fact]
    public void Negative_geometry_is_reset_to_unset()
    {
        var config = new AppConfig { PanelGeometry = new PanelGeometry { OffsetX = 5, Width = -1029, Height = 600 } };

        config.Normalize();

        Assert.False(config.PanelGeometry.IsSet);
        Assert.Equal(0, config.PanelGeometry.OffsetX);
    }
}
