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
    /// <summary>Minutes for the single train to cross the full taskbar left→right.</summary>
    public float TrainCrossingMinutes { get; set; } = 1f;
    /// <summary>"auto" (detect from machine location) or a region id: hcmc, baria-vungtau, hanoi, danang-hoian, tokyo, paris, default.</summary>
    public string RegionMode { get; set; } = "auto";
    /// <summary>Show the temperature / condition / location HUD on the taskbar background.</summary>
    public bool ShowWeatherHud { get; set; } = true;
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
    /// <summary>Shift of the virtual clock in hours (cycle mode), e.g. 6 to start a cycle near dawn.</summary>
    public float DayCycleOffsetHours { get; set; } = 0f;
    /// <summary>Config schema version used for one-time migrations (51 = v5.1).</summary>
    public int ConfigVersion { get; set; } = 0;
    public const int CurrentConfigVersion = 51;

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
        TimeMode = (TimeMode ?? "cycle").Trim().ToLowerInvariant() is "real" or "clock" or "thuc" or "that" ? "real" : "cycle";
        DayCycleMinutes = float.IsFinite(DayCycleMinutes) ? Math.Clamp(DayCycleMinutes, 2f, 1440f) : 30f;
        DayCycleOffsetHours = float.IsFinite(DayCycleOffsetHours) ? ((DayCycleOffsetHours % 24f) + 24f) % 24f : 0f;
        RegionMode = RegionCatalog.NormalizeMode(RegionMode);
        TapMode = (TapMode ?? "auto").Trim().ToLowerInvariant() is "off" or "safe" or "false" ? "off" : "auto";
        TapTimeoutSeconds = float.IsFinite(TapTimeoutSeconds) ? Math.Clamp(TapTimeoutSeconds, 1.5f, 8f) : 3.5f;

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
        BackdropOpacity = Math.Clamp(BackdropOpacity, 0.5f, 1f);

        RgbaLow = ParseColor(ColorLow, Color.FromArgb(255, 7, 21, 38));
        RgbaMid = ParseColor(ColorMid, Color.FromArgb(255, 46, 230, 166));
        RgbaHigh = ParseColor(ColorHigh, Color.FromArgb(255, 122, 231, 255));
        RgbaBar = ParseColor(BarColor, Color.FromArgb(255, 232, 244, 255));
        EffectiveOpacity = Math.Clamp(Opacity * Intensity, 0f, 1f);
        EffectiveBarOpacity = Math.Clamp(BarOpacity * Intensity, 0f, 1f);
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

    public static AppConfig Load(string path)
    {
        AppConfig cfg;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            if (cfg.ConfigVersion < AppConfig.CurrentConfigVersion)
            {
                // v5.1 one-time migration: the train now crosses in 1 minute (was 25), and the
                // 30-minute day/night cycle is on by default.
                if (MathF.Abs(cfg.TrainCrossingMinutes - 25f) < 0.01f) cfg.TrainCrossingMinutes = 1f;
                cfg.ConfigVersion = AppConfig.CurrentConfigVersion;
                cfg.Normalize();
                try { Save(path, cfg); } catch { }
            }
        }
        else cfg = new AppConfig { ConfigVersion = AppConfig.CurrentConfigVersion };
        cfg.Normalize();
        return cfg;
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
