using Xunit;
using ProcessLink.Models;
using ProcessLink.Services;

namespace ProcessLink.Tests;

public sealed class ProcessMonitorTests
{
    private sealed class FakeProcessSource : IProcessSource
    {
        public List<ProcessSnapshot> Processes { get; } = new();

        public IReadOnlyList<ProcessSnapshot> GetProcesses() => Processes.ToList();

        public void Set(params string[] names)
        {
            Processes.Clear();
            int pid = 100;
            foreach (var name in names)
                Processes.Add(new ProcessSnapshot(name, pid++, null));
        }
    }

    private sealed class RecordingLauncher : ICommandLauncher
    {
        public List<string> Commands { get; } = new();

        public void Launch(string command) => Commands.Add(command);
    }

    private sealed class RecordingEventSink : IEventSink
    {
        public List<(string Process, string Action, string Command)> Entries { get; } = new();

        public void Log(string processName, string action, string detail)
            => Entries.Add((processName, action, detail));
    }

    private static (ProcessMonitor Monitor, FakeProcessSource Source, RecordingLauncher Launcher, RecordingEventSink Sink, AppConfig Config)
        CreateMonitor(AppConfig? config = null)
    {
        var source = new FakeProcessSource();
        var launcher = new RecordingLauncher();
        var sink = new RecordingEventSink();
        var cfg = config ?? new AppConfig();
        var monitor = new ProcessMonitor(source, launcher, () => cfg, sink);
        return (monitor, source, launcher, sink, cfg);
    }

    private static AppConfig ConfigWithGame(string processName, params GameEventConfig[] events)
    {
        var game = new GameConfig { ProcessName = processName };
        game.Events.AddRange(events);
        return new AppConfig { Games = { game } };
    }

    [Fact]
    public void ProcessAppears_StartCommandsAreLaunched()
    {
        var (monitor, source, launcher, sink, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig
            {
                Name = "Launch helper",
                StartCommands = { "start-helper.bat" },
                StopCommands = { "stop-helper.bat" },
                Enabled = true,
            }));

        source.Set("other.exe");
        monitor.PollOnce();
        Assert.Empty(launcher.Commands);

        source.Set("other.exe", "game.exe");
        var transitions = monitor.PollOnce();

