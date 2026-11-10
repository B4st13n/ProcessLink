using System.IO;
namespace Spawn.Services;

/// <summary>Filesystem locations for Spawn data.</summary>
public static class AppPaths
{
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spawn");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    public static string EventLogPath => Path.Combine(AppDataDir, "events.log");

    /// <summary>Previous C# app directory (%AppData%\ProcessLink).</summary>
    public static string LegacyProcessLinkDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProcessLink");

    public static string LegacyProcessLinkConfigPath => Path.Combine(LegacyProcessLinkDir, "config.json");

    /// <summary>Original Python app directory (%AppData%\GamePadTrigger).</summary>
    public static string LegacyGamePadTriggerDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamePadTrigger");

    public static string LegacyGamePadTriggerConfigPath => Path.Combine(LegacyGamePadTriggerDir, "config.json");

    /// <summary>
    /// Legacy config files in migration order: ProcessLink, then GamePadTrigger.
    /// Spawn loads the first one that exists and parses.
    /// </summary>
    public static IReadOnlyList<string> LegacyConfigPaths { get; } = new[]
    {
        LegacyProcessLinkConfigPath,
        LegacyGamePadTriggerConfigPath,
    };
}
