namespace AudioTaskbar.Tests;

/// <summary>Season modes (fixed, "Tự đổi mùa", "Thực tế") and the "Ngẫu nhiên" weather (v5.4.2), season-aware.</summary>
[Collection("Statics")]
public class SeasonWeatherCycleTests
{
    private static readonly DateTime T0 = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Spring", 0, "Spring")]
    [InlineData("Spring", 9.99, "Spring")]
    [InlineData("Spring", 10, "Summer")]
    [InlineData("Spring", 25, "Autumn")]
    [InlineData("Spring", 39.9, "Winter")]
    [InlineData("Spring", 30, "Winter")]
    [InlineData("Spring", 40, "Spring")]   // full year wraps
    [InlineData("Winter", 10, "Spring")]
    [InlineData("Autumn", 400, "Autumn")]  // 40 steps = 10 years
    public void Season_advances_every_period_and_wraps(string start, double minutes, string expected)
        => Assert.Equal(Seasons.Parse(expected), Seasons.At(Seasons.Parse(start), minutes, 10f));

    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    [InlineData(float.NaN)]
    public void Invalid_period_keeps_the_start_season(float period)
        => Assert.Equal(SeasonId.Autumn, Seasons.At(SeasonId.Autumn, 100, period));

    [Fact]
    public void Season_next_order_is_spring_summer_autumn_winter()
    {
        Assert.Equal(SeasonId.Summer, Seasons.Next(SeasonId.Spring));
        Assert.Equal(SeasonId.Autumn, Seasons.Next(SeasonId.Summer));
        Assert.Equal(SeasonId.Winter, Seasons.Next(SeasonId.Autumn));
        Assert.Equal(SeasonId.Spring, Seasons.Next(SeasonId.Winter));
    }

