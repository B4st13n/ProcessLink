using Xunit;
using ProcessLink.Models;
using ProcessLink.Services;
using ProcessLink.ViewModels;

namespace ProcessLink.Tests;

/// <summary>Tests the single process list: pinned on top, live flags, pin persistence.</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly string _configPath;

    public MainViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ProcessLinkTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class FakeProcessSource : IProcessSource
    {
        public List<ProcessSnapshot> Processes { get; } = new();
        public IReadOnlyList<ProcessSnapshot> GetProcesses() => Processes.ToList();
        public void Set(params string[] names)
        {
            Processes.Clear();
            int pid = 100;
            foreach (var name in names)
                Processes.Add(new ProcessSnapshot(name, pid++, $@"C:\bin\{name}"));
        }
    }

    private sealed class NullLauncher : ICommandLauncher
    {
        public void Launch(string command) { }
    }

    private (MainViewModel ViewModel, FakeProcessSource Source, ProcessMonitor Monitor, ConfigHolder Holder) CreateViewModel(AppConfig? config = null)
    {
        var cfg = config ?? new AppConfig();
        var holder = new ConfigHolder(cfg);
        var source = new FakeProcessSource();
        var monitor = new ProcessMonitor(source, new NullLauncher(), () => holder.Config, new FileEventSink(Path.Combine(_dir, "events.log")));
        var configService = new ConfigService(_configPath, Path.Combine(_dir, "legacy-missing.json"));
        var vm = new MainViewModel(configService, monitor, holder, new FileEventSink(Path.Combine(_dir, "events.log")), startMonitor: false);
        return (vm, source, monitor, holder);
    }

    [Fact]
    public void PinnedNotRunning_StaysOnTop_WithNoLiveIndicator()
    {
        var (vm, source, monitor, _) = CreateViewModel(new AppConfig
        {
            Pinned = { new PinnedProcessConfig { ProcessName = "dead.exe", DisplayPath = @"C:\Games\dead.exe" } },
        });
        source.Set("zzz.exe", "aaa.exe");

        monitor.PollOnce();

        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("dead.exe", vm.Rows[0].Name);
        Assert.True(vm.Rows[0].IsPinned);
        Assert.False(vm.Rows[0].IsRunning);
        Assert.Equal(@"C:\Games\dead.exe", vm.Rows[0].Path);
        Assert.Equal("aaa.exe", vm.Rows[1].Name);
        Assert.Equal("zzz.exe", vm.Rows[2].Name);
        vm.Dispose();
    }

    [Fact]
    public void RunningProcesses_SortedByName_WithLiveIndicator()
    {
        var (vm, source, monitor, _) = CreateViewModel();
        source.Set("zzz.exe", "aaa.exe", "mmm.exe");

        monitor.PollOnce();

        Assert.Equal(new[] { "aaa.exe", "mmm.exe", "zzz.exe" }, vm.Rows.Select(r => r.Name).ToArray());
        Assert.All(vm.Rows, row => Assert.True(row.IsRunning));
        Assert.All(vm.Rows, row => Assert.False(row.IsPinned));
        vm.Dispose();
    }

    [Fact]
    public void PinnedRunningProcess_ShowsLiveIndicator_AndFreshPid()
    {
        var (vm, source, monitor, _) = CreateViewModel(new AppConfig
        {
            Pinned = { new PinnedProcessConfig { ProcessName = "bbb.exe" } },
        });
        source.Set("bbb.exe", "aaa.exe");

        monitor.PollOnce();

        Assert.Equal("bbb.exe", vm.Rows[0].Name);
        Assert.True(vm.Rows[0].IsRunning);
        Assert.True(vm.Rows[0].IsPinned);
        Assert.Equal(100, vm.Rows[0].Pid);
        vm.Dispose();
    }

    [Fact]
    public void PinRow_MovesItToTop_AndPersistsToConfigJson()
    {
        var (vm, source, monitor, holder) = CreateViewModel();
        source.Set("zzz.exe", "aaa.exe");
        monitor.PollOnce();

        var aaa = vm.Rows.Single(r => r.Name == "aaa.exe");
        vm.TogglePinCommand.Execute(aaa);

        // Moved to the top, flagged pinned.
        Assert.Equal("aaa.exe", vm.Rows[0].Name);
        Assert.True(vm.Rows[0].IsPinned);

        // Persisted to the JSON config file.
        Assert.True(File.Exists(_configPath));
        var reloaded = new ConfigService(_configPath, Path.Combine(_dir, "legacy-missing.json")).Load();
        Assert.True(reloaded.IsPinned("aaa.exe"));
        var pinned = reloaded.FindPinned("aaa.exe");
        Assert.NotNull(pinned);
        Assert.NotNull(pinned!.LastSeen); // was running when pinned
        vm.Dispose();
    }

    [Fact]
    public void UnpinRow_RemovesItFromConfig_AndKeepsItInListWhileRunning()
    {
        var (vm, source, monitor, _) = CreateViewModel(new AppConfig
        {
            Pinned = { new PinnedProcessConfig { ProcessName = "aaa.exe" } },
        });
        source.Set("aaa.exe");
        monitor.PollOnce();
        Assert.True(vm.Rows[0].IsPinned);

        vm.TogglePinCommand.Execute(vm.Rows[0]);

        Assert.False(vm.Rows[0].IsPinned);
        var reloaded = new ConfigService(_configPath, Path.Combine(_dir, "legacy-missing.json")).Load();
        Assert.False(reloaded.IsPinned("aaa.exe"));
        vm.Dispose();
    }

    [Fact]
    public void PinnedProcess_ReappearingAndVanishing_KeepsRowWithLiveFlagUpdated()
    {
        var (vm, source, monitor, _) = CreateViewModel(new AppConfig
        {
            Pinned = { new PinnedProcessConfig { ProcessName = "game.exe" } },
        });
        source.Set("game.exe");
        monitor.PollOnce();
        Assert.True(vm.Rows[0].IsRunning);

        source.Set(); // game exits — pinned row must stay
        monitor.PollOnce();
        var row = Assert.Single(vm.Rows);
        Assert.Equal("game.exe", row.Name);
        Assert.False(row.IsRunning);
        Assert.True(row.IsPinned);
        vm.Dispose();
    }

    [Fact]
    public void PinnedLastSeen_UpdatedWhileRunning()
    {
        var (vm, source, monitor, holder) = CreateViewModel(new AppConfig
        {
            Pinned = { new PinnedProcessConfig { ProcessName = "game.exe" } },
        });
        source.Set("game.exe");
        monitor.PollOnce();

        var pin = holder.Config.FindPinned("game.exe");
        Assert.NotNull(pin);
        Assert.NotNull(pin!.LastSeen);
        Assert.Equal(100, pin.LastPid);
        vm.Dispose();
    }

    [Fact]
    public void ExtensionMismatch_ConfigExeMatchesProcessWithoutExtension()
    {
        var (vm, source, monitor, holder) = CreateViewModel(new AppConfig
        {
            Pinned = { new PinnedProcessConfig { ProcessName = "game.exe" } },
            Games = { new GameConfig { ProcessName = "game.exe" } },
        });
        source.Set("game"); // Process.ProcessName style, no extension
        monitor.PollOnce();

        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsRunning);
        Assert.True(row.IsPinned);
        vm.Dispose();
    }

    [Fact]
    public void SelectingRow_LoadsItsEvents()
    {
        var game = new GameConfig { ProcessName = "aaa.exe" };
        game.Events.Add(new GameEventConfig { Name = "E1", StartCommands = { "s.bat" } });
        var (vm, source, monitor, _) = CreateViewModel(new AppConfig { Games = { game } });
        source.Set("aaa.exe");
        monitor.PollOnce();

        vm.SelectedRow = vm.Rows[0];

        var evt = Assert.Single(vm.Events);
        Assert.Equal("E1", evt.Name);
        vm.Dispose();
    }

    [Fact]
    public void AddEvent_CreatesGameEntryIfMissing_AndPersists()
    {
        var (vm, source, monitor, _) = CreateViewModel();
        source.Set("aaa.exe");
        monitor.PollOnce();
        vm.SelectedRow = vm.Rows[0];

        vm.AddEventCommand.Execute(null);

        var evt = Assert.Single(vm.Events);
        Assert.Equal("New event", evt.Name);
        var reloaded = new ConfigService(_configPath, Path.Combine(_dir, "legacy-missing.json")).Load();
        var reloadedGame = reloaded.FindGame("aaa.exe");
        Assert.NotNull(reloadedGame);
        Assert.Single(reloadedGame!.Events);
        vm.Dispose();
    }
}
