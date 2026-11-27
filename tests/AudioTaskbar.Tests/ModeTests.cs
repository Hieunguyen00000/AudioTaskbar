namespace AudioTaskbar.Tests;

/// <summary>v5.3: fixed day / night time modes and always-on wallpaper blocking.</summary>
[Collection("Statics")]
public class ModeTests
{
    [Theory]
    [InlineData("cycle", "cycle")]
    [InlineData("real", "real")]
    [InlineData("day", "day")]
    [InlineData("Cố định ban ngày", "day")]
    [InlineData("ngay", "day")]
    [InlineData("night", "night")]
    [InlineData("Cố định ban đêm", "night")]
    [InlineData("dem", "night")]
    [InlineData("clock", "real")]
    [InlineData("whatever", "cycle")]
    [InlineData(null, "cycle")]
    public void Time_mode_aliases(string? input, string expected)
    {
        var c = new AppConfig { TimeMode = input! };
        c.Normalize();
        Assert.Equal(expected, c.TimeMode);
    }

    private static AppConfig Mode(string m) { var c = new AppConfig { TimeMode = m }; c.Normalize(); return c; }

    [Fact]
    public void Fixed_day_stays_in_late_morning_with_the_sun_up()
    {
        var cfg = Mode("day");
        var t = new DateTime(2026, 11, 27, 22, 0, 0);
        for (int i = 0; i < 5000; i++)
        {
            float h = SceneClock.VirtualHour(cfg, t.AddSeconds(i * 13.7));
            Assert.InRange(h, SceneClock.FixedDayFrom, SceneClock.FixedDayTo);
            Assert.Equal(DayPhase.Day, SceneClock.PhaseAtHour(h));
            Assert.InRange(SceneClock.SunArcT(h), 0.3f, 0.6f);
        }
    }

    [Fact]
    public void Fixed_night_stays_near_midnight_with_the_moon_up()
    {
        var cfg = Mode("night");
        var t = new DateTime(2026, 11, 27, 12, 0, 0);
        for (int i = 0; i < 5000; i++)
        {
            float h = SceneClock.VirtualHour(cfg, t.AddSeconds(i * 13.7));
            Assert.InRange(h, SceneClock.FixedNightFrom, 23.999f);
            Assert.Equal(DayPhase.Night, SceneClock.PhaseAtHour(h));
            Assert.InRange(SceneClock.MoonArcT(h), 0.4f, 0.55f);
        }
    }

    [Fact]
    public void Fixed_modes_ignore_the_wall_clock()
    {
        var day = Mode("day"); var night = Mode("night");
        foreach (var hour in new[] { 0, 3, 6, 12, 18, 21, 23 })
        {
            var t = new DateTime(2026, 11, 27, hour, 15, 0);
            Assert.True(SceneClock.VirtualHour(day, t) is >= 10.5f and <= 12f);
            Assert.True(SceneClock.VirtualHour(night, t) >= 23.5f);
        }
    }

    [Fact]
    public void Fixed_day_and_night_scene_moods()
    {
        SceneClock.HourOverride = null;
        var d = SceneClock.Compose(null, Mode("day"), DateTime.Now);
        var n = SceneClock.Compose(null, Mode("night"), DateTime.Now);
        Assert.True(d.DayK > 0.8f, "daytime sky");
        Assert.True(d.StarsK < 0.1f);
        Assert.True(n.DayK < 0.1f, "night sky");
        Assert.True(n.StarsK > 0.5f, "stars visible");
        Assert.True(n.LightsK > 0.5f, "city lights on");
    }

    [Theory]
    [InlineData("day", "12")]
    [InlineData("night", "0")]
    public void Fixed_modes_render(string mode, string _)
    {
        using var dir = new TempDir();
        var png = dir.File("f.png");
        try { RenderPreview.Run(new[] { "--render", png, "--size", "1920x48", "--timemode", mode, "--frames", "3", "--region", "hcmc" }); }
        finally { SceneClock.HourOverride = null; }
        Assert.True(new FileInfo(png).Length > 100);
    }

    // ---- wallpaper blocking is not user-configurable ----
    [Theory]
    [InlineData(false, 0.5f)]
    [InlineData(false, 0f)]
    [InlineData(true, 0.7f)]
    [InlineData(false, float.NaN)]
    public void Wallpaper_blocking_is_always_on_and_opaque(bool block, float opacity)
    {
        var c = new AppConfig { BlockWallpaper = block, BackdropOpacity = opacity };
        c.Normalize();
        Assert.True(c.BlockWallpaper);
        Assert.Equal(1f, c.BackdropOpacity);
    }

    [Fact]
    public void Config_file_cannot_turn_wallpaper_blocking_off()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{\"configVersion\":51,\"blockWallpaper\":false,\"backdropOpacity\":0.5}");
        var cfg = ConfigStore.Load(path);
        Assert.True(cfg.BlockWallpaper);
        Assert.Equal(1f, cfg.BackdropOpacity);
        ConfigStore.Save(path, cfg);
        Assert.Contains("\"blockWallpaper\": true", File.ReadAllText(path));
    }
}
