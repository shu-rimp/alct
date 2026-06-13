using AlctClient.Core;

namespace AlctClient.Tests;

// 읽기(가나) 매칭 검증 — NMeCab 사전(IpaDic\)이 테스트 출력 폴더에 복사되어 있어야 동작
public class JapaneseReadingMatcherTests
{
    private const string SampleJson = """
        {
          "version": 2,
          "languages": {
            "ja-JP": { "어시스트": ["アシスト"] }
          },
          "readings": {
            "ja-JP": {
              "킬포": ["きるぽいんと", "きるぽ*"],
              "킬": ["{n}きる", "{n}きろ"],
              "파티": ["{n}ぱー", "{n}ぺー", "{n}ぱ", "{n}ぺ"],
              "2파티": ["にぱー", "にぺー", "にぱ", "にぺ"],
              "위": ["うえ"],
              "앞": ["まえ"],
              "뒤": ["うしろ"],
              "오른쪽": ["みぎ"]
            }
          }
        }
        """;

    private static GlossaryService MakeService() => new(SampleJson);

    [Theory]
    [InlineData("着るぽ取った")]   // STT 동음 한자 오변환 표기들 — 전부 읽기 きるぽ 하나로 커버
    [InlineData("切るぽ取った")]
    [InlineData("キルポ取った")]
    [InlineData("きるぽ取った")]
    public void Apply_HomophoneSurfaceVariants_AllMatchByReading(string input)
        => Assert.Equal("<x>킬포</x>取った", MakeService().Apply(input, "ja-JP"));

    [Fact]
    public void Apply_PrefersLongestReading_OnOverlap()
        => Assert.Equal("<x>킬포</x>", MakeService().Apply("キルポイント", "ja-JP"));

    [Theory]
    [InlineData("11キロ", "11<x>킬</x>")]   // {n} 마커 — 숫자 바로 뒤에서만 매칭
    [InlineData("0切る", "0<x>킬</x>")]
    public void Apply_DigitMarkedReading_MatchesAfterNumber(string input, string expected)
        => Assert.Equal(expected, MakeService().Apply(input, "ja-JP"));

    [Theory]
    [InlineData("回線切るわ")]       // 일반 동사 切る(끊다) — 숫자 문맥이 없으면 미치환
    [InlineData("ジャケット着るわ")] // 일반 동사 着る(입다)
    public void Apply_PlainVerbHomophone_NotReplaced(string input)
        => Assert.Equal(input, MakeService().Apply(input, "ja-JP"));

    [Fact]
    public void Apply_RealCaptionLogSentence_ReplacesEveryVariant()
    {
        // 실제 라이브 캡션 로그 — 표기 매칭(アシスト)과 읽기 매칭이 한 문장에서 함께 동작
        var result = MakeService().Apply("すげー着るぽが切るぽっつってアシストが", "ja-JP");
        Assert.Equal("すげー<x>킬포</x>が<x>킬포</x>っつって<x>어시스트</x>が", result);
    }

    [Theory]
    [InlineData("二パきつい", "<x>2파티</x>きつい")]   // 한자 숫자 표기 — 읽기 ニパ로 수렴
    [InlineData("ニパだ", "<x>2파티</x>だ")]
    [InlineData("にぺーごめん", "<x>2파티</x>ごめん")]
    public void Apply_PartyHomophoneVariants_MatchByReading(string input, string expected)
        => Assert.Equal(expected, MakeService().Apply(input, "ja-JP"));

    [Theory]
    [InlineData("4パで来た", "4<x>파티</x>で来た")]    // {n}ぱ — 임의 숫자 + パ/ペ 일반화
    [InlineData("5ペーきつい", "5<x>파티</x>きつい")]
    public void Apply_DigitParty_KeepsDigitOutsideTag(string input, string expected)
        => Assert.Equal(expected, MakeService().Apply(input, "ja-JP"));

    [Theory]
    [InlineData("次にパスファインダー")]  // に+パ~ 일반 문장 — 경계 끝점 규칙이 오치환 차단
    [InlineData("2パターンある")]         // {n}ぱ가 단어 첫 음절을 자르면 안 됨
    public void Apply_WordInitialPaAfterNi_NotReplaced(string input)
        => Assert.Equal(input, MakeService().Apply(input, "ja-JP"));

    [Theory]
    [InlineData("右から来てる", "<x>오른쪽</x>から来てる")]
    [InlineData("後ろから来てる", "<x>뒤</x>から来てる")]
    [InlineData("飢え取って", "<x>위</x>取って")]   // 上(うえ)의 동음 한자 오변환
    public void Apply_DirectionWords_MatchByReading(string input, string expected)
        => Assert.Equal(expected, MakeService().Apply(input, "ja-JP"));

    [Theory]
    [InlineData("おまえやばい")]   // おまえ 내부의 まえ — 형태소 중간 시작 금지
    [InlineData("お前やばい")]
    [InlineData("名前なんだっけ")] // 名前(ナマエ) — 한자 형태소 내부 미매칭
    public void Apply_WordContainingDirectionReading_NotReplaced(string input)
        => Assert.Equal(input, MakeService().Apply(input, "ja-JP"));

    [Fact]
    public void Apply_ReadingEntries_NotAppliedToOtherLanguage()
        => Assert.Equal("着るぽ", MakeService().Apply("着るぽ", "en-US"));

    [Fact]
    public void Apply_ReturnsUnchanged_WhenNoReadingMatches()
    {
        var text = "ナイスファイト";
        Assert.Equal(text, MakeService().Apply(text, "ja-JP"));
    }
}
