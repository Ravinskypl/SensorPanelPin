using SensorPanelPin.Config;
using SensorPanelPin.Tests.TestSupport;
using SensorPanelPin.Windows;

namespace SensorPanelPin.Tests.Windows;

/// <summary>Only the pure <see cref="WindowQuery.Matches"/> logic; Get/EnumerateTopLevel need a desktop.</summary>
public sealed class WindowQueryMatchesTests
{
    private static readonly WindowInfo SensorPanel = Factories.Window(
        processName: "aida64",
        className: "TForm_HWMonitoringSensorPanel",
        title: "SensorPanel");

    [Fact]
    public void An_entirely_empty_rule_matches_nothing()
    {
        Assert.False(WindowQuery.Matches(SensorPanel, new WindowRule()));
    }

    [Fact]
    public void A_rule_with_only_whitespace_matches_nothing()
    {
        var rule = new WindowRule { ProcessName = "  ", ClassPattern = "\t", TitlePattern = " " };

        Assert.False(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void The_default_panel_rule_matches_the_real_sensor_panel_window()
    {
        var rule = new AppConfig().PanelRule;

        Assert.True(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void The_default_panel_rule_does_not_match_an_unrelated_window_of_the_same_process()
    {
        var mainWindow = Factories.Window(processName: "aida64", className: "TFormMain", title: "AIDA64 Extreme");

        Assert.False(WindowQuery.Matches(mainWindow, new AppConfig().PanelRule));
    }

    [Fact]
    public void A_process_only_rule_matches_every_window_of_that_process()
    {
        var rule = new WindowRule { ProcessName = "aida64" };

        Assert.True(WindowQuery.Matches(SensorPanel, rule));
        Assert.True(WindowQuery.Matches(Factories.Window(processName: "aida64", className: "Other", title: "Other"), rule));
        Assert.False(WindowQuery.Matches(Factories.Window(processName: "notepad"), rule));
    }

    [Fact]
    public void A_class_only_rule_ignores_process_and_title()
    {
        var rule = new WindowRule { ClassPattern = "TForm_*" };

        Assert.True(WindowQuery.Matches(SensorPanel, rule));
        Assert.False(WindowQuery.Matches(Factories.Window(className: "Notepad"), rule));
    }

    [Fact]
    public void A_title_only_rule_ignores_process_and_class()
    {
        var rule = new WindowRule { TitlePattern = "*Notepad" };

        Assert.True(WindowQuery.Matches(Factories.Window(title: "Untitled - Notepad"), rule));
        Assert.False(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void Every_non_empty_field_of_the_rule_must_match()
    {
        var rule = new WindowRule { ProcessName = "aida64", ClassPattern = "*SensorPanel*", TitlePattern = "SensorPanel" };

        Assert.True(WindowQuery.Matches(SensorPanel, rule));

        Assert.False(
            WindowQuery.Matches(Factories.Window(processName: "other", className: SensorPanel.ClassName, title: SensorPanel.Title), rule),
            "process differs");
        Assert.False(
            WindowQuery.Matches(Factories.Window(processName: SensorPanel.ProcessName, className: "Other", title: SensorPanel.Title), rule),
            "class differs");
        Assert.False(
            WindowQuery.Matches(Factories.Window(processName: SensorPanel.ProcessName, className: SensorPanel.ClassName, title: "Other"), rule),
            "title differs");
    }

    [Fact]
    public void A_blank_field_does_not_restrict_the_match_when_another_field_is_set()
    {
        var rule = new WindowRule { ProcessName = "aida64", ClassPattern = "   ", TitlePattern = string.Empty };

        Assert.True(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void Patterns_are_trimmed_before_matching()
    {
        var rule = new WindowRule { ProcessName = "  aida64  ", ClassPattern = " *SensorPanel* ", TitlePattern = " SensorPanel " };

        Assert.True(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var rule = new WindowRule { ProcessName = "AIDA64", ClassPattern = "*SENSORPANEL*" };

        Assert.True(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void A_process_name_without_wildcards_must_match_the_whole_name()
    {
        var rule = new WindowRule { ProcessName = "aida" };

        Assert.False(WindowQuery.Matches(SensorPanel, rule));
    }

    [Fact]
    public void A_window_whose_process_cannot_be_queried_does_not_match_a_process_rule()
    {
        var unknownProcess = Factories.Window(processName: string.Empty, className: "TForm_HWMonitoringSensorPanel", title: "SensorPanel");

        Assert.False(WindowQuery.Matches(unknownProcess, new WindowRule { ProcessName = "aida64" }));
    }

    [Fact]
    public void A_window_whose_process_cannot_be_queried_still_matches_a_class_only_rule()
    {
        var unknownProcess = Factories.Window(processName: string.Empty, className: "TForm_HWMonitoringSensorPanel", title: "SensorPanel");

        Assert.True(WindowQuery.Matches(unknownProcess, new WindowRule { ClassPattern = "*SensorPanel*" }));
    }
}
