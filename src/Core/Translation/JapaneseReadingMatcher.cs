using AlctClient.Utils;
using NMeCab.Specialized;
using System.Text;

namespace AlctClient.Core;

// 일본어 STT의 동음 한자 오변환(キル → 切る/着る) 대응 — 용어를 표기 대신 읽기(가나)로 매칭한다.
// STT는 소리는 정확히 잡고 가나→한자 변환에서만 틀리므로, 읽기 항목 하나가 미등록 표기 변형까지 커버.
// 별칭 마커:
//   "{n}" 접두 — 숫자 바로 뒤에서만 매칭. 切る(끊다)/着る(입다) 같은 일반 동사 오치환 방지
//   "*" 접미 — 매칭이 뒤따르는 단어 중간에서 끝나는 것을 허용("切るぽっつって"의 きるぽ).
//              기본은 형태소 경계에서만 끝남 — にぱ가 "次にパスファインダー"를 자르지 않도록
public sealed class JapaneseReadingMatcher
{
    private const string DIGIT_MARKER = "{n}";
    private const char LOOSE_END_MARKER = '*';

    // 사전(IpaDic) 로드 비용이 커서 앱 수명 동안 공유 — NMeCab 0.10의 Parse는 스레드 세이프
    private static readonly Lazy<MeCabIpaDicTagger> _tagger = new(() =>
        MeCabIpaDicTagger.Create(IOPath.Combine(AppContext.BaseDirectory, "IpaDic")));

    private static bool _failureLogged;

    private readonly List<(string Reading, string Target, bool AfterDigitOnly, bool LooseEnd)> _terms;

    public JapaneseReadingMatcher(IEnumerable<KeyValuePair<string, string>> readingToTarget)
    {
        // 긴 읽기 우선 — 짧은 용어가 긴 용어의 일부를 선점하는 것 방지 (표기 매칭의 SortLongestFirst와 동일 이유)
        _terms = readingToTarget
            .Select(kv =>
            {
                var afterDigitOnly = kv.Key.StartsWith(DIGIT_MARKER);
                var raw = afterDigitOnly ? kv.Key[DIGIT_MARKER.Length..] : kv.Key;
                var looseEnd = raw.EndsWith(LOOSE_END_MARKER);
                var reading = ToKatakana(looseEnd ? raw[..^1] : raw);
                return (Reading: reading, Target: kv.Value, AfterDigitOnly: afterDigitOnly, LooseEnd: looseEnd);
            })
            .Where(t => t.Reading.Length > 0)
            .OrderByDescending(t => t.Reading.Length)
            .ToList();
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || _terms.Count == 0) return text;

        List<(int Start, int Length, string Target)> matches;
        try
        {
            matches = FindMatches(text);
        }
        catch (Exception ex)
        {
            if (!_failureLogged) Logger.Error("ReadingMatch", ex);  // 사전 폴더 누락 등 — 캡션 흐름은 유지, 표기 매칭만으로 동작
            _failureLogged = true;
            return text;
        }
        if (matches.Count == 0) return text;

        var result = new StringBuilder(text.Length + matches.Count * 16);
        var pos = 0;
        foreach (var (start, length, target) in matches.OrderBy(m => m.Start))
        {
            result.Append(text, pos, start - pos).Append("<x>").Append(target).Append("</x>");
            pos = start + length;
        }
        return result.Append(text, pos, text.Length - pos).ToString();
    }

    private List<(int Start, int Length, string Target)> FindMatches(string text)
    {
        var (reading, startAnchors, boundaryEnds, looseEnds) = BuildReadingMap(text);
        var matches = new List<(int Start, int Length, string Target)>();

        foreach (var (termReading, target, afterDigitOnly, looseEnd) in _terms)
        {
            var endAnchors = looseEnd ? looseEnds : boundaryEnds;
            var from = 0;
            int idx;
            while ((idx = reading.IndexOf(termReading, from, StringComparison.Ordinal)) >= 0)
            {
                from = idx + 1;
                if (afterDigitOnly && (idx == 0 || !char.IsDigit(reading[idx - 1]))) continue;
                if (!startAnchors.TryGetValue(idx, out var start)) continue;
                if (!endAnchors.TryGetValue(idx + termReading.Length, out var end)) continue;
                if (matches.Any(m => start < m.Start + m.Length && m.Start < end)) continue;  // 먼저 수집된(더 긴) 용어 우선
                matches.Add((start, end - start, target));
            }
        }
        return matches;
    }

    // 텍스트 전체의 읽기 문자열과, 매칭 경계로 유효한 읽기 위치 ↔ 표면 위치 대응표를 만든다.
    // 시작점: 형태소 경계만 — "おまえ"의 まえ처럼 단어 중간에서 시작하는 오치환 방지.
    // 끝점: 기본은 형태소 경계만(boundaryEnds) — 단어 중간을 자르고 끝나는 오치환 방지.
    //        "*" 용어는 가나 형태소(읽기-표기 1:1) 내부 위치까지 허용(looseEnds) — "切るぽっつって"의 きるぽ용
    private static (string Reading, Dictionary<int, int> StartAnchors, Dictionary<int, int> BoundaryEnds, Dictionary<int, int> LooseEnds) BuildReadingMap(string text)
    {
        var reading = new StringBuilder(text.Length * 2);
        var startAnchors = new Dictionary<int, int>();
        var boundaryEnds = new Dictionary<int, int>();
        var looseEnds = new Dictionary<int, int>();
        var surfacePos = 0;

        foreach (var node in _tagger.Value.Parse(text))
        {
            if (string.IsNullOrEmpty(node.Surface)) continue;
            surfacePos = text.IndexOf(node.Surface, surfacePos, StringComparison.Ordinal);  // 공백 등 비형태소 건너뜀
            if (surfacePos < 0) throw new InvalidOperationException($"형태소 표면이 원문에 없음: {node.Surface}");

            var nodeReading = string.IsNullOrEmpty(node.Reading) || node.Reading == "*"
                ? ToKatakana(node.Surface)  // 미등록어(숫자·신조어·가타카나 약어 등)는 표기를 그대로 읽기로
                : node.Reading;

            startAnchors[reading.Length] = surfacePos;
            boundaryEnds[reading.Length + nodeReading.Length] = surfacePos + node.Surface.Length;
            if (nodeReading.Length == node.Surface.Length && IsAllKana(node.Surface))
                for (var i = 1; i < nodeReading.Length; i++)
                    looseEnds[reading.Length + i] = surfacePos + i;

            reading.Append(nodeReading);
            surfacePos += node.Surface.Length;
        }

        foreach (var (key, value) in boundaryEnds) looseEnds[key] = value;  // 경계 끝점은 loose에도 유효
        return (reading.ToString(), startAnchors, boundaryEnds, looseEnds);
    }

    private static string ToKatakana(string text) =>
        new(text.Select(c => c is >= 'ぁ' and <= 'ゖ' ? (char)(c + 0x60) : c).ToArray());

    private static bool IsAllKana(string text) =>
        text.All(c => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー');
}
