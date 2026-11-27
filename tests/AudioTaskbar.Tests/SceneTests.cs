namespace AudioTaskbar.Tests;

/// <summary>3. Day/night clock, sun/moon arc bounds, train progress, zero-size / extreme bars.</summary>
[Collection("Statics")]
public class SceneTests
{
    private static AppConfig Cycle(float minutes = 30, float offset = 0)
    {
        var c = new AppConfig { TimeMode = "cycle", DayCycleMinutes = minutes, DayCycleOffsetHours = offset };
        c.Normalize();
        return c;
    }

    [Fact]
    public void Virtual_hour_stays_in_0_to_24_over_a_long_run()
    {
        var cfg = Cycle();
        var t = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Local);
        for (int i = 0; i < 20000; i++)
        {
            float h = SceneClock.VirtualHour(cfg, t.AddSeconds(i * 7.3));
            Assert.InRange(h, 0f, 23.99999f);
            Assert.True(float.IsFinite(h));
        }
    }

    [Fact]
    public void Thirty_minute_cycle_wraps_exactly()
    {
        var cfg = Cycle(30);
        var t = new DateTime(2026, 11, 27, 10, 0, 0);
        float a = SceneClock.VirtualHour(cfg, t), b = SceneClock.VirtualHour(cfg, t.AddMinutes(30)), c = SceneClock.VirtualHour(cfg, t.AddMinutes(60));
        Assert.Equal(a, b, 2);
        Assert.Equal(a, c, 2);
        // a quarter of the period is 6 virtual hours
        float q = SceneClock.VirtualHour(cfg, t.AddMinutes(7.5));
        Assert.Equal(((a + 6f) % 24f), q, 1);
    }

    [Fact]
    public void Dates_before_1970_do_not_go_negative()
    {
        var cfg = Cycle();
        float h = SceneClock.VirtualHour(cfg, new DateTime(1900, 1, 1));
        Assert.InRange(h, 0f, 24f);
    }

    [Fact]
    public void Real_mode_uses_wall_clock()
    {
        var cfg = new AppConfig { TimeMode = "real" }; cfg.Normalize();
        Assert.Equal(13.5f, SceneClock.VirtualHour(cfg, new DateTime(2026, 1, 1, 13, 30, 0)), 3);
    }

    [Theory]
    [InlineData(25f, 1f)]
    [InlineData(-1f, 23f)]
    [InlineData(48f, 0f)]
    [InlineData(12f, 12f)]
    public void Hour_override_wraps(float input, float expected)
    {
        try
        {
            SceneClock.HourOverride = input;
            Assert.Equal(expected, SceneClock.VirtualHour(Cycle(), DateTime.Now), 3);
        }
        finally { SceneClock.HourOverride = null; }
    }

    [Fact]
    public void Every_hour_has_a_phase()
    {
        for (float h = 0; h < 24; h += 0.25f)
            Assert.True(Enum.IsDefined(SceneClock.PhaseAtHour(h)));
        Assert.Equal(DayPhase.Night, SceneClock.PhaseAtHour(0));
        Assert.Equal(DayPhase.Day, SceneClock.PhaseAtHour(12));
    }

    [Fact]
    public void Sun_and_moon_arc_parameters()
    {
        Assert.Equal(0f, SceneClock.SunArcT(SceneClock.SunRise), 3);
        Assert.Equal(1f, SceneClock.SunArcT(SceneClock.SunSet), 3);
        Assert.InRange(SceneClock.SunArcT(12f), 0.4f, 0.6f);
        Assert.Equal(0.5f, SceneClock.MoonArcT(0f), 2);
        for (float h = 0; h < 24; h += 0.1f)
        {
            if (h < SceneClock.SunRise || h > SceneClock.SunSet) Assert.InRange(SceneClock.MoonArcT(h), -0.001f, 1.001f);
        }
    }

    [Theory]
    [InlineData(1920, 48)]
    [InlineData(2400, 60)]   // 125 %
    [InlineData(2880, 72)]   // 150 %
    [InlineData(5760, 144)]  // 4K @ 300 %
    [InlineData(800, 30)]
    [InlineData(64, 16)]
    public void Sun_and_moon_stay_inside_the_bar(int w, int h)
    {
        var d = new DioramaRenderer();
        d.Ensure(w, h);
        foreach (float r in new[] { 3f, 5f, h * 0.12f })
            for (float t = -0.2f; t <= 1.2f; t += 0.01f)
            {
                var (x, y, e) = d.ArcPoint(t, r);
                Assert.True(float.IsFinite(x) && float.IsFinite(y));
                Assert.InRange(e, 0f, 1f);
                if (t >= 0f && t <= 1f)
                {
                    // visible part of the arc: always inside the bar
                    Assert.InRange(x, 0f, w);
                    Assert.InRange(y, 0f, h);
                }
                else
                {
                    // before rise / after set: below the horizon (hidden), never far off-screen
                    Assert.Equal(0f, e);
                    Assert.InRange(x, -0.05f * w, 1.05f * w);
                    Assert.True(y >= d.GroundTop);
                }
            }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-50, -10)]
    [InlineData(2, 48)]
    public void Zero_or_negative_size_bars_do_not_crash(int w, int h)
    {
        var d = new DioramaRenderer();
        d.Ensure(w, h);
        var (x, y, _) = d.ArcPoint(0.5f, 3f);
        Assert.True(float.IsFinite(x) && float.IsFinite(y));
    }

    [Theory]
    [InlineData(0.0, 0.0, 60.0)]
    [InlineData(0.37, 1e9, 60.0)]
    [InlineData(0.9, 123456.789, 15.0)]
    [InlineData(double.NaN, 10, 60)]
    [InlineData(0, double.PositiveInfinity, 60)]
    [InlineData(0, 10, 0)]
    [InlineData(0, 10, -5)]
    [InlineData(0, 10, double.NaN)]
    [InlineData(-3.2, -999, 60)]
    public void Train_progress_is_always_in_0_1(double phase, double time, double period)
    {
        double p = SceneMath.TrainProgress(phase, time, period);
        Assert.False(double.IsNaN(p));
        Assert.InRange(p, 0.0, 0.999999999);
    }

    [Fact]
    public void Train_moves_smoothly_and_wraps_once_per_period()
    {
        double prev = SceneMath.TrainProgress(0.2, 0, 60), wraps = 0;
        for (int f = 1; f <= 60 * 30; f++)
        {
            double p = SceneMath.TrainProgress(0.2, f / 30.0, 60);
            double dp = p - prev;
            if (dp < 0) { wraps++; dp += 1; }
            Assert.InRange(dp, 0, 0.01);
            prev = p;
        }
        Assert.Equal(1, wraps);
    }

    [Theory]
    [InlineData("snow", 12f)]
    [InlineData("snow", 0f)]
    [InlineData("rain", 17.5f)]
    [InlineData("storm", 6f)]
    [InlineData("night", 23f)]
    [InlineData("live", 12f)]
    public void Scene_mood_is_finite_for_every_preview(string preview, float hour)
    {
        try
        {
            SceneClock.HourOverride = hour;
            var cfg = new AppConfig { WeatherPreview = preview }; cfg.Normalize();
            var mood = SceneClock.Compose(null, cfg, DateTime.Now);
            Assert.InRange(mood.Hour, 0f, 24f);
            Assert.InRange(mood.DayK, 0f, 1f);
            Assert.InRange(mood.StarsK, 0f, 1f);
            Assert.InRange(mood.LightsK, 0f, 1f);
            Assert.InRange(mood.Cloudiness, 0f, 1f);
            Assert.InRange(mood.RainAmt, 0f, 1f);
            Assert.True(float.IsFinite(mood.SunX));
            if (preview == "snow") Assert.Equal(WeatherKind.Snow, mood.Kind);
        }
        finally { SceneClock.HourOverride = null; }
    }

    [Theory]
    [InlineData("hcmc", "snow", "12", "1920x48")]
    [InlineData("baria-vungtau", "rain", "18.2", "1920x48")]
    [InlineData("hanoi", "storm", "2", "2400x60")]
    [InlineData("tokyo", "snow", "23", "2880x72")]
    [InlineData("paris", "night", "4", "1366x40")]
    [InlineData("default", "live", "9", "1920x48")]
    [InlineData("hcmc", "sunny", "12", "300x24")]
    public void Full_frame_renders_without_exceptions(string region, string preview, string hour, string size)
    {
        using var d = new TempDir();
        var png = d.File("frame.png");
        try
        {
            RenderPreview.Run(new[] { "--render", png, "--size", size, "--region", region, "--preview", preview, "--hour", hour,
                                      "--frames", "4", "--cutouts", "10-60,700-1200,1500-1900", "--code", "73" });
        }
        finally { SceneClock.HourOverride = null; }
        Assert.True(new FileInfo(png).Length > 100);
        using var bmp = new Bitmap(png);
        var wh = size.Split('x');
        Assert.Equal(int.Parse(wh[0]), bmp.Width);
        Assert.Equal(int.Parse(wh[1]), bmp.Height);
    }
}
