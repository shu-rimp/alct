using System;
using AlctClient.Core;
using Xunit;

namespace AlctClient.Tests;

public class TranslationCoordinatorTests
{
    private static TranslationCoordinator Make(
        TranslationEngine voice = TranslationEngine.MyMemory,
        TranslationEngine text = TranslationEngine.MyMemory)
        => new(voice, text, deepLKey: "deepl", geminiKey: "gemini", langblyKey: "langbly", myMemoryEmail: "me@x.com");

    [Fact]
    public void Constructor_BuildsBothServices()
    {
        var c = Make();
        Assert.NotNull(c.VoiceService);
        Assert.NotNull(c.TextService);
    }

    [Theory]
    [InlineData(TranslationEngine.DeepL, "deepl")]
    [InlineData(TranslationEngine.Gemini, "gemini")]
    [InlineData(TranslationEngine.Langbly, "langbly")]
    [InlineData(TranslationEngine.MyMemory, "me@x.com")]
    public void GetCredential_ReturnsPerEngineValue(TranslationEngine engine, string expected)
        => Assert.Equal(expected, Make().GetCredential(engine));

    [Fact]
    public void UpdateCredential_RecreatesOnlyMatchingSlot()
    {
        var c = Make(voice: TranslationEngine.DeepL, text: TranslationEngine.Gemini);
        var voiceBefore = c.VoiceService;
        var textBefore = c.TextService;

        c.UpdateCredential(TranslationEngine.DeepL, "new-deepl");

        Assert.NotSame(voiceBefore, c.VoiceService);  // voice 슬롯이 DeepL → 재생성
        Assert.Same(textBefore, c.TextService);       // text 슬롯은 Gemini → 그대로
        Assert.Equal("new-deepl", c.GetCredential(TranslationEngine.DeepL));
    }

    [Fact]
    public void UpdateCredential_DeepL_ClearsVoiceQuotaBlock()
    {
        var c = Make(voice: TranslationEngine.DeepL);
        c.BlockVoiceQuotaUntil(DateTime.UtcNow.AddHours(1));
        Assert.True(c.IsVoiceQuotaBlocked);

        c.UpdateCredential(TranslationEngine.DeepL, "new-deepl");

        Assert.False(c.IsVoiceQuotaBlocked);  // 새 키 = 새 할당량
    }

    [Fact]
    public void UpdateCredential_MyMemory_ClearsVoiceQuotaBlock()
    {
        var c = Make(voice: TranslationEngine.MyMemory);
        c.BlockVoiceQuotaUntil(DateTime.UtcNow.AddHours(1));

        c.UpdateCredential(TranslationEngine.MyMemory, "new@x.com");

        Assert.False(c.IsVoiceQuotaBlocked);
    }

    [Theory]
    [InlineData(TranslationEngine.Gemini)]
    [InlineData(TranslationEngine.Langbly)]
    public void UpdateCredential_GeminiOrLangbly_KeepsVoiceQuotaBlock(TranslationEngine engine)
    {
        var c = Make(voice: engine);
        c.BlockVoiceQuotaUntil(DateTime.UtcNow.AddHours(1));

        c.UpdateCredential(engine, "new-key");

        Assert.True(c.IsVoiceQuotaBlocked);  // 키 교체가 할당량 컨텍스트를 바꾸지 않음
    }

    [Fact]
    public void SetVoiceEngine_SwapsServiceAndClearsQuotaBlock()
    {
        var c = Make(voice: TranslationEngine.MyMemory);
        var before = c.VoiceService;
        c.BlockVoiceQuotaUntil(DateTime.UtcNow.AddHours(1));

        c.SetVoiceEngine(TranslationEngine.DeepL);

        Assert.NotSame(before, c.VoiceService);
        Assert.False(c.IsVoiceQuotaBlocked);
    }

    [Fact]
    public void SetTextEngine_SwapsServiceWithoutTouchingQuotaBlock()
    {
        var c = Make(text: TranslationEngine.MyMemory);
        var voiceBefore = c.VoiceService;
        var textBefore = c.TextService;
        c.BlockVoiceQuotaUntil(DateTime.UtcNow.AddHours(1));

        c.SetTextEngine(TranslationEngine.DeepL);

        Assert.NotSame(textBefore, c.TextService);
        Assert.Same(voiceBefore, c.VoiceService);  // text 변경은 voice 슬롯에 영향 없음
        Assert.True(c.IsVoiceQuotaBlocked);         // 텍스트 경로는 할당량 차단과 무관
    }

    [Fact]
    public void VoiceQuotaBlock_ExpiresWithTime()
    {
        var c = Make();
        c.BlockVoiceQuotaUntil(DateTime.UtcNow.AddMilliseconds(-1));  // 이미 지난 시각
        Assert.False(c.IsVoiceQuotaBlocked);
    }
}
