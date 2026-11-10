using Spawn.Models;

namespace Spawn.Services;

/// <summary>Holds the live config so the monitor and the view model share one instance.</summary>
public sealed class ConfigHolder
{
    public ConfigHolder(AppConfig config)
    {
        Config = config;
    }

    public AppConfig Config { get; set; }
}
