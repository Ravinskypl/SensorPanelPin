using System.Text.Json;
using SensorPanelPin.Diagnostics;

namespace SensorPanelPin.Config;

/// <summary>
/// Loads and saves %APPDATA%\SensorPanelPin\config.json. On the first run after the rename from
/// SensorPanelGlue it takes over the old %APPDATA%\SensorPanelGlue\config.json, leaving the old
/// folder as it was.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly Logger _log;

    private readonly string? _legacyConfigPath;

    public ConfigStore(Logger log)
        : this(log, DefaultAppDataDirectory, LegacyAppDataDirectory)
    {
    }

    internal ConfigStore(Logger log, string appDataDirectory, string? legacyAppDataDirectory)
    {
        _log = log;
        AppDataDirectory = appDataDirectory;
        ConfigPath = Path.Combine(AppDataDirectory, "config.json");
        _legacyConfigPath = legacyAppDataDirectory is null ? null : Path.Combine(legacyAppDataDirectory, "config.json");
    }

    public static string DefaultAppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SensorPanelPin");

    /// <summary>Where the program kept its settings before it was renamed from SensorPanelGlue.</summary>
    public static string LegacyAppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SensorPanelGlue");

    public string AppDataDirectory { get; }

    public string ConfigPath { get; }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath) && TryTakeOverLegacy() is { } migrated)
            {
                return migrated;
            }

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

    /// <summary>
    /// Reads the config the program saved under its old name and saves it under the new one. The old
    /// file is only read, never moved or deleted: if anything goes wrong it is still there.
    /// </summary>
    private AppConfig? TryTakeOverLegacy()
    {
        if (_legacyConfigPath is null || !File.Exists(_legacyConfigPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_legacyConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            config.Normalize();
            if (!Save(config))
            {
                return null;
            }

            _log.Info("Przeniesiono konfigurację spod poprzedniej nazwy programu: " + _legacyConfigPath
                      + " -> " + ConfigPath + ". Stary folder zostaje bez zmian, są w nim też dawne logi.");
            return config;
        }
        catch (Exception ex)
        {
            _log.Error("Nie udało się przenieść konfiguracji z " + _legacyConfigPath + ", używam ustawień domyślnych.", ex);
            return null;
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
