using System.Text.RegularExpressions;

namespace CommandAtlas;

internal static class CommandCatalog
{
    public static string Root(string text) => text.Trim().Split(' ', 2)[0];
    public static bool ValidText(string text) => text.Length is > 0 and <= 1024 && text.StartsWith('/') && !text.Any(char.IsControl);
    public static bool Executable(string text) => ValidText(text) && text.IndexOfAny(['<', '>', '[', ']', '{', '}', '|']) < 0;

    // Only explicit full commands at the start of help lines are extracted. No guessing from prose.
    public static CommandRow[] Expand(CommandRow[] registered, string pluginId, IEnumerable<CommandPreset> presets)
    {
        var result = new List<CommandRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roots = registered.Select(c => c.Command).ToHashSet(StringComparer.Ordinal);
        foreach (var row in registered)
        {
            if (seen.Add(row.Command)) result.Add(row);
            foreach (var line in row.Help.Replace("\r", "").Split('\n').Take(256))
            {
                var match = Regex.Match(line.Trim(), @"^(?:[-*]\s+|Usage:\s*|使用例[:：]\s*)?`?(?<command>/[^\s`]+\s+[^`]+?)(?:`|\s+[-–—]\s+|\s*:\s+|$)(?<help>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                if (!match.Success) continue;
                var command = match.Groups["command"].Value.Trim();
                // A bare sentence after a slash command is ambiguous. Accept explicit argument syntax or separated descriptions only.
                if (!ValidText(command) || !roots.Contains(Root(command)) || command == Root(command)) continue;
                if (match.Groups["help"].Value.Length == 0 && !line.Contains('`') && !line.TrimStart().StartsWith("Usage:", StringComparison.Ordinal) && command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2) continue;
                if (seen.Add(command)) result.Add(new(command, match.Groups["help"].Value.Trim(), registered.First(c => c.Command == Root(command)).Hidden, "FromHelp"));
            }
        }
        foreach (var preset in presets.Take(256).Where(p => p.PluginId == pluginId))
            if (ValidText(preset.Command) && roots.Contains(Root(preset.Command)) && seen.Add(preset.Command))
                result.Add(new(preset.Command, preset.Help, registered.First(c => c.Command == Root(preset.Command)).Hidden, "Manual", preset.Id));
        return result.ToArray();
    }
}
