using SensorPanelPin.Windows;

namespace SensorPanelPin.Tests.Windows;

public sealed class WildcardTests
{
    [Theory]
    [InlineData("SensorPanel", "*SensorPanel*")]
    [InlineData("TForm_HWMonitoringSensorPanel", "*SensorPanel*")]
    [InlineData("SensorPanel 2", "*SensorPanel*")]
    [InlineData("abc", "a*")]
    [InlineData("abc", "*c")]
    [InlineData("abc", "a*c")]
    [InlineData("ac", "a*c")]
    [InlineData("anything", "*")]
    [InlineData("", "*")]
    public void Star_matches_any_run_of_characters_including_none(string value, string pattern)
    {
        Assert.True(Wildcard.IsMatch(value, pattern));
    }

    [Theory]
    [InlineData("abc", "a?c")]
    [InlineData("a-c", "a?c")]
    [InlineData("abc", "???")]
    [InlineData("a", "?")]
    public void Question_mark_matches_exactly_one_character(string value, string pattern)
    {
        Assert.True(Wildcard.IsMatch(value, pattern));
    }

    [Theory]
    [InlineData("ac", "a?c")]
    [InlineData("abbc", "a?c")]
    [InlineData("", "?")]
    [InlineData("ab", "?")]
    public void Question_mark_does_not_match_zero_or_several_characters(string value, string pattern)
    {
        Assert.False(Wildcard.IsMatch(value, pattern));
    }

    [Theory]
    [InlineData("ABC", "abc")]
    [InlineData("abc", "ABC")]
    [InlineData("AiDa64", "aida64")]
    [InlineData("TForm_HWMonitoringSensorPanel", "*sensorpanel*")]
    public void Matching_is_case_insensitive(string value, string pattern)
    {
        Assert.True(Wildcard.IsMatch(value, pattern));
    }

    [Theory]
    [InlineData("aida64x", "aida64")]
    [InlineData("xaida64", "aida64")]
    [InlineData("aida64 extreme", "aida64")]
    [InlineData("", "aida64")]
    public void A_pattern_without_wildcards_must_match_the_whole_value(string value, string pattern)
    {
        Assert.False(Wildcard.IsMatch(value, pattern));
    }

    [Fact]
    public void A_pattern_without_wildcards_matches_an_identical_value()
    {
        Assert.True(Wildcard.IsMatch("aida64", "aida64"));
    }

    [Fact]
    public void Prefix_and_suffix_patterns_are_anchored()
    {
        Assert.False(Wildcard.IsMatch("xabc", "abc*"));
        Assert.False(Wildcard.IsMatch("abcx", "*abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_pattern_matches_everything(string? pattern)
    {
        Assert.True(Wildcard.IsMatch("anything at all", pattern));
        Assert.True(Wildcard.IsMatch(string.Empty, pattern));
        Assert.True(Wildcard.IsMatch(null, pattern));
    }

    [Fact]
    public void A_null_value_is_treated_as_an_empty_string()
    {
        Assert.True(Wildcard.IsMatch(null, "*"));
        Assert.False(Wildcard.IsMatch(null, "?"));
        Assert.False(Wildcard.IsMatch(null, "abc"));
    }

    [Theory]
    [InlineData("a.c", "a.c", true)]
    [InlineData("abc", "a.c", false)]     // '.' is a literal dot, not "any character"
    [InlineData("a+c", "a+c", true)]
    [InlineData("aac", "a+c", false)]
    [InlineData("(x)", "(x)", true)]
    [InlineData("x", "(x)", false)]
    [InlineData("[ab]", "[ab]", true)]
    [InlineData("a", "[ab]", false)]
    [InlineData("a|b", "a|b", true)]
    [InlineData("a", "a|b", false)]
    [InlineData("^abc$", "^abc$", true)]
    [InlineData("abc", "^abc$", false)]
    [InlineData(@"C:\dir", @"C:\dir", true)]
    [InlineData("a{2}", "a{2}", true)]
    [InlineData("aa", "a{2}", false)]
    [InlineData("a b", "a b", true)]
    [InlineData("a#b", "a#b", true)]
    public void Regex_metacharacters_in_the_pattern_are_literal(string value, string pattern, bool expected)
    {
        Assert.Equal(expected, Wildcard.IsMatch(value, pattern));
    }

    [Fact]
    public void Wildcards_combine_with_literal_metacharacters()
    {
        Assert.True(Wildcard.IsMatch("Setup (x64) v1.2", "Setup (x64) v?.?"));
        Assert.False(Wildcard.IsMatch("Setup (x86) v1.2", "Setup (x64) v?.?"));
    }

    [Fact]
    public void The_same_pattern_gives_the_same_answer_on_repeated_calls()
    {
        // The compiled regex is cached; a second lookup must not change the result.
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Wildcard.IsMatch("Notepad", "note*"));
            Assert.False(Wildcard.IsMatch("Calculator", "note*"));
        }
    }

    [Fact]
    public void Matching_keeps_working_after_the_pattern_cache_overflows()
    {
        // The cache is cleared past 256 entries; results must be unaffected.
        for (var i = 0; i < 300; i++)
        {
            Assert.True(Wildcard.IsMatch("value" + i, "value" + i));
        }

        Assert.True(Wildcard.IsMatch("value1", "value1"));
        Assert.False(Wildcard.IsMatch("value1", "value2"));
    }

    // ---- Regressions: both were real defects, caught by these tests before the fix. ----

    [Fact]
    public void A_pattern_without_wildcards_does_not_match_a_value_with_a_trailing_newline()
    {
        // Regex '$' also matches before a final '\n'; the translation now anchors with \z.
        Assert.False(Wildcard.IsMatch("abc\n", "abc"));
    }

    [Fact]
    public void Star_matches_across_line_breaks_in_the_value()
    {
        // '.' only crosses '\n' with RegexOptions.Singleline.
        Assert.True(Wildcard.IsMatch("a\nb", "a*b"));
    }

    [Fact]
    public void Consecutive_stars_behave_like_one()
    {
        Assert.True(Wildcard.IsMatch("SensorPanel", "***Sensor**Panel***"));
        Assert.False(Wildcard.IsMatch("Sensor", "***Panel**"));
    }

    [Fact]
    public void A_hostile_title_cannot_stall_matching()
    {
        // Several literals between stars backtrack quadratically; window titles are attacker-chosen.
        var hostile = new string('a', 200_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var matched = Wildcard.IsMatch(hostile, "*a*a*a*a*a*b*");

        watch.Stop();
        Assert.False(matched);
        Assert.True(watch.ElapsedMilliseconds < 2_000, $"Matching took {watch.ElapsedMilliseconds} ms.");
    }
}
