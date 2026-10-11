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
        string? temp = null;
        try
        {
            config.Normalize();
            var directory = SafeDirectory.Open(AppDataDirectory);

            // Write-then-replace so a crash mid-write cannot leave a truncated config behind. The
            // temporary name is random and created with CreateNew, so nothing can be waiting at it -
            // a pre-planted hardlink at a fixed ".tmp" name would otherwise receive the elevated
            // write. The handle is then verified before a single byte goes in.
            var tempName = "config." + Guid.NewGuid().ToString("N") + ".tmp";
            temp = directory.Combine(tempName);
            using (var stream = directory.OpenFile(tempName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, config, Options);
            }

            // MoveFileEx replaces a directory entry; it never writes through a link at the target.
            File.Move(temp, ConfigPath, overwrite: true);
            temp = null;
            _log.Info("Zapisano konfigurację.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się zapisać konfiguracji.", ex);
            return false;
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