        var transition = Assert.Single(transitions);
        Assert.Equal("game.exe", transition.ProcessName);
        Assert.Equal(ProcessTransitionKind.Appeared, transition.Transition);
        Assert.Equal(new[] { "start-helper.bat" }, launcher.Commands);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(("game.exe", "START", "start-helper.bat"), entry);
    }

    [Fact]
    public void ProcessDisappears_StopCommandsAreLaunched()
    {
        var (monitor, source, launcher, sink, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig
            {
                Name = "Launch helper",
                StartCommands = { "start-helper.bat" },
                StopCommands = { "stop-helper.bat" },
            }));

        source.Set("game.exe");
        monitor.PollOnce();
        launcher.Commands.Clear();
        sink.Entries.Clear();

        source.Set();
        var transitions = monitor.PollOnce();

        var transition = Assert.Single(transitions);
        Assert.Equal(ProcessTransitionKind.Disappeared, transition.Transition);
        Assert.Equal(new[] { "stop-helper.bat" }, launcher.Commands);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(("game.exe", "STOP", "stop-helper.bat"), entry);
    }

    [Fact]
    public void ProcessAlreadyRunningOnFirstPoll_CountsAsAppeared()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" } }));

        // Game already running when the monitor starts (startup behavior).
        source.Set("game.exe", "explorer.exe");
        monitor.PollOnce();

        Assert.Equal(new[] { "start.bat" }, launcher.Commands);
    }

    [Fact]
    public void SteadyState_NoCommandsLaunchedAgain()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" }, StopCommands = { "stop.bat" } }));

        source.Set("game.exe");
        monitor.PollOnce();
        monitor.PollOnce();
        monitor.PollOnce();

        Assert.Equal(new[] { "start.bat" }, launcher.Commands);
    }

    [Fact]
    public void ReappearAfterDisappear_LaunchesStartCommandsAgain()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" }, StopCommands = { "stop.bat" } }));

        source.Set("game.exe");
        monitor.PollOnce();
        source.Set();
        monitor.PollOnce();
        source.Set("game.exe");
        monitor.PollOnce();

        Assert.Equal(new[] { "start.bat", "stop.bat", "start.bat" }, launcher.Commands);
    }

    [Fact]
    public void DisabledEvent_DoesNotLaunchStartCommands()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" }, Enabled = false },
            new GameEventConfig { Name = "off", StartCommands = { "off.bat" }, Enabled = false }));

        source.Set("game.exe");
        monitor.PollOnce();

        Assert.Empty(launcher.Commands);
    }

    [Fact]
    public void MultipleStartCommands_AllLaunchedInOrder()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "one.bat", "two.bat", "three.bat" } }));

        source.Set("game.exe");
        monitor.PollOnce();

        Assert.Equal(new[] { "one.bat", "two.bat", "three.bat" }, launcher.Commands);
    }

    [Fact]
    public void UnconfiguredProcess_AppearingDoesNotLaunchAnything()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" } }));

        source.Set("someapp.exe", "another.exe");
        monitor.PollOnce();

        Assert.Empty(launcher.Commands);
    }

    [Fact]
    public void ProcessNameMatching_IsCaseInsensitive()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("Game.EXE",
            new GameEventConfig { StartCommands = { "start.bat" } }));

        source.Set("game.exe");
        monitor.PollOnce();

        Assert.Equal(new[] { "start.bat" }, launcher.Commands);
    }

    [Fact]
    public void ProcessNameMatching_IgnoresExeExtension()
    {
        // System.Diagnostics.Process.ProcessName strips ".exe"; the config format
        // (and the Python app) uses image names with the extension.
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" }, StopCommands = { "stop.bat" } }));

        source.Set("game"); // no extension, as Process.ProcessName reports
        monitor.PollOnce();
        Assert.Equal(new[] { "start.bat" }, launcher.Commands);

        source.Set();
        monitor.PollOnce();
        Assert.Equal(new[] { "start.bat", "stop.bat" }, launcher.Commands);
    }

    [Fact]
    public void EmptyOrWhitespaceCommands_AreNotLaunched()
    {
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "", "   ", "real.bat" } }));

        source.Set("game.exe");
        monitor.PollOnce();

        Assert.Equal(new[] { "real.bat" }, launcher.Commands);
    }

    [Fact]
    public void DisabledEvent_DoesNotLaunchStopCommandsEither()
    {
        // Matches the Python app: the enabled flag gates both start and stop commands.
        var (monitor, source, launcher, _, _) = CreateMonitor(ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" }, StopCommands = { "stop.bat" }, Enabled = false }));

        source.Set("game.exe");
        monitor.PollOnce();
        Assert.Empty(launcher.Commands); // disabled -> no start

        source.Set();
        monitor.PollOnce();
        Assert.Empty(launcher.Commands); // disabled -> no stop
    }

    [Fact]
    public void IsRunning_ReflectsLastPoll()
    {
        var (monitor, source, _, _, _) = CreateMonitor();

        source.Set("game.exe");
        monitor.PollOnce();
        Assert.True(monitor.IsRunning("GAME.EXE"));

        source.Set();
        monitor.PollOnce();
        Assert.False(monitor.IsRunning("game.exe"));
    }

    [Fact]
    public void ProcessesChanged_RaisedWithFreshSnapshot()
    {
        var (monitor, source, _, _, _) = CreateMonitor();
        IReadOnlyList<ProcessSnapshot>? received = null;
        monitor.ProcessesChanged += (_, snapshot) => received = snapshot;

        source.Set("a.exe", "b.exe");
        monitor.PollOnce();

        Assert.NotNull(received);
        Assert.Equal(2, received.Count);
        Assert.Contains(received, p => p.Name == "a.exe");
    }

    [Fact]
    public void ConfigReloadedBetweenPolls_UsesNewConfig()
    {
        var config = ConfigWithGame("game.exe",
            new GameEventConfig { StartCommands = { "start.bat" } });
        var (monitor, source, launcher, _, _) = CreateMonitor(config);

        source.Set("game.exe");
        monitor.PollOnce();
        Assert.Equal(new[] { "start.bat" }, launcher.Commands);

        // Simulate a config reload: the process is now unwatched.
        config.Games.Clear();
        source.Set();
        monitor.PollOnce();
        source.Set("game.exe");
        monitor.PollOnce();

        Assert.Equal(new[] { "start.bat" }, launcher.Commands); // no new launches
    }

    [Fact]
    public void FailingPoll_DoesNotThrowFromTimerPath()
    {
        var source = new ThrowingProcessSource();
        var monitor = new ProcessMonitor(source, new RecordingLauncher(), () => new AppConfig(), new RecordingEventSink());

        // PollOnce surfaces the source failure...
        var exception = Record.Exception(() => monitor.PollOnce());
        Assert.NotNull(exception);

        // ...but the timer-based poll path swallows it.
        var ex = Record.Exception(() => monitor.Start());
        Assert.Null(ex);
        monitor.Stop();
        monitor.Dispose();
    }

    [Fact]
    public void FailingLauncher_DoesNotBreakTheMonitor()
    {
        var source = new FakeProcessSource();
        var launcher = new ThrowingLauncher();
        var sink = new RecordingEventSink();
        var config = ConfigWithGame("game.exe", new GameEventConfig { StartCommands = { "boom.bat" } });
        var monitor = new ProcessMonitor(source, launcher, () => config, sink);

        source.Set("game.exe");
        var exception = Record.Exception(() => monitor.PollOnce());

        Assert.Null(exception); // launcher failure is swallowed
        Assert.Single(sink.Entries); // but the attempt is still logged
    }

    private sealed class ThrowingProcessSource : IProcessSource
    {
        public IReadOnlyList<ProcessSnapshot> GetProcesses() => throw new InvalidOperationException("boom");
    }

    private sealed class ThrowingLauncher : ICommandLauncher
    {
        public void Launch(string command) => throw new InvalidOperationException("launch failed");
    }
}
