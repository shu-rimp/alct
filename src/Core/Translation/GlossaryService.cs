using AlctClient.Utils;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlctClient.Core;

// 게임 용어를 번역 요청 전에 한국어로 치환하는 용어집.
// 치환 결과는 <x>한국어</x> 형태 — DeepL은 ignore_tags로 보존하고,
// 나머지 엔진은 StripXmlTags로 태그만 벗겨 한글을 그대로 통과시키므로 엔진에 무관하게 동작.
// 로드 우선순위: 서버 최신본(/glossary) → 로컬 캐시 → 빌드 내장 기본본
public sealed class GlossaryService
{
    public static GlossaryService Instance { get; } = new();

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ALCT", "glossary_data.json");

    // 로드 시점에 1회 만들어 캐시하는 치환 단위. Pattern!=null이면 정규식(ASCII 단어경계), null이면 단순 부분문자열 치환(CJK)
    private readonly record struct GlossaryTerm(string Term, string Replacement, Regex? Pattern);

    // 언어 → 치환 목록. 긴 용어 우선 정렬 — 부분 문자열 중복 매칭 방지
    // 각 언어 목록에는 common(언어 무관 영문 표기) 용어가 병합돼 있음
    private volatile Dictionary<string, List<GlossaryTerm>> _entries = new();
    private volatile List<GlossaryTerm> _commonOnly = new();  // 미등록 언어용 폴백

    private GlossaryService()
    {
        if (LoadCacheFile() is { } cached && TryLoad(cached)) return;
        if (LoadEmbeddedDefault() is { } fallback) TryLoad(fallback);
    }

    internal GlossaryService(string json) => TryLoad(json);  // 테스트용

    public string Apply(string text, string sourceLang)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!_entries.TryGetValue(sourceLang, out var terms)) terms = _commonOnly;

        foreach (var t in terms)
            text = t.Pattern is null ? text.Replace(t.Term, t.Replacement) : t.Pattern.Replace(text, t.Replacement);
        return text;
    }

    // 서버 용어집으로 갱신 — 성공 시 캐시 저장, 실패해도 기존(캐시/내장본) 유지
    public async Task RefreshFromServerAsync(string serverUrl)
    {
        try
        {
            // 정적 호스팅(GitHub Pages) — 인증 없이 단순 GET. 실패 시 캐시/내장본 유지.
            var response = await _http.GetAsync(serverUrl.TrimEnd('/') + "/glossary.json");
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            if (!TryLoad(json)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(_cachePath, json);
            Logger.Info("Glossary", $"Server glossary updated — {_entries.Sum(e => e.Value.Count)} terms");
        }
        catch (Exception ex)
        {
            Logger.Info("Glossary", $"Server glossary update failed ({ex.GetType().Name}) — using cache/embedded");
        }
    }

    private bool TryLoad(string json)
    {
        try
        {
            // 후행 쉼표·주석 허용 — 정규형은 표준 JSON이지만, 머지 중 끼어든 후행 쉼표 등을 관대하게 수용
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = doc.RootElement;
            var common = root.TryGetProperty("common", out var c) ? ParseTerms(c) : new Dictionary<string, string>();

            var dict = new Dictionary<string, List<GlossaryTerm>>();
            foreach (var lang in root.GetProperty("languages").EnumerateObject())
            {
                var merged = new Dictionary<string, string>(common);
                foreach (var (term, target) in ParseTerms(lang.Value))
                    merged[term] = target;  // 언어별 용어가 common보다 우선
                dict[lang.Name] = BuildTerms(merged);
            }

            _commonOnly = BuildTerms(common);
            _entries = dict;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("GlossaryLoad", ex);
            return false;
        }
    }

    // normalizer_data.json과 동일한 "한국어": ["원어 변형", ...] 구조를 (원어 → 한국어)로 펼침
    private static Dictionary<string, string> ParseTerms(JsonElement obj)
    {
        var terms = new Dictionary<string, string>();
        foreach (var target in obj.EnumerateObject())
        {
            if (target.Name.Length == 0) continue;
            foreach (var alias in target.Value.EnumerateArray())
                if (alias.GetString() is { Length: > 0 } term)
                    terms[term] = target.Name;
        }
        return terms;
    }

    // 긴 용어 우선 정렬 후 치환 단위로 변환. ASCII 용어만 Regex를 1회 생성·캐시(핫패스에서 재컴파일 방지).
    // 영문 용어는 영숫자 인접만 차단(\b는 CJK도 단어 문자로 취급해 "wraith来了"를 놓침), CJK는 단순 부분일치 치환.
    private static List<GlossaryTerm> BuildTerms(Dictionary<string, string> terms) =>
        terms.OrderByDescending(kv => kv.Key.Length)
            .Select(kv => new GlossaryTerm(
                kv.Key,
                $"<x>{kv.Value}</x>",
                IsAsciiTerm(kv.Key)
                    ? new Regex($@"(?<![A-Za-z0-9]){Regex.Escape(kv.Key)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase)
                    : null))
            .ToList();

    private static string? LoadCacheFile()
    {
        try { return File.Exists(_cachePath) ? File.ReadAllText(_cachePath) : null; }
        catch { return null; }
    }

    private static string? LoadEmbeddedDefault()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("AlctClient.assets.glossary_data.json");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }

    private static bool IsAsciiTerm(string term) => term.All(c => c < 128);
}
