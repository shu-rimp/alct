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

    [Fact]
    public async Task TranslateToKoreanAsync_KeepsTaggedKorean_TranslatesRest()
    {
        // 모든 API 호출이 "번역됨"을 반환 — <x>2파티</x>는 API를 타지 않고 보존돼야 함
        var svc = new MyMemoryTranslationService(
            new System.Net.Http.HttpClient(new FakeHttpMessageHandler(MyMemoryResponse("번역됨"), System.Net.HttpStatusCode.OK)));

        var result = await svc.TranslateToKoreanAsync("です。<x>2파티</x>だか<x>2파티</x>ごめん。", "ja-JP");

        Assert.Equal("번역됨 2파티 번역됨 2파티 번역됨", result);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_NoTags_TranslatesWholeLine()
    {
        var svc = new MyMemoryTranslationService(
            new System.Net.Http.HttpClient(new FakeHttpMessageHandler(MyMemoryResponse("안녕"), System.Net.HttpStatusCode.OK)));

        var result = await svc.TranslateToKoreanAsync("こんにちは", "ja-JP");

        Assert.Equal("안녕", result);
    }
}

public class GlossaryTranslationDecoratorTests
{
    private sealed class CapturingTranslationService : ITranslationService
    {
        public string? CapturedText;
        public Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
        {
            CapturedText = text;
            return Task.FromResult("번역결과");
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
    public async Task TranslateFromKoreanAsync_PassesThrough_WithoutGlossary()
    {
        var glossary = new GlossaryService("""{ "languages": { "ja-JP": { "윙맨": ["ウィングマン"] } } }""");
        var inner = new CapturingTranslationService();
        var decorated = new GlossaryTranslationDecorator(inner, glossary);

        var result = await decorated.TranslateFromKoreanAsync("윙맨 좋다", "ja-JP");

        Assert.Equal("윙맨 좋다", result);
    }
}
