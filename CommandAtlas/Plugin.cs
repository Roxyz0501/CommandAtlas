using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Roxyz0501.Dalamud.CommonUi;

namespace CommandAtlas;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly ITextureProvider textures;
    private readonly ITextureSubstitutionProvider textureUpdates;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private LocalizedFonts fonts;
    private string fontText = "";
    private readonly IconCache icons;
    private readonly CommandInfo entry;
    private PluginRow[] rows = [];
    private (PluginRow Plugin, CommandRow[] Commands)[] filtered = [];
    private volatile bool dirty = true;
    private bool open, failed;
    private string search = "", copied = "";
    private DateTime copiedUntil;
    private int page;
    private const int PageSize = 20;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IClientState client, ITextureProvider textures, ITextureSubstitutionProvider textureUpdates, IPluginLog log)
    {
        this.pi = pi; this.commands = commands; this.textures = textures; this.log = log;
        this.textureUpdates = textureUpdates;
        config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        ClientLanguage? game;
        try { game = client.ClientLanguage; } catch { game = null; }
        var language = Loc.Resolve(config.Language, game, pi.UiLanguage);
        if (config.Language != language) { config.Language = language; pi.SavePluginConfig(config); }
        fonts = new LocalizedFonts(pi.UiBuilder.FontAtlas, []);
        icons = new IconCache(Path.Combine(pi.GetPluginConfigDirectory(), "icon-cache-v1"));
        entry = new CommandInfo((_, _) => Open()) { HelpMessage = T("CommandHelp") };
        if (!commands.AddHandler("/commandatlas", entry)) throw new InvalidOperationException("Command /commandatlas is already registered.");
        pi.ActivePluginsChanged += PluginsChanged;
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenMainUi += Open;
        pi.UiBuilder.OpenConfigUi += Open;
    }
    private string T(string key) => Loc.Get(config.Language, key);
    private void Open() => open = true;
    private void PluginsChanged(IActivePluginsChangedEventArgs _) => dirty = true;
    private void Filter() { filtered = Catalog.Filter(rows, search, config.ShowHidden); page = 0; }
    private void Reload()
    {
        dirty = false;
        try
        {
            var installed = pi.InstalledPlugins.ToArray();
            var groups = new Dictionary<string, List<CommandRow>>();
            var owners = new Dictionary<Assembly, string>();
            foreach (var (command, info) in commands.Commands.ToArray())
            {
                var assembly = info.Handler.Method.Module.Assembly;
                if (!owners.TryGetValue(assembly, out var id))
                {
                    id = pi.GetPlugin(assembly)?.InternalName ?? "";
                    owners[assembly] = id;
                }
                if (!groups.TryGetValue(id, out var list)) groups[id] = list = [];
                list.Add(new(command, info.HelpMessage ?? "", !info.ShowInHelp));
            }
            var result = installed.Select(p => new PluginRow(p.InternalName, p.Name, p.Manifest.Description ?? "",
                IconUrl(p), p.IsLoaded,
                groups.Remove(p.InternalName, out var list) ? list.OrderBy(c => c.Command, StringComparer.Ordinal).ToArray() : [])).ToList();
            var remaining = groups.Values.SelectMany(x => x).OrderBy(x => x.Command, StringComparer.Ordinal).ToArray();
            if (remaining.Length > 0) result.Add(new("", T("Other"), T("OtherHelp"), "", true, remaining));
            rows = result.ToArray();
            var nextText = string.Concat(rows.SelectMany(p => new[] { p.Name, p.Description }.Concat(p.Commands.SelectMany(c => new[] { c.Command, c.Help }))));
            if (nextText != fontText)
            {
                var replacement = new LocalizedFonts(pi.UiBuilder.FontAtlas, [nextText]);
                fonts.Dispose(); fonts = replacement; fontText = nextText;
            }
            Filter(); failed = false;
        }
        catch (Exception ex) { failed = true; rows = []; Filter(); log.Error(ex, "Could not read command catalog."); }
    }
    private static string IconUrl(IExposedPlugin plugin)
    {
        var manifest = plugin.Manifest;
        if (plugin.IsThirdParty || plugin.IsDev || string.IsNullOrWhiteSpace(manifest.Dip17Channel)) return manifest.IconUrl ?? "";
        return "https://raw.githubusercontent.com/goatcorp/PluginDistD17/main/" + Uri.EscapeDataString(manifest.Dip17Channel)
            + "/" + Uri.EscapeDataString(plugin.InternalName) + "/images/icon.png";
    }
    private void Draw()
    {
        if (!open) return;
        while (icons.TryTakeChanged(out var path)) textureUpdates.InvalidatePaths([path]);
        if (dirty) Reload();
        using var font = fonts.Push(config.Language);
        ImGui.SetNextWindowSize(new Vector2(800, 650), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(460, 330), new Vector2(float.MaxValue));
        if (ImGui.Begin("Command Atlas###CommandAtlas", ref open))
        {
            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem(T("Commands") + "###Commands")) { DrawCatalog(); ImGui.EndTabItem(); }
                StandardPluginTabs.DrawStandardTrailingTabs(T("Settings"), "Settings", DrawSettings, T("Language"), DrawLanguage,
                    T("Support"), () => StandardPluginTabs.DrawSupportContent(T("SupportHeading"), T("SupportBody"), T("Recipient"), T("SupportButton")));
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }
    private void DrawCatalog()
    {
        ImGui.SetNextItemWidth(Math.Max(180, ImGui.GetContentRegionAvail().X - 120));
        if (ImGui.InputTextWithHint("###Search", T("Search"), ref search, 256)) Filter();
        ImGui.SameLine();
        if (ImGui.Button(T("Refresh") + "###Refresh")) Reload();
        ImGui.TextWrapped(T("Info"));
        if (failed) ImGui.TextWrapped(T("Error"));
        if (DateTime.UtcNow < copiedUntil) ImGui.TextColored(new Vector4(.4f, .9f, .65f, 1), T("Copied") + ": " + copied);
        var pages = Math.Max(1, (filtered.Length + PageSize - 1) / PageSize);
        ImGui.BeginDisabled(page == 0);
        if (ImGui.Button(T("Previous") + "###Previous")) page--;
        ImGui.EndDisabled(); ImGui.SameLine(); ImGui.TextUnformatted($"{page + 1} / {pages}"); ImGui.SameLine();
        ImGui.BeginDisabled(page + 1 >= pages);
        if (ImGui.Button(T("Next") + "###Next")) page++;
        ImGui.EndDisabled();
        if (ImGui.BeginChild("Catalog", Vector2.Zero))
        {
            if (filtered.Length == 0 && !failed) ImGui.TextWrapped(T("Empty"));
            foreach (var (plugin, list) in filtered.Skip(page * PageSize).Take(PageSize))
            {
                ImGui.PushID(plugin.Id.Length == 0 ? "Unassigned" : plugin.Id);
                // Only request textures for visible cards; names and descriptions always remain available.
                if (ImGui.IsRectVisible(new Vector2(48, 48)))
                {
                    var path = plugin.Id == "CommandAtlas"
                        ? Path.Combine(pi.AssemblyLocation.DirectoryName!, "images", "icon.png")
                        : icons.Get(plugin.IconUrl);
                    if (path != null && textures.GetFromFile(path).TryGetWrap(out var texture, out _)) ImGui.Image(texture.Handle, new Vector2(44));
                    else ImGui.Button("/ >###Placeholder", new Vector2(44));
                }
                else ImGui.Dummy(new Vector2(44));
                ImGui.SameLine();
                ImGui.BeginGroup();
                if (search.Length > 0) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                var expanded = ImGui.TreeNodeEx(plugin.Name.Replace("#", "＃") + $"  ({list.Length})###Plugin", ImGuiTreeNodeFlags.SpanAvailWidth);
                ImGui.TextWrapped(string.IsNullOrWhiteSpace(plugin.Description) ? T("NoHelp") : plugin.Description);
                if (!plugin.Loaded) ImGui.TextDisabled(T("Disabled"));
                if (expanded)
                {
                    if (list.Length == 0) ImGui.TextWrapped(T("NoCommands"));
                    foreach (var command in list)
                    {
                        ImGui.PushID(command.Command);
                        if (ImGui.Button(command.Command + "###Copy"))
                        {
                            ImGui.SetClipboardText(command.Command); copied = command.Command; copiedUntil = DateTime.UtcNow.AddSeconds(3);
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Copy"));
                        ImGui.TextWrapped(string.IsNullOrWhiteSpace(command.Help) ? T("NoHelp") : command.Help);
                        ImGui.Spacing(); ImGui.PopID();
                    }
                    ImGui.TreePop();
                }
                ImGui.EndGroup(); ImGui.Separator(); ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }
    private void DrawSettings()
    {
        var hidden = config.ShowHidden;
        if (ImGui.Checkbox(T("Hidden") + "###Hidden", ref hidden)) { config.ShowHidden = hidden; pi.SavePluginConfig(config); Filter(); }
        ImGui.TextWrapped(T("CacheInfo"));
    }
    private void DrawLanguage()
    {
        var selected = StandardPluginTabs.DrawLanguageSelector(T("Language"), T("LanguageHelp"), config.Language!);
        if (selected != null && selected != config.Language)
        {
            config.Language = selected; pi.SavePluginConfig(config); entry.HelpMessage = T("CommandHelp");
            rows = rows.Select(p => p.Id.Length == 0 ? p with { Name = T("Other"), Description = T("OtherHelp") } : p).ToArray(); Filter();
        }
    }
    public void Dispose()
    {
        pi.ActivePluginsChanged -= PluginsChanged; pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenMainUi -= Open; pi.UiBuilder.OpenConfigUi -= Open;
        commands.RemoveHandler("/commandatlas"); icons.Dispose(); fonts.Dispose();
    }
}
