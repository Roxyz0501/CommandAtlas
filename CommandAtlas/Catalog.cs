namespace CommandAtlas;

internal sealed record CommandRow(string Command, string Help, bool Hidden);
internal sealed record PluginRow(string Id, string Name, string Description, string IconUrl, bool Loaded, CommandRow[] Commands);

internal static class Catalog
{
    public static (PluginRow Plugin, CommandRow[] Commands)[] Filter(IEnumerable<PluginRow> plugins, string query, bool hidden)
    {
        query = query.Trim();
        bool Match(string text) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
        return plugins.Select(p =>
        {
            var whole = Match(p.Name) || Match(p.Description) || Match(p.Id);
            var commands = p.Commands.Where(c => (hidden || !c.Hidden) && (whole || Match(c.Command) || Match(c.Help))).ToArray();
            return (Plugin: p, Commands: commands, Show: whole || commands.Length > 0);
        }).Where(x => x.Show).OrderBy(x => x.Plugin.Name, StringComparer.OrdinalIgnoreCase)
          .Select(x => (x.Plugin, x.Commands)).ToArray();
    }
}
