using System.Net.Http;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class MyMemoryTranslationService : ITranslationService
{
    private static readonly HttpClient _defaultHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });
    private readonly HttpClient _http;
    private const string BaseUrl = "https://api.mymemory.translated.net/get";

    public MyMemoryTranslationService() : this(_defaultHttp) { }

    internal MyMemoryTranslationService(HttpClient http)
    {
        _http = http;
    }

    public string MapLanguageCode(string bcp47) => bcp47 switch
    {
        "ja-JP" => "ja",
        "zh-CN" => "zh-CN",
        "en-US" => "en",
        _       => bcp47.Split('-')[0].ToLowerInvariant(),
    };

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return await CallAsync(ITranslationService.StripXmlTags(text), MapLanguageCode(sourceLang), "ko", ct);
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return await CallAsync(text, "ko", MapLanguageCode(targetLang));
    }

    private async Task<string> CallAsync(string text, string from, string to, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?q={Uri.EscapeDataString(text)}&langpair={from}|{to}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        if (root.TryGetProperty("quotaFinished", out var quota) && quota.GetBoolean())
            throw new InvalidOperationException("MyMemory 일일 번역 한도를 초과했습니다.");

        var status = root.TryGetProperty("responseStatus", out var s) ? s.GetInt32() : 200;
        if (status != 200)
            throw new InvalidOperationException($"MyMemory 오류: {status}");

        return root.GetProperty("responseData")
                   .GetProperty("translatedText")
                   .GetString() ?? string.Empty;
    }
}
