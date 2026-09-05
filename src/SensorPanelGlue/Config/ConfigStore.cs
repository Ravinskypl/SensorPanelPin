using System.Text.Json;
using SensorPanelGlue.Diagnostics;

namespace SensorPanelGlue.Config;

/// <summary>Loads and saves %APPDATA%\SensorPanelGlue\config.json.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly Logger _log;

    public ConfigStore(Logger log)
    {
        _log = log;
        AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SensorPanelGlue");
        ConfigPath = Path.Combine(AppDataDirectory, "config.json");
    }

    public static string DefaultAppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SensorPanelGlue");

    public string AppDataDirectory { get; }

    public string ConfigPath { get; }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                _log.Info("Brak pliku konfiguracji, tworzę domyślną: " + ConfigPath);
                var fresh = new AppConfig();
                Save(fresh);
                return fresh;
            }

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            config.Normalize();
            _log.Info("Wczytano konfigurację: " + ConfigPath);
            return config;
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się wczytać konfiguracji, używam ustawień domyślnych.", ex);
            return new AppConfig();
        }
    }

    public bool Save(AppConfig config)
    {
        try
        {
            config.Normalize();
            Directory.CreateDirectory(AppDataDirectory);

            // Write-then-replace so a crash mid-write cannot leave a truncated config behind.
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
            File.Move(temp, ConfigPath, overwrite: true);
            _log.Info("Zapisano konfigurację.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się zapisać konfiguracji.", ex);
            return false;
        }
    }
}
