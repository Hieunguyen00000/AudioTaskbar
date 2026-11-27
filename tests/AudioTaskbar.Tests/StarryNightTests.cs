namespace AudioTaskbar.Tests;

/// <summary>v5.3.2: picking "Đêm sao" switches the clock to "Cố định ban đêm"; other weathers keep the user's mode.</summary>
[Collection("Statics")]
public class StarryNightTests
{
    private static AppConfig With(string timeMode, string preview = "live")
    {
        var c = new AppConfig { TimeMode = timeMode, WeatherPreview = preview, DayCycleMinutes = 45 };
        c.Normalize();
        return c;
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("real")]
    [InlineData("day")]
    [InlineData("night")]
    public void Starry_night_forces_fixed_night_from_any_mode(string prior)
    {
        var c = With(prior);
        bool changed = c.ApplyWeatherOverrideSelection("night");
        Assert.Equal("night", c.WeatherPreview);
        Assert.Equal("night", c.TimeMode);
        Assert.Equal(prior != "night", changed);
        Assert.Equal(45f, c.DayCycleMinutes); // the custom cycle length is kept for later
    }

    [Theory]
    [InlineData("Đêm sao")]
    [InlineData("dem sao")]
    [InlineData("đêm")]
    [InlineData("NIGHT")]
    public void Vietnamese_and_mixed_case_aliases_work(string alias)
    {
        var c = With("day");
        c.ApplyWeatherOverrideSelection(alias);
        Assert.Equal("night", c.TimeMode);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("real")]
    [InlineData("day")]
    [InlineData("night")]
    public void Starry_night_renders_a_night_sky_with_stars(string prior)
    {
        SceneClock.HourOverride = null;
        var c = With(prior);
        c.ApplyWeatherOverrideSelection("night");
        foreach (var hour in new[] { 0, 6, 9, 12, 15, 18, 21 })
        {
            var t = new DateTime(2026, 11, 27, hour, 7, 0);
            float h = SceneClock.VirtualHour(c, t);
            Assert.Equal(DayPhase.Night, SceneClock.PhaseAtHour(h));
            var mood = SceneClock.Compose(null, c, t);
            Assert.True(mood.StarsK > 0.5f, $"stars at {hour}h: {mood.StarsK}");
            Assert.True(mood.DayK < 0.1f);
        }
    }

    [Theory]
    [InlineData("live")]
    [InlineData("sunny")]
    [InlineData("cloudy")]
    [InlineData("rain")]
    [InlineData("storm")]
    [InlineData("snow")]
    [InlineData("garbage")]
    public void Other_weathers_keep_the_users_time_mode(string weather)
    {
        foreach (var prior in new[] { "cycle", "real", "day", "night" })
        {
            var c = With(prior);
            bool changed = c.ApplyWeatherOverrideSelection(weather);
            Assert.False(changed);
            Assert.Equal(prior, c.TimeMode);
            Assert.NotEqual("night", c.WeatherPreview);
        }
    }

    [Fact]
    public void Leaving_starry_night_does_not_flip_the_clock_back_or_lock_it()
    {
        var c = With("cycle");
        c.ApplyWeatherOverrideSelection("night");
        c.ApplyWeatherOverrideSelection("sunny");
        Assert.Equal("night", c.TimeMode);      // stays where "Đêm sao" put it…
        c.TimeMode = "cycle"; c.Normalize();    // …and the user can pick another mode freely
        Assert.Equal("cycle", c.TimeMode);
        c.WeatherPreview = "night"; c.Normalize(); // Normalize alone never forces the clock
        Assert.Equal("cycle", c.TimeMode);
    }

    [Fact]
    public void Null_selection_is_live_and_harmless()
    {
        var c = With("day");
        Assert.False(c.ApplyWeatherOverrideSelection(null));
        Assert.Equal("live", c.WeatherPreview);
        Assert.Equal("day", c.TimeMode);
    }

    [Fact]
    public void Starry_night_round_trips_through_config_json()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        var c = With("real");
        c.ApplyWeatherOverrideSelection("Đêm sao");
        ConfigStore.Save(path, c);
        var back = ConfigStore.Load(path);
        Assert.Equal("night", back.WeatherPreview);
        Assert.Equal("night", back.TimeMode);
    }

    [Fact]
    public void Version_is_at_least_5_3_2()
    {
        Assert.True(SemVer.TryParse(AutoUpdater.AppVersion(), out var v));
        Assert.True(v.CompareTo(new SemVer(5, 3, 2, "")) >= 0, v.ToString());
    }

    [Fact]
    public void Portal_source_keeps_hieus_constraints()
    {
        var src = FindSource("src/PortalForm.cs");
        Assert.DoesNotContain("github.com", src);
        Assert.DoesNotContain("UpdateRepo", src);
        Assert.DoesNotContain("Mở thư mục cấu hình", src);
        Assert.DoesNotContain("Hòa nền phía sau icon (TAP)", src);
        Assert.DoesNotContain("Chặn ảnh nền", src);
        Assert.Contains("ApplyWeatherOverrideSelection", src);
        Assert.Contains("ApplyWeatherOverrideSelection", FindSource("src/TrayApp.cs"));
        Assert.DoesNotContain("Chặn ảnh nền desktop", FindSource("src/TrayApp.cs"));
    }

    private static string FindSource(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, rel))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, rel));
    }
}
