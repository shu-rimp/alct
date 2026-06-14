using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    // context 미지원 엔진 — 무시
    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var tasks = lines.Select(l =>
            string.IsNullOrWhiteSpace(l)
                ? Task.FromResult(string.Empty)
                : TranslateLineAsync(l, MapLanguageCode(sourceLang), ct));
        var results = await Task.WhenAll(tasks);
        return string.Join("\n", results);
    }

    // MyMemory는 원어 문장에 섞인 한글을 응답에서 삭제해버림 —
    // <x>한국어</x> 구간(용어집·normalizer 산출물)은 API에 보내지 않고 보존하고 나머지만 번역해 재조립
    private async Task<string> TranslateLineAsync(string line, string source, CancellationToken ct)
    {
        var segments = new List<string>();
        foreach (var part in Regex.Split(line, @"(<x>.*?</x>)"))
        {
            var tag = Regex.Match(part, @"^<x>(.*?)</x>$");
            if (tag.Success) segments.Add(tag.Groups[1].Value);
            else if (!string.IsNullOrWhiteSpace(part)) segments.Add(await CallAsync(part, source, "ko", ct));
        }
        return string.Join(" ", segments.Where(s => s.Length > 0)).Trim();
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
        var body = await response.Content.ReadAsStringAsync(ct);

        // HTTP 429면 본문이 JSON이 아닐 수 있으므로 파싱 전에 한도 초과로 처리 (재개 시각은 본문에서 best-effort 추출)
        if ((int)response.StatusCode == 429)
            throw QuotaException(body);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var quotaFinished = root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True;
        var status = root.TryGetProperty("responseStatus", out var s) ? StatusCode(s) : 200;
        if (quotaFinished || status == 429)
            throw QuotaException(body);
        if (status != 200)
            throw new InvalidOperationException($"MyMemory 오류: {status}");

        return root.GetProperty("responseData")
                   .GetProperty("translatedText")
                   .GetString() ?? string.Empty;
    }

    // responseStatus는 숫자 또는 문자열("200")로 올 수 있음
    private static int StatusCode(JsonElement s) =>
        s.ValueKind == JsonValueKind.Number ? s.GetInt32()
        : int.TryParse(s.GetString(), out var v) ? v : 200;

    // MyMemory는 일일 한도뿐 — 재개 시각은 본문에서 파싱, 없으면 1시간 뒤로 보수적 폴백
    private static TranslationRateLimitException QuotaException(string body) =>
        new("[MyMemory] 일일 번역 한도를 소진했어요.", ParseNextAvailable(body) ?? DateTime.UtcNow.AddHours(1));

    // "...NEXT AVAILABLE IN 07 HOURS 12 MINUTES..." → 지금부터 그만큼 뒤의 UTC 시각. 없으면 null
    private static DateTime? ParseNextAvailable(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var m = Regex.Match(body, @"NEXT AVAILABLE IN\s+(\d+)\s+HOURS?\s+(\d+)\s+MINUTES?", RegexOptions.IgnoreCase);
        return m.Success
            ? DateTime.UtcNow.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
            : null;
    }
}
