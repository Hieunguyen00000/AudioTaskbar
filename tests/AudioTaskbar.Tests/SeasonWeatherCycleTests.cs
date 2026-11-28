namespace AudioTaskbar.Tests;

/// <summary>v5.4: automatic season cycle and automatic weather cycle (interval or random), season-aware.</summary>
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

    [Fact]
    public void Cycler_off_returns_the_configured_season()
    {
        var c = new AppConfig { Season = "autumn", SeasonCycleEnabled = false }; c.Normalize();
        var cy = new SeasonCycler();
        Assert.Equal(SeasonId.Autumn, cy.Current(T0, c));
        Assert.Equal(SeasonId.Autumn, cy.Current(T0.AddDays(30), c));
    }

    [Fact]
    public void Cycler_walks_through_the_seasons_from_the_configured_one()
    {
        var c = new AppConfig { Season = "winter", SeasonCycleEnabled = true, SeasonCycleMinutes = 5 }; c.Normalize();
        var cy = new SeasonCycler();
        Assert.Equal(SeasonId.Winter, cy.Current(T0, c));
        Assert.Equal(SeasonId.Winter, cy.Current(T0.AddMinutes(4.9), c));
        Assert.Equal(SeasonId.Spring, cy.Current(T0.AddMinutes(5.1), c));
        Assert.Equal(SeasonId.Summer, cy.Current(T0.AddMinutes(10.5), c));
        Assert.Equal(SeasonId.Winter, cy.Current(T0.AddMinutes(20.5), c));
    }

    [Fact]
    public void Cycler_restarts_when_the_user_picks_a_season_or_changes_the_period()
    {
        var c = new AppConfig { Season = "spring", SeasonCycleEnabled = true, SeasonCycleMinutes = 5 }; c.Normalize();
        var cy = new SeasonCycler();
        cy.Current(T0, c);
        Assert.Equal(SeasonId.Summer, cy.Current(T0.AddMinutes(6), c));
        c.Season = "winter";                                    // the user picks winter (e.g. by choosing snow)
        Assert.Equal(SeasonId.Winter, cy.Current(T0.AddMinutes(6.1), c));
        Assert.Equal(SeasonId.Winter, cy.Current(T0.AddMinutes(10), c));
        Assert.Equal(SeasonId.Spring, cy.Current(T0.AddMinutes(11.2), c));
        c.SeasonCycleMinutes = 60;                              // new period: restart from the configured season
        Assert.Equal(SeasonId.Winter, cy.Current(T0.AddMinutes(12), c));
    }

    [Fact]
    public void Pool_has_snow_only_in_winter()
    {
        foreach (var s in Seasons.All)
            Assert.Equal(s == SeasonId.Winter, WeatherCycle.InPool(s, "snow"));
        foreach (var s in Seasons.All)
            Assert.All(WeatherCycle.Pool(s), p => Assert.True(p.weight > 0));
    }

    [Fact]
    public void Weather_cycle_off_never_overrides()
    {
        var c = new AppConfig { WeatherCycleMode = "off", WeatherPreview = "rain" }; c.Normalize();
        var s = new WeatherScheduler(new Random(1));
        Assert.Null(s.Current(T0, c, SeasonId.Summer));
        Assert.Null(s.Current(T0.AddHours(5), c, SeasonId.Summer));
    }

    [Fact]
    public void Interval_mode_starts_from_the_users_weather_and_steps_every_interval()
    {
        var c = new AppConfig { WeatherCycleMode = "interval", WeatherCycleMinutes = 10, WeatherPreview = "cloudy" }; c.Normalize();
        var s = new WeatherScheduler(new Random(7));
        Assert.Equal("cloudy", s.Current(T0, c, SeasonId.Summer));
        Assert.Equal("cloudy", s.Current(T0.AddMinutes(9.9), c, SeasonId.Summer));
        var second = s.Current(T0.AddMinutes(10.1), c, SeasonId.Summer);
        Assert.Equal(WeatherCycle.NextInOrder(SeasonId.Summer, "cloudy"), second);
        Assert.Equal(second, s.Current(T0.AddMinutes(19.9), c, SeasonId.Summer));
        var third = s.Current(T0.AddMinutes(20.2), c, SeasonId.Summer);
        Assert.Equal(WeatherCycle.NextInOrder(SeasonId.Summer, second!), third);
    }

    [Fact]
    public void Interval_mode_visits_every_pool_weather_in_order()
    {
        var c = new AppConfig { WeatherCycleMode = "interval", WeatherCycleMinutes = 1, WeatherPreview = "sunny" }; c.Normalize();
        var s = new WeatherScheduler(new Random(3));
        var seen = new List<string>();
        for (int i = 0; i < 12; i++) seen.Add(s.Current(T0.AddMinutes(i + 0.5), c, SeasonId.Winter)!);
        var pool = WeatherCycle.Pool(SeasonId.Winter).Select(p => p.id).ToArray();
        Assert.Equal(pool.OrderBy(x => x), seen.Distinct().OrderBy(x => x));
        for (int i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
    }

    [Fact]
    public void Interval_mode_catches_up_after_a_long_gap_without_looping_forever()
    {
        var c = new AppConfig { WeatherCycleMode = "interval", WeatherCycleMinutes = 1 }; c.Normalize();
        var s = new WeatherScheduler(new Random(3));
        s.Current(T0, c, SeasonId.Summer);
        var r = s.Current(T0.AddDays(2), c, SeasonId.Summer);
        Assert.NotNull(r);
        Assert.True(s.NextChangeUtc > T0.AddDays(2));
    }

    [Fact]
    public void Random_mode_never_repeats_the_same_weather_twice_in_a_row()
    {
        var c = new AppConfig { WeatherCycleMode = "random", WeatherCycleRandomMinMinutes = 1, WeatherCycleRandomMaxMinutes = 1 }; c.Normalize();
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
        var c = new AppConfig { WeatherCycleMode = "random", WeatherCycleRandomMinMinutes = 3, WeatherCycleRandomMaxMinutes = 8 }; c.Normalize();
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
        var c = new AppConfig { WeatherCycleMode = "random", WeatherCycleRandomMinMinutes = 0.5f, WeatherCycleRandomMaxMinutes = 0.5f }; c.Normalize();
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
        var c = new AppConfig { WeatherCycleMode = "interval", WeatherCycleMinutes = 60, WeatherPreview = "snow" }; c.Normalize();
        var s = new WeatherScheduler(new Random(2));
        Assert.Equal("snow", s.Current(T0, c, SeasonId.Winter));
        var r = s.Current(T0.AddSeconds(5), c, SeasonId.Spring);   // winter ended
        Assert.NotEqual("snow", r);
        Assert.True(WeatherCycle.InPool(SeasonId.Spring, r!));
    }

    [Fact]
    public void Switching_the_mode_or_resetting_restarts_from_the_users_weather()
    {
        var c = new AppConfig { WeatherCycleMode = "interval", WeatherCycleMinutes = 5, WeatherPreview = "sunny" }; c.Normalize();
        var s = new WeatherScheduler(new Random(4));
        Assert.Equal("sunny", s.Current(T0, c, SeasonId.Summer));
        s.Current(T0.AddMinutes(6), c, SeasonId.Summer);
        c.WeatherPreview = "storm";                     // the user picks a weather by hand
        s.Reset();
        Assert.Equal("storm", s.Current(T0.AddMinutes(6.5), c, SeasonId.Summer));
        c.WeatherCycleMode = "off";
        Assert.Null(s.Current(T0.AddMinutes(7), c, SeasonId.Summer));
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
