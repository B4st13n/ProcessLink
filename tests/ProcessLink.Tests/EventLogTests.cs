using Xunit;
using ProcessLink.Services;

namespace ProcessLink.Tests;

public sealed class EventLogTests : IDisposable
{
    private readonly string _dir;
    private readonly string _logPath;

    public EventLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ProcessLinkTests_" + Guid.NewGuid().ToString("N"));
        _logPath = Path.Combine(_dir, "events.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Log_AppendsTimestampedLine()
    {
        var sink = new FileEventSink(_logPath);

        sink.Log("eldenring.exe", "START", "run.ahk");
        sink.Log("eldenring.exe", "STOP", "taskkill /IM helper.exe");

        var lines = sink.ReadAll();
        Assert.Equal(2, lines.Count);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \| process=eldenring\.exe \| action=START \| command=run\.ahk$", lines[0]);
        Assert.Contains("action=STOP", lines[1]);
    }

    [Fact]
    public void ReadAll_MissingFile_ReturnsEmpty()
    {
        var sink = new FileEventSink(_logPath);
        Assert.Empty(sink.ReadAll());
    }

    [Fact]
    public void Clear_EmptiesTheLog()
    {
        var sink = new FileEventSink(_logPath);
        sink.Log("a.exe", "START", "x");
        Assert.Single(sink.ReadAll());

        sink.Clear();

        Assert.Empty(sink.ReadAll());
    }

    [Fact]
    public void Log_CreatesDirectoryIfMissing()
    {
        var sink = new FileEventSink(_logPath);
        sink.Log("a.exe", "START", "x");
        Assert.True(File.Exists(_logPath));
    }
}
