using System.Text.Json.Serialization;

namespace ProcessLink.Models;

/// <summary>A start/stop command association for a watched process.</summary>
public sealed class GameEventConfig : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = "";
    private bool _enabled = true;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    [JsonPropertyName("start_commands")]
    public List<string> StartCommands { get; set; } = new();

    [JsonPropertyName("stop_commands")]
    public List<string> StopCommands { get; set; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
    }

    /// <summary>Newline-joined view of <see cref="StartCommands"/> for multiline text boxes.</summary>
    [JsonIgnore]
    public string StartCommandsText
    {
        get => string.Join(Environment.NewLine, StartCommands);
        set => StartCommands = SplitLines(value);
    }

    /// <summary>Newline-joined view of <see cref="StopCommands"/> for multiline text boxes.</summary>
    [JsonIgnore]
    public string StopCommandsText
    {
        get => string.Join(Environment.NewLine, StopCommands);
        set => StopCommands = SplitLines(value);
    }

    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

/// <summary>A watched process with its start/stop event associations.</summary>
public sealed class GameConfig
{
    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("events")]
    public List<GameEventConfig> Events { get; set; } = new();
}

/// <summary>A pinned process. Pinned processes stay at the top of the list even when not running.</summary>
public sealed class PinnedProcessConfig
{
    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("display_path")]
    public string? DisplayPath { get; set; }

    /// <summary>ISO-8601 timestamp of the last poll at which the process was seen running.</summary>
    [JsonPropertyName("last_seen")]
    public string? LastSeen { get; set; }

    [JsonPropertyName("last_pid")]
    public int? LastPid { get; set; }
}

/// <summary>Root of the JSON config file.</summary>
public sealed class AppConfig
{
    [JsonPropertyName("poll_interval")]
    public double PollInterval { get; set; } = 1.5;

    [JsonPropertyName("games")]
    public List<GameConfig> Games { get; set; } = new();

    [JsonPropertyName("pinned")]
    public List<PinnedProcessConfig> Pinned { get; set; } = new();

    public GameConfig GetOrAddGame(string processName)
    {
        var game = FindGame(processName);
        if (game == null)
        {
            game = new GameConfig { ProcessName = processName };
            Games.Add(game);
        }
        return game;
    }

    public GameConfig? FindGame(string processName)
        => Games.FirstOrDefault(g => ProcessNames.Equals(g.ProcessName, processName));

    public PinnedProcessConfig? FindPinned(string processName)
        => Pinned.FirstOrDefault(p => ProcessNames.Equals(p.ProcessName, processName));

    public bool IsPinned(string processName) => FindPinned(processName) != null;

    public void Pin(string processName, string? displayPath = null, bool running = false)
    {
        if (IsPinned(processName)) return;
        Pinned.Add(new PinnedProcessConfig
        {
            ProcessName = processName,
            DisplayPath = displayPath,
            LastSeen = running ? DateTime.Now.ToString("yyyy-MM-ddTHH:mm") : null,
        });
    }

    public void Unpin(string processName)
        => Pinned.RemoveAll(p => ProcessNames.Equals(p.ProcessName, processName));
}
