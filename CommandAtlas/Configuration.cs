using Dalamud.Configuration;

namespace CommandAtlas;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string? Language { get; set; }
    public bool ShowHidden { get; set; }
    public bool JapaneseDescriptions { get; set; }
    public List<CommandPreset> Presets { get; set; } = [];
}

public sealed class CommandPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PluginId { get; set; } = "";
    public string Command { get; set; } = "";
    public string Help { get; set; } = "";
}
