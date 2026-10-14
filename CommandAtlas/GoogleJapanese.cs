using System.Net.Http;
using System.Text.RegularExpressions;
using GTranslate.Translators;

namespace CommandAtlas;

internal sealed class GoogleJapanese : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
    private readonly GoogleTranslator2 translator;
    public GoogleJapanese() => translator = new GoogleTranslator2(client);
    public async Task<string[]> Translate(string[] texts, CancellationToken token)
    {
        using var registration = token.Register(client.CancelPendingRequests);
        token.ThrowIfCancellationRequested();
        var text = string.Join("\n\n", texts.Select((x, i) => $"ZXQ{i:D4}QXZ\n{x}"));
        var result = await translator.TranslateAsync(text, "ja");
        return Split(result.Translation, texts.Length);
    }
    internal static string[] Split(string translated, int count)
    {
        var matches = Regex.Matches(translated, @"ZXQ\s*(\d{4})\s*QXZ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count != count) throw new InvalidDataException("Translation separators were changed");
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            if (int.Parse(matches[i].Groups[1].Value) != i) throw new InvalidDataException("Translation order was changed");
            var begin = matches[i].Index + matches[i].Length;
            var end = i + 1 < count ? matches[i + 1].Index : translated.Length;
            result[i] = translated[begin..end].Trim();
        }
        return result;
    }
    public void Dispose() { client.CancelPendingRequests(); translator.Dispose(); client.Dispose(); }
}
