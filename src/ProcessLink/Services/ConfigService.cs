using System.IO;
using System.Text.Json;
using ProcessLink.Models;

namespace ProcessLink.Services;

/// <summary>
/// Loads/saves the JSON config. Keeps the GamePadTrigger (Python) snake_case format
/// and migrates a legacy config file to the new ProcessLink location on first run.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _configPath;
    private readonly string? _legacyConfigPath;

    public ConfigService(string? configPath = null, string? legacyConfigPath = null)
    {
        _configPath = configPath ?? AppPaths.ConfigPath;
        _legacyConfigPath = legacyConfigPath ?? AppPaths.LegacyConfigPath;
    }

    public string ConfigPath => _configPath;

    /// <summary>
    /// Loads the config. If the new config file does not exist but a legacy
    /// GamePadTrigger config does, it is loaded and migrated (written to the new path).
    /// A missing or corrupt file yields a default config.
    /// </summary>
    public AppConfig Load()
    {
        if (File.Exists(_configPath))
            return LoadFile(_configPath) ?? new AppConfig();

        if (_legacyConfigPath != null && File.Exists(_legacyConfigPath))
        {
            var migrated = LoadFile(_legacyConfigPath) ?? new AppConfig();
            try { Save(migrated); }
            catch { /* migration is best-effort; the in-memory config still works */ }
            return migrated;
        }

        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    internal static AppConfig? LoadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch
        {
            return null; // missing or corrupt -> caller falls back to defaults
        }
    }
}
