using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioTaskbar;

public sealed class AppConfig
{
    public int BarCount { get; set; } = 72;
    public float Sensitivity { get; set; } = 1.0f;
    public bool AutoGain { get; set; } = true;
    public float Opacity { get; set; } = 0.52f;
    public float BarOpacity { get; set; } = 0.78f;
    public string ColorLow { get; set; } = "#071526";
    public string ColorMid { get; set; } = "#2EE6A6";
    public string ColorHigh { get; set; } = "#7AE7FF";
    public string BarColor { get; set; } = "#E8F4FF";
    public bool BarColorByFrequency { get; set; } = true;
    public int Fps { get; set; } = 30;
    public int FftSize { get; set; } = 2048;
    public string Mode { get; set; } = "diorama";
    public float BarHeightPercent { get; set; } = 62f;
    public float BarGapPercent { get; set; } = 32f;
    public float Smoothing { get; set; } = 0.72f;
    public float ColorAttack { get; set; } = 0.28f;
    public float ColorRelease { get; set; } = 0.07f;
    public float MinFreq { get; set; } = 30f;
    public float MaxFreq { get; set; } = 16000f;
    public bool ShowOnAllMonitors { get; set; } = true;
    public bool PeakHold { get; set; } = true;
    public float PeakDecay { get; set; } = 0.025f;
    public bool HideWhenFullscreen { get; set; } = true;

    // v2
    public string Preset { get; set; } = "aurora";
    public float Intensity { get; set; } = 1.0f;
    public bool IdleFade { get; set; } = false;
    public float IdleFadeSeconds { get; set; } = 1.6f;
    public bool IconSafeFade { get; set; } = true;
    public int IconSafePadding { get; set; } = 28;
    public bool FuseIntoTaskbar { get; set; } = false;
    public bool MakeTaskbarTransparent { get; set; } = false;
    public string Hotkey { get; set; } = "F8";
    /// <summary>Solid taskbar base (1 = fully opaque, wallpaper cannot bleed through).</summary>
    public float BackdropOpacity { get; set; } = 1.0f;
    /// <summary>Seal the real taskbar so wallpaper/mica cannot show through, including behind icons.</summary>
    public bool BlockWallpaper { get; set; } = true;
    public bool MirroredBars { get; set; } = true;
    public bool ShowSpectrum { get; set; } = false;
    public bool ShowWaveform { get; set; } = false;
    public bool ShowWeather { get; set; } = true;
    public bool ShowWeatherAmbient { get; set; } = true;
    public string WeatherPosition { get; set; } = "left";
    public string WeatherUnits { get; set; } = "celsius";
    public string WeatherCity { get; set; } = "";
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public int WeatherRefreshMinutes { get; set; } = 15;
    public bool WeatherOnPrimaryOnly { get; set; } = true;
    public bool EnableAudio { get; set; } = false;
    /// <summary>v4 living scenery.</summary>
    public bool ShowTrain { get; set; } = true;
    public bool ShowCyclist { get; set; } = true;
    public bool ShowAmbientDetails { get; set; } = true;
    public bool ShowWeatherEffects { get; set; } = true;
    /// <summary>live | sunny | cloudy | rain | storm | night</summary>
    public string WeatherPreview { get; set; } = "live";

    // v5
    /// <summary>
    /// Minutes for one full train lifecycle: it arrives and slows down, stops at the platform, passengers get off and on,
    /// then it departs and speeds up and leaves (v5.4; it used to be a plain left→right crossing).
    /// </summary>
    public float TrainCrossingMinutes { get; set; } = 1f;
    /// <summary>"auto" (detect from machine location) or a region id: hcmc, baria-vungtau, hanoi, danang-hoian, tokyo, paris, default.</summary>
    public string RegionMode { get; set; } = "auto";
    /// <summary>Removed in v5.4 (weather and location info now live only in the control panel). Kept so old config files still load; ignored.</summary>
    public bool ShowWeatherHud { get; set; } = false;
    /// <summary>When a region is picked manually, fetch that region's weather instead of the machine's.</summary>
    public bool WeatherFollowsRegion { get; set; } = false;
    /// <summary>Seconds to wait for the TAP DLL inside Explorer before using safe mode.</summary>
    public float TapTimeoutSeconds { get; set; } = 3.5f;
    /// <summary>"auto" = try the Explorer TAP for true behind-icon mode; "off" = always UIA icon-safe mode.</summary>
    public string TapMode { get; set; } = "auto";

