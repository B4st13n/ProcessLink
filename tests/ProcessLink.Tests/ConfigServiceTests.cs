using Xunit;
using ProcessLink.Models;
using ProcessLink.Services;

namespace ProcessLink.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _configPath;
    private readonly string _legacyPath;

    public ConfigServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ProcessLinkTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "ProcessLink", "config.json");
        _legacyPath = Path.Combine(_dir, "GamePadTrigger", "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private ConfigService CreateService() => new(_configPath, _legacyPath);

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
    public void Load_LegacyGamePadTriggerJson_IsMigratedAndCopiedToNewPath()
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
        Directory.CreateDirectory(Path.GetDirectoryName(_legacyPath)!);
        File.WriteAllText(_legacyPath, legacyJson);

        var config = CreateService().Load();

        Assert.Equal(1.5, config.PollInterval);
        var game = Assert.Single(config.Games);
        Assert.Equal("eldenring.exe", game.ProcessName);
        var gameEvent = Assert.Single(game.Events);
        Assert.Equal("Launch helper", gameEvent.Name);
        Assert.Equal("taskkill /IM helper.exe", Assert.Single(gameEvent.StopCommands));
        Assert.Empty(config.Pinned);

        // Migration: the config now also exists at the new ProcessLink path.
        Assert.True(File.Exists(_configPath));
        var reloaded = CreateService().Load();
        Assert.Equal("eldenring.exe", Assert.Single(reloaded.Games).ProcessName);
    }

    [Fact]
    public void Load_NewPathWins_OverLegacyPath()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_legacyPath)!);
        File.WriteAllText(_configPath, """{"poll_interval": 9.0, "games": [], "pinned": []}""");
        File.WriteAllText(_legacyPath, """{"poll_interval": 1.0, "games": [{"process_name": "legacy.exe", "events": []}], "pinned": []}""");

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
}
