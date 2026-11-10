using System.IO;
using System.Text.Json;
using Spawn.Models;

namespace Spawn.Services;

/// <summary>
/// Loads/saves the JSON config. Keeps the GamePadTrigger (Python) snake_case format
/// and migrates a legacy config into the Spawn location on first run.
/// Legacy search order is ProcessLink, then GamePadTrigger.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _configPath;
    private readonly IReadOnlyList<string> _legacyConfigPaths;

    /// <param name="configPath">Destination config. Defaults to %AppData%\Spawn\config.json.</param>
    /// <param name="legacyConfigPaths">
    /// Ordered legacy sources. The first existing file that parses is migrated.
    /// When omitted, the order is ProcessLink, then GamePadTrigger.
    /// </param>
    public ConfigService(string? configPath = null, params string[] legacyConfigPaths)
    {
        _configPath = configPath ?? AppPaths.ConfigPath;
        _legacyConfigPaths = legacyConfigPaths is { Length: > 0 }
            ? legacyConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)).ToArray()
            : AppPaths.LegacyConfigPaths;
    }

    public string ConfigPath => _configPath;

    /// <summary>
    /// Loads the config. An existing Spawn file wins (a corrupt Spawn file yields
    /// defaults and does not fall through). Otherwise the first legacy file that
    /// exists and parses is copied to the Spawn path. A corrupt legacy file is
    /// skipped so an older valid config is not discarded. A missing file yields
    /// a default config. Legacy files are not deleted.
    /// </summary>
    public AppConfig Load()
    {
        if (File.Exists(_configPath))
            return LoadFile(_configPath) ?? new AppConfig();

        foreach (var legacyPath in _legacyConfigPaths)
        {
            if (!File.Exists(legacyPath))
                continue;

            var migrated = LoadFile(legacyPath);
            if (migrated == null)
                continue;

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
