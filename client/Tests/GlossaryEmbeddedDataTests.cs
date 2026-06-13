using AlctClient.Core;
using System.IO;

namespace AlctClient.Tests;

// 실제 내장 glossary_data.json으로 매칭을 검증 — 데이터 편집 후 회귀 확인용
public class GlossaryEmbeddedDataTests
{
    private static GlossaryService MakeFromEmbedded()
    {
        using var stream = typeof(GlossaryService).Assembly
            .GetManifestResourceStream("AlctClient.assets.glossary_data.json")!;
        using var reader = new StreamReader(stream);
        return new GlossaryService(reader.ReadToEnd());
    }

    [Fact]
    public void Apply_ConvertsNiPe_FromRealData()
    {
        var result = MakeFromEmbedded().Apply("二ペ二ペごめん", "ja-JP");
        Assert.Equal("<x>2파티</x><x>2파티</x>ごめん", result);
    }
}
