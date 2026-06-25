using AlctClient.Utils;

namespace AlctClient.Tests;

public class KoreanParticleTests
{
    [Theory]
    // 받침 있음 → withBatchim 형태
    [InlineData("궁", "가", "이")]
    [InlineData("궁", "를", "을")]
    [InlineData("궁", "는", "은")]
    [InlineData("궁", "와", "과")]
    // 받침 없음 → withoutBatchim 형태
    [InlineData("이보", "이", "가")]
    [InlineData("이보", "을", "를")]
    [InlineData("이보", "은", "는")]
    [InlineData("이보", "과", "와")]
    // 으로/로: 받침(ㄹ 아님)→으로, ㄹ받침→로, 받침없음→로
    [InlineData("부산", "로", "으로")]
    [InlineData("서울", "으로", "로")]
    [InlineData("판교", "으로", "로")]
    public void Correct_FixesAllomorphByBatchim(string term, string given, string expected) =>
        Assert.Equal(expected, KoreanParticle.Correct(term, given));

    [Fact]
    public void Correct_LeavesUnknownParticleUnchanged() =>
        Assert.Equal("에서", KoreanParticle.Correct("궁", "에서"));

    [Fact]
    public void Correct_HandlesNonHangulTerm() =>
        // 한글 음절이 아니면 받침 없음으로 간주
        Assert.Equal("가", KoreanParticle.Correct("loba", "이"));
}
