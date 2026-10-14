using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using CommandAtlas;

unsafe class Program
{
    private static string clipboard = "";
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void CaptureClipboard(void* _, byte* value) => clipboard = Marshal.PtrToStringUTF8((nint)value) ?? "";
    static T Stub<T>() where T : class => DispatchProxy.Create<T, Dummy>();
    static int checks;
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); Dummy.CacheDirectory = Path.Combine(output, "cache");
        ImGui.CreateContext(); var io = ImGui.GetIO(); io.DisplaySize = new(1440, 900); io.DeltaTime = 1f / 60; io.ConfigInputTrickleEventQueue = false;
        io.SetClipboardTextFn = (void*)(delegate* unmanaged[Cdecl]<void*, byte*, void>)&CaptureClipboard;
        using (var plugin = new Plugin(Stub<IDalamudPluginInterface>(), Stub<ICommandManager>(), Stub<IClientState>(), Stub<ITextureProvider>(), Stub<ITextureSubstitutionProvider>(), Stub<IPluginLog>()))
        {
            var type = typeof(Plugin);
            object? Invoke(string method) => type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(plugin, null);
            Invoke("Reload"); // Build fonts outside the native frame, as the host's managed atlas does.
            io.Fonts.Build();
            var loc = type.Assembly.GetType("CommandAtlas.Loc")!;
            var text = (string)loc.GetProperty("FontText")!.GetValue(null)!;
            foreach (var font in Dummy.Fonts)
                foreach (var ch in text.Distinct().Where(c => !char.IsControl(c))) Check(font.FindGlyphNoFallback(ch) != null, $"Glyph {ch}");
            var textures = new Dictionary<ulong, (nint, int, int)>();
            for (var i = 0; i < io.Fonts.Textures.Size; i++)
            {
                byte* pixels; int w = 0, h = 0, bpp = 0; io.Fonts.GetTexDataAsRGBA32(i, &pixels, &w, &h, &bpp);
                io.Fonts.SetTexID(i, new ImTextureID((ulong)i + 1)); textures[(ulong)i + 1] = ((nint)pixels, w, h);
            }
            void Frame() { ImGui.NewFrame(); Invoke("Draw"); ImGui.Render(); }
            void Click(float x, float y) { io.AddMousePosEvent(x, y); Frame(); io.AddMouseButtonEvent(0, true); Frame(); io.AddMouseButtonEvent(0, false); Frame(); Frame(); }
            Dummy.Commands["/commandatlas"].Handler("/commandatlas", "");
            Frame(); Frame();
            Click(145, 204); // Expand the first plugin card.
            foreach (var code in new[] { "ja", "en", "de", "fr", "ko", "zh-Hans", "zh-Hant" })
            {
                Dummy.Config.Language = code;
                Frame(); Frame(); Frame();
                Check(ImGui.GetDrawData().TotalVtxCount > 0, "Draw " + code);
                Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, code + ".bmp"));
            }
            Check(Dummy.CatalogReads == 1 && Dummy.CommandReads == 1, "Unchanged rendering has no catalog reads");
            Dummy.Config.Language = "ja"; Frame(); Frame();
            Click(145, 309); Click(180, 357);
            Check(clipboard == "/example", "One click copies the command without dispatching its handler or using the OS clipboard");
            Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, "copy-ja.bmp"));
            var saves = Dummy.SaveCount;
            Dummy.Config.Language = "en"; Frame(); Frame();
            Click(185, 100); // Settings tab.
            Frame(); Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, "settings.bmp"));
            Check(Dummy.SaveCount == saves, "Tab navigation has no saves");
            // Filtering reads the snapshot only, without reading Dalamud again.
            type.GetField("search", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(plugin, "/example"); Invoke("Filter");
            Check(Dummy.CatalogReads == 1 && Dummy.CommandReads == 1, "Search reads snapshot only");
            Check(Dummy.SaveCount == saves, "Search has no writes");
        }
        Check(Dummy.Commands.Count == 1 && Dummy.Commands.ContainsKey("/example"), "Dispose removes only own command");
        ImGui.DestroyContext(); foreach (var pin in Dummy.Pins) pin.Free();
        Console.WriteLine($"Passed {checks} glyph, production-rendering, lifecycle and access-count checks.");
    }
}

