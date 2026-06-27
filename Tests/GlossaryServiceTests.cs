using AlctClient.Core;

namespace AlctClient.Tests;

public class GlossaryServiceTests
{
    private const string SampleJson = """
        {
          "version": 2,
          "common": { "레이스": ["wraith"], "피스키퍼": ["peacekeeper", "pk"] },
          "languages": {
            "zh-CN": { "바론": ["大龙"], "용": ["龙"], "골드 아머": ["金甲"] },
            "ja-JP": { "윙맨": ["ウィングマン"] },
            "en-US": { "존버충": ["rat"] }
          }
        }
        """;

    private static GlossaryService MakeService() => new(SampleJson);

    [Fact]
    public void Apply_ReplacesCjkTerm_WithTaggedKorean()
    {
        var result = MakeService().Apply("我们去打大龙吧", "zh-CN");
        Assert.Equal("我们去打<x>바론</x>吧", result);
    }

    [Fact]
    public void Apply_PrefersLongestTerm_OnOverlap()
    {
        // "大龙"이 "龙"보다 먼저 매칭되어야 함
        var result = MakeService().Apply("打大龙", "zh-CN");
        Assert.Equal("打<x>바론</x>", result);
    }

    [Fact]
    public void Apply_ReplacesMultipleTerms()
    {
        var result = MakeService().Apply("大龙旁边有金甲", "zh-CN");
        Assert.Equal("<x>바론</x>旁边有<x>골드 아머</x>", result);
    }

    [Fact]
    public void Apply_AsciiTerm_RespectsWordBoundary()
    {
        var svc = MakeService();
        Assert.Equal("he is a <x>존버충</x>", svc.Apply("he is a rat", "en-US"));
        Assert.Equal("nice rating", svc.Apply("nice rating", "en-US"));  // 단어 일부는 미치환
    }

    [Fact]
    public void Apply_CommonTerms_ApplyToEveryLanguage()
    {
        var svc = MakeService();
        Assert.Equal("<x>레이스</x>来了", svc.Apply("wraith来了", "zh-CN"));
        Assert.Equal("<x>피스키퍼</x>強い", svc.Apply("pk強い", "ja-JP"));
    }

    [Fact]
    public void Apply_AllAliasVariants_MapToSameTarget()
    {
        var svc = MakeService();
        Assert.Equal("<x>피스키퍼</x>", svc.Apply("peacekeeper", "ja-JP"));
        Assert.Equal("<x>피스키퍼</x>", svc.Apply("pk", "ja-JP"));
    }

    [Fact]
    public void Apply_CommonTerms_ApplyEvenWhenLanguageUnknown()
    {
        Assert.Equal("nice <x>레이스</x>", MakeService().Apply("nice wraith", "fr-FR"));
    }

    [Fact]
    public void Apply_LanguageTerm_NotAppliedToOtherLanguage()
    {
        // zh 전용 용어는 ja에 미적용
        var text = "大龙";
        Assert.Equal(text, MakeService().Apply(text, "ja-JP"));
    }

    [Fact]
    public void Apply_ReturnsUnchanged_WhenNoTermMatches()
    {
        var text = "ナイスファイト";
        Assert.Equal(text, MakeService().Apply(text, "ja-JP"));
    }

    [Fact]
    public void Apply_ReturnsEmpty_WhenTextEmpty()
    {
        Assert.Equal("", MakeService().Apply("", "zh-CN"));
    }

    [Fact]
    public void EmbeddedDefaultGlossary_ExistsAsResource()
    {
        using var stream = typeof(GlossaryService).Assembly
            .GetManifestResourceStream("AlctClient.assets.glossary_data.json");
        Assert.NotNull(stream);
    }
}

public class MyMemoryTagPreservationTests
{
    private static string MyMemoryResponse(string text) =>
        """{"responseStatus":200,"responseData":{"translatedText":"@T@"}}""".Replace("@T@", text);

    // MT가 토큰을 보존해 돌려준다고 가정한 고정 응답 + 호출 횟수 카운트
    private static MyMemoryTranslationService Svc(string responseText, out CallCountingHandler handler)
    {
        handler = new CallCountingHandler(MyMemoryResponse(responseText));
        return new MyMemoryTranslationService(new System.Net.Http.HttpClient(handler));
    }