    // v5.1
    /// <summary>"cycle" = accelerated day (one full 24 h day every <see cref="DayCycleMinutes"/>); "real" = wall clock.</summary>
    public string TimeMode { get; set; } = "cycle";
    /// <summary>Real minutes for one full virtual day in cycle mode.</summary>
    public float DayCycleMinutes { get; set; } = 30f;
    public const float MinDayCycleMinutes = 0.5f;
    public const float MaxDayCycleMinutes = 10080f;
    /// <summary>Shift of the virtual clock in hours (cycle mode), e.g. 6 to start a cycle near dawn.</summary>
    public float DayCycleOffsetHours { get; set; } = 0f;
    /// <summary>Config schema version used for one-time migrations (51 = v5.1).</summary>
    public int ConfigVersion { get; set; } = 0;

    // v5.3 portal + auto-update
    /// <summary>Show the control portal when the user launches the app manually (autostart always uses --minimized).</summary>
    public bool StartMinimized { get; set; } = false;
    /// <summary>Check for new releases on startup (after 10 s) and every 6 hours.</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    /// <summary>Download, verify (SHA-256) and install a newer release silently, then relaunch.</summary>
    public bool AutoUpdateSilentInstall { get; set; } = true;
    /// <summary>GitHub "owner/repo" whose public Releases feed the updater (must be public for anonymous users).</summary>
    public string UpdateRepo { get; set; } = "Hieunguyen00000/AudioTaskbar";
    /// <summary>Optional static latest.json manifest URL; overrides the GitHub Releases API when set.</summary>
    public string UpdateFeedUrl { get; set; } = "";

    // v5.4
    /// <summary>
    /// Show the soft grey plate / margin that keeps taskbar icons readable (the dimmed band behind each icon and the
    /// guard gap around it). Off = the scenery runs right up to the icons.
    /// </summary>
    public bool ShowIconPadding { get; set; } = true;
    /// <summary>spring | summer | autumn | winter. Summer is the look of earlier versions.</summary>
    public string Season { get; set; } = "summer";
    /// <summary>Advance the season automatically, every <see cref="SeasonCycleMinutes"/> real minutes.</summary>
    public bool SeasonCycleEnabled { get; set; } = false;
    public float SeasonCycleMinutes { get; set; } = 30f;
    public const float MinSeasonCycleMinutes = 0.5f;
    public const float MaxSeasonCycleMinutes = 10080f;
    /// <summary>off | interval (a new weather every <see cref="WeatherCycleMinutes"/>) | random (random weather after a random wait).</summary>
    public string WeatherCycleMode { get; set; } = "off";
    public float WeatherCycleMinutes { get; set; } = 10f;
    public float WeatherCycleRandomMinMinutes { get; set; } = 5f;
    public float WeatherCycleRandomMaxMinutes { get; set; } = 20f;
    public const float MinWeatherCycleMinutes = 0.5f;
    public const float MaxWeatherCycleMinutes = 1440f;
    /// <summary>Ambient sound master volume 0..100. 0 on first run and after migrating an older config.</summary>
    public int SoundMasterVolume { get; set; } = 0;
    /// <summary>Per-effect volume 0..100 by effect id (rain, thunder, wind, traffic, horns, train, birds, insects, animals, waves). All 0 by default.</summary>
    public Dictionary<string, int> SoundVolumes { get; set; } = new();
    /// <summary>v5.4 airplane: lands, taxis to the stand, drops off and boards passengers, takes off again (or just crosses the sky when no airport fits).</summary>
    public bool ShowAirplane { get; set; } = true;
    /// <summary>Minutes for one full airplane lifecycle.</summary>
    public float AirplaneCycleMinutes { get; set; } = 3f;
    public const float MinAirplaneCycleMinutes = 0.5f;
    public const float MaxAirplaneCycleMinutes = 240f;
    public const float DefaultAirplaneCycleMinutes = 3f;
    /// <summary>Config schema: 51 = v5.1, 54 = v5.4 sounds / seasons / cycles, 55 = v5.4 airplane + train lifecycle.</summary>
    public const int CurrentConfigVersion = 55;

