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
    private readonly GoogleJapanese google;
    private readonly TranslationCache translations;
    private int translationRevision = -1;
    private readonly Dictionary<string, string> drafts = [];
    private (string PluginId, string Text)? requestedExecution;
    private string status = "", customCommand = "", customHelp = "";
    private string? addingTo;
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
        config.Presets ??= [];
        ClientLanguage? game;
        try { game = client.ClientLanguage; } catch { game = null; }
        var language = Loc.Resolve(config.Language, game, pi.UiLanguage);
        if (config.Language != language) { config.Language = language; pi.SavePluginConfig(config); }
        fonts = new LocalizedFonts(pi.UiBuilder.FontAtlas, []);
        icons = new IconCache(Path.Combine(pi.GetPluginConfigDirectory(), "icon-cache-v1"));
        google = new GoogleJapanese();
        translations = new TranslationCache(Path.Combine(pi.GetPluginConfigDirectory(), "translations-google-ja-v1.json"), google.Translate);
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
    private void Filter() { filtered = Catalog.Filter(rows, search, config.ShowHidden, config.JapaneseDescriptions ? text => translations.Get(text, false) : null); page = 0; }
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
                CommandCatalog.Expand(groups.Remove(p.InternalName, out var list) ? list.OrderBy(c => c.Command, StringComparer.Ordinal).ToArray() : [], p.InternalName, config.Presets))).Where(p => p.Loaded).ToList();
            var remaining = groups.Values.SelectMany(x => x).OrderBy(x => x.Command, StringComparer.Ordinal).ToArray();
            if (remaining.Length > 0) result.Add(new("", T("Other"), T("OtherHelp"), "", true, CommandCatalog.Expand(remaining, "", config.Presets)));
            rows = result.ToArray();
            var validKeys = rows.SelectMany(p => p.Commands.Select(c => p.Id + "\n" + c.Command)).ToHashSet();
            foreach (var key in drafts.Keys.Where(k => !validKeys.Contains(k)).ToArray()) drafts.Remove(key);
            RefreshFonts();
            Filter(); failed = false;
        }
        catch (Exception ex) { failed = true; rows = []; Filter(); log.Error(ex, "Could not read command catalog."); }
    }
    private void RefreshFonts()
    {
            var nextText = string.Concat(rows.SelectMany(p => new[] { p.Name, p.Description }.Concat(p.Commands.SelectMany(c => new[] { c.Command, c.Help })))) + translations.FontText;
            if (nextText != fontText)
            {
                var replacement = new LocalizedFonts(pi.UiBuilder.FontAtlas, [nextText]);
                fonts.Dispose(); fonts = replacement; fontText = nextText;
            }
            translationRevision = translations.Revision;
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
        if (!open) { translations.SetActive(false); return; }
        while (icons.TryTakeChanged(out var path)) textureUpdates.InvalidatePaths([path]);
        if (dirty) Reload();
        if (translationRevision != translations.Revision) { RefreshFonts(); var previous = page; Filter(); page = Math.Min(previous, Math.Max(0, (filtered.Length - 1) / PageSize)); }
        DrawWindow();
        // Dispatch only after every ImGui scope and font scope has closed.
        if (requestedExecution is { } request) { requestedExecution = null; Execute(request.PluginId, request.Text); }
    }
    private void DrawWindow()
    {
        var catalogVisible = false;
        using var font = fonts.Push(config.Language);
        ImGui.SetNextWindowSize(new Vector2(800, 650), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(460, 330), new Vector2(float.MaxValue));
        if (ImGui.Begin("Command Atlas###CommandAtlas", ref open))
        {
            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem(T("Commands") + "###Commands")) { catalogVisible = true; translations.SetActive(config.JapaneseDescriptions); DrawCatalog(); ImGui.EndTabItem(); }
                StandardPluginTabs.DrawStandardTrailingTabs(T("Settings"), "Settings", DrawSettings, T("Language"), DrawLanguage,
                    T("Support"), () => StandardPluginTabs.DrawSupportContent(T("SupportHeading"), T("SupportBody"), T("Recipient"), T("SupportButton")));
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
        translations.SetActive(open && catalogVisible && config.JapaneseDescriptions);
        translations.Pump();
    }
    private void DrawCatalog()
    {
        translations.BeginFrame();
        ImGui.SetNextItemWidth(Math.Max(180, ImGui.GetContentRegionAvail().X - 120));
        if (ImGui.InputTextWithHint("###Search", T("Search"), ref search, 256)) Filter();
        ImGui.SameLine();
        if (ImGui.Button(T("Refresh") + "###Refresh")) Reload();
        ImGui.TextWrapped(T("Info"));
        DrawTranslationToggle();
        if (failed) ImGui.TextWrapped(T("Error"));
        if (DateTime.UtcNow < copiedUntil) ImGui.TextColored(new Vector4(.4f, .9f, .65f, 1), T(status) + ": " + copied);
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
                DrawDescription(plugin.Description);
                if (!plugin.Loaded) ImGui.TextDisabled(T("Disabled"));
                if (expanded)
                {
                    if (list.Length == 0) ImGui.TextWrapped(T("NoCommands"));
                    if (ImGui.BeginTable("CommandList", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn(T("Commands"), ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn(T("Actions"), ImGuiTableColumnFlags.WidthFixed, Math.Max(150, ImGui.CalcTextSize(T("Run") + T("Copy")).X + 40));
                        ImGui.TableHeadersRow();
                        foreach (var command in list)
                        {
                            DrawCommand(plugin, command);
                        }
                        ImGui.EndTable();
                    }
                    DrawAddCommand(plugin);
                    ImGui.TreePop();
                }
                ImGui.EndGroup(); ImGui.Separator(); ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }
    private void DrawTranslationToggle()
    {
        var enabled = config.JapaneseDescriptions;
        if (ImGui.Checkbox(T("JapaneseDescriptions") + "###Translate", ref enabled))
        {
            config.JapaneseDescriptions = enabled; pi.SavePluginConfig(config); translations.SetActive(enabled); Filter();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("TranslationInfo"));
        if (enabled) { ImGui.SameLine(); ImGui.TextDisabled(T(translations.Status)); }
    }
    private void DrawDescription(string text, bool external = true)
    {
        var display = config.JapaneseDescriptions && external ? translations.Get(text, ImGui.IsRectVisible(new Vector2(1, ImGui.GetTextLineHeight()))) : text;
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(display) ? T("NoHelp") : display);
        if (display != text && ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.PushTextWrapPos(500); ImGui.TextUnformatted(text); ImGui.PopTextWrapPos(); ImGui.EndTooltip(); }
    }
    private void DrawCommand(PluginRow plugin, CommandRow command)
    {
        ImGui.PushID(command.Command);
        ImGui.TableNextRow(); ImGui.TableNextColumn();
        var key = plugin.Id + "\n" + command.Command;
        var text = drafts.GetValueOrDefault(key, command.Command);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("###CommandText", ref text, 1024)) drafts[key] = text;
        if (command.Origin != "Registered") ImGui.TextDisabled(T(command.Origin));
        DrawDescription(command.Help, command.Origin != "Manual");
        ImGui.TableNextColumn();
        var validRoot = CommandCatalog.Root(text) == CommandCatalog.Root(command.Command);
        ImGui.BeginDisabled(!validRoot || !CommandCatalog.Executable(text));
        if (ImGui.Button(T("Run") + "###Run")) requestedExecution = (plugin.Id, text.Trim());
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button(T("Copy") + "###Copy")) { ImGui.SetClipboardText(text); Feedback("Copied", text); }
        if (!validRoot || !CommandCatalog.Executable(text)) ImGui.TextWrapped(T("FillArguments"));
        if (command.PresetId != null && ImGui.SmallButton(T("Remove") + "###Remove"))
        {
            config.Presets.RemoveAll(p => p.Id == command.PresetId); pi.SavePluginConfig(config); dirty = true;
        }
        ImGui.PopID();
    }
    private void Feedback(string key, string text) { status = key; copied = text; copiedUntil = DateTime.UtcNow.AddSeconds(4); }
    private bool Execute(string pluginId, string text)
    {
        try
        {
            if (!CommandCatalog.Executable(text) || !commands.Commands.TryGetValue(CommandCatalog.Root(text), out var current)) throw new InvalidOperationException("Command unavailable");
            var owner = pi.GetPlugin(current.Handler.Method.Module.Assembly);
            if ((owner?.InternalName ?? "") != pluginId || (owner != null && !owner.IsLoaded)) throw new InvalidOperationException("Owner changed or unloaded");
            if (!commands.ProcessCommand(text)) throw new InvalidOperationException("Dispatch rejected");
            Feedback("Executed", text); return true;
        }
        catch (Exception ex) { log.Warning(ex, "Could not execute selected command"); Feedback("ExecutionFailed", text); dirty = true; return false; }
    }
    private void DrawAddCommand(PluginRow plugin)
    {
        if (ImGui.SmallButton(T("AddSubcommand") + "###Add")) { addingTo = plugin.Id; customCommand = plugin.Commands.FirstOrDefault()?.Command.Split(' ')[0] + " "; customHelp = ""; }
        if (addingTo != plugin.Id) return;
        ImGui.TextWrapped(T("SubcommandInfo"));
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("###CustomCommand", T("CommandText"), ref customCommand, 1024);
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("###CustomHelp", T("Description"), ref customHelp, 2048);
        if (ImGui.Button(T("Save") + "###Save"))
        {
            var text = customCommand.Trim();
            if (config.Presets.Count >= 256 || !CommandCatalog.ValidText(text) || !plugin.Commands.Any(c => c.Origin == "Registered" && c.Command == CommandCatalog.Root(text)) || plugin.Commands.Any(c => c.Command == text))
                Feedback("InvalidPreset", text);
            else
            {
                config.Presets.Add(new() { PluginId = plugin.Id, Command = text, Help = customHelp.Trim() });
                pi.SavePluginConfig(config); addingTo = null; dirty = true;
            }
        }
        ImGui.SameLine(); if (ImGui.Button(T("Cancel") + "###Cancel")) addingTo = null;
    }
    private void DrawSettings()
    {
        var hidden = config.ShowHidden;
        if (ImGui.Checkbox(T("Hidden") + "###Hidden", ref hidden)) { config.ShowHidden = hidden; pi.SavePluginConfig(config); Filter(); }
        ImGui.TextWrapped(T("CacheInfo"));
        ImGui.TextWrapped(T("TranslationInfo"));
        ImGui.TextWrapped(T("SubcommandInfo"));
    }
    private void DrawLanguage()
    {
        var selected = StandardPluginTabs.DrawLanguageSelector(T("Language"), T("LanguageHelp"), config.Language!);
        if (selected != null && selected != config.Language)
        {
            config.Language = selected; pi.SavePluginConfig(config); entry.HelpMessage = T("CommandHelp");
            rows = rows.Select(p => p.Id.Length == 0 ? p with { Name = T("Other"), Description = T("OtherHelp") }
                : p.Id == "CommandAtlas" ? p with { Commands = p.Commands.Select(c => c.Command == "/commandatlas" ? c with { Help = entry.HelpMessage } : c).ToArray() } : p).ToArray(); Filter();
        }
    }
    public void Dispose()
    {
        pi.ActivePluginsChanged -= PluginsChanged; pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenMainUi -= Open; pi.UiBuilder.OpenConfigUi -= Open;
        commands.RemoveHandler("/commandatlas"); translations.Dispose(); google.Dispose(); icons.Dispose(); fonts.Dispose();
    }
}
