using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class DeepLTranslationService : ITranslationService
{
    private static readonly HttpClient _defaultHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public DeepLTranslationService(string apiKey) : this(apiKey, _defaultHttp) { }

    internal DeepLTranslationService(string apiKey, HttpClient http)
    {
        _apiKey = apiKey;
        _http = http;
        _baseUrl = apiKey.EndsWith(":fx")
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";
    }

    public string MapLanguageCode(string bcp47) => bcp47 switch
    {
        "ja-JP" => "JA",
        "zh-CN" => "ZH",
        "en-US" => "EN",
        _ => bcp47.Split('-')[0].ToUpperInvariant(),
    };

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;

        // 줄별로 배열에 담아 전송 — DeepL이 줄을 병합하지 않고 1:1 번역 보장
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var payload = new
        {
            text = lines,
            source_lang = MapLanguageCode(sourceLang),
            target_lang = "KO",
            tag_handling = "xml",
            ignore_tags = new[] { "x" },
        };
        var results = await CallDeepLArrayAsync(payload, ct);
        return string.Join("\n", results.Select(StripXTags));
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;

        var payload = new
        {
            text = new[] { text },
            source_lang = "KO",
            target_lang = MapLanguageCode(targetLang),
        };
        return await CallDeepLAsync(payload);
    }

    private static string StripXTags(string text) =>
        WebUtility.HtmlDecode(text.Replace("<x>", "").Replace("</x>", " ").Trim());

    private async Task<string> CallDeepLAsync(object payload, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement
            .GetProperty("translations")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string[]> CallDeepLArrayAsync(object payload, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var arr = doc.RootElement.GetProperty("translations");
        return Enumerable.Range(0, arr.GetArrayLength())
            .Select(i => arr[i].GetProperty("text").GetString() ?? string.Empty)
            .ToArray();
    }
}
