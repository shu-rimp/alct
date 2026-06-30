using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlctClient.Core;

// 채팅 약어(gg, brb, yoroshiku 등)를 <x>한국어</x>로 단일 패스 치환한다. <x> 태그는 DeepL은
// ignore_tags로 보존하고 나머지 엔진은 StripXmlTags로 벗겨 한글을 통과시킨다(GlossaryService와 동일 규약).
//
// GlossaryService(순차 다중치환)와 분리한 이유: 약어는 치환 결과가 다시 약어를 포함하는 경우가 있어
// (예: "gg"→"gg", "gg ez"→"gg 쉽네") 순차 치환 시 결과 안의 약어가 재매칭돼 이중치환된다.
// 서버처럼 길이desc 결합정규식 1패스로 처리하면 치환 영역을 건너뛰어 이 연쇄가 없다.
// 서버 alct-server/src/core/text_normalizer.py 의 normalizeText 포팅 — 경계/대소문자 규칙 동일.
internal static class ChatSlangNormalizer
{
    private static readonly Dictionary<string, string> _aliasMap;  // alias(ascii는 소문자) → 한국어
    private static readonly Regex _pattern;

    static ChatSlangNormalizer()
    {
        var map = new Dictionary<string, string>();
        foreach (var (korean, aliases) in LoadAliases())
            foreach (var alias in aliases)
                map[IsAscii(alias) ? alias.ToLowerInvariant() : alias] = korean;
        _aliasMap = map;

        // 긴 alias 우선(정규식 alternation은 좌→우 순서라 "gg ez"가 "gg"보다 앞서야 함)
        var entries = map.Keys.OrderByDescending(k => k.Length).Select(BuildAliasEntry);
        _pattern = new Regex(string.Join("|", entries), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public static string Normalize(string text)
    {
        text = EscapeXml(text);
        return _pattern.Replace(text, m =>
        {
            var key = IsAscii(m.Value) ? m.Value.ToLowerInvariant() : m.Value;
            return $"<x>{_aliasMap[key]}</x>";
        });
    }

    private static string BuildAliasEntry(string alias) =>
        IsAscii(alias)
            ? @"\b" + Regex.Escape(alias) + @"\b"
            : Regex.Escape(alias);

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static bool IsAscii(string s) => s.All(c => c < 128);

    // assets/normalizer_data.json (EmbeddedResource) → 한국어 → [aliases]
    private static Dictionary<string, string[]> LoadAliases()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("AlctClient.assets.normalizer_data.json")
            ?? throw new InvalidOperationException("normalizer_data.json embedded resource missing");
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string[]>();
        foreach (var korean in doc.RootElement.GetProperty("aliases").EnumerateObject())
            result[korean.Name] = korean.Value.EnumerateArray().Select(a => a.GetString()!).ToArray();
        return result;
    }
}
