using SensorPanelPin.Diagnostics;
using SensorPanelPin.Tests.TestSupport;

namespace SensorPanelPin.Tests.Diagnostics;

public sealed class LoggerTests : IDisposable
{
    // Mirrors the private Logger constants (documented in the README as "1 MB x 5 files").
    private const long RotationThresholdBytes = 1024 * 1024;
    private const int MaxFiles = 5;

    private readonly TempLog _temp = new();

    private Logger Log => _temp.Logger;

    public void Dispose() => _temp.Dispose();

    private string[] Lines() =>
        _temp.ReadAll().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    // ---- Line forging and tampered targets --------------------------------------------------

    [Fact]
    public void A_line_break_inside_a_message_cannot_forge_a_new_log_line()
    {
        // Window titles end up in messages, and any program can name its window like this.
        Log.Info("title\n2026-01-01 00:00:00.000 [ERROR] forged");

        var lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("    ", lines[1]);
        Assert.DoesNotMatch(@"^\d{4}-", lines[1]);
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    public void Every_kind_of_line_break_is_neutralised(string message)
    {
        var result = Logger.NeutraliseLineBreaks(message);

        Assert.Equal("a" + Environment.NewLine + "    b", result);
    }

    [Fact]
    public void A_message_without_line_breaks_is_left_untouched()
    {
        Assert.Equal("plain text", Logger.NeutraliseLineBreaks("plain text"));
    }

    [Fact]
    public void Logging_refuses_to_write_through_a_hardlinked_log_file()
    {
        // An elevated logger must not append into a file the attacker linked from elsewhere.
        var victim = Path.Combine(_temp.Directory, "victim.txt");
        File.WriteAllText(victim, "PRIVILEGED");
        Assert.True(TestLinks.CreateHardLink(Log.LogFilePath, victim), "could not create a hardlink for the test");

        Log.Info("attacker-controlled text");

        Assert.Equal("PRIVILEGED", File.ReadAllText(victim));
        Assert.NotNull(Log.DisabledReason);
        Assert.True(Log.DroppedLines > 0);
    }

    [Fact]
    public void Logging_is_disabled_when_the_log_directory_is_a_junction()
    {
        var real = Path.Combine(_temp.Directory, "elsewhere");
        Directory.CreateDirectory(real);
        var junction = Path.Combine(_temp.Directory, "app", "logs");
        Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
        Assert.True(TestLinks.CreateJunction(junction, real), "could not create a junction for the test");

        using var logger = new Logger(junction);
        logger.Info("must not land in the junction target");

        Assert.NotNull(logger.DisabledReason);
        Assert.Empty(Directory.GetFiles(real));
    }

    [Fact]
    public void Lines_lost_while_the_file_was_unavailable_are_reported_afterwards()
    {
        // Another program holds the file exclusively before the logger gets to it.
        using (TestLinks.LockExclusively(Log.LogFilePath))
        {
            Log.Info("lost one");
            Log.Info("lost two");
        }

        Log.Info("after");

        var text = _temp.ReadAll();
        Assert.DoesNotContain("lost one", text);
        Assert.Contains("Lost 2 log lines", text);
        Assert.Contains("after", text);
        Assert.Equal(2, Log.DroppedLines);
        Assert.Null(Log.DisabledReason);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    // ---- Construction and format ------------------------------------------------------------

    [Fact]
    public void Constructor_creates_the_log_directory()
    {
        var nested = Path.Combine(_temp.Directory, "nested", "logs");

        var logger = new Logger(nested);

        Assert.True(Directory.Exists(nested));
        Assert.Equal(Path.Combine(nested, "sensorpanelpin.log"), logger.LogFilePath);
    }

    [Fact]
    public void A_line_carries_a_timestamp_the_padded_level_and_the_message()
    {
        Log.Info("hello world");

        var line = Lines().Single();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO \] hello world$", line);
    }

    [Fact]
    public void Each_message_is_written_on_its_own_line_and_appended_in_order()
    {
        Log.Info("first");
        Log.Info("second");

        var lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("first", lines[0]);
        Assert.EndsWith("second", lines[1]);
    }

    [Fact]
    public void Error_with_an_exception_appends_the_exception_text()
    {
        Log.Error("operation failed", new InvalidOperationException("boom"));

        var text = _temp.ReadAll();
        Assert.Contains("[ERROR]", text);
        Assert.Contains("operation failed :: ", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    // ---- Level filtering --------------------------------------------------------------------

    [Fact]
    public void Default_minimum_level_is_Info()
    {
        Assert.Equal(LogLevel.Info, Log.MinimumLevel);
    }

    [Fact]
    public void At_the_default_level_debug_messages_are_dropped_and_everything_else_is_written()
    {
        Log.Debug("dbg-message");
        Log.Info("info-message");
        Log.Warn("warn-message");
        Log.Error("error-message");

        var text = _temp.ReadAll();
        Assert.DoesNotContain("dbg-message", text);
        Assert.Contains("[INFO ] info-message", text);
        Assert.Contains("[WARN ] warn-message", text);
        Assert.Contains("[ERROR] error-message", text);
    }

    [Fact]
    public void At_Debug_level_every_message_is_written()
    {
        Log.MinimumLevel = LogLevel.Debug;

        Log.Debug("dbg-message");
        Log.Info("info-message");

        var text = _temp.ReadAll();
        Assert.Contains("[DEBUG] dbg-message", text);
        Assert.Contains("[INFO ] info-message", text);
    }

    [Fact]
    public void At_Warn_level_info_is_dropped()
    {
        Log.MinimumLevel = LogLevel.Warn;

        Log.Info("info-message");
        Log.Warn("warn-message");
        Log.Error("error-message");

        var text = _temp.ReadAll();
        Assert.DoesNotContain("info-message", text);
        Assert.Contains("warn-message", text);
        Assert.Contains("error-message", text);
    }

    [Fact]
    public void At_Error_level_only_errors_are_written()
    {
        Log.MinimumLevel = LogLevel.Error;

        Log.Warn("warn-message");
        Log.Error("error-message");
        Log.Error("error-with-exception", new Exception("x"));

        var text = _temp.ReadAll();
        Assert.DoesNotContain("warn-message", text);
        Assert.Contains("error-message", text);
        Assert.Contains("error-with-exception", text);
    }

    [Fact]
    public void Nothing_is_created_on_disk_when_every_message_is_filtered_out()
    {
        Log.Debug("filtered");

        Assert.False(File.Exists(Log.LogFilePath));
    }

    [Fact]
    public void Changing_the_minimum_level_takes_effect_immediately()
    {
        Log.Debug("before");
        Log.MinimumLevel = LogLevel.Debug;
        Log.Debug("after");

        var text = _temp.ReadAll();
        Assert.DoesNotContain("before", text);
        Assert.Contains("after", text);
    }

    // ---- Throttling -------------------------------------------------------------------------

    [Fact]
    public void Throttled_writes_the_first_occurrence_and_suppresses_repeats_within_the_period()
    {
        var period = TimeSpan.FromHours(1);

        Log.Throttled("aida-missing", period, LogLevel.Warn, "AIDA64 not running");
        Log.Throttled("aida-missing", period, LogLevel.Warn, "AIDA64 not running");
        Log.Throttled("aida-missing", period, LogLevel.Warn, "AIDA64 not running");

        var text = _temp.ReadAll();
        Assert.Equal(1, Count(text, "AIDA64 not running"));
        Assert.Contains("[WARN ]", text);
    }

    [Fact]
    public void Throttled_uses_the_requested_level_for_the_message()
    {
        Log.Throttled("k", TimeSpan.FromHours(1), LogLevel.Error, "something bad");

        Assert.Contains("[ERROR] something bad", _temp.ReadAll());
    }

    [Fact]
    public void Throttle_keys_are_independent()
    {
        var period = TimeSpan.FromHours(1);

        Log.Throttled("key-a", period, LogLevel.Info, "message-a");
        Log.Throttled("key-b", period, LogLevel.Info, "message-b");
        Log.Throttled("key-a", period, LogLevel.Info, "message-a");

        var text = _temp.ReadAll();
        Assert.Equal(1, Count(text, "message-a"));
        Assert.Equal(1, Count(text, "message-b"));
    }

    [Fact]
    public void Throttle_keys_are_case_sensitive()
    {
        var period = TimeSpan.FromHours(1);

        Log.Throttled("Key", period, LogLevel.Info, "upper");
        Log.Throttled("key", period, LogLevel.Info, "lower");

        var text = _temp.ReadAll();
        Assert.Contains("upper", text);
        Assert.Contains("lower", text);
    }

    [Fact]
    public void ResetThrottle_lets_the_next_occurrence_through_immediately()
    {
        var period = TimeSpan.FromHours(1);

        Log.Throttled("target-missing", period, LogLevel.Warn, "target gone");
        Log.Throttled("target-missing", period, LogLevel.Warn, "target gone");
        Log.ResetThrottle("target-missing");
        Log.Throttled("target-missing", period, LogLevel.Warn, "target gone");

        Assert.Equal(2, Count(_temp.ReadAll(), "target gone"));
    }

    [Fact]
    public void ResetThrottle_only_releases_the_named_key()
    {
        var period = TimeSpan.FromHours(1);

        Log.Throttled("a", period, LogLevel.Info, "message-a");
        Log.Throttled("b", period, LogLevel.Info, "message-b");
        Log.ResetThrottle("a");
        Log.Throttled("a", period, LogLevel.Info, "message-a");
        Log.Throttled("b", period, LogLevel.Info, "message-b");

        var text = _temp.ReadAll();
        Assert.Equal(2, Count(text, "message-a"));
        Assert.Equal(1, Count(text, "message-b"));
    }

    [Fact]
    public void ResetThrottle_for_an_unknown_key_is_harmless()
    {
        Log.ResetThrottle("never-used");

        Log.Throttled("never-used", TimeSpan.FromHours(1), LogLevel.Info, "ok");

        Assert.Contains("ok", _temp.ReadAll());
    }

    [Fact]
    public void Throttled_writes_again_once_the_period_has_elapsed()
    {
        // No clock seam in Logger, so use a short real period.
        var period = TimeSpan.FromMilliseconds(150);

        Log.Throttled("k", period, LogLevel.Info, "again");
        Log.Throttled("k", period, LogLevel.Info, "again");
        Thread.Sleep(350);
        Log.Throttled("k", period, LogLevel.Info, "again");

        Assert.Equal(2, Count(_temp.ReadAll(), "again"));
    }

    [Fact]
    public void Throttled_messages_below_the_minimum_level_are_not_written()
    {
        Log.Throttled("k", TimeSpan.FromHours(1), LogLevel.Debug, "too quiet");

        Assert.DoesNotContain("too quiet", _temp.ReadAll());
    }

    // ---- Rotation ---------------------------------------------------------------------------

    /// <summary>Fills the active log file to <paramref name="bytes"/> so the next write has to rotate.</summary>
    private void FillActiveLogTo(long bytes, string marker)
    {
        // The logger keeps the file open, so this has to share with it.
        using var stream = new FileStream(Log.LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var header = System.Text.Encoding.UTF8.GetBytes(marker + Environment.NewLine);
        stream.Write(header, 0, header.Length);
        stream.SetLength(bytes);
    }

    private string RotatedPath(int index) => Log.LogFilePath + "." + index;

    [Fact]
    public void A_file_below_the_threshold_is_not_rotated()
    {
        FillActiveLogTo(RotationThresholdBytes - 1, "OLD-CONTENT");

        Log.Info("new line");

        Assert.False(File.Exists(RotatedPath(1)));
        Assert.True(new FileInfo(Log.LogFilePath).Length > RotationThresholdBytes - 1);
    }

    [Fact]
    public void A_file_at_the_threshold_is_rotated_to_dot_one_and_a_fresh_file_is_started()
    {
        FillActiveLogTo(RotationThresholdBytes, "OLD-CONTENT");

        Log.Info("fresh line");

        Assert.True(File.Exists(RotatedPath(1)));
        Assert.StartsWith("OLD-CONTENT", File.ReadAllText(RotatedPath(1)));
        Assert.Equal(RotationThresholdBytes, new FileInfo(RotatedPath(1)).Length);

        var active = _temp.ReadAll();
        Assert.Contains("fresh line", active);
        Assert.DoesNotContain("OLD-CONTENT", active);
        Assert.True(new FileInfo(Log.LogFilePath).Length < 1024);
    }

    [Fact]
    public void Successive_rotations_shift_older_files_up_by_one()
    {
        FillActiveLogTo(RotationThresholdBytes, "GEN-1");
        Log.Info("trigger one");

        FillActiveLogTo(RotationThresholdBytes, "GEN-2");
        Log.Info("trigger two");

        FillActiveLogTo(RotationThresholdBytes, "GEN-3");
        Log.Info("trigger three");

        Assert.StartsWith("GEN-3", File.ReadAllText(RotatedPath(1)));
        Assert.StartsWith("GEN-2", File.ReadAllText(RotatedPath(2)));
        Assert.StartsWith("GEN-1", File.ReadAllText(RotatedPath(3)));
        Assert.False(File.Exists(RotatedPath(4)));
    }

    [Fact]
    public void Rotation_keeps_at_most_the_active_file_plus_five_rotated_files()
    {
        for (var generation = 1; generation <= MaxFiles + 3; generation++)
        {
            FillActiveLogTo(RotationThresholdBytes, "GEN-" + generation);
            Log.Info("trigger " + generation);
        }

        for (var i = 1; i <= MaxFiles; i++)
        {
            Assert.True(File.Exists(RotatedPath(i)), $"expected rotated file .{i} to exist");
        }

        Assert.False(File.Exists(RotatedPath(MaxFiles + 1)));

        // Newest rotated generation is .1; the oldest survivor is the one MaxFiles rotations back.
        var newest = MaxFiles + 3;
        Assert.StartsWith("GEN-" + newest, File.ReadAllText(RotatedPath(1)));
        Assert.StartsWith("GEN-" + (newest - (MaxFiles - 1)), File.ReadAllText(RotatedPath(MaxFiles)));
    }

    [Fact]
    public void Writing_enough_through_the_public_api_triggers_rotation()
    {
        var chunk = new string('x', 4000);
        var writes = (int)(RotationThresholdBytes / chunk.Length) + 20;

        for (var i = 0; i < writes; i++)
        {
            Log.Info(chunk);
        }

        Assert.True(File.Exists(RotatedPath(1)), "a rotated file should exist after writing more than 1 MB");
        Assert.True(new FileInfo(RotatedPath(1)).Length >= RotationThresholdBytes);
        Assert.True(new FileInfo(Log.LogFilePath).Length < RotationThresholdBytes);
    }
}