    /// <summary>Runtime only (never saved): weather chosen by the automatic weather cycle, or null.</summary>
    [JsonIgnore] public string? RuntimeWeather { get; set; }
    /// <summary>Runtime only (never saved): season chosen by the automatic season cycle, or null.</summary>
    [JsonIgnore] public string? RuntimeSeason { get; set; }
    /// <summary>Weather shown right now: the automatic cycle if it is on, otherwise the user's pick.</summary>
    [JsonIgnore] public string EffectiveWeather => RuntimeWeather ?? WeatherPreview;
    [JsonIgnore] public string EffectiveSeason => RuntimeSeason ?? Season;

    [JsonIgnore] public Color RgbaLow { get; private set; }
    [JsonIgnore] public Color RgbaMid { get; private set; }
    [JsonIgnore] public Color RgbaHigh { get; private set; }
    [JsonIgnore] public Color RgbaBar { get; private set; }
    [JsonIgnore] public float EffectiveOpacity { get; private set; }
    [JsonIgnore] public float EffectiveBarOpacity { get; private set; }

    public void Normalize()
    {
        BarCount = Math.Clamp(BarCount, 8, 256);
        Sensitivity = Math.Clamp(Sensitivity, 0.05f, 8f);
        Opacity = Math.Clamp(Opacity, 0f, 1f);
        BarOpacity = Math.Clamp(BarOpacity, 0f, 1f);
        Fps = Math.Clamp(Fps, 15, 90);
        int fft = FftSize;
        if (fft < 512) fft = 512;
        if (fft > 8192) fft = 8192;
        int p = 1;
        while (p < fft) p <<= 1;
        FftSize = p;
        BarHeightPercent = Math.Clamp(BarHeightPercent, 10f, 100f);
        BarGapPercent = Math.Clamp(BarGapPercent, 0f, 80f);
        Smoothing = Math.Clamp(Smoothing, 0f, 0.95f);
        ColorAttack = Math.Clamp(ColorAttack, 0.01f, 1f);
        ColorRelease = Math.Clamp(ColorRelease, 0.01f, 1f);
        MinFreq = Math.Clamp(MinFreq, 10f, 2000f);
        MaxFreq = Math.Max(MinFreq + 100f, Math.Clamp(MaxFreq, 200f, 24000f));
        PeakDecay = Math.Clamp(PeakDecay, 0.005f, 0.5f);
        Intensity = Math.Clamp(Intensity, 0.25f, 2f);
        IdleFadeSeconds = Math.Clamp(IdleFadeSeconds, 0.3f, 8f);
        IconSafePadding = Math.Clamp(IconSafePadding, 0, 200);
        WeatherRefreshMinutes = Math.Clamp(WeatherRefreshMinutes, 5, 120);
        TrainCrossingMinutes = float.IsFinite(TrainCrossingMinutes) ? Math.Clamp(TrainCrossingMinutes, 0.25f, 240f) : 1f;
        AirplaneCycleMinutes = float.IsFinite(AirplaneCycleMinutes) && AirplaneCycleMinutes > 0f
            ? Math.Clamp(AirplaneCycleMinutes, MinAirplaneCycleMinutes, MaxAirplaneCycleMinutes) : DefaultAirplaneCycleMinutes;
        TimeMode = NormalizeTimeMode(TimeMode);
        // v5.3.1: any custom cycle length from 30 s to 7 days; only invalid values (<= 0, NaN, ∞) fall back to 30.
        DayCycleMinutes = float.IsFinite(DayCycleMinutes) && DayCycleMinutes > 0f
            ? Math.Clamp(DayCycleMinutes, MinDayCycleMinutes, MaxDayCycleMinutes) : 30f;
        DayCycleOffsetHours = float.IsFinite(DayCycleOffsetHours) ? ((DayCycleOffsetHours % 24f) + 24f) % 24f : 0f;
        RegionMode = RegionCatalog.NormalizeMode(RegionMode);
        TapMode = (TapMode ?? "auto").Trim().ToLowerInvariant() is "off" or "safe" or "false" ? "off" : "auto";
        TapTimeoutSeconds = float.IsFinite(TapTimeoutSeconds) ? Math.Clamp(TapTimeoutSeconds, 1.5f, 8f) : 3.5f;
        Opacity = float.IsFinite(Opacity) ? Opacity : 0.52f;
        BackdropOpacity = float.IsFinite(BackdropOpacity) ? BackdropOpacity : 1f;
        UpdateRepo = (UpdateRepo ?? "").Trim().Trim('/');
        if (UpdateRepo.Length == 0 || UpdateRepo.Count(c => c == '/') != 1) UpdateRepo = "Hieunguyen00000/AudioTaskbar";
        UpdateFeedUrl = (UpdateFeedUrl ?? "").Trim();
        Season = Seasons.Normalize(Season);
        SeasonCycleMinutes = float.IsFinite(SeasonCycleMinutes) && SeasonCycleMinutes > 0f
            ? Math.Clamp(SeasonCycleMinutes, MinSeasonCycleMinutes, MaxSeasonCycleMinutes) : 30f;
        WeatherCycleMode = WeatherCycle.NormalizeMode(WeatherCycleMode);
        WeatherCycleMinutes = float.IsFinite(WeatherCycleMinutes) && WeatherCycleMinutes > 0f
            ? Math.Clamp(WeatherCycleMinutes, MinWeatherCycleMinutes, MaxWeatherCycleMinutes) : 10f;
        WeatherCycleRandomMinMinutes = float.IsFinite(WeatherCycleRandomMinMinutes) && WeatherCycleRandomMinMinutes > 0f
            ? Math.Clamp(WeatherCycleRandomMinMinutes, MinWeatherCycleMinutes, MaxWeatherCycleMinutes) : 5f;
        WeatherCycleRandomMaxMinutes = float.IsFinite(WeatherCycleRandomMaxMinutes) && WeatherCycleRandomMaxMinutes > 0f
            ? Math.Clamp(WeatherCycleRandomMaxMinutes, MinWeatherCycleMinutes, MaxWeatherCycleMinutes) : 20f;
        if (WeatherCycleRandomMaxMinutes < WeatherCycleRandomMinMinutes) WeatherCycleRandomMaxMinutes = WeatherCycleRandomMinMinutes;
        SoundMasterVolume = Math.Clamp(SoundMasterVolume, 0, 100);
        var vols = new Dictionary<string, int>();
        foreach (var id in SoundEffects.Ids)
            vols[id] = SoundVolumes is not null && SoundVolumes.TryGetValue(id, out var v) ? Math.Clamp(v, 0, 100) : 0;
        SoundVolumes = vols;
        if (UpdateFeedUrl.Length > 0 && !(Uri.TryCreate(UpdateFeedUrl, UriKind.Absolute, out var feed) && (feed.Scheme == Uri.UriSchemeHttps || feed.Scheme == Uri.UriSchemeHttp)))
            UpdateFeedUrl = "";

        Mode = (Mode ?? "diorama").Trim().ToLowerInvariant();
        if (Mode is "diorama" or "scene")
        {
            // v4: spectrum/waveform stay off unless the user explicitly turns them back on.
        }
        else if (Mode is not ("spectrum" or "waveform" or "both"))
        {
            Mode = "diorama";
        }
        else if (!ShowSpectrum && !ShowWaveform)
        {
            ShowSpectrum = Mode is "spectrum" or "both";
            ShowWaveform = Mode is "waveform" or "both";
        }

        WeatherPreview = (WeatherPreview ?? "live").Trim().ToLowerInvariant();
        WeatherPreview = WeatherPreview switch
        {
            "sunny" or "nang" or "nắng" or "nắng đẹp" or "nang dep" => "sunny",
            "cloudy" or "may" or "mây" or "mây trôi" or "may troi" => "cloudy",
            "rain" or "mua" or "mưa" or "mưa rào" or "mua rao" => "rain",
            "storm" or "giong" or "giông" or "giông bão" or "giong bao" => "storm",
            "night" or "dem" or "đêm" or "đêm sao" or "dem sao" => "night",
            "snow" or "tuyet" or "tuyết" or "tuyết rơi" or "tuyet roi" => "snow",
            _ => "live"
        };

        WeatherPosition = (WeatherPosition ?? "left").Trim().ToLowerInvariant();
        if (WeatherPosition is not ("left" or "right")) WeatherPosition = "left";
        WeatherUnits = (WeatherUnits ?? "celsius").Trim().ToLowerInvariant();
        if (WeatherUnits is "f" or "fahrenheit" or "°f") WeatherUnits = "fahrenheit";
        else WeatherUnits = "celsius";
        WeatherCity = (WeatherCity ?? "").Trim();
        Preset = (Preset ?? "aurora").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "F8";
        else Hotkey = Hotkey.Trim();
        // v5.3: wallpaper blocking is always on (opaque backdrop behind the taskbar) — not user-configurable.
        BlockWallpaper = true;
        BackdropOpacity = 1f;

        RgbaLow = ParseColor(ColorLow, Color.FromArgb(255, 7, 21, 38));
        RgbaMid = ParseColor(ColorMid, Color.FromArgb(255, 46, 230, 166));
        RgbaHigh = ParseColor(ColorHigh, Color.FromArgb(255, 122, 231, 255));
        RgbaBar = ParseColor(BarColor, Color.FromArgb(255, 232, 244, 255));
        EffectiveOpacity = Math.Clamp(Opacity * Intensity, 0f, 1f);
        EffectiveBarOpacity = Math.Clamp(BarOpacity * Intensity, 0f, 1f);
    }

