using Dalamud.Configuration;

namespace CommandAtlas;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string? Language { get; set; }
    public bool ShowHidden { get; set; }
}
