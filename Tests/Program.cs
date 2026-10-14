using CommandAtlas;
using Dalamud.Game;

var checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
PluginRow[] rows = [new("Alpha", "Alpha Plugin", "Inventory tools", "", true, [new("/alpha", "Open inventory", false), new("/secret", "Debug", true)]), new("Beta", "Beta", "Travel", "", false, [])];
Check(Catalog.Filter(rows, "", false).Length == 1, "Stopped plugins hidden");
Check(Catalog.Filter(rows, "INVENTORY", false)[0].Commands.Length == 1, "Description and case-insensitive search");
Check(Catalog.Filter(rows, "/alpha", false).Single().Commands.Single().Command == "/alpha", "Command search");
Check(Catalog.Filter(rows, "Debug", false).Length == 0, "Hidden commands excluded");
Check(Catalog.Filter(rows, "Debug", true).Single().Commands.Single().Command == "/secret", "Hidden commands opt-in");
Check(Catalog.Filter(rows, "  travel  ", false).Length == 0, "Stopped plugins hidden from search");
Check(Catalog.Filter(rows, "missing", false).Length == 0, "Empty results");
Check(Loc.Resolve("de", ClientLanguage.Japanese, "fr") == "de", "Saved selection wins");
Check(Loc.Resolve(null, ClientLanguage.Japanese, "fr") == "ja", "Game wins");
Check(Loc.Resolve("Auto", null, "ko-KR") == "ko", "Invalid saved choice and UI fallback");
Check(Loc.Resolve(null, null, "zh") == "en", "Ambiguous zh fallback");
Check(Loc.Resolve(null, null, null) == "en", "Final fallback");
foreach (var (input, expected) in new[] { ("ja-JP","ja"), ("EN_us","en"), ("de-DE","de"), ("fr-FR","fr"), ("ko-KR","ko"), ("zh-CN","zh-Hans"), ("zh-SG","zh-Hans"), ("zh-TW","zh-Hant"), ("zh-HK","zh-Hant"), ("zh-MO","zh-Hant"), ("zh-Hans-CN","zh-Hans"), ("zh-Hant-TW","zh-Hant") })
    Check(Loc.Normalize(input) == expected, "Normalize " + input);
foreach (var code in Loc.Codes)
{
    Check(Loc.Resolve(code, ClientLanguage.English, "ja") == code, "Preserve " + code);
    foreach (var key in Loc.Messages.Keys) Check(!string.IsNullOrWhiteSpace(Loc.Get(code, key)), code + ":" + key);
}
Console.WriteLine($"Passed {checks} catalog and localization checks.");
await CacheChecks.Run();
await FeatureChecks.Run(args.Contains("--live-translation"));
