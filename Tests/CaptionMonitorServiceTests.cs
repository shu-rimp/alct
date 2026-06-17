using AlctClient.Core;

namespace AlctClient.Tests;

// 첫 단어 잘림 회귀 테스트 — Live Captions가 진행 중인 줄을 재작성(STT 후행 수정)할 때
// 커밋된 prefix를 원시 char offset으로 잘라내면 새 줄의 앞 글자가 잘리던 버그를 방지하는지 검증.
public class CaptionMonitorServiceTests
{
    private static (CaptionMonitorService svc, List<string> fired) MakeService()
    {
        var svc = new CaptionMonitorService();
        var fired = new List<string>();
        svc.CaptionStabilized += fired.Add;
        svc.InitForTest("");
        return (svc, fired);
    }

    [Fact]
    public void RevisedLine_DoesNotTruncateFirstWord()
    {
        var (svc, fired) = MakeService();

        // 1) 짧은 조각이 떠 있다가 멈춤 → 디바운스로 발송 + 커밋
        svc.FeedForTest("During our star");
        svc.ForceDebounceForTest();

        // 2) STT가 줄을 통째로 재작성 (prefix 불일치) 후 멈춤 → 발송
        svc.FeedForTest("Throwing our star.");
        svc.ForceDebounceForTest();

        // 재작성된 줄은 통째로 발송돼야 한다. 단어 중간이 잘린 "ar." 같은 조각이 나오면 안 됨.
        Assert.Contains("Throwing our star.", fired);
        Assert.DoesNotContain(fired, f => f == "ar.");
    }

    [Fact]
    public void GenuineContinuation_StripsCommittedPrefix_NoDuplicate()
    {
        var (svc, fired) = MakeService();

        // 1) 짧은 조각 발송 + 커밋
        svc.FeedForTest("I am");
        svc.ForceDebounceForTest();

        // 2) 같은 줄이 실제로 이어짐(커밋 prefix 유지) → 뒷부분만 발송, "I am" 재발송 없음
        svc.FeedForTest("I am powered up. Come to overtake");
        svc.ForceDebounceForTest();

        Assert.Equal(new[] { "I am", "powered up. Come to overtake" }, fired);
    }

    [Fact]
    public void CompletedLine_WithNewline_NotTruncated()
    {
        var (svc, fired) = MakeService();

        // 짧은 조각 커밋 후, \n으로 줄이 완성되면서 동시에 재작성된 케이스
        svc.FeedForTest("During our star");
        svc.ForceDebounceForTest();
        svc.FeedForTest("Throwing our star.\nNext");

        // 완성된 첫 줄이 "ar." 처럼 잘리지 않고 온전히 발송돼야 함
        Assert.Contains("Throwing our star.", fired);
        Assert.DoesNotContain(fired, f => f == "ar.");
    }
}