    private static readonly DateTime Jan = new(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private const double Paris = 48.86, Sydney = -33.87, Hcmc = 10.78, Hanoi = 21.03;

    [Fact]
    public void Fixed_season_is_returned_as_is()
    {
        var c = new AppConfig { Season = "autumn" }; c.Normalize();
        var cy = new SeasonCycler();
        Assert.Equal(SeasonId.Autumn, cy.Current(T0, c, Paris));
        Assert.Equal(SeasonId.Autumn, cy.Current(T0.AddDays(90), c, Sydney));
    }

    [Fact]
    public void Auto_cycle_starts_at_the_real_season_and_walks_through_the_year()
    {
        var c = new AppConfig { Season = "cycle", SeasonCycleMinutes = 5 }; c.Normalize();
        Assert.Equal(Seasons.Cycle, c.Season);
        var cy = new SeasonCycler();
        Assert.Equal(SeasonId.Winter, cy.Current(Jan, c, Paris));        // January in Paris
        Assert.Equal(SeasonId.Winter, cy.Current(Jan.AddMinutes(4.9), c, Paris));
        Assert.Equal(SeasonId.Spring, cy.Current(Jan.AddMinutes(5.1), c, Paris));
        Assert.Equal(SeasonId.Summer, cy.Current(Jan.AddMinutes(10.5), c, Paris));
        Assert.Equal(SeasonId.Winter, cy.Current(Jan.AddMinutes(20.5), c, Paris));
        c.SeasonCycleMinutes = 60;                                       // new period: restart from the real season
        Assert.Equal(SeasonId.Winter, cy.Current(Jan.AddMinutes(21), c, Paris));
        Assert.Equal(SeasonId.Winter, cy.Current(Jan.AddMinutes(80), c, Paris));
        Assert.Equal(SeasonId.Spring, cy.Current(Jan.AddMinutes(81.5), c, Paris));
    }

    [Theory]
    // northern temperate (Hà Nội, Tokyo, Paris, New York): meteorological seasons
    [InlineData(Hanoi, 1, "Winter")] [InlineData(Hanoi, 3, "Spring")] [InlineData(Hanoi, 5, "Spring")] [InlineData(Hanoi, 6, "Summer")]
    [InlineData(Hanoi, 8, "Summer")] [InlineData(Hanoi, 9, "Autumn")] [InlineData(Hanoi, 11, "Autumn")] [InlineData(Hanoi, 12, "Winter")]
    [InlineData(Paris, 7, "Summer")] [InlineData(Paris, 2, "Winter")]
    // southern hemisphere: six months later
    [InlineData(Sydney, 1, "Summer")] [InlineData(Sydney, 4, "Autumn")] [InlineData(Sydney, 7, "Winter")] [InlineData(Sydney, 10, "Spring")]
    // tropics (TP.HCM, Vũng Tàu, Đà Nẵng): Tết spring, hot season, rainy/cool season — never winter
    [InlineData(Hcmc, 1, "Spring")] [InlineData(Hcmc, 2, "Spring")] [InlineData(Hcmc, 3, "Summer")] [InlineData(Hcmc, 7, "Summer")]
    [InlineData(Hcmc, 8, "Autumn")] [InlineData(Hcmc, 12, "Autumn")]
    [InlineData(-6.2, 7, "Spring")] [InlineData(-6.2, 1, "Summer")]   // Jakarta: southern tropics, shifted
    public void Real_season_follows_date_and_hemisphere(double lat, int month, string expected)
        => Assert.Equal(Seasons.Parse(expected), Seasons.RealAt(new DateTime(2027, month, 15), lat));

    [Fact]
    public void Tropics_never_get_winter_and_real_mode_uses_the_scene_latitude()
    {
        for (int m = 1; m <= 12; m++)
        {
            Assert.NotEqual(SeasonId.Winter, Seasons.RealAt(new DateTime(2027, m, 15), Hcmc));
            Assert.NotEqual(SeasonId.Winter, Seasons.RealAt(new DateTime(2027, m, 15), -Hcmc));
        }
        Assert.Equal(SeasonId.Spring, Seasons.RealAt(new DateTime(2027, 1, 15), double.NaN));   // unknown → TP.HCM
        var c = new AppConfig { Season = "real" }; c.Normalize();
        Assert.Equal(Seasons.Real, c.Season);
        var cy = new SeasonCycler();
        Assert.Equal(SeasonId.Summer, cy.Current(new DateTime(2027, 7, 15, 12, 0, 0, DateTimeKind.Utc), c, Paris));
        Assert.Equal(SeasonId.Winter, cy.Current(new DateTime(2027, 7, 15, 12, 0, 0, DateTimeKind.Utc), c, Sydney));
        // the scene latitude: a picked region's city, else the detected location, else TP.HCM
        Assert.Equal(-33.8688, Seasons.SceneLatitude(RegionCatalog.Sydney, null), 3);
        Assert.Equal(52.5, Seasons.SceneLatitude(RegionCatalog.Default, new WeatherSnapshot { Ok = true, Latitude = 52.5 }), 3);
        Assert.Equal(Seasons.FallbackLatitude, Seasons.SceneLatitude(RegionCatalog.Default, null), 3);
    }

    [Theory]
    [InlineData("cycle", "cycle")] [InlineData("Tự đổi mùa", "cycle")] [InlineData("real", "real")] [InlineData("Thực tế", "real")]
    [InlineData("winter", "winter")] [InlineData("Mùa thu", "autumn")] [InlineData("rome?", "summer")] [InlineData(null, "summer")]
    public void Season_modes_normalize(string? input, string expected) => Assert.Equal(expected, Seasons.NormalizeMode(input));

    [Fact]
    public void Pool_has_snow_only_in_winter()
    {
        foreach (var s in Seasons.All)
            Assert.Equal(s == SeasonId.Winter, WeatherCycle.InPool(s, "snow"));
        foreach (var s in Seasons.All)
            Assert.All(WeatherCycle.Pool(s), p => Assert.True(p.weight > 0));
    }

    [Fact]
    public void A_fixed_weather_is_never_overridden()
    {
        var c = new AppConfig { WeatherPreview = "rain" }; c.Normalize();
        var s = new WeatherScheduler(new Random(1));
        Assert.Null(s.Current(T0, c, SeasonId.Summer));
        Assert.Null(s.Current(T0.AddHours(5), c, SeasonId.Summer));
        Assert.Equal("rain", c.EffectiveWeather);
    }

    [Fact]
    public void Random_weather_catches_up_after_a_long_gap_without_looping_forever()
    {
        var c = new AppConfig { WeatherPreview = "random", WeatherCycleRandomMinMinutes = 1, WeatherCycleRandomMaxMinutes = 1 }; c.Normalize();
        var s = new WeatherScheduler(new Random(3));
        s.Current(T0, c, SeasonId.Summer);
        var r = s.Current(T0.AddDays(2), c, SeasonId.Summer);
        Assert.NotNull(r);
        Assert.True(s.NextChangeUtc > T0.AddDays(2));
        // until the first pick the scene shows the live weather, never the literal "random"
        Assert.Equal("live", new AppConfig { WeatherPreview = "random" }.EffectiveWeather);
    }

    [Fact]
    public void Random_mode_never_repeats_the_same_weather_twice_in_a_row()
    {
        var c = new AppConfig { WeatherPreview = "random", WeatherCycleRandomMinMinutes = 1, WeatherCycleRandomMaxMinutes = 1 }; c.Normalize();
        var s = new WeatherScheduler(new Random(99));
        string? prev = null;
        for (int i = 0; i < 500; i++)
        {
            var w = s.Current(T0.AddMinutes(i * 1.0), c, SeasonId.Summer);
            Assert.NotNull(w);
            if (prev is not null && i > 0) Assert.NotEqual(prev, w);
            prev = w;
        }
    }

    [Fact]
    public void Random_mode_waits_a_random_time_inside_the_configured_range()
    {
        var c = new AppConfig { WeatherPreview = "random", WeatherCycleRandomMinMinutes = 3, WeatherCycleRandomMaxMinutes = 8 }; c.Normalize();
        var rng = new Random(5);
        var s = new WeatherScheduler(rng);
        var waits = new List<double>();
        s.Current(T0, c, SeasonId.Autumn);
        var prevNext = s.NextChangeUtc;
        waits.Add((prevNext - T0).TotalMinutes);
        for (int i = 0; i < 60; i++)
        {
            s.Current(prevNext.AddSeconds(1), c, SeasonId.Autumn);
            waits.Add((s.NextChangeUtc - prevNext).TotalMinutes);
            prevNext = s.NextChangeUtc;
        }
        Assert.All(waits, w => Assert.InRange(w, 2.99, 8.01));
        Assert.True(waits.Max() - waits.Min() > 1.5, "waits should vary");
    }

    [Fact]
    public void Random_mode_only_picks_snow_in_winter()
    {
        var c = new AppConfig { WeatherPreview = "random", WeatherCycleRandomMinMinutes = 0.5f, WeatherCycleRandomMaxMinutes = 0.5f }; c.Normalize();
        foreach (var season in Seasons.All)
        {
            var s = new WeatherScheduler(new Random(11));
            bool snow = false;
            for (int i = 0; i < 400; i++) snow |= s.Current(T0.AddMinutes(i * 0.6), c, season) == "snow";
            Assert.Equal(season == SeasonId.Winter, snow);
        }
    }

    [Fact]
    public void Weather_that_no_longer_fits_the_season_is_replaced_at_once()
    {
        var c = new AppConfig { WeatherPreview = "random", WeatherCycleRandomMinMinutes = 60, WeatherCycleRandomMaxMinutes = 60 }; c.Normalize();
        var s = new WeatherScheduler(new Random(2));
        string? w = null;
        for (int i = 0; i < 50 && w != "snow"; i++) { s.Reset(); w = s.Current(T0, c, SeasonId.Winter); }
        Assert.Equal("snow", w);
        var r = s.Current(T0.AddSeconds(5), c, SeasonId.Spring);   // winter ended
        Assert.NotEqual("snow", r);
        Assert.True(WeatherCycle.InPool(SeasonId.Spring, r!));
    }

    [Fact]
    public void Picking_a_fixed_weather_stops_the_random_weather()
    {
        var c = new AppConfig { WeatherPreview = "random" }; c.Normalize();
        var s = new WeatherScheduler(new Random(4));
        Assert.NotNull(s.Current(T0, c, SeasonId.Summer));
        c.ApplyWeatherOverrideSelection("storm");
        Assert.Null(s.Current(T0.AddMinutes(1), c, SeasonId.Summer));
        Assert.Equal("storm", c.EffectiveWeather);
        c.ApplyWeatherOverrideSelection("Ngẫu nhiên");
        Assert.Equal("random", c.WeatherPreview);
        Assert.NotNull(s.Current(T0.AddMinutes(2), c, SeasonId.Summer));
    }

    [Theory]
    [InlineData("interval", 7f, 7f, 7f)]       // v5.4 "Theo khoảng thời gian" 7 min → Ngẫu nhiên changing every 7 min
    [InlineData("random", 0f, 4f, 9f)]          // v5.4 "Ngẫu nhiên" keeps its range
    public void Legacy_weather_cycle_migrates_to_the_random_entry(string mode, float interval, float lo, float hi)
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        File.WriteAllText(p, "{ \"configVersion\": 55, \"weatherPreview\": \"rain\", \"weatherCycleMode\": \"" + mode + "\", \"weatherCycleMinutes\": " +
            interval.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"weatherCycleRandomMinMinutes\": 4, \"weatherCycleRandomMaxMinutes\": 9 }");
        var c = ConfigStore.Load(p);
        Assert.Equal("random", c.WeatherPreview);
        Assert.Equal(lo, c.WeatherCycleRandomMinMinutes);
        Assert.Equal(hi, c.WeatherCycleRandomMaxMinutes);
        var json = File.ReadAllText(p);
        Assert.DoesNotContain("weatherCycleMode", json);
        Assert.DoesNotContain("seasonCycleEnabled", json);
        var off = d.File("off.json");
        File.WriteAllText(off, "{ \"configVersion\": 55, \"weatherPreview\": \"rain\", \"weatherCycleMode\": \"off\" }");
        Assert.Equal("rain", ConfigStore.Load(off).WeatherPreview);
    }

