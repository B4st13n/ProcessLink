using System.IO;
namespace ProcessLink.Services;

/// <summary>Filesystem locations for ProcessLink data.</summary>
public static class AppPaths
{
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProcessLink");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    public static string EventLogPath => Path.Combine(AppDataDir, "events.log");

    // Legacy locations used by the previous Python app (GamePadTrigger).
    public static string LegacyAppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamePadTrigger");

    public static string LegacyConfigPath => Path.Combine(LegacyAppDataDir, "config.json");
}
