using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CommandAtlas;

internal sealed class TranslationCache : IDisposable
{
    internal sealed record Entry(string Text, DateTime Expires);
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> cache = [];
    private readonly Dictionary<string, string> queued = [];
    private readonly HashSet<string> pending = [];
    private readonly Func<string[], CancellationToken, Task<string[]>> translate;
    private readonly string path;
    private CancellationTokenSource cancellation = new();
    private Task? worker;
    private DateTime next;
    private bool active, disposed;
    private int revision;
    public int Revision { get { lock (gate) return revision; } }
    public string FontText { get { lock (gate) return string.Concat(cache.Values.Select(x => x.Text)); } }
    public string Status { get; private set; } = "TranslationReady";
    internal bool IsIdle { get { lock (gate) return worker == null || worker.IsCompleted; } }
    private static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("google-ja-v1:" + text)));
    public TranslationCache(string path, Func<string[], CancellationToken, Task<string[]>> translate)
    {
        this.path = path; this.translate = translate;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 4 * 1024 * 1024)
                foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? [])
                    if (cache.Count < 512 && key.Length == 64 && value.Text.Length <= 4000 && value.Expires > DateTime.UtcNow) cache[key] = value;
            Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NullReferenceException) { cache.Clear(); }
    }
    public void SetActive(bool value)
    {
        lock (gate)
        {
            if (disposed || active == value) return;
            active = value;
            if (!value) { queued.Clear(); cancellation.Cancel(); }
            else { cancellation.Dispose(); cancellation = new(); }
        }
    }
    public void BeginFrame() { lock (gate) queued.Clear(); }
    public string Get(string text, bool request)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        lock (gate)
        {
            var key = Key(text);
            if (cache.TryGetValue(key, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Text;
            if (active && request && text.Length <= 2000 && !text.Any(c => c is >= '\u3040' and <= '\u30ff') && !pending.Contains(key) && queued.Count < 64)
                queued.TryAdd(key, text);
            return text;
        }
    }
    public void Pump()
    {
        lock (gate)
        {
            if (!active || disposed || !IsIdle || next > DateTime.UtcNow || queued.Count == 0) return;
            var selected = new List<KeyValuePair<string, string>>(); int length = 0;
            foreach (var item in queued)
            {
                if (selected.Count == 8 || length + item.Value.Length > 3500) break;
                selected.Add(item); length += item.Value.Length;
            }
            foreach (var item in selected) { queued.Remove(item.Key); pending.Add(item.Key); }
            var token = cancellation.Token;
            Status = "Translating";
            worker = Task.Run(async () =>
            {
                try
                {
                    var translated = await translate(selected.Select(x => x.Value).ToArray(), token);
                    token.ThrowIfCancellationRequested();
                    if (translated.Length != selected.Count || translated.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4000)) throw new InvalidDataException("Invalid translation batch");
                    string json;
                    lock (gate)
                    {
                        for (var i = 0; i < selected.Count; i++) cache[selected[i].Key] = new(translated[i], DateTime.UtcNow.AddDays(90));
                        Trim(); revision++; Status = "TranslationReady"; next = DateTime.UtcNow.AddSeconds(2);
                        json = JsonSerializer.Serialize(cache);
                    }
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllTextAsync(path + ".tmp", json, token);
                        File.Move(path + ".tmp", path, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* In-memory translations still work. */ }
                }
                catch (OperationCanceledException) { lock (gate) Status = "TranslationReady"; }
                catch (Exception) { lock (gate) { next = DateTime.UtcNow.AddMinutes(5); Status = "TranslationFailed"; queued.Clear(); } }
                finally { lock (gate) foreach (var item in selected) pending.Remove(item.Key); }
            });
        }
    }
    private void Trim()
    {
        foreach (var item in cache.Where(x => x.Value.Expires <= DateTime.UtcNow).ToArray()) cache.Remove(item.Key);
        var size = cache.Values.Sum(x => x.Text.Length);
        foreach (var item in cache.OrderBy(x => x.Value.Expires).ToArray())
        {
            if (cache.Count <= 512 && size <= 400000) break;
            cache.Remove(item.Key); size -= item.Value.Text.Length;
        }
    }
    public void Dispose() { lock (gate) { disposed = true; active = false; queued.Clear(); cancellation.Cancel(); } }
}
