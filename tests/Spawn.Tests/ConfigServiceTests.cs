using Xunit;
using Spawn.Models;
using Spawn.Services;

namespace Spawn.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _configPath;
    private readonly string _processLinkPath;
    private readonly string _gamePadPath;

    public ConfigServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SpawnTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "Spawn", "config.json");
        _processLinkPath = Path.Combine(_dir, "ProcessLink", "config.json");
        _gamePadPath = Path.Combine(_dir, "GamePadTrigger", "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private ConfigService CreateService() => new(_configPath, _processLinkPath, _gamePadPath);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var config = CreateService().Load();

        Assert.Equal(1.5, config.PollInterval);
        Assert.Empty(config.Games);
        Assert.Empty(config.Pinned);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, "{ this is not json !!!");

        var config = CreateService().Load();

        Assert.Equal(1.5, config.PollInterval);
        Assert.Empty(config.Games);
    }

    [Fact]
    public void SaveLoad_Roundtrip_PreservesGamesPinnedAndInterval()
    {
        var service = CreateService();
        var config = new AppConfig
        {
            PollInterval = 2.5,
            Games =
            {
                new GameConfig
                {
                    ProcessName = "eldenring.exe",
                    Events =
                    {
                        new GameEventConfig
                        {
                            Name = "Launch helper",
                            StartCommands = { "\"C:\\AutoHotkey\\AutoHotkey.exe\" \"D:\\Scripts\\eldenring.ahk\"" },
                            StopCommands = { "taskkill /IM helper.exe" },
                            Enabled = true,
                        },
                        new GameEventConfig { Name = "Disabled event", Enabled = false },
                    },
                },
            },
            Pinned =
            {
                new PinnedProcessConfig
                {
                    ProcessName = "eldenring.exe",
                    DisplayPath = "C:\\Games\\eldenring.exe",
                    LastSeen = "2026-10-25T14:03",
                    LastPid = 4242,
                },
                new PinnedProcessConfig { ProcessName = "notrunning.exe" },
            },
        };

        service.Save(config);
        var loaded = CreateService().Load();

        Assert.Equal(2.5, loaded.PollInterval);

        var game = Assert.Single(loaded.Games);
        Assert.Equal("eldenring.exe", game.ProcessName);
        Assert.Equal(2, game.Events.Count);
        Assert.Equal("Launch helper", game.Events[0].Name);
        Assert.True(game.Events[0].Enabled);
        Assert.Equal("\"C:\\AutoHotkey\\AutoHotkey.exe\" \"D:\\Scripts\\eldenring.ahk\"", Assert.Single(game.Events[0].StartCommands));
        Assert.Equal("taskkill /IM helper.exe", Assert.Single(game.Events[0].StopCommands));
        Assert.False(game.Events[1].Enabled);

        Assert.Equal(2, loaded.Pinned.Count);
        var pinned = loaded.Pinned[0];
        Assert.Equal("eldenring.exe", pinned.ProcessName);
        Assert.Equal("C:\\Games\\eldenring.exe", pinned.DisplayPath);
        Assert.Equal("2026-10-25T14:03", pinned.LastSeen);
        Assert.Equal(4242, pinned.LastPid);
        Assert.Equal("notrunning.exe", loaded.Pinned[1].ProcessName);
        Assert.Null(loaded.Pinned[1].LastSeen);
    }

    [Fact]
    public void Save_WritesSnakeCaseJson_CompatibleWithPythonFormat()
    {
        var service = CreateService();
        service.Save(new AppConfig
        {
            PollInterval = 1.5,
            Games =
            {
                new GameConfig
                {
                    ProcessName = "eldenring.exe",
                    Events =
                    {
                        new GameEventConfig
                        {
                            Name = "Launch helper",
                            StartCommands = { "run.ahk" },
                            StopCommands = { "taskkill /IM helper.exe" },
                            Enabled = true,
                        },
                    },
                },
            },
            Pinned = { new PinnedProcessConfig { ProcessName = "eldenring.exe", DisplayPath = "C:\\Games\\e.exe" } },
        });

        var json = File.ReadAllText(_configPath);

        Assert.Contains("\"poll_interval\": 1.5", json);
        Assert.Contains("\"process_name\": \"eldenring.exe\"", json);
        Assert.Contains("\"start_commands\"", json);
        Assert.Contains("\"stop_commands\"", json);
        Assert.Contains("\"enabled\": true", json);
        Assert.Contains("\"pinned\"", json);
        Assert.Contains("\"display_path\"", json);
    }

    [Fact]
    public void Load_LegacyProcessLinkJson_IsMigratedAndPreferredOverGamePadTrigger()
    {
        WriteLegacy(_processLinkPath, "processlink.exe", 2.5);
        WriteLegacy(_gamePadPath, "gamepad.exe", 1.0);

        var config = CreateService().Load();

        Assert.Equal(2.5, config.PollInterval);
        Assert.Equal("processlink.exe", Assert.Single(config.Games).ProcessName);

        Assert.True(File.Exists(_configPath));
        Assert.True(File.Exists(_processLinkPath));
        Assert.True(File.Exists(_gamePadPath));

        var reloaded = CreateService().Load();
        Assert.Equal("processlink.exe", Assert.Single(reloaded.Games).ProcessName);
    }

    [Fact]
    public void Load_LegacyGamePadTriggerJson_IsMigratedWhenProcessLinkIsAbsent()
    {
        // The exact shape produced by the previous Python app.
        var legacyJson = """
        {
          "poll_interval": 1.5,
          "games": [
            {
              "process_name": "eldenring.exe",
              "events": [
                {
                  "name": "Launch helper",
                  "start_commands": ["\"C:\\Program Files\\AutoHotkey\\AutoHotkey.exe\" \"D:\\Scripts\\eldenring.ahk\""],
                  "stop_commands": ["taskkill /IM helper.exe"],
                  "enabled": true
                }
              ]
            }
          ]
        }
        """;
        Directory.CreateDirectory(Path.GetDirectoryName(_gamePadPath)!);
        File.WriteAllText(_gamePadPath, legacyJson);

        var config = CreateService().Load();

        Assert.Equal(1.5, config.PollInterval);
        var game = Assert.Single(config.Games);
        Assert.Equal("eldenring.exe", game.ProcessName);
        var gameEvent = Assert.Single(game.Events);
        Assert.Equal("Launch helper", gameEvent.Name);
        Assert.Equal("taskkill /IM helper.exe", Assert.Single(gameEvent.StopCommands));
        Assert.Empty(config.Pinned);

        // Migration copies into the Spawn path and leaves the legacy file in place.
        Assert.True(File.Exists(_configPath));
        Assert.True(File.Exists(_gamePadPath));
        var reloaded = CreateService().Load();
        Assert.Equal("eldenring.exe", Assert.Single(reloaded.Games).ProcessName);
    }

    [Fact]
    public void Load_CorruptProcessLinkLegacy_FallsThroughToGamePadTrigger()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_processLinkPath)!);
        File.WriteAllText(_processLinkPath, "{ this is not json !!!");
        WriteLegacy(_gamePadPath, "gamepad.exe", 3.0);

        var config = CreateService().Load();

        Assert.Equal(3.0, config.PollInterval);
        Assert.Equal("gamepad.exe", Assert.Single(config.Games).ProcessName);
        Assert.True(File.Exists(_configPath));
        Assert.True(File.Exists(_processLinkPath));
    }

    [Fact]
    public void Load_SpawnPathWins_OverLegacyPaths()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_processLinkPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_gamePadPath)!);
        File.WriteAllText(_configPath, """{"poll_interval": 9.0, "games": [], "pinned": []}""");
        File.WriteAllText(_processLinkPath, """{"poll_interval": 4.0, "games": [{"process_name": "processlink.exe", "events": []}], "pinned": []}""");
        File.WriteAllText(_gamePadPath, """{"poll_interval": 1.0, "games": [{"process_name": "legacy.exe", "events": []}], "pinned": []}""");

        var config = CreateService().Load();

        Assert.Equal(9.0, config.PollInterval);
        Assert.Empty(config.Games);
    }

    [Fact]
    public void PinUnpin_AndIsPinned_WorkCaseInsensitively()
    {
        var config = new AppConfig();

        config.Pin("Game.EXE", "C:\\game.exe", running: true);

        Assert.True(config.IsPinned("game.exe"));
        Assert.True(config.IsPinned("GAME.EXE"));
        var pinned = Assert.Single(config.Pinned);
        Assert.Equal("Game.EXE", pinned.ProcessName);
        Assert.NotNull(pinned.LastSeen);

        config.Unpin("GAME.exe");
        Assert.False(config.IsPinned("Game.EXE"));
        Assert.Empty(config.Pinned);
    }

    [Fact]
    public void GetOrAddGame_ReturnsExistingInstance()
    {
        var config = new AppConfig();
        var first = config.GetOrAddGame("a.exe");
        var second = config.GetOrAddGame("A.EXE");

        Assert.Same(first, second);
        Assert.Single(config.Games);
    }

    private static void WriteLegacy(string path, string processName, double pollInterval)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
        {
          "poll_interval": {{pollInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "games": [
            { "process_name": "{{processName}}", "events": [] }
          ],
          "pinned": []
        }
        """);
    }
}