public unsafe class Dummy : DispatchProxy
{
    public static Configuration Config = new() { Language = "ja" };
    public static string CacheDirectory = "";
    public static int SaveCount, CatalogReads, CommandReads;
    public static Dictionary<string, IReadOnlyCommandInfo> Commands = new() { ["/example"] = new CommandInfo((_, _) => throw new Exception("Must not execute commands")) { HelpMessage = "Example command help / コマンドの説明" } };
    public static List<ImFontPtr> Fonts = [];
    public static List<GCHandle> Pins = [];
    public ImFontPtr Font;
    public string Id = "";
    static IExposedPlugin Exposed(string id)
    {
        var value = DispatchProxy.Create<IExposedPlugin, Dummy>(); ((Dummy)(object)value).Id = id; return value;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "GetPluginConfig": return Config;
            case "GetPluginConfigDirectory": return CacheDirectory;
            case "get_AssemblyLocation": return new FileInfo(typeof(Plugin).Assembly.Location);
            case "GetFromFile": return DispatchProxy.Create(method.ReturnType, typeof(Dummy));
            case "get_InstalledPlugins": CatalogReads++; return new[] { Exposed("ExampleTools"), Exposed("StoppedTools"), Exposed("CommandAtlas") };
            case "GetPlugin": return Exposed((Assembly)args![0]! == typeof(Plugin).Assembly ? "CommandAtlas" : "ExampleTools");
            case "get_InternalName": return Id;
            case "get_Name": return Id == "ExampleTools" ? "Example Tools" : Id == "StoppedTools" ? "Stopped Tools" : "Command Atlas";
            case "get_IsLoaded": return Id != "StoppedTools";
            case "get_Manifest": var manifest = DispatchProxy.Create(method.ReturnType, typeof(Dummy)); ((Dummy)manifest).Id = Id; return manifest;
            case "get_Description": return Id == "CommandAtlas" ? "Browse and copy commands grouped by plugin." : "Example plugin description / プラグインの説明";
            case "get_Commands": CommandReads++; return new ReadOnlyDictionary<string, IReadOnlyCommandInfo>(Commands);
            case "SavePluginConfig": SaveCount++; return null;
            case "AddHandler": return Commands.TryAdd((string)args![0]!, (CommandInfo)args[1]!);
            case "RemoveHandler": return Commands.Remove((string)args![0]!);
            case "get_UiBuilder": case "get_FontAtlas": return DispatchProxy.Create(method.ReturnType, typeof(Dummy));
            case "get_BuildStep": return FontAtlasBuildStep.PreBuild;
            case "NewDelegateFontHandle":
                var toolkit = DispatchProxy.Create<IFontAtlasBuildToolkitPreBuild, Dummy>();
                ((FontAtlasBuildStepDelegate)args![0]!)(toolkit);
                var handle = DispatchProxy.Create<IFontHandle, Dummy>(); ((Dummy)(object)handle).Font = Fonts.Last(); return handle;
            case "AddDalamudAssetFont":
                var safe = (SafeFontConfig)args![1]!;
                var pin = GCHandle.Alloc(safe.GlyphRanges, GCHandleType.Pinned); Pins.Add(pin);
                var native = ImGui.ImFontConfig();
                native.FontDataOwnedByAtlas = true; native.OversampleH = 2; native.OversampleV = 1; native.RasterizerMultiply = 1; native.GlyphMaxAdvanceX = float.MaxValue; native.FontNo = safe.FontNo;
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher/dalamudAssets/dev/UIRes/NotoSansCJK-Regular.ttc");
                var font = ImGui.GetIO().Fonts.AddFontFromFileTTF(path, safe.SizePx, native, (ushort*)pin.AddrOfPinnedObject());
                native.Destroy(); Fonts.Add(font); return font;
            case "Push": ImGui.PushFont(Font); return new PopFont();
        }
        return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
    private sealed class PopFont : IDisposable { public void Dispose() => ImGui.PopFont(); }
}
