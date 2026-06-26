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

    public const string Model = "gemini-3.1-flash-lite";
    // 서비스와 키 검증(ApiConfigModal)이 공유 — 모델 ID/엔드포인트 단일 출처
    public const string GenerateContentEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/" + Model + ":generateContent";

    public GeminiTranslationService(string apiKey) : this(apiKey, _defaultHttp) { }

    internal GeminiTranslationService(string apiKey, HttpClient http)
    {
        _apiKey = apiKey;
        _http = http;
        _endpoint = GenerateContentEndpoint;
    }

    // LLM 프롬프트에 들어갈 타깃 언어명 — 한국어 명("일본어")을 쓰면 입력에 섞인 한국어와 충돌해 모델이 한국어로 번역해버려 영어로 명시
    public string MapLanguageCode(string bcp47) => bcp47 switch
    {
        "ja-JP" => "Japanese",
        "zh-CN" => "Simplified Chinese",
        "en-US" => "English",
        _       => bcp47,
    };

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;

        var contextBlock = string.IsNullOrWhiteSpace(context)
            ? ""
            : $"Recent utterances for context (reference only, do NOT translate):\n{context}\n\n";

        return await CallAsync(
            systemInstruction: "You are a translation engine for fps in-game chat. The input may contain gaming slang, abbreviations, or several languages mixed together. Translate each line separately and output exactly the same number of lines as the input. Only output the translated text.",
            userContent: $"{contextBlock}Translate each line below to Korean, outputting the same number of lines:\n\n{ITranslationService.StripXmlTags(text)}",
            ct: ct);
    }

    // 한 요청에 너무 많은 줄을 담으면 출력이 maxOutputTokens(아래)에 잘려 뒤쪽 줄이 사라진다.
    // 줄 수와 누적 문자 수로 묶음을 제한하고, 묶음별로 출력 토큰 한도도 넉넉히 올린다.
    private const int MAX_BATCH_ITEMS = 20;
    private const int MAX_BATCH_CHARS = 1500;
    private const int BATCH_OUTPUT_TOKENS = 2048;

    // 줄 앞 번호("1. ...")로 입력/출력을 정렬 — 모델이 줄을 합치거나 빠뜨려도 번호로 1:1 복원한다.
    private static readonly Regex NumberedLine = new(@"^\s*(\d+)\s*[\.\):]\s*(.*)$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<string>> TranslateBatchToKoreanAsync(IReadOnlyList<string> texts, string sourceLang, CancellationToken ct = default)
    {
        if (texts.Count == 0 || string.IsNullOrEmpty(_apiKey)) return texts;

        var results = new List<string>(texts.Count);
        foreach (var chunk in BatchChunker.Chunk(texts, MAX_BATCH_ITEMS, MAX_BATCH_CHARS))
            results.AddRange(await TranslateChunkAsync(chunk, ct));
        return results;
    }

    private async Task<IReadOnlyList<string>> TranslateChunkAsync(List<string> chunk, CancellationToken ct)
    {
        // 번호 매긴 줄만 모델에 보낸다(빈 줄은 호출에서 제외하고 자리만 보존).
        var numbered = new List<(int Index, string Text)>();
        for (int i = 0; i < chunk.Count; i++)
            if (!string.IsNullOrWhiteSpace(chunk[i]))
                numbered.Add((i, ITranslationService.StripXmlTags(chunk[i])));

        if (numbered.Count == 0) return chunk;

        var prompt = string.Join("\n", numbered.Select((n, i) => $"{i + 1}. {n.Text}"));
        var raw = await CallAsync(
            systemInstruction: "You are a translation engine. The input may contain slang, abbreviations, or several languages mixed together.",
            userContent: $"Translate each numbered line below to Korean. Output each translation on its own line with the same number prefix (e.g. \"1. ...\"). Do not merge, drop, reorder, or summarize lines. Only output the numbered translations:\n\n{prompt}",
            BATCH_OUTPUT_TOKENS, ct);

        // 출력의 "n. 번역"을 파싱해 prompt 순번(1-based) → 번역으로 매핑
        var byNumber = new Dictionary<int, string>();
        foreach (var line in raw.Split('\n'))
        {
            var m = NumberedLine.Match(line);
            if (m.Success) byNumber[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Trim();
        }

        var results = chunk.ToArray();  // 기본값 = 원문(빈 줄/매핑 누락 시 폴백)

        // 단일 줄이면 모델이 번호 없이 번역만 내놓는 경우가 흔하다 — 통째로 그 줄에 대응
        if (numbered.Count == 1 && byNumber.Count == 0)
        {
            var only = raw.Trim();
            if (only.Length > 0) results[numbered[0].Index] = only;
            return results;
        }

        for (int i = 0; i < numbered.Count; i++)
            if (byNumber.TryGetValue(i + 1, out var translated) && translated.Length > 0)
                results[numbered[i].Index] = translated;
        return results;
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;
        return await CallAsync(
            systemInstruction: "You are a translation engine for fps in-game chat. The input may contain gaming slang, abbreviations, or several languages mixed together. Translate the entire input into the target language faithfully and completely, preserving every sentence — never merge, summarize, deduplicate, or drop any part, even if some sentences share the same meaning. Only output the translated text.",
            userContent: $"Translate the following text into {MapLanguageCode(targetLang)}, translating every sentence separately:\n\n{text}");
    }

    private async Task<string> CallAsync(string systemInstruction, string userContent, int maxOutputTokens = 512, CancellationToken ct = default)
    {
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { parts = new[] { new { text = userContent } } } },
            generationConfig = new { temperature = 0.1, maxOutputTokens, thinkingConfig = new { thinkingBudget = 0 } },
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
        var root = doc.RootElement;

        // 안전성 차단/빈 응답 등은 HTTP 200이어도 candidates가 없거나 비어 옴 — 깨진/빈 문장 대신 명확한 실패로 처리
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini returned no candidates.");

        return candidates[0]
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
