using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class GeminiTranslationService : ITranslationService
{
    private static readonly HttpClient _defaultHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private const string Model = "gemini-3.1-flash-lite";

    public GeminiTranslationService(string apiKey) : this(apiKey, _defaultHttp) { }

    internal GeminiTranslationService(string apiKey, HttpClient http)
    {
        _apiKey = apiKey;
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

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;

        var contextBlock = string.IsNullOrWhiteSpace(context)
            ? ""
            : $"Recent utterances for context (reference only, do NOT translate):\n{context}\n\n";

        return await CallAsync(
            systemInstruction: "You are translating in-game chat messages from Apex Legends. The text may contain gaming slang, or abbreviations. Translate each line separately and output exactly the same number of lines as the input. Only output the translated text.",
            userContent: $"{contextBlock}Translate each line below to Korean, outputting the same number of lines:\n\n{ITranslationService.StripXmlTags(text)}",
            ct);
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;
        return await CallAsync(
            systemInstruction: "You are translating in-game chat messages. The text may contain gaming slang or abbreviations. Only output the translated text.",
            userContent: $"Translate the following Korean text to {MapLanguageCode(targetLang)}:\n\n{text}");
    }

    private async Task<string> CallAsync(string systemInstruction, string userContent, CancellationToken ct = default)
    {
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { parts = new[] { new { text = userContent } } } },
            generationConfig = new { temperature = 0.1, maxOutputTokens = 512 },
        };

        var response = await _http.PostAsync(_endpoint, JsonContent.Create(payload), ct);
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
