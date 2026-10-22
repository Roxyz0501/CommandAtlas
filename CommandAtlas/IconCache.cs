using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CommandAtlas;

// Persistent bounded cache of public plugin icons. No credentials or game data are sent.
internal sealed class IconCache : IDisposable
{
    private const int Limit = 128, MaxBytes = 524288;
    private readonly string directory;
    private readonly HttpClient client;
    private readonly ConcurrentQueue<string> changed = new();
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    private int active;
    private readonly object gate = new(), disposeGate = new();
    private Task? worker;
    private bool disposed;
    public bool Stopped => entries.Values.Any(e => e.Failures >= 3);
    internal sealed record Metadata(string? ETag, DateTime Checked);
    private sealed class Entry { public string? Path; public int Busy; public int Failures; public DateTime Retry; }

    public IconCache(string directory, HttpMessageHandler? handler = null)
    {
        this.directory = directory;
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in new DirectoryInfo(directory).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Limit))
            {
                file.Delete(); File.Delete(file.FullName + ".json");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public bool TryTakeChanged(out string path) => changed.TryDequeue(out path!);
    internal bool IsIdle => Volatile.Read(ref active) == 0;
    public string? Get(string url)
    {
        lock (gate)
        {
        if (disposed) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) return null;
        if (!entries.TryGetValue(url, out var entry))
        {
            if (entries.Count >= Limit) return null;
            entry = entries.GetOrAdd(url, _ => new Entry());
        }
        if (entry.Failures < 3 && entry.Retry <= DateTime.UtcNow && Interlocked.CompareExchange(ref entry.Busy, 1, 0) == 0)
        {
            if (Interlocked.Increment(ref active) <= 1) worker = Task.Run(() => Fetch(uri, entry));
            else { Interlocked.Decrement(ref active); Volatile.Write(ref entry.Busy, 0); }
        }
        return Volatile.Read(ref entry.Path);
        }
    }
    private async Task Fetch(Uri uri, Entry entry)
    {
        var success = false;
        try
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
            var path = Path.Combine(directory, key + ".png");
            Metadata? meta = null;
            if (File.Exists(path))
            {
                entry.Path = path;
                if (File.Exists(path + ".json"))
                    try { meta = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(path + ".json", stop.Token)); } catch (JsonException) { }
                if (meta != null && meta.Checked.AddDays(7) > DateTime.UtcNow) { entry.Retry = meta.Checked.AddDays(7); success = true; return; }
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (meta?.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", meta.ETag);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
            if (response.StatusCode == HttpStatusCode.NotModified && entry.Path != null)
            {
                await Save(path + ".json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Metadata(meta?.ETag, DateTime.UtcNow))));
                entry.Retry = DateTime.UtcNow.AddDays(7); success = true; return;
            }
            // Expired/deleted/unauthorized resources do not retain a stale image.
            entry.Path = null;
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType != "image/png" || response.Content.Headers.ContentLength > MaxBytes) return;
            using var data = new MemoryStream();
            await using var stream = await response.Content.ReadAsStreamAsync(stop.Token);
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, stop.Token)) > 0)
            {
                if (data.Length + count > MaxBytes) return;
                data.Write(buffer, 0, count);
            }
            var bytes = data.ToArray();
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return;
            var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (width is 0 or > 1024 || height is 0 or > 1024) return;
            Directory.CreateDirectory(directory);
            // Keep disk capacity bounded too, including cache entries from previous sessions.
            if (!File.Exists(path) && Directory.EnumerateFiles(directory, "*.png").Count() >= Limit) return;
            stop.Token.ThrowIfCancellationRequested();
            // Finish accepted writes, without leaving a partial PNG or obsolete ETag.
            File.Delete(path + ".json");
            await Save(path, bytes);
            await Save(path + ".json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Metadata(response.Headers.ETag?.ToString(), DateTime.UtcNow))));
            changed.Enqueue(path);
            entry.Path = path; entry.Retry = DateTime.UtcNow.AddDays(7); success = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException) { entry.Path = null; }
        finally
        {
            if (success) entry.Failures = 0; else if (!stop.IsCancellationRequested) entry.Failures++;
            if (entry.Retry <= DateTime.UtcNow) entry.Retry = DateTime.UtcNow.AddMinutes(10);
            Interlocked.Decrement(ref active); Volatile.Write(ref entry.Busy, 0);
        }
    }
    private static async Task Save(string path, byte[] bytes)
    {
        try { await File.WriteAllBytesAsync(path + ".tmp", bytes).ConfigureAwait(false); File.Move(path + ".tmp", path, true); }
        finally { try { File.Delete(path + ".tmp"); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public void Retry() { lock (gate) { if (disposed) return; foreach (var e in entries.Values) { e.Failures = 0; if (e.Retry < DateTime.UtcNow.AddDays(1)) e.Retry = default; } } }
    public void Dispose()
    {
        lock (disposeGate)
        {
            Task? running;
            lock (gate) { if (disposed) return; disposed = true; stop.Cancel(); running = worker; }
            running?.GetAwaiter().GetResult();
            changed.Clear(); client.Dispose(); stop.Dispose();
        }
    }
}
