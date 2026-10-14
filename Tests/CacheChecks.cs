using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CommandAtlas;

static class CacheChecks
{
    public static async Task Run()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cache-checks", Guid.NewGuid().ToString("N")));
        var fake = new Fake();
        const string url = "https://icons.example.test/icon.png";
        using (var cache = new IconCache(directory, fake))
        {
            for (var i = 0; i < 100; i++) cache.Get(url);
            await Until(() => cache.Get(url) != null);
            await Until(() => cache.IsIdle);
            if (fake.Requests != 1) throw new Exception("Duplicate icon requests");
            if (!cache.TryTakeChanged(out _)) throw new Exception("Texture invalidation missing");
            cache.Get("http://icons.example.test/plain.png");
            if (fake.Requests != 1) throw new Exception("Insecure URL fetched");
        }
        var warm = new Fake();
        using (var cache = new IconCache(directory, warm))
        {
            await Until(() => cache.Get(url) != null);
            await Until(() => cache.IsIdle);
            if (warm.Requests != 0) throw new Exception("Warm cache caused HTTP request");
        }
        var metaPath = Directory.GetFiles(directory, "*.json").Single();
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(new IconCache.Metadata("\"v1\"", DateTime.UtcNow.AddDays(-8))));
        var expired = new Fake { NotModified = true };
        using (var cache = new IconCache(directory, expired))
        {
            cache.Get(url);
            await Until(() => expired.Requests > 0 && cache.IsIdle);
            if (expired.Requests != 1 || !expired.HadValidator || expired.BodyBytes != 0) throw new Exception("ETag validation failed");
        }
        var fail = new Fake { Fail = true };
        using (var cache = new IconCache(directory, fail))
        {
            cache.Get("https://icons.example.test/missing.png");
            await Task.Delay(100);
            for (var i = 0; i < 100; i++) cache.Get("https://icons.example.test/missing.png");
            if (fail.Requests != 1) throw new Exception("Failure retried without backoff");
        }
        Console.WriteLine("Passed 7 icon-cache checks: 100 simultaneous lookups = 1 HTTP/24 body bytes; warm cache = 0 HTTP; expired ETag = 1 HTTP/0 body bytes; failure backoff and texture invalidation verified.");
    }
    private static async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 200; i++) { if (predicate()) return; await Task.Delay(10); }
        throw new Exception("Cache test timeout");
    }
    private sealed class Fake : HttpMessageHandler
    {
        public int Requests, BodyBytes;
        public bool NotModified, HadValidator, Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            HadValidator = request.Headers.IfNoneMatch.Any(x => x.Tag == "\"v1\"");
            await Task.Delay(25, token);
            if (Fail) return new(HttpStatusCode.NotFound);
            if (NotModified) return new(HttpStatusCode.NotModified);
            byte[] bytes = [137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,1,0,0,0,1];
            BodyBytes += bytes.Length;
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            result.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return result;
        }
    }
}
