using CommandAtlas;
using System.Text.Json;

static class FeatureChecks
{
    public static async Task Run(bool live)
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        var roots = new[] { new CommandRow("/x", "/x open - Open settings\n/x start <name> - Start a named task\n/x opens the settings window.\nOther /x stop prose\n/y stop - Different root", false) };
        var custom = new CommandPreset { PluginId = "X", Command = "/x stop", Help = "Stop" };
        var expanded = CommandCatalog.Expand(roots, "X", [custom]);
        Check(expanded.Length == 4, "Base, two documented subcommands and manual preset");
        Check(expanded[1].Command == "/x open" && expanded[1].Help == "Open settings", "Separated help parsed");
        Check(!CommandCatalog.Executable(expanded[2].Command), "Placeholder cannot execute");
        Check(CommandCatalog.Executable("/x start example"), "Completed arguments can execute");
        Check(!CommandCatalog.Executable("/x\n/x stop"), "Multiple lines cannot execute");
        Check(CommandCatalog.Expand(roots, "Y", [custom]).Length == 3, "Custom command scoped to plugin");
        Check(CommandCatalog.Expand(roots, "X", [custom, custom]).Length == 4, "Duplicate presets collapsed");
        var roundtrip = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(new Configuration { Language = "de", JapaneseDescriptions = true, Presets = [custom] }))!;
        Check(roundtrip.Language == "de" && roundtrip.JapaneseDescriptions && roundtrip.Presets.Single().Command == "/x stop", "Saved language, translation toggle and preset persist");
        var legacy = JsonSerializer.Deserialize<Configuration>("{\"Version\":1,\"Language\":\"ja\",\"ShowHidden\":true}")!;
        Check(legacy.Language == "ja" && !legacy.JapaneseDescriptions && legacy.Presets.Count == 0 && legacy.ShowHidden, "Old config migrates without enabling network translation");
        Check(GoogleJapanese.Split("ZXQ0000QXZ\n設定\n\nZXQ0001QXZ\n実行", 2).SequenceEqual(new[] { "設定", "実行" }), "Translation batch split");
        try { GoogleJapanese.Split("wrong response", 2); throw new Exception("Accepted corrupt batch"); } catch (InvalidDataException) { checks++; }
        var path = Path.Combine(AppContext.BaseDirectory, "translation-checks", Guid.NewGuid().ToString("N"), "cache.json");
        int calls = 0, sent = 0;
        using (var cache = new TranslationCache(path, async (texts, token) => { calls++; sent += texts.Length; await Task.Delay(20, token); return texts.Select(x => "和訳：" + x).ToArray(); }))
        {
            cache.Get("Open settings", true); cache.Pump(); Check(calls == 0, "Off makes no requests");
            cache.SetActive(true);
            for (int i = 0; i < 100; i++) cache.Get("Open settings", true);
            cache.Get("Start a task", true); cache.Pump();
            await Until(() => cache.IsIdle);
            Check(calls == 1 && sent == 2, "100 repeated rows and two descriptions coalesced into one batch");
            Check(cache.Get("Open settings", true) == "和訳：Open settings", "Translation cached");
            for (int i = 0; i < 100; i++) { cache.Get("Open settings", true); cache.Pump(); }
            Check(calls == 1, "Unchanged view no retransmission");
            cache.Get("Hidden old row", true); cache.BeginFrame(); cache.SetActive(false); cache.Pump(); Check(calls == 1, "Hidden or disabled queue stops");
        }
        using (var warm = new TranslationCache(path, (_, _) => throw new Exception("Warm lookup used network")))
        {
            warm.SetActive(true); Check(warm.Get("Open settings", true) == "和訳：Open settings", "Cache survives restart"); warm.Pump();
            Check(warm.IsIdle, "No request on warm view");
        }
        var failCalls = 0;
        using (var failed = new TranslationCache(path + ".failure", (_, _) => { failCalls++; throw new IOException("offline"); }))
        {
            failed.SetActive(true); failed.Get("Unavailable description", true); failed.Pump(); await Until(() => failed.IsIdle);
            for (int i = 0; i < 100; i++) { failed.Get("Unavailable description", true); failed.Pump(); }
            Check(failCalls == 1 && failed.Status == "TranslationFailed", "Failure backoff blocks repeat traffic");
        }
        if (live)
        {
            using var google = new GoogleJapanese();
            var result = await google.Translate(["Open the settings window.", "Copy the selected command to the clipboard."], CancellationToken.None);
            Check(result.Length == 2 && result.All(x => x.Any(c => c is >= '\u3040' and <= '\u9fff')), "Live keyless Japanese batch translation");
            Console.WriteLine("Live translation: " + string.Join(" / ", result));
        }
        Console.WriteLine($"Passed {checks} subcommand, migration, execution-input and translation-cache checks.");
    }
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 300; i++) { if (condition()) return; await Task.Delay(10); } throw new Exception("Timed out"); }
}
