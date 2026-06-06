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

public enum TranslationEngine { MyMemory, DeepL }

public static class TranslationEngineFactory
{
    public static ITranslationService Create(TranslationEngine engine, string apiKey = "")
        => engine switch
        {
            TranslationEngine.DeepL => new DeepLTranslationService(apiKey),
            _                       => new MyMemoryTranslationService(),
        };

    public static TranslationEngine Parse(string? raw) => raw switch
    {
        "DeepL" => TranslationEngine.DeepL,
        _       => TranslationEngine.MyMemory,  // "LibreTranslate" 등 구버전 값 포함
    };
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

public sealed class MyMemoryTranslationService : ITranslationService
{
    private static readonly HttpClient _http = new();
    private const string BaseUrl = "https://api.mymemory.translated.net/get";

    private static string Bcp47ToMyMemory(string bcp47) => bcp47 switch
    {
        "ja-JP" => "ja",
        "zh-CN" => "zh-CN",
        "en-US" => "en",
        _       => bcp47.Split('-')[0].ToLowerInvariant(),
    };

    // OCR 정규화 서버가 삽입하는 <x>한국어</x> 태그 제거 (MyMemory는 태그 무시 불가)
    private static string StripXmlTags(string text) =>
        text.Replace("<x>", "").Replace("</x>", "").Trim();

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return await CallAsync(StripXmlTags(text), Bcp47ToMyMemory(sourceLang), "ko");
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return await CallAsync(text, "ko", Bcp47ToMyMemory(targetLang));
    }

    private static async Task<string> CallAsync(string text, string from, string to)
    {
        var url = $"{BaseUrl}?q={Uri.EscapeDataString(text)}&langpair={from}|{to}";
        var response = await _http.GetAsync(url);
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
