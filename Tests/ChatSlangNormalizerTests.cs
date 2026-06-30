using AlctClient.Core;

namespace AlctClient.Tests;

public class ChatSlangNormalizerTests
{
    [Fact]
    public void Normalize_SingleAlias_Wrapped()
    {
        Assert.Equal("<x>괜찮아</x>", ChatSlangNormalizer.Normalize("np"));
    }

    [Fact]
    public void Normalize_MultipleAliasesInText()
    {
        Assert.Equal("<x>ㅋㅋ</x> <x>헐</x>", ChatSlangNormalizer.Normalize("lol omg"));
    }

    [Fact]
    public void Normalize_SpecialCharAlias()
    {
        // tl;dr처럼 비단어 문자를 포함한 alias도 통째로 매칭돼야 함
        Assert.Equal("<x>요약하면</x>", ChatSlangNormalizer.Normalize("tl;dr"));
    }

    [Fact]
    public void Normalize_NonAsciiAlias()
    {
        Assert.Equal("<x>ㅋㅋ</x>", ChatSlangNormalizer.Normalize("草"));
    }

    [Fact]
    public void Normalize_CaseInsensitive()
    {
        Assert.Equal("<x>ㅋㅋ</x>", ChatSlangNormalizer.Normalize("LOL"));
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
        Assert.Equal("ok <x>잠깐만</x> guys", ChatSlangNormalizer.Normalize("ok brb guys"));
    }

    [Fact]
    public void Normalize_NoPartialWordMatch()
    {
        // "omg"가 더 긴 단어 안에 포함될 때 매칭되지 않아야 함 (수정된 \b 경계 검증)
        Assert.Equal("omgosh", ChatSlangNormalizer.Normalize("omgosh"));
    }
}