    /// <summary>cycle (default) | real | day (fixed daytime) | night (fixed night).</summary>
    /// <summary>
    /// v5.3.2: the single place where a weather-preview pick from the portal, tray menu or preview command is
    /// applied. "Đêm sao" (night) also switches the day/night clock to "Cố định ban đêm" so the sky really is a
    /// starry night; every other weather keeps the user's own time mode. Returns true when the time mode changed.
    /// </summary>
    public bool ApplyWeatherOverrideSelection(string? weatherMode)
    {
        WeatherPreview = weatherMode ?? "live";
        string before = TimeMode;
        Normalize();
        if (WeatherPreview == "night") TimeMode = "night";
        // v5.4: snowfall belongs to winter, so picking it switches the season to winter.
        if (WeatherPreview == "snow") Season = "winter";
        return TimeMode != NormalizeTimeMode(before);
    }

    /// <summary>
    /// v5.4: the single place a season pick is applied. Snowfall is a winter-only weather, so leaving winter while snow
    /// is the chosen weather puts the weather back to "Thực tế" (live).
    /// </summary>
    public void ApplySeasonSelection(string? season)
    {
        Season = Seasons.Normalize(season);
        if (Season != "winter" && WeatherPreview == "snow") WeatherPreview = "live";
    }

    public static string NormalizeTimeMode(string? mode)
    {
        var m = (mode ?? "cycle").Trim().ToLowerInvariant();
        return m switch
        {
            "real" or "clock" or "thuc" or "that" or "theo gio that" or "theo giờ thật" => "real",
            "day" or "ngay" or "ngày" or "ban ngay" or "ban ngày" or "co dinh ban ngay" or "cố định ban ngày" or "fixed-day" => "day",
            "night" or "dem" or "đêm" or "ban dem" or "ban đêm" or "co dinh ban dem" or "cố định ban đêm" or "fixed-night" => "night",
            _ => "cycle"
        };
    }

