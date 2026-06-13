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
