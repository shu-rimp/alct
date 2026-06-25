using AlctClient.Utils;
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
    private readonly string _email;
    private const string BaseUrl = "https://api.mymemory.translated.net/get";

    public MyMemoryTranslationService(string email = "") : this(email, _defaultHttp) { }

    internal MyMemoryTranslationService(HttpClient http) : this(string.Empty, http) { }

    internal MyMemoryTranslationService(string email, HttpClient http)
    {
        _email = email?.Trim() ?? string.Empty;
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
    // <x>한국어</x> 용어(용어집·normalizer 산출물)를 ASCII 토큰으로 마스킹해 한글을 아예 안 보낸 뒤,
    // 줄 전체를 1회 번역하고 응답의 토큰을 용어로 복원(MT가 토큰에 붙인 조사는 받침에 맞게 보정).
    // 이전 방식(태그 기준 분할 → 조각마다 순차 호출)이 용어 수만큼 늘리던 HTTP 왕복을 1회로 줄인다.
    private static readonly Regex TagRegex = new(@"<x>(.*?)</x>", RegexOptions.Compiled);
    private static readonly Regex MaskRegex = new(@"qzxk(\d+)wq", RegexOptions.Compiled);
    private static readonly Regex RestoreRegex =
        new($@"qzxk(\d+)wq(?<p>{KoreanParticle.ParticlePattern})?", RegexOptions.Compiled);

    // 토큰 형식 — 음차되지 않고 라틴으로 보존됨이 실측된 자음+숫자형. 인덱스로 다중 용어를 구분.
    internal static string MaskToken(int index) => $"qzxk{index}wq";

    private async Task<string> TranslateLineAsync(string line, string source, CancellationToken ct)
    {
        var terms = new List<string>();  // 토큰 인덱스 → 한국어 용어
        var masked = TagRegex.Replace(line, m =>
        {
            terms.Add(m.Groups[1].Value);
            return MaskToken(terms.Count - 1);
        });

        // 토큰을 빼면 번역할 텍스트가 없는 줄(태그만 있음)은 API 생략 — 불필요한 호출 방지
        var hasText = !string.IsNullOrWhiteSpace(MaskRegex.Replace(masked, " "));
        var translated = hasText ? await CallAsync(masked, source, "ko", ct) : masked;

        return Restore(translated, terms).Trim();
    }

    // 응답의 토큰(+바로 붙은 조사)을 한국어 용어로 되돌린다. 누락된 토큰의 용어는 폴백으로 끝에 덧붙임.
    private static string Restore(string text, List<string> terms)
    {
        if (terms.Count == 0) return text;

        var seen = new HashSet<int>();
        var restored = RestoreRegex.Replace(text, m =>
        {
            int idx = int.Parse(m.Groups[1].Value);
            if (idx < 0 || idx >= terms.Count) return m.Value;  // 범위 밖 — 손대지 않음
            seen.Add(idx);
            var term = terms[idx];
            return m.Groups["p"].Success ? term + KoreanParticle.Correct(term, m.Groups["p"].Value) : term;
        });

        var missing = terms.Where((_, i) => !seen.Contains(i)).ToList();
        return missing.Count == 0 ? restored : $"{restored.TrimEnd()} {string.Join(" ", missing)}";
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return await CallAsync(text, "ko", MapLanguageCode(targetLang));
    }

    private async Task<string> CallAsync(string text, string from, string to, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?q={Uri.EscapeDataString(text)}&langpair={from}|{to}";
        // 이메일 등록 시 일일 한도 상향(5천→5만 자). 형식이 틀리면 붙이지 않아 번역 요청이 깨지지 않도록
        if (IsLikelyEmail(_email))
            url += $"&de={Uri.EscapeDataString(_email)}";
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
            throw new InvalidOperationException($"MyMemory error: {status}");

        var translated = root.GetProperty("responseData")
                             .GetProperty("translatedText")
                             .GetString() ?? string.Empty;

        // MyMemory는 HTTP 200으로 quotaFinished/429 없이 translatedText에
        // "MYMEMORY WARNING: ... NEXT AVAILABLE IN ..." 경고문을 담아 보냄 — 번역문처럼 노출되지 않게 한도로 처리
        if (Regex.IsMatch(translated, "MYMEMORY WARNING|QUOTA", RegexOptions.IgnoreCase))
            throw QuotaException(translated);

        return translated;
    }

    private static bool IsLikelyEmail(string s) =>
        !string.IsNullOrEmpty(s) && Regex.IsMatch(s, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

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