    [Fact]
    public async Task TranslateToKoreanAsync_MasksTermsAndRestores_InSingleCall()
    {
        // <x>궁</x>, <x>폭탄</x>이 토큰으로 마스킹돼 줄 전체가 1회 번역되고, 응답에서 용어로 복원됨
        var t0 = MyMemoryTranslationService.MaskToken(0);
        var t1 = MyMemoryTranslationService.MaskToken(1);
        var svc = Svc($"적 {t0} 그리고 {t1} 사용", out var handler);

        var result = await svc.TranslateToKoreanAsync("enemy <x>궁</x> and <x>폭탄</x> use", "en-US");

        Assert.Equal("적 궁 그리고 폭탄 사용", result);
        Assert.Equal(1, handler.CallCount);  // 조각 분할 없이 1회
    }

    [Fact]
    public async Task TranslateToKoreanAsync_CorrectsParticleByBatchim()
    {
        // MT가 토큰에 붙인 '가'를 용어(궁=받침O) 받침에 맞게 '이'로 보정
        var t0 = MyMemoryTranslationService.MaskToken(0);
        var svc = Svc($"{t0}가 필요해", out _);

        var result = await svc.TranslateToKoreanAsync("need <x>궁</x>", "en-US");

        Assert.Equal("궁이 필요해", result);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_TagOnlyLine_SkipsApiCall()
    {
        var t0 = MyMemoryTranslationService.MaskToken(0);
        var svc = Svc(t0, out var handler);

        var result = await svc.TranslateToKoreanAsync("<x>궁</x>", "en-US");

        Assert.Equal("궁", result);
        Assert.Equal(0, handler.CallCount);  // 번역할 텍스트 없음 → 호출 안 함
    }

    [Fact]
    public async Task TranslateToKoreanAsync_DroppedToken_FallsBackToAppend()
    {
        // 응답에 토큰이 없으면(MT 누락) 용어를 잃지 않고 끝에 덧붙임
        var svc = Svc("적이 공격", out _);

        var result = await svc.TranslateToKoreanAsync("enemy <x>궁</x> attack", "en-US");

        Assert.StartsWith("적이 공격", result);
        Assert.Contains("궁", result);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_NoTags_TranslatesWholeLine()
    {
        var svc = Svc("안녕", out var handler);

        var result = await svc.TranslateToKoreanAsync("こんにちは", "ja-JP");

        Assert.Equal("안녕", result);
        Assert.Equal(1, handler.CallCount);
    }
}

public class GlossaryTranslationDecoratorTests
{
    private sealed class CapturingTranslationService : ITranslationService
    {
        public string? CapturedText;
        public IReadOnlyList<string>? CapturedBatch;
        public Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
        {
            CapturedText = text;
            return Task.FromResult("번역결과");
        }
        public Task<IReadOnlyList<string>> TranslateBatchToKoreanAsync(IReadOnlyList<string> texts, string sourceLang, CancellationToken ct = default)
        {
            CapturedBatch = texts;
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(_ => "번역결과").ToList());
        }
        public Task<string> TranslateFromKoreanAsync(string text, string targetLang) => Task.FromResult(text);
        public string MapLanguageCode(string bcp47) => bcp47;
    }

    [Fact]
    public async Task TranslateToKoreanAsync_SendsGlossaryAppliedText_ToInnerEngine()
    {
        var glossary = new GlossaryService("""{ "languages": { "ja-JP": { "윙맨": ["ウィングマン"] } } }""");
        var inner = new CapturingTranslationService();
        var decorated = new GlossaryTranslationDecorator(inner, glossary);

        await decorated.TranslateToKoreanAsync("ウィングマン強い", "ja-JP");

        Assert.Equal("<x>윙맨</x>強い", inner.CapturedText);
    }

    [Fact]
    public async Task TranslateBatchToKoreanAsync_AppliesGlossaryPerItem_ToInnerEngine()
    {
        var glossary = new GlossaryService("""{ "languages": { "ja-JP": { "윙맨": ["ウィングマン"] } } }""");
        var inner = new CapturingTranslationService();
        var decorated = new GlossaryTranslationDecorator(inner, glossary);

        var result = await decorated.TranslateBatchToKoreanAsync(new[] { "ウィングマン強い", "普通の行" }, "ja-JP");

        Assert.Equal(new[] { "<x>윙맨</x>強い", "普通の行" }, inner.CapturedBatch);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task TranslateFromKoreanAsync_PassesThrough_WithoutGlossary()
    {
        var glossary = new GlossaryService("""{ "languages": { "ja-JP": { "윙맨": ["ウィングマン"] } } }""");
        var inner = new CapturingTranslationService();
        var decorated = new GlossaryTranslationDecorator(inner, glossary);

        var result = await decorated.TranslateFromKoreanAsync("윙맨 좋다", "ja-JP");

        Assert.Equal("윙맨 좋다", result);
    }
}
