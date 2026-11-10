using System.Diagnostics;

namespace Spawn.Services;

public interface ICommandLauncher
{
    void Launch(string command);
}

/// <summary>
/// Launches a shell command detached through cmd.exe with no console window,
/// so any program can be started: .exe, .bat, .ahk (quoted with its interpreter), etc.
/// </summary>
public sealed class CmdLauncher : ICommandLauncher
{
    public void Launch(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(startInfo);
    }
}
