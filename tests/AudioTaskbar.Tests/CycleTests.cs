namespace AudioTaskbar.Tests;

/// <summary>v5.3.1: user-defined day/night cycle length (no 30-minute limit), live change without a jump.</summary>
[Collection("Statics")]
public class CycleTests
{
    private static AppConfig Cycle(float minutes, float offset = 0)
    {
        var c = new AppConfig { TimeMode = "cycle", DayCycleMinutes = minutes, DayCycleOffsetHours = offset };
        c.Normalize();
        return c;
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(15f)]
    [InlineData(30f)]
    [InlineData(45.5f)]
    [InlineData(90f)]
    [InlineData(1440f)]
    [InlineData(10080f)]
    public void Custom_lengths_are_kept_as_entered(float minutes)
    {
        var c = Cycle(minutes);
        Assert.Equal(minutes, c.DayCycleMinutes);
        Assert.Equal(minutes * 60.0, SceneClock.CyclePeriodSeconds(c), 3);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(15f)]
    [InlineData(30f)]
    [InlineData(90f)]
    [InlineData(1440f)]
    public void One_full_virtual_day_takes_exactly_the_configured_minutes(float minutes)
    {
        var c = Cycle(minutes);
        SceneClock.HourOverride = null;
        var t0 = new DateTime(2026, 11, 27, 8, 0, 0);
        float h0 = SceneClock.VirtualHour(c, t0);
        float h1 = SceneClock.VirtualHour(c, t0.AddMinutes(minutes));
        float dh = Math.Abs(h1 - h0); dh = Math.Min(dh, 24f - dh);
        Assert.True(dh < 0.01f, $"{minutes} min: {h0} → {h1}");
        // A quarter of the period = 6 virtual hours.
        float hq = SceneClock.VirtualHour(c, t0.AddMinutes(minutes / 4.0));
        float dq = ((hq - h0) % 24f + 24f) % 24f;
        Assert.InRange(dq, 5.98f, 6.02f);
    }

    [Fact]
    public void Longer_cycles_are_slower()
    {
        var t0 = new DateTime(2026, 11, 27, 8, 0, 0);
        float Speed(float m) { var c = Cycle(m); float a = SceneClock.VirtualHour(c, t0), b = SceneClock.VirtualHour(c, t0.AddSeconds(10)); return ((b - a) % 24f + 24f) % 24f; }
        Assert.True(Speed(15) > Speed(30));
        Assert.True(Speed(30) > Speed(90));
        Assert.True(Speed(90) > Speed(1440));
    }

    [Theory]
    [InlineData(30f, 1f)]
    [InlineData(30f, 90f)]
    [InlineData(30f, 1440f)]
    [InlineData(1440f, 0.5f)]
    [InlineData(10080f, 15f)]
    public void Changing_the_length_live_does_not_jump_the_sky(float from, float to)
    {
        SceneClock.HourOverride = null;
        var now = new DateTime(2026, 11, 27, 15, 52, 17, 345);
        var c = Cycle(from, 7.25f);
        float before = SceneClock.VirtualHour(c, now);
        SceneClock.SetCycleMinutes(c, to, now);
        float after = SceneClock.VirtualHour(c, now);
        float d = Math.Abs(after - before); d = Math.Min(d, 24f - d);
        Assert.True(d < 0.02f, $"{before} → {after}");
        Assert.Equal(to, c.DayCycleMinutes);
        Assert.InRange(c.DayCycleOffsetHours, 0f, 24f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    public void Invalid_live_values_fall_back_cleanly(float bad)
    {
        var now = new DateTime(2026, 11, 27, 12, 0, 0);
        var c = Cycle(60);
        SceneClock.SetCycleMinutes(c, bad, now);
        Assert.Equal(30f, c.DayCycleMinutes);
        Assert.True(float.IsFinite(SceneClock.VirtualHour(c, now)));
    }

    [Fact]
    public void Custom_length_round_trips_through_config_json()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        var c = Cycle(90);
        ConfigStore.Save(path, c);
        Assert.Contains("\"dayCycleMinutes\": 90", File.ReadAllText(path));
        Assert.Equal(90f, ConfigStore.Load(path).DayCycleMinutes);
        File.WriteAllText(path, "{\"configVersion\":53,\"timeMode\":\"cycle\",\"dayCycleMinutes\":2880}");
        Assert.Equal(2880f, ConfigStore.Load(path).DayCycleMinutes);
        File.WriteAllText(path, "{\"configVersion\":53,\"dayCycleMinutes\":-1}");
        Assert.Equal(30f, ConfigStore.Load(path).DayCycleMinutes);
    }

    [Fact]
    public void Fixed_modes_ignore_the_custom_length()
    {
        var t = new DateTime(2026, 11, 27, 3, 0, 0);
        foreach (var m in new[] { 0.5f, 30f, 10080f })
        {
            var day = new AppConfig { TimeMode = "day", DayCycleMinutes = m }; day.Normalize();
            var night = new AppConfig { TimeMode = "night", DayCycleMinutes = m }; night.Normalize();
            Assert.InRange(SceneClock.VirtualHour(day, t), SceneClock.FixedDayFrom, SceneClock.FixedDayTo);
            Assert.True(SceneClock.VirtualHour(night, t) >= SceneClock.FixedNightFrom);
        }
    }

    [Theory]
    [InlineData(30f, "30 phút")]
    [InlineData(60f, "60 phút")]
    [InlineData(1.5f, "1.5 phút")]
    [InlineData(0.5f, "30 giây")]
    [InlineData(1440f, "1440 phút")]
    public void Tray_label_reflects_the_length(float minutes, string expected)
        => Assert.Equal(expected, SceneClock.CycleLabel(minutes));
}
