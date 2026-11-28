using System.Text.Json;

namespace AudioTaskbar.Tests;

/// <summary>v5.4: config migration, defaults (all sound volumes 0), seasons and the snow→winter rule.</summary>
[Collection("Statics")]
public class V54ConfigTests
{
    private static readonly string[] EffectIds = { "rain", "thunder", "wind", "traffic", "horns", "train", "airplane", "birds", "insects", "animals", "waves" };

    // A realistic v5.3.3 config.json: no season / cycle / sound keys at all.
    private const string OldConfig = """
    {
      "configVersion": 51,
      "hotkey": "Ctrl+Alt+F8",
      "regionMode": "tokyo",
      "weatherPreview": "rain",
      "timeMode": "real",
      "trainCrossingMinutes": 2.5,
      "showWeatherHud": true,
      "autoUpdateEnabled": false,
      "startMinimized": true
    }
    """;

    [Fact]
    public void Old_config_migrates_keeps_user_choices_and_starts_silent()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, OldConfig);

        var cfg = ConfigStore.Load(path);

        Assert.Null(ConfigStore.LastLoadError);
        Assert.Equal(AppConfig.CurrentConfigVersion, cfg.ConfigVersion);
        // user choices survive
        Assert.Equal("Ctrl+Alt+F8", cfg.Hotkey);
        Assert.Equal("tokyo", cfg.RegionMode);
        Assert.Equal("rain", cfg.WeatherPreview);
        Assert.Equal("real", cfg.TimeMode);
        Assert.Equal(2.5f, cfg.TrainCrossingMinutes);
        Assert.False(cfg.AutoUpdateEnabled);
        Assert.True(cfg.StartMinimized);
        // new settings: silent, summer (the old look), cycles off, icon padding as before, removed HUD off
        Assert.Equal(0, cfg.SoundMasterVolume);
        Assert.Equal(EffectIds.Length, cfg.SoundVolumes.Count);
        Assert.All(EffectIds, id => Assert.Equal(0, cfg.SoundVolumes[id]));
        Assert.Equal("summer", cfg.Season);
        Assert.False(cfg.SeasonCycleEnabled);
        Assert.Equal("off", cfg.WeatherCycleMode);
        Assert.True(cfg.ShowIconPadding);
        Assert.False(cfg.ShowWeatherHud);

        // the file on disk was upgraded and still loads
        var json = File.ReadAllText(path);
        Assert.Contains("soundVolumes", json);
        Assert.Contains("\"configVersion\": " + AppConfig.CurrentConfigVersion, json);
        var again = ConfigStore.Load(path);
        Assert.Equal(0, again.SoundMasterVolume);
        Assert.Equal("Ctrl+Alt+F8", again.Hotkey);
    }

    [Fact]
    public void Migration_zeroes_stray_sound_values_in_an_older_file()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{ \"configVersion\": 51, \"soundMasterVolume\": 80, \"soundVolumes\": { \"rain\": 90, \"birds\": 40 } }");
        var cfg = ConfigStore.Load(path);
        Assert.Equal(0, cfg.SoundMasterVolume);
        Assert.All(EffectIds, id => Assert.Equal(0, cfg.SoundVolumes[id]));
    }

    [Fact]
    public void Config_without_a_version_number_also_migrates()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{ \"fps\": 45 }");
        var cfg = ConfigStore.Load(path);
        Assert.Equal(45, cfg.Fps);
        Assert.Equal(AppConfig.CurrentConfigVersion, cfg.ConfigVersion);
        Assert.Equal(0, cfg.SoundMasterVolume);
    }

    [Fact]
    public void Current_version_keeps_the_volumes_the_user_saved()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        var c = new AppConfig { ConfigVersion = AppConfig.CurrentConfigVersion, SoundMasterVolume = 60, Season = "autumn", SeasonCycleEnabled = true, SeasonCycleMinutes = 12, WeatherCycleMode = "random", ShowIconPadding = false };
        c.SoundVolumes["rain"] = 35;
        c.SoundVolumes["birds"] = 70;
        ConfigStore.Save(path, c);

        var back = ConfigStore.Load(path);

        Assert.Equal(60, back.SoundMasterVolume);
        Assert.Equal(35, back.SoundVolumes["rain"]);
        Assert.Equal(70, back.SoundVolumes["birds"]);
        Assert.Equal(0, back.SoundVolumes["thunder"]);
        Assert.Equal("autumn", back.Season);
        Assert.True(back.SeasonCycleEnabled);
        Assert.Equal(12f, back.SeasonCycleMinutes);
        Assert.Equal("random", back.WeatherCycleMode);
        Assert.False(back.ShowIconPadding);
    }

    [Fact]
    public void Train_crossing_25_to_1_migration_only_applies_below_v51()
    {
        using var d = new TempDir();
        var p1 = d.File("a.json"); File.WriteAllText(p1, "{ \"configVersion\": 0, \"trainCrossingMinutes\": 25 }");
        Assert.Equal(1f, ConfigStore.Load(p1).TrainCrossingMinutes);
        var p2 = d.File("b.json"); File.WriteAllText(p2, "{ \"configVersion\": 51, \"trainCrossingMinutes\": 25 }");
        Assert.Equal(25f, ConfigStore.Load(p2).TrainCrossingMinutes);
    }

    [Fact]
    public void Fresh_defaults_have_every_volume_at_zero()
    {
        var cfg = new AppConfig(); cfg.Normalize();
        Assert.Equal(0, cfg.SoundMasterVolume);
        Assert.Equal(EffectIds.OrderBy(x => x), cfg.SoundVolumes.Keys.OrderBy(x => x));
        Assert.All(cfg.SoundVolumes.Values, v => Assert.Equal(0, v));
        Assert.Equal(EffectIds.OrderBy(x => x), SoundEffects.Ids.OrderBy(x => x));
        Assert.Equal(0f, SoundScape.Gain(0, 100));
        Assert.Equal(0f, SoundScape.Gain(100, 0));
        Assert.Equal(1f, SoundScape.Gain(100, 100));
        // a brand-new config on disk is silent too
        using var d = new TempDir();
        var (loaded, _, _) = (ConfigStore.Load(d.File("none.json")), 0, 0);
        Assert.Equal(0, loaded.SoundMasterVolume);
    }

    [Fact]
    public void Volumes_are_clamped_and_unknown_effects_dropped()
    {
        var cfg = new AppConfig { SoundMasterVolume = 250 };
        cfg.SoundVolumes = new Dictionary<string, int> { ["rain"] = 999, ["birds"] = -20, ["kazoo"] = 50 };
        cfg.Normalize();
        Assert.Equal(100, cfg.SoundMasterVolume);
        Assert.Equal(100, cfg.SoundVolumes["rain"]);
        Assert.Equal(0, cfg.SoundVolumes["birds"]);
        Assert.False(cfg.SoundVolumes.ContainsKey("kazoo"));
        Assert.Equal(0, cfg.SoundVolumes["wind"]);
    }

    [Fact]
    public void Null_sound_dictionary_in_json_does_not_crash()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{ \"configVersion\": 54, \"soundVolumes\": null, \"season\": null }");
        var cfg = ConfigStore.Load(path);
        Assert.All(cfg.SoundVolumes.Values, v => Assert.Equal(0, v));
        Assert.Equal("summer", cfg.Season);
    }

    [Theory]
    [InlineData("spring", "spring")]
    [InlineData("Mùa xuân", "spring")]
    [InlineData("mua thu", "autumn")]
    [InlineData("FALL", "autumn")]
    [InlineData("đông", "winter")]
    [InlineData("hè", "summer")]
    [InlineData("nonsense", "summer")]
    [InlineData(null, "summer")]
    public void Season_names_normalize(string? input, string expected)
        => Assert.Equal(expected, Seasons.Normalize(input));

    [Fact]
    public void Choosing_snow_switches_the_season_to_winter()
    {
        var c = new AppConfig { Season = "summer" }; c.Normalize();
        c.ApplyWeatherOverrideSelection("snow");
        Assert.Equal("snow", c.WeatherPreview);
        Assert.Equal("winter", c.Season);
    }

    [Fact]
    public void Other_weathers_do_not_touch_the_season()
    {
        foreach (var w in new[] { "sunny", "cloudy", "rain", "storm", "night", "live" })
        {
            var c = new AppConfig { Season = "autumn" }; c.Normalize();
            c.ApplyWeatherOverrideSelection(w);
            Assert.Equal("autumn", c.Season);
        }
    }

    [Fact]
    public void Leaving_winter_while_snow_is_chosen_puts_the_weather_back_to_live()
    {
        var c = new AppConfig(); c.Normalize();
        c.ApplyWeatherOverrideSelection("snow");
        c.ApplySeasonSelection("summer");
        Assert.Equal("summer", c.Season);
        Assert.Equal("live", c.WeatherPreview);
        // staying in winter keeps the snow
        c.ApplyWeatherOverrideSelection("snow");
        c.ApplySeasonSelection("winter");
        Assert.Equal("snow", c.WeatherPreview);
        // a season pick never touches non-snow weather
        c.ApplyWeatherOverrideSelection("rain");
        c.ApplySeasonSelection("spring");
        Assert.Equal("rain", c.WeatherPreview);
    }

    [Fact]
    public void Cycle_settings_are_clamped()
    {
        var c = new AppConfig { SeasonCycleMinutes = -5, WeatherCycleMinutes = float.NaN, WeatherCycleRandomMinMinutes = 30, WeatherCycleRandomMaxMinutes = 2, WeatherCycleMode = "RANDOM" };
        c.Normalize();
        Assert.Equal(30f, c.SeasonCycleMinutes);
        Assert.Equal(10f, c.WeatherCycleMinutes);
        Assert.True(c.WeatherCycleRandomMaxMinutes >= c.WeatherCycleRandomMinMinutes);
        Assert.Equal("random", c.WeatherCycleMode);
        var d = new AppConfig { SeasonCycleMinutes = 99999, WeatherCycleMode = "weird" }; d.Normalize();
        Assert.Equal(AppConfig.MaxSeasonCycleMinutes, d.SeasonCycleMinutes);
        Assert.Equal("off", d.WeatherCycleMode);
    }

    [Fact]
    public void Runtime_overrides_are_never_serialized()
    {
        var c = new AppConfig { RuntimeWeather = "storm", RuntimeSeason = "winter" };
        var json = JsonSerializer.Serialize(c, ConfigStore.JsonOptions);
        Assert.DoesNotContain("runtime", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("storm", c.EffectiveWeather);
        Assert.Equal("winter", c.EffectiveSeason);
        c.RuntimeWeather = null; c.RuntimeSeason = null;
        Assert.Equal(c.WeatherPreview, c.EffectiveWeather);
        Assert.Equal(c.Season, c.EffectiveSeason);
    }

    [Fact]
    public void Config_example_json_still_loads()
    {
        var example = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config.example.json"));
        if (!File.Exists(example)) return;
        using var d = new TempDir();
        var p = d.File("config.json"); File.Copy(example, p);
        var cfg = ConfigStore.Load(p);
        Assert.Null(ConfigStore.LastLoadError);
        Assert.Equal(0, cfg.SoundMasterVolume);
    }
}
