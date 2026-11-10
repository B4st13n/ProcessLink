using Xunit;
using Spawn.Services;

namespace Spawn.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Paths_UseSpawnDirectory_AndLegacySourcesInOrder()
    {
        Assert.EndsWith(Path.Combine("Spawn", "config.json"), AppPaths.ConfigPath);
        Assert.EndsWith(Path.Combine("Spawn", "events.log"), AppPaths.EventLogPath);
        Assert.EndsWith(Path.Combine("ProcessLink", "config.json"), AppPaths.LegacyProcessLinkConfigPath);
        Assert.EndsWith(Path.Combine("GamePadTrigger", "config.json"), AppPaths.LegacyGamePadTriggerConfigPath);

        Assert.Equal(
            new[] { AppPaths.LegacyProcessLinkConfigPath, AppPaths.LegacyGamePadTriggerConfigPath },
            AppPaths.LegacyConfigPaths);

        var service = new ConfigService();
        Assert.Equal(AppPaths.ConfigPath, service.ConfigPath);
    }
}
