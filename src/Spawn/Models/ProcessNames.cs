namespace Spawn.Models;

/// <summary>
/// Process-name helpers. Windows reports process image names with the extension
/// (e.g. "eldenring.exe", as psutil did in the Python app), but
/// System.Diagnostics.Process.ProcessName strips ".exe". All comparisons in
/// Spawn are therefore extension-insensitive so configs written for
/// "game.exe" match a process reported as "game".
/// </summary>
public static class ProcessNames
{
    /// <summary>Canonical comparison key: trimmed, without a trailing ".exe", lowercased.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.ToLowerInvariant();
    }

    public static bool Equals(string? a, string? b) => Normalize(a) == Normalize(b);
}
