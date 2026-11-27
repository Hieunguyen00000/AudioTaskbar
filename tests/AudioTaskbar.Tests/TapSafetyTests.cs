namespace AudioTaskbar.Tests;

/// <summary>7. TAP double-injection guard and the 90 s Explorer crash-loop breaker.</summary>
public class TapSafetyTests
{
    [Theory]
    [InlineData(true, true, false, "Reuse")]
    [InlineData(true, false, false, "Reuse")]
    [InlineData(true, true, true, "Reuse")]
    [InlineData(true, false, true, "Reuse")]
    [InlineData(false, false, true, "RefuseLegacy")]   // old v1/v2 TAP in explorer: a 2nd copy crashes it
    [InlineData(false, true, true, "Inject")]          // teardown-safe v3 whose site was released
    [InlineData(false, true, false, "Inject")]
    [InlineData(false, false, false, "Inject")]        // clean explorer
    public void Never_injects_twice_into_the_same_explorer(bool alive, bool loaded, bool hasModule, string expected)
        => Assert.Equal(expected, TapSafety.Decide(alive, loaded, hasModule).ToString());

    [Fact]
    public void A_live_v3_service_is_always_reused()
    {
        foreach (var loaded in new[] { false, true })
            foreach (var module in new[] { false, true })
                Assert.Equal(TapAttachDecision.Reuse, TapSafety.Decide(true, loaded, module));
    }

    [Fact]
    public void Explorer_exit_within_90s_trips_the_breaker()
    {
        var b = new CrashLoopBreaker();
        var t0 = new DateTime(2026, 11, 27, 15, 0, 0);
        b.RecordAttach(t0);
        Assert.True(b.OnExplorerExited(t0.AddSeconds(30)));
        Assert.True(b.Tripped);
        Assert.False(b.AllowsAttach);
        Assert.False(string.IsNullOrWhiteSpace(b.Reason));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(91)]
    [InlineData(3600)]
    [InlineData(-5)]   // clock went backwards: do not blame the TAP
    public void Late_or_bogus_exit_does_not_trip(int seconds)
    {
        var b = new CrashLoopBreaker();
        var t0 = new DateTime(2026, 11, 27, 15, 0, 0);
        b.RecordAttach(t0);
        Assert.False(b.OnExplorerExited(t0.AddSeconds(seconds)));
        Assert.True(b.AllowsAttach);
    }

    [Fact]
    public void Exit_without_any_attach_does_not_trip()
    {
        var b = new CrashLoopBreaker();
        Assert.False(b.OnExplorerExited(DateTime.Now));
        Assert.True(b.AllowsAttach);
    }

    [Fact]
    public void Once_tripped_stays_tripped()
    {
        var b = new CrashLoopBreaker();
        var t0 = DateTime.Now;
        b.RecordAttach(t0);
        b.OnExplorerExited(t0.AddSeconds(10));
        b.RecordAttach(t0.AddMinutes(10));
        b.OnExplorerExited(t0.AddMinutes(30));
        Assert.True(b.Tripped);
        Assert.False(b.AllowsAttach);
    }

    [Fact]
    public void Breaker_window_is_90_seconds()
        => Assert.Equal(TimeSpan.FromSeconds(90), CrashLoopBreaker.Window);
}
