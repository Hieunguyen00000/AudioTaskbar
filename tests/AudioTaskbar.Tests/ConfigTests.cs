namespace AudioTaskbar.Tests;

/// <summary>1. Config: corrupted JSON, missing fields, out-of-range values, 25→1 migration.</summary>
[Collection("Statics")]
public class ConfigTests
{
    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{\"fps\": \"fast\"}")]
    [InlineData("{\"hotkey\": 123}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"showTrain\": \"maybe\"}")]
    [InlineData("\u0000\u0001garbage")]
    public void Corrupted_json_falls_back_to_defaults_and_keeps_a_backup(string content)
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, content);

        var cfg = ConfigStore.Load(path);

        Assert.NotNull(ConfigStore.LastLoadError);
        Assert.Equal("F8", cfg.Hotkey);
        Assert.Equal(30, cfg.Fps);
        Assert.Equal(AppConfig.CurrentConfigVersion, cfg.ConfigVersion);
        Assert.NotEmpty(Directory.GetFiles(d.Path, "config.json.corrupt-*"));
        // The rewritten file is valid again and loads without error.
        var again = ConfigStore.Load(path);
        Assert.Null(ConfigStore.LastLoadError);
        Assert.Equal("F8", again.Hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Empty_or_missing_fields_use_defaults(string content)
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, content);
        var cfg = ConfigStore.Load(path);
        Assert.Equal("F8", cfg.Hotkey);
        Assert.Equal(1f, cfg.TrainCrossingMinutes);
        Assert.Equal(30f, cfg.DayCycleMinutes);
        Assert.Equal("auto", cfg.RegionMode);
        Assert.Equal("live", cfg.WeatherPreview);
        Assert.True(cfg.AutoUpdateEnabled);
        Assert.Equal("Hieunguyen00000/AudioTaskbar", cfg.UpdateRepo);
    }

    [Fact]
    public void Missing_file_gives_defaults_without_error()
    {
        using var d = new TempDir();
        var cfg = ConfigStore.Load(d.File("nope.json"));
        Assert.Null(ConfigStore.LastLoadError);
        Assert.Equal(72, cfg.BarCount);
    }

    [Fact]
    public void Json_with_comments_and_trailing_commas_is_accepted()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{\n // comment\n \"hotkey\": \"F9\",\n \"configVersion\": 51,\n}");
        var cfg = ConfigStore.Load(path);
        Assert.Null(ConfigStore.LastLoadError);
        Assert.Equal("F9", cfg.Hotkey);
    }

    [Theory]
    [InlineData(-5f, 0.25f)]
    [InlineData(0f, 0.25f)]
    [InlineData(99999f, 240f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(3f, 3f)]
    public void Train_minutes_are_clamped(float input, float expected)
    {
        var c = new AppConfig { TrainCrossingMinutes = input };
        c.Normalize();
        Assert.Equal(expected, c.TrainCrossingMinutes);
    }

    [Theory]
    [InlineData(0f, 2f)]
    [InlineData(-1f, 2f)]
    [InlineData(100000f, 1440f)]
    [InlineData(float.NaN, 30f)]
    public void Day_cycle_minutes_are_clamped(float input, float expected)
    {
        var c = new AppConfig { DayCycleMinutes = input };
        c.Normalize();
        Assert.Equal(expected, c.DayCycleMinutes);
    }

    [Fact]
    public void Other_out_of_range_values_are_clamped()
    {
        var c = new AppConfig
        {
            BarCount = 100000, Fps = 1, Opacity = 7, BarOpacity = -3, IconSafePadding = -9, WeatherRefreshMinutes = 0,
            TapTimeoutSeconds = 999, DayCycleOffsetHours = -3, BackdropOpacity = float.NaN, Intensity = 50,
            MinFreq = 50000, MaxFreq = 10
        };
        c.Normalize();
        Assert.Equal(256, c.BarCount);
        Assert.Equal(15, c.Fps);
        Assert.Equal(1f, c.Opacity);
        Assert.Equal(0f, c.BarOpacity);
        Assert.Equal(0, c.IconSafePadding);
        Assert.Equal(5, c.WeatherRefreshMinutes);
        Assert.Equal(8f, c.TapTimeoutSeconds);
        Assert.Equal(21f, c.DayCycleOffsetHours);
        Assert.Equal(1f, c.BackdropOpacity);
        Assert.Equal(2f, c.Intensity);
        Assert.True(c.MaxFreq > c.MinFreq);
        Assert.InRange(c.EffectiveOpacity, 0f, 1f);
    }

    [Fact]
    public void Opacity_nan_does_not_poison_rendering()
    {
        var c = new AppConfig { Opacity = float.NaN };
        c.Normalize();
        Assert.True(float.IsFinite(c.Opacity));
        Assert.True(float.IsFinite(c.EffectiveOpacity));
    }

    [Theory]
    [InlineData("tuyết rơi", "snow")]
    [InlineData("SNOW", "snow")]
    [InlineData("mưa", "rain")]
    [InlineData("đêm", "night")]
    [InlineData("hurricane", "live")]
    [InlineData(null, "live")]
    public void Weather_preview_aliases(string? input, string expected)
    {
        var c = new AppConfig { WeatherPreview = input! };
        c.Normalize();
        Assert.Equal(expected, c.WeatherPreview);
    }

    [Theory]
    [InlineData("saigon", "hcmc")]
    [InlineData("vung-tau", "baria-vungtau")]
    [InlineData("atlantis", "auto")]
    [InlineData("", "auto")]
    [InlineData(null, "auto")]
    public void Region_mode_aliases(string? input, string expected)
    {
        var c = new AppConfig { RegionMode = input! };
        c.Normalize();
        Assert.Equal(expected, c.RegionMode);
    }

    [Theory]
    [InlineData("", "Hieunguyen00000/AudioTaskbar")]
    [InlineData("noslash", "Hieunguyen00000/AudioTaskbar")]
    [InlineData("a/b/c", "Hieunguyen00000/AudioTaskbar")]
    [InlineData("/me/fork/", "me/fork")]
    [InlineData(null, "Hieunguyen00000/AudioTaskbar")]
    public void Update_repo_is_validated(string? input, string expected)
    {
        var c = new AppConfig { UpdateRepo = input! };
        c.Normalize();
        Assert.Equal(expected, c.UpdateRepo);
    }

    [Theory]
    [InlineData("ftp://example.com/latest.json", "")]
    [InlineData("not a url", "")]
    [InlineData("file:///C:/latest.json", "")]
    [InlineData(" https://example.com/latest.json ", "https://example.com/latest.json")]
    [InlineData("http://127.0.0.1:8080/latest.json", "http://127.0.0.1:8080/latest.json")]
    public void Update_feed_url_must_be_http(string input, string expected)
    {
        var c = new AppConfig { UpdateFeedUrl = input };
        c.Normalize();
        Assert.Equal(expected, c.UpdateFeedUrl);
    }

    [Fact]
    public void Old_config_migrates_train_25_to_1_once()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{\"trainCrossingMinutes\": 25, \"configVersion\": 0}");
        var cfg = ConfigStore.Load(path);
        Assert.Equal(1f, cfg.TrainCrossingMinutes);
        Assert.Equal(AppConfig.CurrentConfigVersion, cfg.ConfigVersion);
        Assert.Contains("\"configVersion\": 51", File.ReadAllText(path));
    }

    [Fact]
    public void Current_config_keeps_a_deliberate_25_minute_train()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{\"trainCrossingMinutes\": 25, \"configVersion\": 51}");
        Assert.Equal(25f, ConfigStore.Load(path).TrainCrossingMinutes);
    }

    [Fact]
    public void Old_config_with_other_train_value_is_not_touched()
    {
        using var d = new TempDir();
        var path = d.File("config.json");
        File.WriteAllText(path, "{\"trainCrossingMinutes\": 5, \"configVersion\": 0}");
        Assert.Equal(5f, ConfigStore.Load(path).TrainCrossingMinutes);
    }

    [Fact]
    public void Save_then_load_round_trips_v53_keys()
    {
        using var d = new TempDir();
        var path = d.File("sub\\config.json");
        var c = new AppConfig { ConfigVersion = 51, StartMinimized = true, AutoUpdateEnabled = false, AutoUpdateSilentInstall = false, UpdateFeedUrl = "https://x.example/latest.json", Hotkey = "Ctrl+Alt+F8" };
        ConfigStore.Save(path, c);
        var back = ConfigStore.Load(path);
        Assert.True(back.StartMinimized);
        Assert.False(back.AutoUpdateEnabled);
        Assert.False(back.AutoUpdateSilentInstall);
        Assert.Equal("https://x.example/latest.json", back.UpdateFeedUrl);
        Assert.Equal("Ctrl+Alt+F8", back.Hotkey);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0, 255)]
    [InlineData("80FF0000", 255, 0, 0, 128)]
    [InlineData("zzz", 1, 2, 3, 255)]
    [InlineData("", 1, 2, 3, 255)]
    [InlineData("#12345", 1, 2, 3, 255)]
    public void Colors_parse_or_fall_back(string text, int r, int g, int b, int a)
    {
        var c = AppConfig.ParseColor(text, Color.FromArgb(255, 1, 2, 3));
        Assert.Equal((r, g, b, a), ((int)c.R, (int)c.G, (int)c.B, (int)c.A));
    }
}
