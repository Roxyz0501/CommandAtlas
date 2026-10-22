using System.Net;
using System.Net.Http;
using System.Text.Json;
using CommandAtlas;

internal static class PersistenceChecks
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static async Task Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "command-atlas-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new Configuration { Language = "en" }));
            using (var store = new ConfigurationStore(path, (message, error) => Console.WriteLine(message + ": " + error)))
            {
                for (int i = 0; i < 480; i++) store.Tick(Environment.TickCount64 + (i + 1) * 60000L);
                Check(store.Writes == 0 && store.LogicalBytes == 0, "8h unchanged = 0 writes / 0 payload bytes");
                for (int i = 0; i < 600; i++) store.Edit(c => c.Language = i % 2 == 0 ? "ja" : "de");
                Check(store.Writes == 0, "600 UI edits do not write immediately");
                store.Tick(Environment.TickCount64 + 481 * 60000L);
                Check(store.Writes == 1, "Continuous edits coalesce into one periodic save");
                Console.WriteLine($"Persistence measured: 600 edits => {store.Writes} commit / {store.LogicalBytes} UTF-8 payload bytes; 480 idle periods => 0 commits / 0 bytes.");
                store.Edit(c => c.Language = "ja"); store.Edit(c => c.Language = "de"); store.Flush();
                Check(store.Writes == 1, "Reverted edit saves nothing");
                foreach (var language in new[] { "fr", "ko", "zh-Hans", "zh-Hant" }) { store.Edit(c => c.Language = language); store.Flush(); }
                Check(Directory.GetFiles(dir, "config.json.bak.*").Length == 3, "Exactly three previous valid generations");
                store.Edit(c => c.ShowHidden = true); store.Flush(); store.Flush();
                Check(store.Writes == 6, "Logout plus exit duplicate flush saves once");
                store.Edit(c => c.JapaneseDescriptions = true);
            }
            using (var loaded = new ConfigurationStore(path))
                Check(loaded.Value.JapaneseDescriptions && loaded.Writes == 0, "Dispose flush durable; healthy startup does not write");
            var backup = Directory.GetFiles(dir, "config.json.bak.*").OrderDescending().ToArray();
            var expected = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(backup[1]))!;
            File.WriteAllText(path, "broken-primary"); File.WriteAllText(backup[0], "broken-backup");
            using (var recovered = new ConfigurationStore(path))
            {
                Check(recovered.Notice == "ConfigRecovered" && recovered.Value.Language == expected.Language && recovered.Value.ShowHidden == expected.ShowHidden, "Newest valid backup selected, corrupt newest skipped");
                Check(recovered.Writes == 1 && Directory.GetFiles(dir, "config.json.corrupt.*").Length == 1, "Recovery preserves corrupt main and restores once");
            }
            foreach (var stage in new[] { "write", "replace" })
            {
                var fail = true; var notices = 0;
                var original = File.ReadAllText(path); var generations = Directory.GetFiles(dir, "config.json.bak.*").Order().ToArray();
                using var store = new ConfigurationStore(path, (_, _) => notices++, point => { if (fail && point == stage) throw new IOException(stage); });
                store.Edit(c => c.ShowHidden = !c.ShowHidden);
                for (int i = 0; i < 10; i++) store.Tick(Environment.TickCount64 + (i + 1) * 60000L);
                Check(store.Attempts == 3 && store.Pending && notices == 2, "Three failures stop bounded autosave and notify once at each state");
                Check(File.ReadAllText(path) == original && Directory.GetFiles(dir, "config.json.bak.*").Order().SequenceEqual(generations), "Failure preserves main and backup generations");
                fail = false; store.Retry(); Check(store.Writes == 1 && !store.Pending, "Explicit retry saves pending snapshot");
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var store = new ConfigurationStore(path, fault: point => { if (point == "write") { entered.Set(); release.Wait(); } }))
            {
                store.Edit(c => c.Language = "ja"); var saving = Task.Run(store.Flush);
                Check(entered.Wait(3000), "Synthetic write in progress");
                var editing = Task.Run(() => store.Edit(c => c.Language = "ko"));
                var second = Task.Run(store.Flush); release.Set(); await Task.WhenAll(saving, editing, second); store.Flush();
                Check(store.Value.Language == "ko" && JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path))!.Language == "ko", "Edit during save survives; flushes serialized");
                store.Dispose(); var writes = store.Writes; store.Edit(c => c.Language = "ja"); store.Flush();
                Check(store.Writes == writes, "No configuration writes accepted after shutdown");
            }
            // Ordinary settings do not partition by calendar day: a midnight/restart sequence preserves state.
            using (var a = new ConfigurationStore(path)) { a.Edit(c => c.Language = "fr"); a.Tick(Environment.TickCount64 + 86400000); }
            using (var a = new ConfigurationStore(path)) Check(a.Value.Language == "fr" && a.Writes == 0, "Day boundary / restart retains one config, no history migration");
            using (var locked = new ConfigurationStore(path))
            {
                locked.Edit(c => c.Language = "en");
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                { locked.Flush(); Check(locked.Writes == 0 && locked.Pending, "Real file lock retains pending settings"); }
                locked.Retry(); Check(locked.Writes == 1, "Released file lock permits explicit recovery");
            }
            var failedCalls = 0;
            using (var failing = new TranslationCache(Path.Combine(dir, "failure.json"), (_, _) => { failedCalls++; throw new IOException("Synthetic unavailable network"); }))
            {
                failing.SetActive(true);
                for (var i = 0; i < 4; i++)
                {
                    typeof(TranslationCache).GetField("next", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(failing, default(DateTime));
                    failing.Get("Failure limit", true); failing.Pump(); await Until(() => failing.IsIdle);
                }
                Check(failedCalls == 3 && failing.Status == "CacheStopped", "Translation stops after three failures despite elapsed backoff");
                failing.Retry(); failing.Get("Failure limit", true); failing.Pump(); await Until(() => failing.IsIdle);
                Check(failedCalls == 4, "Manual refresh resumes translation");
            }
            var failureHttp = new FailureHandler();
            using (var failing = new IconCache(Path.Combine(dir, "failure-icons"), failureHttp))
            {
                for (var i = 0; i < 4; i++)
                {
                    var entries = typeof(IconCache).GetField("entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(failing)!;
                    var values = (System.Collections.IEnumerable)entries.GetType().GetProperty("Values")!.GetValue(entries)!;
                    foreach (var entry in values) entry!.GetType().GetField("Retry")!.SetValue(entry, default(DateTime));
                    failing.Get("https://example.invalid/failure.png"); await Until(() => failing.IsIdle);
                }
                Check(failureHttp.Calls == 3 && failing.Stopped, "Icon stops after three failures despite elapsed backoff");
                failing.Retry(); failing.Get("https://example.invalid/failure.png"); await Until(() => failing.IsIdle);
                Check(failureHttp.Calls == 4, "Manual refresh resumes icons");
            }
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = false;
            var cache = new TranslationCache(Path.Combine(dir, "cancel.json"), async (_, ct) => { started.SetResult(); try { await Task.Delay(Timeout.Infinite, ct); return []; } finally { finished = true; } });
            cache.SetActive(true); cache.Get("Cancel this description", true); cache.Pump(); await started.Task;
            cache.Dispose(); Check(finished && cache.IsIdle, "Translation cancellation drained before Dispose returns");
            cache.SetActive(true); cache.Get("After shutdown", true); cache.Pump(); Check(cache.IsIdle, "Translation refuses post-shutdown work");
            var acceptedPath = Path.Combine(dir, "accepted.json");
            using (var accepted = new TranslationCache(acceptedPath, (_, _) => Task.FromResult(new[] { "翻訳済み" })))
            {
                accepted.SetActive(true); accepted.Get("Accepted description", true); accepted.Pump();
                await Until(() => accepted.Revision == 1); accepted.Dispose();
                Check(accepted.IsIdle && File.Exists(acceptedPath) && !File.Exists(acceptedPath + ".tmp"), "Accepted translation commits fully before shutdown; no partial file");
            }
            var http = new BlockingHandler(); var icons = new IconCache(Path.Combine(dir, "icons"), http);
            icons.Get("https://example.invalid/icon.png"); await http.Started.Task; icons.Dispose();
            Check(http.Finished && icons.IsIdle && http.Disposed, "Icon HTTP drains before handler disposal");
            icons.Get("https://example.invalid/after.png"); Check(http.Calls == 1, "No icon requests accepted after shutdown");
            Console.WriteLine($"Passed {checks} persistence, recovery, fault-injection and shutdown checks. All files synthetic; no game data used.");
        }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 300; i++) { if (condition()) return; await Task.Delay(10); } throw new Exception("Timed out"); }
    private sealed class FailureHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
    }
    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Finished, Disposed; public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; Started.SetResult(); try { await Task.Delay(Timeout.Infinite, cancellationToken); return new(HttpStatusCode.OK); } finally { Finished = true; } }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}

