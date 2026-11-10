using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using Spawn.Models;
using Spawn.Services;

namespace Spawn.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ConfigService _configService;
    private readonly ProcessMonitor _monitor;
    private readonly ConfigHolder _configHolder;
    private readonly FileEventSink _eventSink;
    private readonly DispatcherTimer _debouncedSaveTimer;

    private readonly RelayCommand<ProcessRowViewModel> _togglePinCommand;
    private readonly RelayCommand _toggleSelectedPinCommand;
    private readonly RelayCommand _addEventCommand;
    private readonly RelayCommand _deleteEventCommand;
    private readonly RelayCommand _saveConfigCommand;
    private readonly RelayCommand _reloadConfigCommand;
    private readonly RelayCommand _clearLogCommand;

    private IReadOnlyList<ProcessSnapshot> _snapshot = Array.Empty<ProcessSnapshot>();
    private ProcessRowViewModel? _selectedRow;
    private GameEventConfig? _selectedEvent;
    private string _statusText = "Starting…";
    private string _pollIntervalText;
    private bool _pinnedDirty;
    private bool _disposed;

    public MainViewModel(
        ConfigService configService,
        ProcessMonitor monitor,
        ConfigHolder configHolder,
        FileEventSink eventSink,
        bool startMonitor = true)
    {
        _configService = configService;
        _monitor = monitor;
        _configHolder = configHolder;
        _eventSink = eventSink;

        _pollIntervalText = Config.PollInterval.ToString("0.0");

        _monitor.ProcessesChanged += OnProcessesChanged;
        _monitor.TransitionsOccurred += OnTransitionsOccurred;

        _debouncedSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _debouncedSaveTimer.Tick += (_, _) =>
        {
            _debouncedSaveTimer.Stop();
            if (_pinnedDirty)
            {
                _pinnedDirty = false;
                SaveConfig();
            }
        };

        if (startMonitor)
            _monitor.Start();

        _togglePinCommand = new RelayCommand<ProcessRowViewModel>(TogglePin);
        _toggleSelectedPinCommand = new RelayCommand(_ => TogglePin(SelectedRow), _ => SelectedRow != null);
        _addEventCommand = new RelayCommand(_ => AddEvent(), _ => SelectedRow != null);
        _deleteEventCommand = new RelayCommand(_ => DeleteEvent(), _ => SelectedEvent != null);
        _saveConfigCommand = new RelayCommand(_ => SaveConfig());
        _reloadConfigCommand = new RelayCommand(_ => ReloadConfig());
        _clearLogCommand = new RelayCommand(_ => { _eventSink.Clear(); StatusText = "Event log cleared."; });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private AppConfig Config => _configHolder.Config;

    /// <summary>The single full-height process list: pinned processes on top, then running processes.</summary>
    public ObservableCollection<ProcessRowViewModel> Rows { get; } = new();

    /// <summary>Start/stop events of the currently selected process.</summary>
    public ObservableCollection<GameEventConfig> Events { get; } = new();

    public ProcessRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (_selectedRow == value) return;
            _selectedRow = value;
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(SelectedProcessName));
            OnPropertyChanged(nameof(HasSelection));
            ReloadEventsPanel();
        }
    }

    public GameEventConfig? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            _selectedEvent = value;
            OnPropertyChanged(nameof(SelectedEvent));
            OnPropertyChanged(nameof(HasSelectedEvent));
        }
    }

    public string SelectedProcessName => SelectedRow?.Name ?? "(no process selected)";

    public bool HasSelection => SelectedRow != null;

    public bool HasSelectedEvent => SelectedEvent != null;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    public string PollIntervalText
    {
        get => _pollIntervalText;
        set { _pollIntervalText = value; OnPropertyChanged(nameof(PollIntervalText)); }
    }

    public ICommand TogglePinCommand => _togglePinCommand;

    public ICommand ToggleSelectedPinCommand => _toggleSelectedPinCommand;

    public ICommand AddEventCommand => _addEventCommand;

    public ICommand DeleteEventCommand => _deleteEventCommand;

    public ICommand SaveConfigCommand => _saveConfigCommand;

    public ICommand ReloadConfigCommand => _reloadConfigCommand;

    public ICommand ClearLogCommand => _clearLogCommand;

    private void TogglePin(ProcessRowViewModel? row)
    {
        if (row == null) return;

        if (Config.IsPinned(row.Name))
        {
            Config.Unpin(row.Name);
            StatusText = $"Unpinned {row.Name}.";
        }
        else
        {
            Config.Pin(row.Name, row.Path, row.IsRunning);
            StatusText = $"Pinned {row.Name} — it will stay at the top of the list.";
        }

        SaveConfig();
        SyncRows();
    }

    private void AddEvent()
    {
        if (SelectedRow == null) return;
        var game = Config.GetOrAddGame(SelectedRow.Name);
        var gameEvent = new GameEventConfig { Name = "New event" };
        game.Events.Add(gameEvent);
        Events.Add(gameEvent);
        SelectedEvent = gameEvent;
        SaveConfig();
    }

    private void DeleteEvent()
    {
        if (SelectedEvent == null || SelectedRow == null) return;
        var game = Config.FindGame(SelectedRow.Name);
        game?.Events.Remove(SelectedEvent);
        Events.Remove(SelectedEvent);
        SelectedEvent = Events.LastOrDefault();
        SaveConfig();
    }

    public void SaveConfig()
    {
        if (double.TryParse(PollIntervalText, out var seconds) && seconds >= 0.2)
        {
            Config.PollInterval = seconds;
            _monitor.PollInterval = TimeSpan.FromSeconds(seconds);
        }
        else
        {
            PollIntervalText = Config.PollInterval.ToString("0.0");
        }

        _configService.Save(Config);
        StatusText = $"Saved {DateTime.Now:HH:mm:ss} — {Config.Games.Count} watched, {Config.Pinned.Count} pinned.";
    }

    public void ReloadConfig()
    {
        _configHolder.Config = _configService.Load();
        PollIntervalText = Config.PollInterval.ToString("0.0");
        _monitor.PollInterval = TimeSpan.FromSeconds(Config.PollInterval);
        SyncRows();
        ReloadEventsPanel();
        StatusText = "Config reloaded from disk.";
    }

    private void ReloadEventsPanel()
    {
        Events.Clear();
        if (SelectedRow == null) return;
        var game = Config.FindGame(SelectedRow.Name);
        if (game == null) return;
        foreach (var gameEvent in game.Events)
            Events.Add(gameEvent);
        SelectedEvent = Events.FirstOrDefault();
    }

    private void OnProcessesChanged(object? sender, IReadOnlyList<ProcessSnapshot> snapshot)
    {
        // Marshal to the UI thread — the monitor polls on a thread-pool thread.
        if (_disposed) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
        }
    }

    private void OnTransitionsOccurred(object? sender, IReadOnlyList<ProcessTransitionEvent> transitions)
    {
        if (_disposed) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Update()
        {
            var parts = transitions.Select(t =>
                t.Transition == ProcessTransitionKind.Appeared ? $"+ {t.ProcessName}" : $"- {t.ProcessName}");
            StatusText = $"{DateTime.Now:HH:mm:ss} — {string.Join(", ", parts)}";
        }

        if (dispatcher == null || dispatcher.CheckAccess())
            Update();
        else
            dispatcher.BeginInvoke(new Action(Update));
    }

    private void ApplySnapshot(IReadOnlyList<ProcessSnapshot> snapshot)
    {
        _snapshot = snapshot;
        SyncRows();
        UpdatePinnedLastSeen();
        StatusText = $"Monitoring — {snapshot.Count} running processes, {Config.Pinned.Count} pinned. Last poll {DateTime.Now:HH:mm:ss}.";
    }

    /// <summary>
    /// Rebuilds the single list in place: pinned processes first (running or not),
    /// then the remaining running processes; each section sorted by name.
    /// </summary>
    private void SyncRows()
    {
        // Keys are extension-insensitive so a config written for "game.exe"
        // matches a process reported as "game" (or vice versa).
        var runningByKey = new Dictionary<string, ProcessSnapshot>(StringComparer.Ordinal);
        foreach (var process in _snapshot)
            runningByKey[ProcessNames.Normalize(process.Name)] = process;

        var desired = new List<(string Name, int? Pid, string? Path, bool IsRunning, bool IsPinned)>();

        foreach (var pin in Config.Pinned.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            runningByKey.TryGetValue(ProcessNames.Normalize(pin.ProcessName), out var snap);
            desired.Add((snap?.Name ?? pin.ProcessName, snap?.Id, snap?.Path ?? pin.DisplayPath, snap != null, true));
        }

        foreach (var pair in runningByKey.OrderBy(p => p.Value.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (Config.IsPinned(pair.Value.Name))
                continue;
            desired.Add((pair.Value.Name, pair.Value.Id, pair.Value.Path, true, false));
        }

        var desiredKeys = new HashSet<string>(desired.Select(d => ProcessNames.Normalize(d.Name)), StringComparer.Ordinal);

        // Remove rows that are no longer desired (pinned rows are always desired).
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(ProcessNames.Normalize(Rows[i].Name)))
                Rows.RemoveAt(i);
        }

        // Insert / move rows so the collection matches the desired order, updating in place.
        for (int i = 0; i < desired.Count; i++)
        {
            var d = desired[i];
            int existingIndex = IndexOfRow(d.Name);
            if (existingIndex == i)
            {
                Rows[i].Update(d.Pid, d.Path, d.IsRunning, d.IsPinned);
            }
            else if (existingIndex > i)
            {
                var row = Rows[existingIndex];
                Rows.RemoveAt(existingIndex);
                Rows.Insert(i, row);
                row.Update(d.Pid, d.Path, d.IsRunning, d.IsPinned);
            }
            else
            {
                Rows.Insert(i, new ProcessRowViewModel(d.Name, d.Pid, d.Path, d.IsRunning, d.IsPinned));
            }
        }

        // Keep the selection valid.
        if (SelectedRow != null && !desiredKeys.Contains(ProcessNames.Normalize(SelectedRow.Name)))
            SelectedRow = null;
    }

    private int IndexOfRow(string name)
    {
        var key = ProcessNames.Normalize(name);
        for (int i = 0; i < Rows.Count; i++)
        {
            if (ProcessNames.Normalize(Rows[i].Name) == key)
                return i;
        }
        return -1;
    }

    private void UpdatePinnedLastSeen()
    {
        var runningByKey = new Dictionary<string, ProcessSnapshot>(StringComparer.Ordinal);
        foreach (var process in _snapshot)
            runningByKey[ProcessNames.Normalize(process.Name)] = process;

        foreach (var pin in Config.Pinned)
        {
            if (!runningByKey.TryGetValue(ProcessNames.Normalize(pin.ProcessName), out var snap))
                continue;

            // Minute precision: last_seen is a human-facing value, and this keeps
            // the debounced config save from writing on every poll.
            var lastSeen = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
            if (pin.LastSeen != lastSeen || pin.LastPid != snap.Id)
            {
                pin.LastSeen = lastSeen;
                pin.LastPid = snap.Id;
                _pinnedDirty = true;
            }
        }

        if (_pinnedDirty && !_debouncedSaveTimer.IsEnabled)
            _debouncedSaveTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.ProcessesChanged -= OnProcessesChanged;
        _monitor.TransitionsOccurred -= OnTransitionsOccurred;
        _monitor.Stop();
        _debouncedSaveTimer.Stop();
        if (_pinnedDirty)
        {
            _pinnedDirty = false;
            SaveConfig();
        }
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?> _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (_ => true);
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);
    }

    private sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;

        public RelayCommand(Action<T?> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => parameter is T;

        public void Execute(object? parameter) => _execute(parameter is T t ? t : default);
    }
}
