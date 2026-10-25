using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace ProcessLink.ViewModels;

/// <summary>One row of the single process list: a pinned process or a running process.</summary>
public sealed class ProcessRowViewModel : INotifyPropertyChanged
{
    private int? _pid;
    private string? _path;
    private bool _isRunning;
    private bool _isPinned;

    public ProcessRowViewModel(string name, int? pid, string? path, bool isRunning, bool isPinned)
    {
        Name = name;
        _pid = pid;
        _path = path;
        _isRunning = isRunning;
        _isPinned = isPinned;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string Name { get; }

    public int? Pid
    {
        get => _pid;
        private set { _pid = value; OnPropertyChanged(nameof(Pid)); }
    }

    public string? Path
    {
        get => _path;
        private set { _path = value; OnPropertyChanged(nameof(Path)); }
    }

    /// <summary>True when the process is alive right now (live detection indicator).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(LiveIndicator));
            OnPropertyChanged(nameof(LiveColor));
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        private set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            OnPropertyChanged(nameof(IsPinned));
            OnPropertyChanged(nameof(PinButtonText));
            OnPropertyChanged(nameof(IsPinnedFontWeight));
        }
    }

    /// <summary>Green dot when running, hollow gray dot otherwise.</summary>
    public string LiveIndicator => IsRunning ? "●" : "○";

    public System.Windows.Media.Brush LiveColor => IsRunning ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.Gray;

    public string PinButtonText => IsPinned ? "Unpin" : "Pin";

    public System.Windows.FontWeight IsPinnedFontWeight => IsPinned ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

    public void Update(int? pid, string? path, bool isRunning, bool isPinned)
    {
        Pid = pid;
        Path = path;
        IsRunning = isRunning;
        IsPinned = isPinned;
    }
}
