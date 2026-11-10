using System.IO;
namespace Spawn.Services;

public interface IEventSink
{
    void Log(string processName, string action, string detail);
}

/// <summary>Timestamped append-only event log at %APPDATA%\Spawn\events.log.</summary>
public sealed class FileEventSink : IEventSink
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileEventSink(string? path = null)
    {
        _path = path ?? AppPaths.EventLogPath;
    }

    public string Path => _path;

    public void Log(string processName, string action, string detail)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | process={processName} | action={action} | command={detail}";
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the monitor.
        }
    }

    public IReadOnlyList<string> ReadAll()
    {
        try
        {
            if (!File.Exists(_path))
                return Array.Empty<string>();
            lock (_gate)
            {
                return File.ReadAllLines(_path);
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Clear()
    {
        try
        {
            lock (_gate)
            {
                File.WriteAllText(_path, string.Empty);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
