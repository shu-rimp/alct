/** 
Langbly Translation API
API 서버가 EU(네덜란드)에 위치해 응답이 느려 음성 번역에 부적합하고,
신생 서비스이므로 다수의 사용자에게 결제 정보 등록 및 키 발급을 요구하기에
신뢰성이 불충분하다고 판단함. 
매월 무료 제공 한도가 서비스에 적합하고 context, instruction, formality 등을
설정할 수 있다는 이점이 있어 구현만 남겨두고 UI에서는 노출하지 않음.
**/
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class LangblyTranslationService : ITranslationService
{
    private static readonly HttpClient _defaultHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string Endpoint = "https://api.langbly.com/language/translate/v2";

    public LangblyTranslationService(string apiKey) : this(apiKey, _defaultHttp) { }

    internal LangblyTranslationService(string apiKey, HttpClient http)
    {
        _apiKey = apiKey;
        _http   = http;
    }

    public string MapLanguageCode(string bcp47) => bcp47 switch
    {
        "ja-JP" => "ja",
        "zh-CN" => "zh",
        "en-US" => "en",
        _       => bcp47.Split('-')[0].ToLowerInvariant(),
    };

    // UI 미노출 엔진 — context 전달은 생략
    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;
        return await CallAsync(ITranslationService.StripXmlTags(text), MapLanguageCode(sourceLang), "ko", ct);
    }

    // 배치 API가 없어 줄마다 순차 호출(UI 미노출 엔진 — 단순하게 유지)
    public async Task<IReadOnlyList<string>> TranslateBatchToKoreanAsync(IReadOnlyList<string> texts, string sourceLang, CancellationToken ct = default)
    {
        if (texts.Count == 0 || string.IsNullOrEmpty(_apiKey)) return texts;
        var source = MapLanguageCode(sourceLang);
        var results = new string[texts.Count];
        for (int i = 0; i < texts.Count; i++)
            results[i] = string.IsNullOrWhiteSpace(texts[i])
                ? texts[i]
                : await CallAsync(ITranslationService.StripXmlTags(texts[i]), source, "ko", ct);
        return results;
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;
        return await CallAsync(text, "ko", MapLanguageCode(targetLang));
    }

    private async Task<string> CallAsync(string text, string source, string target, CancellationToken ct = default)
    {
        var payload = new { q = text, source, target };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-API-Key", _apiKey);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement
            .GetProperty("data")
            .GetProperty("translations")[0]
            .GetProperty("translatedText")
            .GetString()?.Trim() ?? string.Empty;
    }
}
