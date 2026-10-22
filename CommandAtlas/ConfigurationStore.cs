using System.Text;
using System.Text.Json;

namespace CommandAtlas;

// Small configuration only. Framework/UI edits and lifecycle flushes share one lock.
internal sealed class ConfigurationStore : IDisposable
{
    private readonly string path;
    private readonly Action<string, Exception?> report;
    private readonly Action<string>? fault;
    private readonly object gate = new();
    private string? saved;
    private bool dirty, disposed;
    private int failures;
    private long backupSequence;
    private long next = Environment.TickCount64 + 60000;
    public Configuration Value { get; private set; } = new();
    public string Notice { get; private set; } = "";
    public long Writes { get; private set; }
    public long LogicalBytes { get; private set; }
    public int Attempts { get; private set; }
    public bool Pending { get { lock (gate) return dirty; } }
    public ConfigurationStore(string path, Action<string, Exception?>? report = null, Action<string>? fault = null)
    {
        this.path = path; this.report = report ?? ((_, _) => { }); this.fault = fault;
        backupSequence = Backups().Select(p => p[(path.Length + 5)..]).Where(s => s.Length >= 19)
            .Select(s => long.TryParse(s[..19], out var n) ? n : 0).DefaultIfEmpty().Max();
        if (TryRead(path, out var value)) { Value = value!; saved = Encode(Value); return; }
        var backups = Backups();
        foreach (var backup in backups)
        {
            if (!TryRead(backup, out value)) continue;
            Value = value!;
            // Recovery never promotes the corrupt primary to a normal backup.
            dirty = true;
            Notice = "ConfigRecovered"; this.report(Notice, null);
            Flush();
            return;
        }
        dirty = true;
        if (File.Exists(path)) { Notice = "ConfigDamaged"; this.report(Notice, null); }
    }
    private static string Encode(Configuration value) => JsonSerializer.Serialize(value);
    private static Configuration Decode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Version", out var version) || version.GetInt32() != 1)
            throw new JsonException("Unsupported or missing configuration version");
        var value = JsonSerializer.Deserialize<Configuration>(json) ?? throw new JsonException("Null configuration");
        value.Presets ??= [];
        if (value.Presets.Count > 256 || value.Presets.Any(p => p == null || p.Id == null || p.PluginId == null || p.Command == null || p.Help == null))
            throw new JsonException("Invalid presets");
        return value;
    }
    private static bool TryRead(string file, out Configuration? value)
    {
        value = null;
        try { if (!File.Exists(file) || new FileInfo(file).Length > 4 * 1024 * 1024) return false; value = Decode(File.ReadAllText(file)); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { return false; }
    }
    private string[] Backups()
    {
        try { return Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bak.*").OrderDescending(StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    public void Edit(Action<Configuration> edit) { lock (gate) { if (disposed) return; edit(Value); dirty = true; } }
    public void Tick(long? milliseconds = null)
    {
        lock (gate) { var now = milliseconds ?? Environment.TickCount64; if (disposed || now < next) return; next = now + 60000; Flush(); }
    }
    public void Retry() { lock (gate) { if (disposed) return; failures = 0; Flush(); } }
    public void Flush()
    {
        lock (gate)
        {
            if (disposed || !dirty || failures >= 3) return;
            var json = Encode(Value);
            if (json == saved) { dirty = false; return; }
            var temp = path + ".tmp";
            try
            {
                Attempts++;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                fault?.Invoke("write");
                var bytes = Encoding.UTF8.GetBytes(json);
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); LogicalBytes += bytes.Length; stream.Flush(true); }
                Decode(File.ReadAllText(temp));
                fault?.Invoke("replace");
                if (File.Exists(path))
                {
                    var suffix = TryRead(path, out _) ? ".bak." + (backupSequence = Math.Max(backupSequence + 1, DateTime.UtcNow.Ticks)).ToString("D19") : ".corrupt.";
                    File.Replace(temp, path, path + suffix + Guid.NewGuid().ToString("N"));
                }
                else File.Move(temp, path);
                Writes++; saved = json; dirty = false; failures = 0;
                if (Notice is "ConfigSaveFailed" or "ConfigSaveStopped") Notice = "";
                // Only prune after successful commit. Preserve invalid backup candidates for diagnosis.
                foreach (var stale in Backups().Where(p => TryRead(p, out _)).Skip(3))
                    try { File.Delete(stale); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                failures++; Notice = failures >= 3 ? "ConfigSaveStopped" : "ConfigSaveFailed";
                if (failures is 1 or 3) report(Notice, ex);
            }
            finally { try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
        }
    }
    public void Dispose() { lock (gate) { if (disposed) return; Flush(); disposed = true; } }
}
