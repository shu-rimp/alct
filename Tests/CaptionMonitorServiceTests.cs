using AlctClient.Core;

namespace AlctClient.Tests;

// 첫 단어 잘림 회귀 테스트 - Live Captions가 진행 중인 줄을 재작성(STT 후행 수정)할 때
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

    // 폭탄 회귀 테스트 - Live Captions가 이미 확정된 줄의 뒷부분만 재구두점(쉼표 삽입 등)할 때
    // 커밋 전체를 무효화하면 누적 줄 전체가 한꺼번에 재발사(재번역)되던 버그를 방지하는지 검증.
    [Fact]
    public void RepunctuatedTail_DoesNotRefireWholeLine()
    {
        var (svc, fired) = MakeService();

        // 1) 한 줄을 발송 + 커밋
        svc.FeedForTest("I am powered up come on");
        svc.ForceDebounceForTest();

        // 2) 확정부 뒤쪽에 쉼표가 끼워지고 단어가 덧붙음(꼬리만 변경) 후 멈춤
        svc.FeedForTest("I am powered up, come on now");
        svc.ForceDebounceForTest();

        // 바뀐 꼬리만 발송돼야 함 - 누적 줄 전체("I am powered up, come on now")가 통째로 재발사되면 안 됨
        // (삽입된 쉼표가 공통 접두사 경계라 꼬리는 ", come on now"; 줄 전체 재발사가 아니라는 게 핵심)
        Assert.Equal(new[] { "I am powered up come on", ", come on now" }, fired);
    }

    // 기아(starvation) 회귀 테스트 - 앞부분에 경계(쉼표 등)가 없는 긴 무구두점 발화에서
    // 커밋 경계 탐색 범위를 좁히면 머리를 영영 못 끊어 누적됐다 통째로 발사되던 폭탄을 방지.
    [Fact]
    public void LongUnpunctuatedRun_DoesNotStarve_CommitsAtFirstBoundary()
    {
        var (svc, fired) = MakeService();

        // 앞 60자에 경계가 전혀 없는 발화(가중 120 > MIN_COMMIT_LENGTH). 이후 쉼표가 등장하며 자라기만 함.
        var head = new string('가', 60);
        for (int i = 1; i <= 14; i++)
            svc.FeedForTest(head + "，" + new string('나', i));

        // 머리(60자)가 첫 쉼표 경계에서 끊겨 발송돼야 함 - 굶어서 누적되면 이 조각이 없음
        Assert.Contains(fired, f => f.StartsWith(head) && f.EndsWith("，"));
    }
}
