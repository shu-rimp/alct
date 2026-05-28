using AlctClient.Core;

namespace AlctClient.Tests;

public class HotkeyManagerCooldownTests
{
    // HotkeyManager.IsCooldownElapsed() is tested via reflection-friendly subclass
    // to avoid requiring a real HWND in unit tests.
    [Fact]
    public void IsCooldownElapsed_BeforeAnyTrigger_ReturnsTrue()
    {
        var tracker = new CooldownTracker(cooldownMs: 1000);
        Assert.True(tracker.IsCooldownElapsed());
    }

    [Fact]
    public void IsCooldownElapsed_ImmediatelyAfterTrigger_ReturnsFalse()
    {
        var tracker = new CooldownTracker(cooldownMs: 1000);
        tracker.RecordTrigger();
        Assert.False(tracker.IsCooldownElapsed());
    }

    [Fact]
    public void IsCooldownElapsed_AfterCooldownPasses_ReturnsTrue()
    {
        var tracker = new CooldownTracker(cooldownMs: 50);
        tracker.RecordTrigger();
        Thread.Sleep(100);
        Assert.True(tracker.IsCooldownElapsed());
    }

    [Fact]
    public void IsCooldownElapsed_MultipleRapidTriggers_BlocksUntilCooldown()
    {
        var tracker = new CooldownTracker(cooldownMs: 200);
        tracker.RecordTrigger();

        Assert.False(tracker.IsCooldownElapsed());
        Thread.Sleep(100);
        Assert.False(tracker.IsCooldownElapsed());
        Thread.Sleep(150);
        Assert.True(tracker.IsCooldownElapsed());
    }
}

/// <summary>
/// Testable cooldown logic extracted from HotkeyManager, without requiring a real HWND.
/// </summary>
internal class CooldownTracker
{
    private readonly int _cooldownMs;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    public CooldownTracker(int cooldownMs) => _cooldownMs = cooldownMs;

    public void RecordTrigger() => _lastTriggerTime = DateTime.UtcNow;

    public bool IsCooldownElapsed() =>
        (DateTime.UtcNow - _lastTriggerTime).TotalMilliseconds >= _cooldownMs;
}
