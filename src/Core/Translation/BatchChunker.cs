namespace AlctClient.Core;

// 여러 텍스트를 엔진별 "안전 요청" 한도에 맞춰 묶음으로 자른다.
// 한 번에 대량 텍스트(전체화면 캡처 등)를 보낼 때, 항목 수(maxItems)와 누적 문자 수(maxChars)
// 두 기준 중 먼저 걸리는 쪽에서 묶음을 끊는다.
internal static class BatchChunker
{
    public static IEnumerable<List<string>> Chunk(IReadOnlyList<string> items, int maxItems, int maxChars)
    {
        var chunk = new List<string>();
        int chars = 0;
        foreach (var item in items)
        {
            int len = item?.Length ?? 0;
            if (chunk.Count > 0 && (chunk.Count >= maxItems || chars + len > maxChars))
            {
                yield return chunk;
                chunk = new List<string>();
                chars = 0;
            }
            chunk.Add(item ?? string.Empty);
            chars += len;
        }
        if (chunk.Count > 0) yield return chunk;
    }
}
