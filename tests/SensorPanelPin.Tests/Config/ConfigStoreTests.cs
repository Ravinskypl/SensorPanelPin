using System.Text.Json;
using SensorPanelPin.Config;
using SensorPanelPin.Tests.TestSupport;

namespace SensorPanelPin.Tests.Config;

/// <summary>
/// Taking over the settings saved before the rename from SensorPanelGlue. Each test gets its own pair
/// of folders standing in for %APPDATA%\SensorPanelPin and %APPDATA%\SensorPanelGlue.
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly TempLog _log = new();
    private readonly string _current;
    private readonly string _legacy;

    public ConfigStoreTests()
    {
        _current = Path.Combine(_log.Directory, "SensorPanelPin");
        _legacy = Path.Combine(_log.Directory, "SensorPanelGlue");
        Directory.CreateDirectory(_legacy);
    }

    public void Dispose() => _log.Dispose();

    [Fact]
    public void The_first_run_after_the_rename_takes_over_the_old_settings()
    {
        var legacyFile = WriteLegacy(new AppConfig { PanelMonitorId = "eM713A-path", PingPongLimit = 7 });
        var before = File.ReadAllText(legacyFile);

        var config = new ConfigStore(_log.Logger, _current, _legacy).Load();

        Assert.Equal("eM713A-path", config.PanelMonitorId);
        Assert.Equal(7, config.PingPongLimit);

        // Saved under the new name, so the next start reads it from there.
        var saved = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path.Combine(_current, "config.json")))!;
        Assert.Equal("eM713A-path", saved.PanelMonitorId);

        // The old file is only read: if anything went wrong, it is still there as it was.
        Assert.Equal(before, File.ReadAllText(legacyFile));
    }

    [Fact]
    public void Settings_already_saved_under_the_new_name_win_over_the_old_ones()
    {
        WriteLegacy(new AppConfig { PanelMonitorId = "old" });
        new ConfigStore(_log.Logger, _current, null).Save(new AppConfig { PanelMonitorId = "new" });

        var config = new ConfigStore(_log.Logger, _current, _legacy).Load();

        Assert.Equal("new", config.PanelMonitorId);
    }

    [Fact]
    public void An_unreadable_old_file_falls_back_to_defaults_and_stays_untouched()
    {
        var legacyFile = Path.Combine(_legacy, "config.json");
        File.WriteAllText(legacyFile, "{ not json");

        var config = new ConfigStore(_log.Logger, _current, _legacy).Load();

        Assert.Equal(string.Empty, config.PanelMonitorId);
        Assert.Equal("{ not json", File.ReadAllText(legacyFile));
    }

    private string WriteLegacy(AppConfig config)
    {
        var path = Path.Combine(_legacy, "config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }
}
