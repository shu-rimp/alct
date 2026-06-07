using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class GeminiTranslationService : ITranslationService
{
    private static readonly HttpClient _defaultHttp = new();
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private const string Model = "gemini-3.1-flash-lite";

    public GeminiTranslationService(string apiKey) : this(apiKey, _defaultHttp) { }

    internal GeminiTranslationService(string apiKey, HttpClient http)
    {
        _http = http;
        _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";
    }

    public string MapLanguageCode(string bcp47) => bcp47 switch
    {
        "ja-JP" => "일본어",
        "zh-CN" => "중국어(간체)",
        "en-US" => "영어",
        _       => bcp47,
    };

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var prompt = $"다음 텍스트를 한국어로 번역해. 번역 결과만 출력해. 설명은 하지 마:\n\n{ITranslationService.StripXmlTags(text)}";
        return await CallAsync(prompt);
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var prompt = $"다음 한국어 텍스트를 {MapLanguageCode(targetLang)}로 번역해. 번역 결과만 출력해. 설명은 하지 마:\n\n{text}";
        return await CallAsync(prompt);
    }

    private async Task<string> CallAsync(string prompt)
    {
        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.1, maxOutputTokens = 512 },
        };

        var response = await _http.PostAsync(_endpoint, JsonContent.Create(payload));
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()?.Trim() ?? string.Empty;
    }
}
