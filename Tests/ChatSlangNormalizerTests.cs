using AlctClient.Core;

namespace AlctClient.Tests;

// 서버 text_normalizer.normalizeText 와 동일하게 동작하는지 검증.
public class ChatSlangNormalizerTests
{
    [Fact]
    public void Normalize_SingleAlias_Wrapped()
    {
        Assert.Equal("<x>gg</x>", ChatSlangNormalizer.Normalize("gg"));
    }

    [Fact]
    public void Normalize_LongerAliasWins_NoDoubleSubstitution()
    {
        // 핵심: "gg ez"는 "gg 쉽네"로 한 번만 치환돼야 함. 순차 다중치환이면 결과 안의 "gg"가
        // 재매칭돼 "<x><x>gg</x> 쉽네</x>"로 깨지는데, 단일패스라 그러지 않아야 한다.
        Assert.Equal("<x>gg 쉽네</x>", ChatSlangNormalizer.Normalize("gg ez"));
    }

    [Fact]
    public void Normalize_MultiWordAlias()
    {
        Assert.Equal("<x>화이팅 즐겜</x>", ChatSlangNormalizer.Normalize("gl hf"));
    }

    [Fact]
    public void Normalize_JapaneseRomaji()
    {
        Assert.Equal("<x>잘 부탁해</x>", ChatSlangNormalizer.Normalize("yoroshiku"));
    }

    [Fact]
    public void Normalize_CaseInsensitive()
    {
        Assert.Equal("<x>gg</x>", ChatSlangNormalizer.Normalize("GG"));
    }

    [Fact]
    public void Normalize_EscapesXmlSpecialChars()
    {
        Assert.Equal("&lt;3 &amp; 5&gt;", ChatSlangNormalizer.Normalize("<3 & 5>"));
    }

    [Fact]
    public void Normalize_NonAliasText_Unchanged()
    {
        Assert.Equal("hello world", ChatSlangNormalizer.Normalize("hello world"));
    }

    [Fact]
    public void Normalize_AliasWithinSentence()
    {
        Assert.Equal("ok <x>gg</x> guys", ChatSlangNormalizer.Normalize("ok gg guys"));
    }
}