    [Theory]
    [InlineData(true, "winter", "cycle")]
    [InlineData(false, "winter", "winter")]
    public void Legacy_season_checkbox_migrates_to_the_cycle_entry(bool enabled, string season, string expected)
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        File.WriteAllText(p, "{ \"configVersion\": 55, \"season\": \"" + season + "\", \"seasonCycleEnabled\": " + (enabled ? "true" : "false") + ", \"seasonCycleMinutes\": 12 }");
        var c = ConfigStore.Load(p);
        Assert.Equal(expected, c.Season);
        Assert.False(c.SeasonCycleEnabled);
        Assert.Equal(12f, c.SeasonCycleMinutes);
    }

    [Theory]
    [InlineData("interval", "interval")]
    [InlineData("Theo khoảng thời gian", "interval")]
    [InlineData("random", "random")]
    [InlineData("Ngẫu nhiên", "random")]
    [InlineData("whatever", "off")]
    [InlineData(null, "off")]
    public void Mode_names_normalize(string? input, string expected) => Assert.Equal(expected, WeatherCycle.NormalizeMode(input));

    [Fact]
    public void Compose_uses_the_runtime_weather_and_season_when_set()
    {
        SceneClock.HourOverride = 12f;
        try
        {
            var c = new AppConfig { WeatherPreview = "sunny", Season = "summer" }; c.Normalize();
            var now = new DateTime(2026, 6, 1, 12, 0, 0);
            var sunny = SceneClock.Compose(null, c, now);
            Assert.Equal(WeatherKind.Clear, sunny.Kind);
            Assert.Equal(SeasonId.Summer, sunny.Season);
            c.RuntimeWeather = "storm"; c.RuntimeSeason = "winter";
            var stormy = SceneClock.Compose(null, c, now);
            Assert.True(stormy.IsStorm);
            Assert.Equal(SeasonId.Winter, stormy.Season);
        }
        finally { SceneClock.HourOverride = null; }
    }
}
