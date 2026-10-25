using System.Diagnostics;
using ProcessLink.Models;

namespace ProcessLink.Services;

/// <summary>A running process as seen by the monitor.</summary>
public sealed record ProcessSnapshot(string Name, int Id, string? Path);

public interface IProcessSource
{
    IReadOnlyList<ProcessSnapshot> GetProcesses();
}

/// <summary>Enumerates running processes via System.Diagnostics.</summary>
public sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlyList<ProcessSnapshot> GetProcesses()
    {
        var result = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // Prefer the image name with extension (as psutil reported it in the
                // Python app); Process.ProcessName strips ".exe".
                string name = process.ProcessName;
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        var fileName = System.IO.Path.GetFileName(path);
                        if (!string.IsNullOrEmpty(fileName))
                            name = fileName;
                    }
                }
                catch
                {
                    // Access denied for system/protected processes — path is optional.
                }
                result.Add(new ProcessSnapshot(name, process.Id, path));
            }
        }
        return result;
    }
}

public enum ProcessTransitionKind
{
    Appeared,
    Disappeared,
}

public sealed record ProcessTransitionEvent(string ProcessName, ProcessTransitionKind Transition);

/// <summary>
/// Polls the running processes and fires the configured start/stop commands when a
/// watched process appears or disappears. A process already running on the first
/// poll counts as "appeared".
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    private readonly IProcessSource _source;
    private readonly ICommandLauncher _launcher;
    private readonly Func<AppConfig> _configProvider;
    private readonly IEventSink _eventSink;
    private readonly object _gate = new();
    private readonly HashSet<string> _knownRunning = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _displayByKey = new(StringComparer.Ordinal);
    private System.Threading.Timer? _timer;
    private bool _started;
    private bool _disposed;

    public ProcessMonitor(
        IProcessSource source,
        ICommandLauncher launcher,
        Func<AppConfig> configProvider,
        IEventSink eventSink)
    {
        _source = source;
        _launcher = launcher;
        _configProvider = configProvider;
        _eventSink = eventSink;
    }

    /// <summary>Raised after every poll with the fresh process snapshot.</summary>
    public event EventHandler<IReadOnlyList<ProcessSnapshot>>? ProcessesChanged;

    /// <summary>Raised after a poll in which at least one process appeared or disappeared.</summary>
    public event EventHandler<IReadOnlyList<ProcessTransitionEvent>>? TransitionsOccurred;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1.5);

    public bool IsRunning(string processName)
    {
        lock (_gate)
        {
            return _knownRunning.Contains(ProcessNames.Normalize(processName));
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;
            _started = true;
            _timer = new System.Threading.Timer(_ => PollSafely(), null, TimeSpan.Zero, PollInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _started = false;
        }
    }

    /// <summary>Runs one poll synchronously and returns the transitions detected.</summary>
    public IReadOnlyList<ProcessTransitionEvent> PollOnce()
    {
        var snapshot = _source.GetProcesses();

        // Keys are extension-insensitive (ProcessNames.Normalize); display names keep the .exe.
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        var displayByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var process in snapshot)
        {
            var key = ProcessNames.Normalize(process.Name);
            currentKeys.Add(key);
            displayByKey[key] = process.Name;
        }

        var transitions = new List<ProcessTransitionEvent>();
        lock (_gate)
        {
            foreach (var key in currentKeys)
            {
                _displayByKey[key] = displayByKey[key];
                if (_knownRunning.Add(key))
                    transitions.Add(new ProcessTransitionEvent(displayByKey[key], ProcessTransitionKind.Appeared));
            }

            var disappeared = _knownRunning.Where(key => !currentKeys.Contains(key)).ToList();
            foreach (var key in disappeared)
            {
                _knownRunning.Remove(key);
                _displayByKey.TryGetValue(key, out var gone);
                transitions.Add(new ProcessTransitionEvent(gone ?? key, ProcessTransitionKind.Disappeared));
            }
        }

        var config = _configProvider();
        FireCommands(config, transitions);

        ProcessesChanged?.Invoke(this, snapshot);
        if (transitions.Count > 0)
            TransitionsOccurred?.Invoke(this, transitions);

        return transitions;
    }

    private void FireCommands(AppConfig config, IReadOnlyList<ProcessTransitionEvent> transitions)
    {
        foreach (var game in config.Games)
        {
            var processName = game.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
                continue;

            foreach (var transition in transitions)
            {
                if (!ProcessNames.Equals(game.ProcessName, transition.ProcessName))
                    continue;

                if (transition.Transition == ProcessTransitionKind.Appeared)
                {
                    foreach (var gameEvent in game.Events)
                    {
                        if (!gameEvent.Enabled)
                            continue;
                        foreach (var command in gameEvent.StartCommands)
                            Fire(processName, "START", command);
                    }
                }
                else
                {
                    foreach (var gameEvent in game.Events)
                    {
                        if (!gameEvent.Enabled)
                            continue;
                        foreach (var command in gameEvent.StopCommands)
                            Fire(processName, "STOP", command);
                    }
                }
            }
        }
    }

    private void Fire(string processName, string action, string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return;

        _eventSink.Log(processName, action, command);
        try
        {
            _launcher.Launch(command);
        }
        catch
        {
            // A failing command must never kill the monitor.
        }
    }

    private void PollSafely()
    {
        try
        {
            PollOnce();
        }
        catch
        {
            // A failing poll must never kill the monitor.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
