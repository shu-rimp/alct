using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AlctClient.Core;

public interface ITranslationService
{
    Task<string> TranslateToKoreanAsync(string text, string sourceLang);
    Task<string> TranslateFromKoreanAsync(string text, string targetLang);
}

public sealed class DeepLTranslationService : ITranslationService
{
    private static readonly HttpClient _defaultHttp = new();
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

    private static string Bcp47ToDeepL(string bcp47) => bcp47 switch
    {
        "ja-JP" => "JA",
        "zh-CN" => "ZH",
        "en-US" => "EN",
        _ => bcp47.Split('-')[0].ToUpperInvariant(),
    };

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var payload = new
        {
            text = new[] { text },
            source_lang = Bcp47ToDeepL(sourceLang),
            target_lang = "KO",
            tag_handling = "xml",
            ignore_tags = new[] { "x" },
        };
        var result = await CallDeepLAsync(payload);
        return StripXTags(result);
    }

    private static string StripXTags(string text) =>
        WebUtility.HtmlDecode(text.Replace("<x>", "").Replace("</x>", " ").Trim());

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var payload = new
        {
            text = new[] { text },
            source_lang = "KO",
            target_lang = Bcp47ToDeepL(targetLang),
        };
        return await CallDeepLAsync(payload);
    }

    private async Task<string> CallDeepLAsync(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement
            .GetProperty("translations")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }
}
