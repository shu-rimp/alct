using AlctClient.Core;

namespace AlctClient.Tests;

public class AppStateTests
{
    [Fact]
    public void Initial_HasExpectedDefaults()
    {
        var state = AppState.Initial;

        Assert.False(state.IsConnected);
        Assert.False(state.IsCapturing);
        Assert.Equal(string.Empty, state.TranslationResult);
        Assert.Equal("대기 중", state.StatusMessage);
    }

    [Fact]
    public void With_IsConnected_CreatesNewRecord()
    {
        var original = AppState.Initial;
        var updated = original with { IsConnected = true };

        Assert.False(original.IsConnected);
        Assert.True(updated.IsConnected);
    }

    [Fact]
    public void With_TranslationResult_DoesNotMutateOriginal()
    {
        var original = AppState.Initial;
        var updated = original with { TranslationResult = "안녕하세요" };

        Assert.Equal(string.Empty, original.TranslationResult);
        Assert.Equal("안녕하세요", updated.TranslationResult);
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var a = AppState.Initial;
        var b = AppState.Initial;
        Assert.Equal(a, b);
    }
}
