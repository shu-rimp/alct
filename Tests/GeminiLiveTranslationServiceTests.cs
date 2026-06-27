using AlctClient.Core;
using Xunit;

namespace AlctClient.Tests;

public class GeminiLiveTranslationServiceTests
{
    private static GeminiLiveTranslationService Connect(TranslationMockServer server) =>
        new("test-key", server.WsEndpoint);

    [Fact]
    public async Task TranslateToKoreanAsync_CompletesHandshakeAndReturnsText()
    {
        using var server = new TranslationMockServer { Response = "안녕하세요." };
        server.Start();
        using var svc = Connect(server);

        var result = await svc.TranslateToKoreanAsync("hello", "en-US");

        Assert.Equal("안녕하세요.", result);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_ReusesSession_AcrossCalls()
    {
        using var server = new TranslationMockServer();
        server.Start();
        using var svc = Connect(server);

        await svc.TranslateToKoreanAsync("a", "en-US");
        await svc.TranslateToKoreanAsync("b", "en-US");

        Assert.Equal(1, server.ConnectionCount);  // 같은 소켓 재사용 → 연결 1회
    }

    [Fact]
    public async Task TranslateToKoreanAsync_ReturnsInput_WhenEmptyOrNoKey()
    {
        using var server = new TranslationMockServer();
        server.Start();

        using var noKey = new GeminiLiveTranslationService("", server.WsEndpoint);
        Assert.Equal("hi", await noKey.TranslateToKoreanAsync("hi", "en-US"));  // 키 없음 → 호출 안 함

        using var svc = Connect(server);
        Assert.Equal("   ", await svc.TranslateToKoreanAsync("   ", "en-US"));  // 공백 → 그대로
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_ReconnectsAndRetries_AfterSocketDrop()
    {
        using var server = new TranslationMockServer { FailFirstConnection = true };
        server.Start();
        using var svc = Connect(server);

        var result = await svc.TranslateToKoreanAsync("hello", "en-US");

        Assert.Equal("안녕하세요.", result);     // 드롭 후 재연결한 두 번째 세션이 응답
        Assert.Equal(2, server.ConnectionCount);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_RecyclesSession_WhenTokenThresholdExceeded()
    {
        using var server = new TranslationMockServer { EmitTokenCount = 30000 };  // 임계(24k) 초과 보고
        server.Start();
        using var svc = Connect(server);

        await svc.TranslateToKoreanAsync("a", "en-US");  // 첫 턴에서 누적 토큰이 임계 초과로 보고됨
        await svc.TranslateToKoreanAsync("b", "en-US");  // 다음 턴 전에 세션 재생성

        Assert.Equal(2, server.ConnectionCount);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_FinishesTurnThenRecycles_OnGoAway()
    {
        using var server = new TranslationMockServer { EmitGoAwayOnFirstTurn = true };
        server.Start();
        using var svc = Connect(server);

        var first  = await svc.TranslateToKoreanAsync("a", "en-US");  // goAway 예고에도 이번 턴은 정상 응답(예전엔 NRE)
        var second = await svc.TranslateToKoreanAsync("b", "en-US");  // 다음 턴은 재생성된 새 세션에서 응답

        Assert.Equal("안녕하세요.", first);
        Assert.Equal("안녕하세요.", second);
        Assert.Equal(2, server.ConnectionCount);  // goAway 후 세션 재생성
    }

    [Fact]
    public async Task TranslateToKoreanAsync_ThrowsRateLimit_OnResourceExhaustedClose()
    {
        using var server = new TranslationMockServer { RateLimitClose = true };
        server.Start();
        using var svc = Connect(server);

        await Assert.ThrowsAsync<TranslationRateLimitException>(
            () => svc.TranslateToKoreanAsync("hello", "en-US"));
    }
}