    public static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length is not (6 or 8)) return fallback;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return fallback;
        if (s.Length == 6)
            return Color.FromArgb(255, (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
        return Color.FromArgb((int)((v >> 24) & 0xFF), (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string LocalConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioTaskbar", "config.json");

    public static string FindConfigPath()
    {
        var local = LocalConfigPath();
        if (File.Exists(local)) return local;
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "config.json")),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "audio-taskbar", "config.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AudioTaskbar", "config.json")
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return local;
    }

    /// <summary>
    /// Prefer %LOCALAPPDATA%\AudioTaskbar\config.json. If only a legacy config
    /// exists, migrate it and turn fusion off (opt-in) after the Explorer teardown incident.
    /// </summary>
    public static (AppConfig cfg, string path, string? note) LoadPreferred()
    {
        var local = LocalConfigPath();
        if (File.Exists(local))
            return (Load(local), local, null);

        var legacy = FindConfigPath();
        if (File.Exists(legacy) && !string.Equals(legacy, local, StringComparison.OrdinalIgnoreCase))
        {
            var cfg = Load(legacy);
            cfg.FuseIntoTaskbar = false;
            cfg.MakeTaskbarTransparent = false;
            if (string.IsNullOrWhiteSpace(cfg.Hotkey)) cfg.Hotkey = "F8";
            try { Save(local, cfg); }
            catch { return (cfg, legacy, "migrate-save-failed"); }
            return (cfg, local, "migrated-fusion-opt-in");
        }

        var fresh = new AppConfig { ConfigVersion = AppConfig.CurrentConfigVersion };
        fresh.Normalize();
        try { Save(local, fresh); } catch { }
        return (fresh, File.Exists(local) ? local : (File.Exists(legacy) ? legacy : local), "new-local-config");
    }

    /// <summary>Last parse error from <see cref="Load"/> (null when the file was fine).</summary>
    public static string? LastLoadError { get; private set; }

    public static AppConfig Load(string path)
    {
        LastLoadError = null;
        AppConfig cfg;
        if (File.Exists(path))
        {
            AppConfig? parsed = null;
            try
            {
                var json = File.ReadAllText(path);
                parsed = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // v5.3: a corrupted / hand-edited config must never stop the app. Keep a copy for
                // the user, then continue with defaults (and rewrite a clean file).
                LastLoadError = ex.GetType().Name + ": " + ex.Message;
                try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true); } catch { }
                var fresh = new AppConfig { ConfigVersion = AppConfig.CurrentConfigVersion };
                fresh.Normalize();
                try { Save(path, fresh); } catch { }
                return fresh;
            }
            cfg = parsed ?? new AppConfig { ConfigVersion = AppConfig.CurrentConfigVersion };
            if (cfg.ConfigVersion < AppConfig.CurrentConfigVersion)
            {
                // v5.1 one-time migration: the train now crosses in 1 minute (was 25), and the
                // 30-minute day/night cycle is on by default.
                if (cfg.ConfigVersion < 51 && MathF.Abs(cfg.TrainCrossingMinutes - 25f) < 0.01f) cfg.TrainCrossingMinutes = 1f;
                if (cfg.ConfigVersion < 54) MigrateToV54(cfg);
                if (cfg.ConfigVersion < 55) MigrateToV55(cfg);
                cfg.ConfigVersion = AppConfig.CurrentConfigVersion;
                cfg.Normalize();
                try { Save(path, cfg); } catch { }
            }
        }
        else cfg = new AppConfig { ConfigVersion = AppConfig.CurrentConfigVersion };
        cfg.Normalize();
        return cfg;
    }

    /// <summary>
    /// v5.4 one-time migration. Older files have no sound, season or cycle keys, so the new defaults apply; this makes the
    /// important ones explicit: every sound volume starts at 0 (the app is silent until the user raises it), the season
    /// is summer (the look of earlier versions), the automatic cycles are off, the grey icon padding stays as it was, and
    /// the removed weather-info badge stays off.
    /// </summary>
    public static void MigrateToV54(AppConfig cfg)
    {
        cfg.SoundMasterVolume = 0;
        cfg.SoundVolumes = SoundEffects.Ids.ToDictionary(id => id, _ => 0);
        cfg.Season = "summer";
        cfg.SeasonCycleEnabled = false;
        cfg.WeatherCycleMode = "off";
        cfg.ShowIconPadding = true;
        cfg.ShowWeatherHud = false;
    }

    /// <summary>
    /// Schema 55: the airplane is new and defaults to ON (3-minute lifecycle) with its sound at 0. Files written by a v5.4
    /// build from before the airplane keep every other setting, including the sound volumes the user already chose.
    /// </summary>
    public static void MigrateToV55(AppConfig cfg)
    {
        cfg.ShowAirplane = true;
        cfg.AirplaneCycleMinutes = AppConfig.DefaultAirplaneCycleMinutes;
        cfg.SoundVolumes ??= new Dictionary<string, int>();
        if (!cfg.SoundVolumes.ContainsKey(SoundEffects.Airplane)) cfg.SoundVolumes[SoundEffects.Airplane] = 0;
    }

    public static void Save(string path, AppConfig cfg)
    {
        cfg.Normalize();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonOptions));
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }
}
