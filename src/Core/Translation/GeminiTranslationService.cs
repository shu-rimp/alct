using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
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
            generationConfig = new { temperature = 0.1, maxOutputTokens = 512, thinkingConfig = new { thinkingBudget = 0 } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("x-goog-api-key", _apiKey);  // 쿼리스트링(?key=) 대신 헤더 인증 — DeepL/Langbly와 일관

        var response = await _http.SendAsync(request, ct);
        if ((int)response.StatusCode == 429)
            throw RateLimitException(await response.Content.ReadAsStringAsync(ct));
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()?.Trim() ?? string.Empty;
    }

    // 429 본문에서 제한 종류와 retryDelay를 추출.
    //   일일(PerDay) 초과: 무료 등급은 태평양 자정에 리셋, 그때까지 차단 
    //   분당 초과: retryDelay(보통 수십 초)만큼만 차단하면 자연 회복
    private static TranslationRateLimitException RateLimitException(string body)
    {
        if (Regex.IsMatch(body, "PerDay", RegexOptions.IgnoreCase))
            return new TranslationRateLimitException("[Gemini] 일일 번역 한도를 소진했어요.", NextPacificMidnightUtc());

        var delay = Regex.Match(body, @"""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""");
        var retryAt = delay.Success
            ? DateTime.UtcNow.AddSeconds(double.Parse(delay.Groups[1].Value, CultureInfo.InvariantCulture))
            : DateTime.UtcNow.AddSeconds(60);  // retryDelay 누락 시 분당 윈도우만큼 폴백
        return new TranslationRateLimitException("[Gemini] 분당 번역 한도를 소진했어요.", retryAt);
    }

    private static DateTime NextPacificMidnightUtc()
    {
        try
        {
            var pt = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            var nextMidnight = DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pt).Date.AddDays(1), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(nextMidnight, pt);
        }
        catch { return DateTime.UtcNow.AddHours(3); }  // 타임존 DB 누락 — 과한 재시도 방지용 보수적 폴백
    }
}
