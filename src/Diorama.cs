using System.Drawing.Drawing2D;

namespace AudioTaskbar;

/// <summary>
/// Time-of-day + weather mood used by the v4 living taskbar diorama.
/// </summary>
internal enum DayPhase { Night, Dawn, Day, Golden, Dusk }

internal readonly struct SceneMood
{
    public readonly DayPhase Phase;
    public readonly WeatherKind Kind;
    public readonly bool IsDay;
    public readonly bool IsNight;
    public readonly bool IsRainy;
    public readonly bool IsStorm;
    public readonly bool IsFog;
    public readonly bool IsCloudy;
    public readonly bool IsClear;
    public readonly float Cloudiness;   // 0..1
    public readonly float RainAmt;      // 0..1
    public readonly Color SkyTop, SkyMid, SkyBot;
    public readonly Color HorizonGlow;
    public readonly Color Ground;
    public readonly Color GroundHi;
    public readonly Color Silhouette;
    public readonly Color Accent;
    public readonly string ConditionVi;
    public readonly string TemperatureText;
    public readonly string Place;
    public readonly bool Ok;
    public readonly float SunX;         // 0..1 across the bar (sun by day, moon by night)
    public readonly float MoonPhase;    // 0 new .. 1 full-ish

    // v5.1 continuous day/night values (no abrupt jumps across the 24 h cycle)
    public float Hour { get; init; }      // virtual (or real) hour 0..24
    public float DayK { get; init; }      // 0 night .. 1 full daylight
    public float LightsK { get; init; }   // 0 .. 1 artificial lights (windows, lamps, headlights)
    public float StarsK { get; init; }    // 0 .. 1 stars / fireflies visibility
    public float WarmK { get; init; }     // 0 .. 1 sunrise / sunset warmth
    public float SunT { get; init; }      // 0..1 along the day arc, <0 or >1 = below horizon
    public float MoonT { get; init; }     // 0..1 along the night arc, <0 or >1 = below horizon

    // v5.2 snowfall
    public bool IsSnow => Kind == WeatherKind.Snow;
    public float SnowAmt { get; init; }   // 0..1 (light 0.45, moderate 0.7, heavy 1)

    // v5.4 seasons
    public SeasonId Season { get; init; } = SeasonId.Summer;
    /// <summary>Snow lying on the ground and rooftops: the snowfall amount, or a light cover in winter without snowfall.</summary>
    public float SnowCover { get; init; }

    public SceneMood(
        DayPhase phase, WeatherKind kind, bool isDay,
        float cloudiness, float rainAmt,
        Color skyTop, Color skyMid, Color skyBot, Color horizonGlow,
        Color ground, Color groundHi, Color silhouette, Color accent,
        string conditionVi, string temperatureText, string place, bool ok,
        float sunX, float moonPhase)
    {
        Phase = phase;
        Kind = kind;
        IsDay = isDay;
        IsNight = phase is DayPhase.Night or DayPhase.Dusk && !isDay || phase == DayPhase.Night;
        IsRainy = kind is WeatherKind.Rain or WeatherKind.Storm; // v5.2: snow has its own renderer
        IsStorm = kind == WeatherKind.Storm;
        IsFog = kind == WeatherKind.Fog;
        IsCloudy = kind is WeatherKind.Cloudy or WeatherKind.Fog or WeatherKind.Storm
                   || (kind == WeatherKind.Rain);
        IsClear = kind is WeatherKind.Clear or WeatherKind.Night;
        Cloudiness = cloudiness;
        RainAmt = rainAmt;
        SkyTop = skyTop; SkyMid = skyMid; SkyBot = skyBot;
        HorizonGlow = horizonGlow;
        Ground = ground; GroundHi = groundHi; Silhouette = silhouette;
        Accent = accent;
        ConditionVi = conditionVi;
        TemperatureText = temperatureText;
        Place = place;
        Ok = ok;
        SunX = sunX;
        MoonPhase = moonPhase;
    }
}

internal static class SceneClock
{
    /// <summary>Render/test override for the scene hour (0..24). Null = clock driven.</summary>
    public static float? HourOverride;

    public const float SunRise = 5.5f, SunSet = 18.5f;

    /// <summary>timeMode "day": pleasant late-morning sky, sun drifting 10:30 → 12:00 → 10:30.</summary>
    public const float FixedDayFrom = 10.5f, FixedDayTo = 12f;
    /// <summary>timeMode "night": starry sky, moon near its peak, 23:30 → 23:59 → 23:30.</summary>
    public const float FixedNightFrom = 23.5f, FixedNightTo = 23.98f;

    /// <summary>
    /// Scene hour 0..24. timeMode "cycle": one full virtual day every
    /// <c>dayCycleMinutes</c> real minutes (default 30), advancing smoothly.
    /// timeMode "real": the local wall clock.
    /// </summary>
    public static float VirtualHour(AppConfig cfg, DateTime local)
    {
        if (HourOverride is { } ho) return ((ho % 24f) + 24f) % 24f;
        if (cfg.TimeMode is "day" or "night")
        {
            // Fixed day / night: a slow ping-pong inside a narrow window so the sun / moon still drift a little.
            const double period = FixedDriftMinutes * 60.0; // independent of the custom cycle length
            double secs = (local - DateTime.UnixEpoch).TotalSeconds;
            double frac = ((secs % period) + period) % period / period;
            float pp = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * frac)); // 0 → 1 → 0
            return cfg.TimeMode == "day"
                ? FixedDayFrom + (FixedDayTo - FixedDayFrom) * pp
                : FixedNightFrom + (FixedNightTo - FixedNightFrom) * pp;
        }
        if (cfg.TimeMode == "cycle")
        {
            double period = CyclePeriodSeconds(cfg);
            double secs = (local - DateTime.UnixEpoch).TotalSeconds + cfg.DayCycleOffsetHours / 24.0 * period;
            double frac = ((secs % period) + period) % period / period;
            return (float)(frac * 24.0);
        }
        return local.Hour + local.Minute / 60f + local.Second / 3600f + local.Millisecond / 3_600_000f;
    }

    /// <summary>Fixed day / night drift period (minutes) — the sun / moon wobble slowly inside their window.</summary>
    public const double FixedDriftMinutes = 30.0;

    public static double CyclePeriodSeconds(AppConfig cfg)
        => Math.Clamp(float.IsFinite(cfg.DayCycleMinutes) && cfg.DayCycleMinutes > 0 ? cfg.DayCycleMinutes : 30f,
                      AppConfig.MinDayCycleMinutes, AppConfig.MaxDayCycleMinutes) * 60.0;

    /// <summary>
    /// v5.3.1: change the cycle length without the sky jumping — re-anchors <c>DayCycleOffsetHours</c> so the
    /// scene hour at <paramref name="now"/> is the same before and after; only the speed changes.
    /// </summary>
    public static void SetCycleMinutes(AppConfig cfg, float minutes, DateTime now)
    {
        float before = VirtualHourCycle(cfg, now);
        cfg.DayCycleMinutes = minutes;
        cfg.Normalize();
        double period = CyclePeriodSeconds(cfg);
        double secs = (now - DateTime.UnixEpoch).TotalSeconds;
        double baseFrac = ((secs % period) + period) % period / period;
        double off = (before / 24.0 - baseFrac) * 24.0;
        cfg.DayCycleOffsetHours = (float)(((off % 24.0) + 24.0) % 24.0);
    }

    private static float VirtualHourCycle(AppConfig cfg, DateTime local)
    {
        double period = CyclePeriodSeconds(cfg);
        double secs = (local - DateTime.UnixEpoch).TotalSeconds + cfg.DayCycleOffsetHours / 24.0 * period;
        return (float)((((secs % period) + period) % period / period) * 24.0);
    }

    /// <summary>"30 phút", "1,5 giờ", "1 ngày" style label for a cycle length in minutes.</summary>
    public static string CycleLabel(float minutes)
    {
        var vi = System.Globalization.CultureInfo.InvariantCulture;
        if (minutes < 1f) return (minutes * 60f).ToString("0", vi) + " giây";
        return minutes.ToString("0.##", vi) + " phút";
    }

    public static DayPhase PhaseAtHour(float h)
    {
        if (h < 5.0f || h >= 19.5f) return DayPhase.Night;
        if (h < 7.0f) return DayPhase.Dawn;
        if (h < 16.5f) return DayPhase.Day;
        if (h < 18.2f) return DayPhase.Golden;
        return DayPhase.Dusk;
    }

    public static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Bump(float h, float c, float w)
    {
        float d = (h - c) / w;
        return MathF.Exp(-d * d);
    }

    // Palette keyframes across 24 h: top, mid, bot, glow, ground, groundHi, silhouette, accent.
    private static readonly (float H, Color[] P)[] Keys =
    {
        (0.0f,  P(6,8,22,  14,18,40,  24,30,56,  80,90,130,  8,10,18,  22,26,40,  4,6,12,  230,226,200)),
        (4.4f,  P(8,10,28, 18,22,48,  30,36,66,  90,100,140, 10,12,20, 24,28,42,  5,7,14,  230,226,200)),
        (5.3f,  P(30,30,74, 72,60,112, 150,100,122, 255,150,120, 20,18,30, 44,36,48, 14,12,22, 255,180,120)),
        (6.1f,  P(140,110,155, 232,170,150, 255,214,180, 255,190,140, 62,48,52, 96,70,64, 44,32,40, 255,186,120)),
        (7.3f,  P(72,138,210, 142,192,232, 232,226,208, 255,226,170, 44,56,44, 70,88,58, 30,36,34, 255,214,130)),
        (9.0f,  P(62,132,210, 118,186,232, 196,226,245, 255,236,170, 46,58,44, 72,92,58, 28,36,32, 255,220,120)),
        (12.5f, P(48,124,216, 108,184,242, 200,232,252, 255,244,200, 48,62,44, 76,98,60, 28,38,32, 255,224,130)),
        (15.6f, P(62,132,210, 118,186,232, 196,226,245, 255,236,170, 46,58,44, 72,92,58, 28,36,32, 255,220,120)),
        (17.0f, P(168,72,58, 232,122,58, 255,188,110, 255,200,110, 52,32,30, 92,52,36, 38,22,26, 255,196,110)),
        (18.1f, P(70,42,92, 196,92,82, 255,160,100, 255,150,90, 36,22,28, 70,40,40, 24,14,22, 255,170,100)),
        (19.0f, P(28,24,62, 74,48,96, 176,86,78, 255,140,90, 22,18,28, 48,32,42, 12,10,20, 255,176,110)),
        (20.0f, P(8,10,26, 16,20,44, 28,34,62, 84,94,134, 9,11,19, 22,26,40, 4,6,12, 230,226,200)),
        (24.0f, P(6,8,22,  14,18,40,  24,30,56,  80,90,130,  8,10,18,  22,26,40,  4,6,12,  230,226,200)),
    };

    private static Color[] P(params int[] v)
    {
        var c = new Color[v.Length / 3];
        for (int i = 0; i < c.Length; i++) c[i] = Color.FromArgb(255, v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);
        return c;
    }

    public static Color[] PaletteAt(float h)
    {
        h = ((h % 24f) + 24f) % 24f;
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var (h0, p0) = Keys[i];
            var (h1, p1) = Keys[i + 1];
            if (h < h0 || h > h1) continue;
            float t = Smooth(0f, 1f, (h - h0) / Math.Max(0.001f, h1 - h0));
            var r = new Color[p0.Length];
            for (int k = 0; k < r.Length; k++) r[k] = Mix(p0[k], p1[k], t);
            return r;
        }
        return Keys[0].P;
    }

    /// <summary>Position along the sun's day arc (0 rise .. 1 set).</summary>
    public static float SunArcT(float h) => (h - SunRise) / (SunSet - SunRise);

    /// <summary>Position along the moon's night arc (0 rise at dusk .. 0.5 midnight .. 1 set before dawn).</summary>
    public static float MoonArcT(float h)
    {
        float span = 24f - (SunSet - SunRise);
        float d = h - SunSet;
        if (d < -(SunSet - SunRise) / 2f) d += 24f; // early-morning hours belong to the previous night
        return d / span;
    }

    public static SceneMood Compose(WeatherSnapshot? live, AppConfig cfg, DateTime local)
    {
        var preview = (cfg.EffectiveWeather ?? "live").Trim().ToLowerInvariant();
        var season = Seasons.Parse(cfg.EffectiveSeason);
        float hour = VirtualHour(cfg, local);
        if (preview is "night" or "dem" or "đêm sao" && cfg.TimeMode == "real" && HourOverride is null && hour is > 5f and < 19.5f)
            hour = 0.5f; // real-clock mode: "Đêm sao" preview shows a night sky (cycle mode keeps its own clock)
        DayPhase phase = PhaseAtHour(hour);
        bool cycle = cfg.TimeMode is "cycle" or "day" or "night" || HourOverride is not null;
        WeatherKind kind = live is { Ok: true } ? live.Kind : WeatherKind.Cloudy;
        bool isDay = phase is DayPhase.Day or DayPhase.Dawn or DayPhase.Golden;
        float cloud = live is { Ok: true } ? Math.Clamp(live.CloudCover / 100f, 0f, 1f) : 0.55f;
        float rain = 0f;
        if (live is { Ok: true })
            rain = kind is WeatherKind.Storm ? 1f : kind is WeatherKind.Rain ? 0.7f : Math.Clamp(live.Precipitation / 6f, 0f, 1f);

        float snow = live is { Ok: true } && kind == WeatherKind.Snow ? WeatherCodes.SnowAmount(live.WeatherCode) : 0f;
        string cond = live is { Ok: true } ? live.ConditionVi : "…";
        string temp = live is { Ok: true }
            ? live.TemperatureText(cfg.WeatherUnits == "fahrenheit")
            : "—";
        string place = ShortPlace(live?.LocationName);

        switch (preview)
        {
            case "sunny":
            case "nang":
            case "nắng đẹp":
                kind = WeatherKind.Clear;
                cloud = 0.12f; rain = 0f; cond = "Nắng đẹp";
                break;
            case "cloudy":
            case "may":
            case "mây trôi":
                kind = WeatherKind.Cloudy;
                cloud = 0.85f; rain = 0f; cond = "Mây trôi";
                break;
            case "rain":
            case "mua":
            case "mưa rào":
                kind = WeatherKind.Rain;
                cloud = 0.9f; rain = 0.85f; cond = "Mưa rào";
                break;
            case "storm":
            case "giong":
            case "giông bão":
                kind = WeatherKind.Storm;
                cloud = 1f; rain = 1f; cond = "Giông bão";
                break;
            case "snow":
            case "tuyet":
            case "tuyết rơi":
                kind = WeatherKind.Snow;
                cloud = 0.85f; rain = 0f; snow = 0.75f; cond = "Tuyết rơi";
                break;
            case "night":
            case "dem":
            case "đêm sao":
                kind = WeatherKind.Clear;
                cloud = 0.15f; rain = 0f; cond = "Đêm sao";
                break;
            default:
                // live weather supplies the sky condition; the scene clock supplies the time.
                if (kind == WeatherKind.Night) kind = WeatherKind.Clear;
                if (!cycle && live is { Ok: true, IsDay: false } && phase == DayPhase.Day)
                    phase = DayPhase.Dusk;
                break;
        }

        // Palettes: smooth keyframe interpolation across the whole 24 h cycle.
        var pal = PaletteAt(hour);
        Color top = pal[0], mid = pal[1], bot = pal[2], glow = pal[3],
              ground = pal[4], groundHi = pal[5], sil = pal[6], accent = pal[7];

        // Weather tints
        if (kind == WeatherKind.Snow)
        {
            // v5.2 winter sky: pale icy blue-gray by day (kept mid-tone so light taskbar icons
            // stay readable), deep cozy indigo at night, pastel frost-pink at dawn / sunset.
            float dk = Smooth(5.2f, 7.0f, hour) * (1f - Smooth(17.6f, 19.4f, hour));
            float wk = Math.Max(Bump(hour, 6.1f, 0.9f), Bump(hour, 17.7f, 0.9f));
            var sTop = Mix(C(255, 16, 20, 48), C(255, 98, 118, 144), dk);
            var sMid = Mix(C(255, 28, 34, 72), C(255, 122, 140, 162), dk);
            var sBot = Mix(C(255, 48, 56, 98), C(255, 150, 166, 186), dk);
            top = Mix(top, sTop, 0.8f);
            mid = Mix(mid, sMid, 0.82f);
            bot = Mix(bot, sBot, 0.85f);
            mid = Mix(mid, C(255, 168, 140, 170), 0.35f * wk);
            bot = Mix(bot, C(255, 212, 166, 186), 0.5f * wk);
            glow = Mix(glow, Mix(C(255, 150, 170, 230), C(255, 236, 214, 226), Math.Max(dk, wk)), 0.55f);
            groundHi = Mix(C(255, 70, 80, 116), C(255, 160, 172, 190), dk);
            ground = Mix(C(255, 40, 46, 76), C(255, 116, 128, 148), dk);
            groundHi = Mix(groundHi, C(255, 196, 160, 180), 0.3f * wk);
            sil = Mix(sil, C(255, 34, 40, 62), 0.35f);
        }
        else if (kind is WeatherKind.Cloudy or WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Fog)
        {
            float k = kind == WeatherKind.Storm ? 0.72f : kind == WeatherKind.Fog ? 0.45f : 0.50f;
            top = Mix(top, C(255, 70, 80, 96), k);
            mid = Mix(mid, C(255, 110, 118, 128), k);
            bot = Mix(bot, C(255, 150, 156, 164), k * 0.8f);
            glow = Mix(glow, C(255, 180, 186, 196), 0.4f);
            if (kind == WeatherKind.Storm)
            {
                top = Mix(top, C(255, 24, 28, 40), 0.45f);
                mid = Mix(mid, C(255, 40, 48, 64), 0.4f);
                bot = Mix(bot, C(255, 58, 64, 78), 0.35f);
            }
            if (kind == WeatherKind.Fog)
            {
                mid = Mix(mid, C(255, 190, 196, 202), 0.5f);
                bot = Mix(bot, C(255, 210, 214, 216), 0.55f);
            }
        }

        // v5.4 seasonal palette. Summer is the unchanged look of earlier versions; the others tint the sky and ground and
        // are scaled by daylight so the night sky stays dark.
        {
            float dkS = Smooth(5.2f, 7.0f, hour) * (1f - Smooth(17.6f, 19.4f, hour));
            float k = 0.3f + 0.7f * dkS;
            switch (season)
            {
                case SeasonId.Spring:
                    top = Mix(top, C(255, 112, 168, 220), 0.10f * k);
                    bot = Mix(bot, C(255, 250, 204, 220), 0.16f * k);
                    glow = Mix(glow, C(255, 255, 196, 214), 0.3f * k);
                    ground = Mix(ground, C(255, 84, 146, 84), 0.26f * k);
                    groundHi = Mix(groundHi, C(255, 128, 198, 112), 0.24f * k);
                    break;
                case SeasonId.Autumn:
                    mid = Mix(mid, C(255, 216, 152, 86), 0.18f * k);
                    bot = Mix(bot, C(255, 238, 162, 88), 0.24f * k);
                    glow = Mix(glow, C(255, 255, 168, 78), 0.38f * k);
                    ground = Mix(ground, C(255, 148, 94, 46), 0.34f * k);
                    groundHi = Mix(groundHi, C(255, 198, 134, 64), 0.32f * k);
                    sil = Mix(sil, C(255, 58, 40, 30), 0.10f);
                    break;
                case SeasonId.Winter when kind != WeatherKind.Snow:
                    top = Mix(top, C(255, 94, 114, 152), 0.24f * k);
                    mid = Mix(mid, C(255, 138, 156, 186), 0.24f * k);
                    bot = Mix(bot, C(255, 178, 192, 212), 0.26f * k);
                    glow = Mix(glow, C(255, 190, 206, 238), 0.3f * k);
                    ground = Mix(ground, C(255, 126, 138, 160), 0.38f * k);
                    groundHi = Mix(groundHi, C(255, 172, 184, 204), 0.34f * k);
                    break;
            }
        }

        if (phase is DayPhase.Night or DayPhase.Dusk) isDay = false;

        float cloudiness = kind switch
        {
            WeatherKind.Clear or WeatherKind.Night => Math.Min(cloud, 0.25f),
            WeatherKind.Cloudy => Math.Max(cloud, 0.65f),
            WeatherKind.Fog => 0.8f,
            WeatherKind.Rain or WeatherKind.Storm => Math.Max(cloud, 0.8f),
            WeatherKind.Snow => Math.Max(cloud, 0.7f),
            _ => cloud
        };
        float rainAmt = kind switch
        {
            WeatherKind.Storm => 1f,
            WeatherKind.Rain => Math.Max(0.55f, rain),
            _ => 0f
        };

        float dayK = Smooth(5.2f, 7.0f, hour) * (1f - Smooth(17.6f, 19.4f, hour));
        float lightsK = Math.Max(1f - Smooth(5.6f, 6.9f, hour), Smooth(17.3f, 18.8f, hour));
        if (kind is WeatherKind.Storm) lightsK = Math.Max(lightsK, 0.6f);
        else if (kind is WeatherKind.Rain or WeatherKind.Fog) lightsK = Math.Max(lightsK, 0.3f);
        else if (kind is WeatherKind.Snow) lightsK = Math.Max(lightsK, 0.25f);
        float starsK = Math.Max(1f - Smooth(4.6f, 5.9f, hour), Smooth(18.7f, 19.9f, hour));
        float warmK = Math.Max(Bump(hour, 6.1f, 0.9f), Bump(hour, 17.7f, 0.9f));
        float sunT = SunArcT(hour), moonT = MoonArcT(hour);
        float bodyX = sunT is >= 0f and <= 1f ? sunT : Math.Clamp(moonT, 0f, 1f);

        return new SceneMood(phase, kind, isDay, cloudiness, rainAmt,
            top, mid, bot, glow, ground, groundHi, sil, accent,
            cond, temp, place, live is { Ok: true } || preview != "live",
            0.03f + 0.94f * bodyX, MoonAmount(local))
        {
            Hour = hour, DayK = dayK, LightsK = lightsK, StarsK = starsK, WarmK = warmK,
            SunT = sunT, MoonT = moonT, SnowAmt = kind == WeatherKind.Snow ? Math.Clamp(snow <= 0 ? 0.7f : snow, 0f, 1f) : 0f,
            Season = season,
            SnowCover = kind == WeatherKind.Snow ? Math.Clamp(snow <= 0 ? 0.7f : snow, 0f, 1f) : season == SeasonId.Winter ? 0.34f : 0f
        };
    }

    public static string ShortPlace(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Bà Rịa";
        var n = name.Trim();
        var f = RegionCatalog.Fold(n);
        if (f.Contains("ho chi minh") || f.Contains("saigon")) return "TP.HCM";
        if (f.Contains("ba ria")) return "Bà Rịa";
        if (f.Contains("vung tau")) return "Vũng Tàu";
        if (f.Contains("ha noi") || f.Contains("hanoi")) return "Hà Nội";
        if (f.Contains("da nang")) return "Đà Nẵng";
        if (f.Contains("hoi an")) return "Hội An";
        if (n.StartsWith("Thanh Pho ", StringComparison.OrdinalIgnoreCase))
            n = n["Thanh Pho ".Length..].Trim();
        if (n.StartsWith("Thành phố ", StringComparison.OrdinalIgnoreCase))
            n = n["Thành phố ".Length..].Trim();
        int comma = n.IndexOf(',');
        if (comma > 0) n = n[..comma].Trim();
        return n.Length > 18 ? n[..18] + "…" : n;
    }

    private static float MoonAmount(DateTime local)
    {
        // Rough synodic cycle, aesthetic only (not an ephemeris).
        var origin = new DateTime(2026, 11, 9, 0, 0, 0); // approx full-ish
        double days = (local.Date - origin.Date).TotalDays;
        double phase = (days % 29.53 + 29.53) % 29.53;
        // 0 new, 14.7 full
        return (float)(0.5 - 0.5 * Math.Cos(phase / 29.53 * Math.PI * 2));
    }

    public static Color C(int a, int r, int g, int b) => Color.FromArgb(a, r, g, b);

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Color WithA(Color c, int a) => Color.FromArgb(Math.Clamp(a, 0, 255), c);
}

/// <summary>Animated 48px panoramic weather diorama painted into the taskbar overlay.</summary>
internal sealed partial class DioramaRenderer
{
    private readonly Random _rng = new(11);
    private int _w, _h;
    private GraphicsPath? _horizon;
    private GraphicsPath? _trees;
    private GraphicsPath? _city;
    private PointF[]? _rail;
    private float _groundTop;
    private float _railY;

    private Cloud[] _farClouds = Array.Empty<Cloud>();
    private Cloud[] _nearClouds = Array.Empty<Cloud>();
    private Drop[] _farRain = Array.Empty<Drop>();
    private Drop[] _nearRain = Array.Empty<Drop>();
    private Ripple[] _ripples = Array.Empty<Ripple>();
    private Puddle[] _puddles = Array.Empty<Puddle>();
    private Star[] _stars = Array.Empty<Star>();
    private Puff[] _steam = Array.Empty<Puff>();
    private Bird[] _birds = Array.Empty<Bird>();
    private Firefly[] _flies = Array.Empty<Firefly>();
    private Lamp[] _lamps = Array.Empty<Lamp>();
    private float[] _grassX = Array.Empty<float>();

    /// <summary>0..1 position of the most recently drawn train across the bar; the ambient sound follows it. -1 = none.</summary>
    internal static volatile float TrainProgress = -1f;
    /// <summary>0 (standing at the platform) .. 1 (running) for the train drawn last; drives the rolling sound.</summary>
    internal static volatile float TrainSpeed = 1f;
    /// <summary>0..1 position of the airplane across the bar, or -1 while it is away; its sound follows it.</summary>
    internal static volatile float PlaneX01 = -1f;
    /// <summary>0..1 engine effort of the airplane drawn last.</summary>
    internal static volatile float PlaneThrust;
    /// <summary>Preview / test hook: pin the cycle position (0..1) of the train / airplane instead of following the clock.</summary>
    internal static double? DebugTrainCycle, DebugPlaneCycle;
    private float _trainX = -400;
    private float _bikeX = 200;
    private readonly RegionScenery _scenery = new();
    private RegionProfile _region = RegionCatalog.Default;
    private List<(float L, float R)> _spans = new();
    private double _trainPhase0;
    private bool _trainSeeded;
    private bool _trainSeededWithSpans;
    private float _trainSeedTime;
    private float _lastSteamEmit = -1;
    private const float TrainScale = 0.8f;
    private float _trainLights;
    private TrainState _trainState;
    private float _platL, _platR;
    private float _frost;
    private Color _frostCol;
    private float _headX0 = -1, _headX1 = -1, _headY;
    private Flake[] _snowFar = Array.Empty<Flake>();
    private Flake[] _snowMid = Array.Empty<Flake>();
    private Flake[] _snowNear = Array.Empty<Flake>();
    private float _snowLast = -1;
    private const float TrainW = 176f * TrainScale;
    public string ActiveRegionId => _scenery.ActiveId;
    internal RegionScenery Scenery => _scenery;
    private float _balloonX = 0.35f;
    private float _flash;          // 0..1 lightning envelope
    private float _flashX;
    private long _lightCursor;
    /// <summary>The strikes this renderer flashes come from here, and so does the thunder (see <see cref="LightningSchedule"/>).</summary>
    internal LightningSchedule Lightning { get; set; } = LightningSchedule.Shared;
    /// <summary>0..1 brightness of the flash right now (the overlay uses it too).</summary>
    internal float FlashLevel => _flash;
    private float _shootX = -1, _shootY, _shootLife;
    private bool _ready;

    private struct Cloud { public float X, Y, W, H, Speed, Alpha; public int Seed; }
    private struct Drop { public float X, Y, Vx, Vy, Len, Alpha; }
    private struct Flake { public float X, Y, Vy, Phase, Freq, Sway, Size, Alpha, Glint; }
    private struct Ripple { public float X, Y, Life, Max; }
    private struct Puddle { public float X, W; }
    private struct Star { public float X, Y, Size, Phase, Speed; }
    private struct Puff { public float X, Y, Vx, Vy, Life, Max, Size; public bool On; }
    private struct Bird { public float X, Y, Speed, Amp, Phase, Wing; public bool On; }
    private struct Firefly { public float X, Y, Phase, Speed, Size; }
    private struct Lamp { public float X, H; }

    public void Ensure(int width, int height)
    {
        if (_ready && _w == width && _h == height) return;
        _w = Math.Max(1, width);
        _h = Math.Max(1, height);
        _groundTop = _h * 0.72f;
        _railY = _h - Math.Max(7f, _h * 0.20f);
        BuildHorizon();
        InitActors();
        _ready = true;
    }

    public void Reset()
    {
        _ready = false;
        _horizon?.Dispose(); _horizon = null;
        _trees?.Dispose(); _trees = null;
        _city?.Dispose(); _city = null;
    }

    /// <summary>Region + free spans (logical units) where landmarks may stand.</summary>
    public void SetLayout(RegionProfile region, List<(float L, float R)> spans)
    {
        _region = region;
        _spans = spans;
    }

    public void Draw(Graphics g, FrameContext ctx, SceneMood mood, float time)
    {
        if (!ctx.Cfg.ShowTrain) { _headX0 = -1; _frost = 0; TrainProgress = -1f; TrainSpeed = 1f; }
        if (!ctx.Cfg.ShowAirplane) { PlaneX01 = -1f; PlaneThrust = 0f; }
        _scenery.Build(_region, _w, _h, _groundTop, _spans);

        DrawSky(g, mood, time);
        // v5.1: stars fade with StarsK; the sun and moon follow the scene clock along an arc.
        if (mood.StarsK > 0.01f && (ctx.Cfg.ShowWeatherEffects || ctx.Cfg.ShowAmbientDetails)) DrawStars(g, mood, time);
        if (mood.MoonT is > -0.02f and < 1.02f) DrawMoon(g, mood, time);
        if (mood.SunT is > -0.02f and < 1.02f) DrawSun(g, mood, time);

        if (ctx.Cfg.ShowWeatherEffects || mood.IsCloudy)
            DrawClouds(g, mood, time, far: true);

        StepLightning(mood, ctx.Cfg.ShowWeatherEffects);
        if (mood.IsStorm && ctx.Cfg.ShowWeatherEffects)
            DrawLightning(g, mood, time);

        if (mood.IsRainy && ctx.Cfg.ShowWeatherEffects)
            DrawRain(g, mood, time, near: false);

        bool snow = mood.IsSnow && ctx.Cfg.ShowWeatherEffects;
        if (snow) StepSnow(time);

        if (ctx.Cfg.ShowAmbientDetails && mood.IsClear && mood.DayK > 0.05f)
            DrawBalloon(g, mood, time);

        // v5 regional horizon: far mountains/filler skyline, water + boats, landmarks.
        _scenery.DrawFar(g, mood, time);
        if (snow) DrawSnowLayer(g, mood, time, _snowFar, 0); // distant flakes in front of far hills, behind landmarks
        _scenery.DrawWater(g, mood, time, 0);
        _scenery.DrawLandmarks(g, mood, time);

        DrawHorizon(g, mood, time);
        if (mood.SnowCover > 0.01f) DrawSnowGround(g, mood, time);
        if (ctx.Cfg.ShowAmbientDetails)
            DrawLampsAndWindows(g, mood, time);
        if (snow) DrawSnowLayer(g, mood, time, _snowMid, 1);

        if (ctx.Cfg.ShowWeatherEffects && mood.IsCloudy)
            DrawClouds(g, mood, time, far: false);

        if (ctx.Cfg.ShowTrain)
            DrawTrain(g, mood, time, ctx.Cfg);

        // v5.4: the runway exists only inside the airport span, so road traffic keeps out of it.
        if (ctx.Cfg.ShowCyclist)
            _scenery.DrawRoadTraffic(g, mood, time);

        // v5.4 take-off strip + airplane: drawn after the road traffic so the plane is never hidden behind a passing car.
        if (ctx.Cfg.ShowAirplane)
            DrawFlight(g, mood, time, ctx.Cfg);

        if (mood.IsRainy && ctx.Cfg.ShowWeatherEffects)
        {
            DrawRain(g, mood, time, near: true);
            DrawRipplesAndPuddles(g, mood, time);
        }

        if (snow) DrawSnowLayer(g, mood, time, _snowNear, 2);

        if (mood.IsFog && ctx.Cfg.ShowWeatherEffects)
            DrawFog(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails)
            DrawGrass(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails && mood.DayK > 0.05f && !mood.IsStorm)
            DrawBirds(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails && mood.StarsK > 0.05f && mood.IsClear)
            DrawFirefliesAndShootingStar(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails)
            _scenery.DrawPetals(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails && ctx.Cfg.ShowWeatherEffects && mood.Season is SeasonId.Spring or SeasonId.Autumn && !mood.IsRainy && !mood.IsSnow)
            DrawSeasonDrift(g, mood, time);
    }

    // v5.4: spring blossom petals and autumn leaves drifting across every region.
    private struct Drift { public float X, Y, Vx, Vy, Phase, Size; public int Tone; }
    private Drift[] _drift = Array.Empty<Drift>();
    private SeasonId _driftSeason = SeasonId.Summer;
    private float _driftLast = -1;

    private void DrawSeasonDrift(Graphics g, SceneMood mood, float time)
    {
        if (_drift.Length == 0 || _driftSeason != mood.Season)
        {
            _driftSeason = mood.Season;
            _drift = new Drift[mood.Season == SeasonId.Autumn ? 22 : 24];
            for (int i = 0; i < _drift.Length; i++)
                _drift[i] = new Drift
                {
                    X = (float)_rng.NextDouble() * _w, Y = (float)_rng.NextDouble() * _h,
                    Vx = 3 + (float)_rng.NextDouble() * 6, Vy = 2.5f + (float)_rng.NextDouble() * 4,
                    Phase = (float)_rng.NextDouble() * 6.28f, Size = 1.2f + (float)_rng.NextDouble() * 1.1f, Tone = i % 3
                };
        }
        float dt = _driftLast < 0 ? 1f / 30f : Math.Clamp(time - _driftLast, 0f, 0.1f);
        _driftLast = time;
        bool autumn = mood.Season == SeasonId.Autumn;
        Color[] leaf =
        {
            Color.FromArgb(235, 214, 98, 36), Color.FromArgb(235, 232, 164, 48), Color.FromArgb(235, 168, 60, 38)
        };
        var dim = SceneClock.Mix(Color.White, mood.Silhouette, 0.45f * (1f - mood.DayK));
        for (int i = 0; i < _drift.Length; i++)
        {
            var d = _drift[i];
            d.X += (d.Vx + MathF.Sin(time * 0.9f + d.Phase) * 3f) * dt;
            d.Y += (d.Vy + MathF.Cos(time * 1.4f + d.Phase) * 1.4f) * dt;
            if (d.X > _w + 4) d.X = -4;
            if (d.Y > _h + 2) { d.Y = -2; d.X = (float)_rng.NextDouble() * _w; }
            _drift[i] = d;
            var c = autumn ? leaf[d.Tone] : Color.FromArgb(215, 255, 190, 214);
            c = SceneClock.Mix(c, dim, 0.35f * (1f - mood.DayK));
            using var br = new SolidBrush(c);
            float s = d.Size * (0.8f + 0.2f * MathF.Sin(time * 3 + d.Phase));
            var st = g.Save();
            g.TranslateTransform(d.X, d.Y);
            g.RotateTransform((time * 90f + d.Phase * 57f) % 360f);
            g.FillEllipse(br, -s * 0.9f, -s * 0.5f, s * 1.8f, s);
            g.Restore(st);
        }
    }

    #region sky & celestial

    private void DrawSky(Graphics g, SceneMood mood, float time)
    {
        var prev = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using (var br = new LinearGradientBrush(
                   new Rectangle(0, -1, _w, _h + 2),
                   mood.SkyTop, mood.SkyBot, LinearGradientMode.Vertical))
        {
            var blend = new ColorBlend
            {
                Colors = new[] { mood.SkyTop, mood.SkyMid, mood.SkyBot },
                Positions = new[] { 0f, 0.48f, 1f }
            };
            br.InterpolationColors = blend;
            g.FillRectangle(br, 0, 0, _w, _h);
        }
        g.CompositingMode = prev;

        // Horizon wash (sunrise / sunset / moon glow)
        int ga = (int)(40 * (1f - mood.DayK) + (mood.IsClear ? 70 : 30) * mood.DayK + 120 * mood.WarmK);
        if (ga > 0)
        {
            using var path = new GraphicsPath();
            float cx = mood.SunX * _w;
            path.AddEllipse(cx - _w * 0.45f, _h * 0.15f, _w * 0.9f, _h * 1.6f);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = SceneClock.WithA(mood.HorizonGlow, ga);
            pgb.SurroundColors = new[] { SceneClock.WithA(mood.HorizonGlow, 0) };
            pgb.CenterPoint = new PointF(cx, _h * 0.85f);
            g.FillPath(pgb, path);
        }

        if (mood.IsStorm && _flash > 0.02f)
        {
            using var flash = new SolidBrush(Color.FromArgb((int)(_flash * 90), 210, 220, 245));
            g.FillRectangle(flash, 0, 0, _w, _h);
        }

        // Hairline top edge
        using var edge = new Pen(Color.FromArgb(36, 255, 255, 255), 1f);
        g.DrawLine(edge, 0, 0, _w, 0);
    }

    private RectangleF _badgeLogical = RectangleF.Empty;

    /// <summary>Weather badge rectangle in logical units, so the sun/moon can step back behind it.</summary>
    public void SetBadge(RectangleF logical) => _badgeLogical = logical;

    private bool _skyInGaps;

    /// <summary>
    /// Safe mode (icons cut out of the overlay): run the sun/moon arc only through the visible
    /// gaps between icon groups so the orb is never hidden under icons for hours.
    /// Fused mode (scene behind the taskbar): use the full width.
    /// </summary>
    public void SetSkyMapping(bool gapsOnly) => _skyInGaps = gapsOnly;

    private float ArcX(float t, float r)
    {
        float full = _w * (0.03f + 0.94f * t);
        if (!_skyInGaps || _spans.Count == 0) return full;
        float inset = r * 1.2f;
        float total = 0;
        foreach (var (l, rr) in _spans)
            if (rr - l > inset * 2 + r * 2) total += rr - l - inset * 2;
        if (total <= 0) return full;
        float pos = Math.Clamp(t, 0f, 1f) * total;
        float lastR = full;
        foreach (var (l, rr) in _spans.OrderBy(s => s.L))
        {
            float len = rr - l - inset * 2;
            if (len <= r * 2) continue;
            if (pos <= len) return l + inset + pos;
            pos -= len;
            lastR = rr - inset;
        }
        return lastR;
    }

    /// <summary>Point on the celestial arc: rises at the left/east horizon, peaks near the top, sets right/west.</summary>
    internal float GroundTop => _groundTop;

    internal (float X, float Y, float Elev) ArcPoint(float t, float r)
    {
        t = Math.Clamp(t, -0.05f, 1.05f);
        float elev = MathF.Max(0f, MathF.Sin(MathF.PI * Math.Clamp(t, 0f, 1f)));
        float x = ArcX(t, r);
        float horizon = _groundTop + r * 0.35f;      // sinks behind the skyline at the ends
        float top = r + 1.6f;                        // just under the top edge at noon / midnight
        float y = horizon - elev * (horizon - top);
        return (x, y, elev);
    }

    /// <summary>1 normally, fades toward 0.22 when the orb sits behind the weather badge.</summary>
    private float BadgeFade(float cx, float cy, float r)
    {
        if (_badgeLogical.IsEmpty) return 1f;
        var b = RectangleF.Inflate(_badgeLogical, r, r);
        float dx = Math.Max(0f, Math.Max(b.Left - cx, cx - b.Right));
        float dy = Math.Max(0f, Math.Max(b.Top - cy, cy - b.Bottom));
        float d = MathF.Sqrt(dx * dx + dy * dy);
        return 0.22f + 0.78f * SceneClock.Smooth(0f, r * 2.5f, d);
    }

    private float WeatherVeil(SceneMood mood)
        => Math.Clamp(1f - mood.Cloudiness * 0.6f - (mood.IsRainy ? 0.25f : 0f) - (mood.IsSnow ? 0.2f : 0f) - (mood.IsStorm ? 0.25f : 0f), 0.12f, 1f);

    private void DrawSun(Graphics g, SceneMood mood, float time)
    {
        float t = mood.SunT;
        float r0 = _h * 0.13f;
        var (_, _, e0) = ArcPoint(t, r0);
        float r = r0 * (1f + 0.25f * (1f - e0));     // a touch larger near the horizon
        var (cx, cy, elev) = ArcPoint(t, r);
        float edge = SceneClock.Smooth(-0.02f, 0.05f, t) * (1f - SceneClock.Smooth(0.95f, 1.02f, t));
        float k = edge * WeatherVeil(mood) * BadgeFade(cx, cy, r);
        if (k < 0.01f) return;

        // golden-orange at the horizon -> warm white at noon
        float hi = SceneClock.Smooth(0.05f, 0.6f, elev);
        var coreCol = SceneClock.Mix(Color.FromArgb(255, 255, 148, 64), Color.FromArgb(255, 255, 246, 216), hi);
        var glowCol = SceneClock.Mix(Color.FromArgb(255, 255, 140, 70), Color.FromArgb(255, 255, 230, 160), hi);

        using (var path = new GraphicsPath())
        {
            float gr = r * (3.4f + 0.8f * (1f - elev));
            path.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = SceneClock.WithA(glowCol, (int)(105 * k));
            pgb.SurroundColors = new[] { SceneClock.WithA(glowCol, 0) };
            pgb.CenterPoint = new PointF(cx, cy);
            g.FillPath(pgb, path);
        }
        if (mood.IsClear && elev > 0.12f)
        {
            int sa = (int)(15 * k * SceneClock.Smooth(0.12f, 0.4f, elev));
            using var shaft = new SolidBrush(SceneClock.WithA(glowCol, sa));
            for (int i = 0; i < 6; i++)
            {
                float ang = i * MathF.PI / 3f + time * 0.05f;
                var pts = new[]
                {
                    new PointF(cx, cy),
                    new PointF(cx + MathF.Cos(ang - 0.07f) * _h * 1.6f, cy + MathF.Sin(ang - 0.07f) * _h * 1.6f),
                    new PointF(cx + MathF.Cos(ang + 0.07f) * _h * 1.6f, cy + MathF.Sin(ang + 0.07f) * _h * 1.6f)
                };
                g.FillPolygon(shaft, pts);
            }
        }
        using var rim = new SolidBrush(SceneClock.WithA(coreCol, (int)(85 * k)));
        g.FillEllipse(rim, cx - r * 1.3f, cy - r * 1.3f, r * 2.6f, r * 2.6f);
        using var core = new SolidBrush(SceneClock.WithA(coreCol, (int)(245 * k)));
        g.FillEllipse(core, cx - r, cy - r, r * 2, r * 2);
        using var hiDot = new SolidBrush(Color.FromArgb((int)(110 * k), 255, 255, 240));
        g.FillEllipse(hiDot, cx - r * 0.55f, cy - r * 0.6f, r * 0.8f, r * 0.8f);
    }

    private void DrawStars(Graphics g, SceneMood mood, float time)
    {
        if (_stars.Length == 0) return;
        for (int i = 0; i < _stars.Length; i++)
        {
            ref var s = ref _stars[i];
            float tw = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(time * s.Speed + s.Phase));
            // brighter stars appear first at dusk and linger longest at dawn
            float own = SceneClock.Smooth(i % 3 * 0.18f, 0.55f + i % 3 * 0.18f, mood.StarsK);
            int a = Math.Clamp((int)(tw * 210 * own * (1f - mood.Cloudiness * 0.7f)), 0, 230);
            if (a < 8) continue;
            using var br = new SolidBrush(Color.FromArgb(a, 230, 236, 255));
            g.FillEllipse(br, s.X, s.Y, s.Size, s.Size);
        }
    }

    private Color SkyAt(SceneMood mood, float y)
    {
        float f = Math.Clamp(y / Math.Max(1f, _h), 0f, 1f);
        return f < 0.48f ? SceneClock.Mix(mood.SkyTop, mood.SkyMid, f / 0.48f)
                         : SceneClock.Mix(mood.SkyMid, mood.SkyBot, (f - 0.48f) / 0.52f);
    }

    private void DrawMoon(Graphics g, SceneMood mood, float time)
    {
        float t = mood.MoonT;
        float mr = _h * 0.115f;
        var (mx, my, elev) = ArcPoint(t, mr);
        float edge = SceneClock.Smooth(-0.02f, 0.06f, t) * (1f - SceneClock.Smooth(0.94f, 1.02f, t));
        // the moon washes out as the daylight grows
        float k = edge * (1f - 0.85f * mood.DayK) * WeatherVeil(mood) * BadgeFade(mx, my, mr);
        if (k < 0.01f) return;
        var tint = SceneClock.Mix(Color.FromArgb(255, 255, 214, 160), Color.FromArgb(255, 238, 236, 220), SceneClock.Smooth(0.05f, 0.5f, elev));

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(mx - mr * 3.2f, my - mr * 3.2f, mr * 6.4f, mr * 6.4f);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = Color.FromArgb((int)((45 + 45 * mood.MoonPhase) * k), 200, 212, 255);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 200, 210, 255) };
            pgb.CenterPoint = new PointF(mx, my);
            g.FillPath(pgb, path);
        }
        using var moon = new SolidBrush(SceneClock.WithA(tint, (int)(235 * k)));
        g.FillEllipse(moon, mx - mr, my - mr, mr * 2, mr * 2);
        using var mare = new SolidBrush(Color.FromArgb((int)(36 * k), 120, 120, 130));
        g.FillEllipse(mare, mx - mr * 0.45f, my - mr * 0.35f, mr * 0.5f, mr * 0.45f);
        g.FillEllipse(mare, mx + mr * 0.05f, my + mr * 0.1f, mr * 0.4f, mr * 0.35f);
        // Crescent bite when not full (painted with the sky colour behind the moon)
        if (mood.MoonPhase < 0.85f)
        {
            float bite = 0.35f + (1f - mood.MoonPhase) * 0.55f;
            using var hole = new SolidBrush(SceneClock.WithA(SkyAt(mood, my), (int)(255 * Math.Min(1f, k * 1.1f))));
            g.FillEllipse(hole, mx - mr + mr * bite, my - mr * 1.05f, mr * 2.05f, mr * 2.1f);
        }
    }

    #endregion

    #region weather layers

    private void DrawClouds(Graphics g, SceneMood mood, float time, bool far)
    {
        var arr = far ? _farClouds : _nearClouds;
        float mul = far ? 0.55f : 1f;
        float wind = mood.IsStorm ? 1.8f : mood.IsRainy ? 1.25f : 0.7f;
        int baseA = far ? 70 : 110;
        if (mood.IsFog) baseA = (int)(baseA * 0.7f);
        var col = SceneClock.Mix(Color.FromArgb(255, 90, 98, 120), Color.FromArgb(255, 242, 246, 250), mood.DayK);
        col = SceneClock.Mix(col, Color.FromArgb(255, 255, 196, 170), mood.WarmK * 0.7f);

        for (int i = 0; i < arr.Length; i++)
        {
            ref var c = ref arr[i];
            c.X += c.Speed * wind;
            if (c.X > _w + c.W) c.X = -c.W - 10;
            int a = Math.Clamp((int)(c.Alpha * baseA * (0.55f + mood.Cloudiness)), 0, far ? 120 : 170);
            DrawCloudPuff(g, c, SceneClock.WithA(col, a), mul);
        }
    }

    private static void DrawCloudPuff(Graphics g, Cloud c, Color col, float scale)
    {
        using var br = new SolidBrush(col);
        float x = c.X, y = c.Y, w = c.W, h = c.H;
        g.FillEllipse(br, x, y + h * 0.25f, w * 0.55f, h * 0.7f);
        g.FillEllipse(br, x + w * 0.28f, y, w * 0.5f, h * 0.75f);
        g.FillEllipse(br, x + w * 0.48f, y + h * 0.22f, w * 0.5f, h * 0.7f);
    }

    private void DrawRain(Graphics g, SceneMood mood, float time, bool near)
    {
        var arr = near ? _nearRain : _farRain;
        float speed = mood.IsStorm ? 1.45f : 1f;
        using var pen = new Pen(Color.FromArgb(0, 0, 0, 0), near ? 1.35f : 1f);
        for (int i = 0; i < arr.Length; i++)
        {
            ref var d = ref arr[i];
            d.X += d.Vx * speed;
            d.Y += d.Vy * speed;
            if (d.Y > _h + 4)
            {
                d.Y = -d.Len;
                d.X = (float)_rng.NextDouble() * _w;
                if (near && _rng.NextDouble() < 0.35 && _ripples.Length > 0)
                    SpawnRipple(d.X);
            }
            if (d.X < -8) d.X += _w + 16;
            if (d.X > _w + 8) d.X -= _w + 16;
            int a = Math.Clamp((int)(d.Alpha * (near ? 200 : 110) * (0.7f + mood.RainAmt * 0.4f)), 0, near ? 200 : 130);
            pen.Color = Color.FromArgb(a, near ? 210 : 180, near ? 228 : 206, 245);
            g.DrawLine(pen, d.X, d.Y, d.X + d.Vx * 0.9f, d.Y + d.Len);
        }
    }

    private void SpawnRipple(float x)
    {
        for (int i = 0; i < _ripples.Length; i++)
        {
            if (_ripples[i].Life <= 0)
            {
                _ripples[i] = new Ripple { X = x, Y = _h - 3.5f, Life = 1f, Max = 1f };
                return;
            }
        }
    }

    private void DrawRipplesAndPuddles(Graphics g, SceneMood mood, float time)
    {
        using var puddle = new SolidBrush(Color.FromArgb(55, 30, 50, 70));
        using var shine = new Pen(Color.FromArgb(70, 200, 220, 240), 1f);
        for (int i = 0; i < _puddles.Length; i++)
        {
            var p = _puddles[i];
            float y = _h - 3.2f;
            g.FillEllipse(puddle, p.X, y, p.W, 3.4f);
            float glint = 0.5f + 0.5f * MathF.Sin(time * 2.1f + i);
            shine.Color = Color.FromArgb((int)(40 + 40 * glint), 210, 230, 250);
            g.DrawLine(shine, p.X + 2, y + 1.1f, p.X + p.W * 0.55f, y + 1.1f);
        }
        using var rp = new Pen(Color.FromArgb(0, 0, 0, 0), 1f);
        for (int i = 0; i < _ripples.Length; i++)
        {
            ref var r = ref _ripples[i];
            if (r.Life <= 0) continue;
            r.Life -= 0.045f;
            float t = 1f - r.Life;
            int a = Math.Clamp((int)((1f - t) * 140), 0, 140);
            rp.Color = Color.FromArgb(a, 200, 220, 240);
            float rad = 1.5f + t * 6f;
            g.DrawEllipse(rp, r.X - rad, r.Y - rad * 0.35f, rad * 2, rad * 0.7f);
        }
    }

    private void DrawFog(Graphics g, SceneMood mood, float time)
    {
        for (int i = 0; i < 3; i++)
        {
            float x = ((time * (4 + i * 2) + i * 200) % (_w + 260)) - 130;
            int a = 28 + i * 10;
            using var br = new SolidBrush(Color.FromArgb(a, 210, 214, 220));
            g.FillEllipse(br, x, _h * (0.45f + i * 0.12f), _w * 0.55f, _h * 0.5f);
        }
    }

    /// <summary>Takes the strikes that are due from the shared schedule and flashes them; a flashed strike is what the thunder follows.</summary>
    private void StepLightning(SceneMood mood, bool showEffects)
    {
        var sched = Lightning;
        double now = sched.Now;
        sched.Advance(now, mood.IsStorm);
        if (!mood.IsStorm) return;
        foreach (var s in sched.StrikesSince(_lightCursor))
        {
            _lightCursor = s.Id;
            if (!showEffects || now - s.Time > 0.75) continue;      // not drawn (or long past): no flash, so no thunder either
            _flash = LightningStrike.FlashPowerFor(s.Distance);
            _flashX = _w * (0.18f + 0.64f * s.X01);
            sched.MarkShown(s);
        }
    }

    private void DrawLightning(Graphics g, SceneMood mood, float time)
    {
        if (_flash > 0) _flash = Math.Max(0, _flash - 0.08f);
        if (_flash < 0.15f) return;
        float x0 = _flashX;
        using var bolt = new Pen(Color.FromArgb((int)(_flash * 200), 230, 236, 255), 1.4f);
        using var glow = new Pen(Color.FromArgb((int)(_flash * 70), 180, 200, 255), 4f);
        var pts = new PointF[5];
        float y = 0;
        float x = x0;
        for (int i = 0; i < 5; i++)
        {
            pts[i] = new PointF(x, y);
            y += _h * 0.22f;
            x += MathF.Sin(i * 2.1f + time) * 14f;
        }
        g.DrawLines(glow, pts);
        g.DrawLines(bolt, pts);
    }

    #endregion

    #region ground, lamps, flora

    private void DrawHorizon(Graphics g, SceneMood mood, float time)
    {
        if (_horizon is not null)
        {
            using var br = new SolidBrush(mood.Silhouette);
            g.FillPath(br, _horizon);
        }
        if (_city is not null)
        {
            using var br = new SolidBrush(SceneClock.Mix(mood.Silhouette, mood.Ground, 0.15f));
            g.FillPath(br, _city);
        }
        if (_trees is not null)
        {
            using var br = new SolidBrush(mood.Silhouette);
            g.FillPath(br, _trees);
        }

        // Ground strip
        using (var ground = new LinearGradientBrush(
                   new RectangleF(0, _groundTop - 1, _w, _h - _groundTop + 2),
                   mood.GroundHi, mood.Ground, LinearGradientMode.Vertical))
        {
            g.FillRectangle(ground, 0, _groundTop, _w, _h - _groundTop + 1);
        }

        // Path / road line
        using var pathPen = new Pen(SceneClock.WithA(mood.GroundHi, 160), 1.2f);
        g.DrawLine(pathPen, 0, _h - 4.5f, _w, _h - 4.5f);

        // Rails
        if (_rail is not null)
        {
            using var rail = new Pen(Color.FromArgb((int)(80 + 20 * mood.DayK), 40, 36, 32), 1.15f);
            g.DrawLines(rail, _rail);
            using var rail2 = new Pen(Color.FromArgb((int)(60 + 20 * mood.DayK), 30, 28, 26), 1f);
            var lower = new PointF[_rail.Length];
            for (int i = 0; i < _rail.Length; i++)
                lower[i] = new PointF(_rail[i].X, _rail[i].Y + 2.1f);
            g.DrawLines(rail2, lower);
            // sleepers
            using var sl = new Pen(Color.FromArgb(50, 30, 24, 20), 1f);
            for (float x = 0; x < _w; x += 9)
                g.DrawLine(sl, x, _railY - 1, x + 3, _railY + 3.2f);
        }
    }

    private void DrawLampsAndWindows(Graphics g, SceneMood mood, float time)
    {
        float lk = mood.LightsK;
        if (lk < 0.02f) return;

        // Tiny warm windows in the city third
        int seed = 17;
        var rnd = new Random(seed + _w);
        float cityL = _w * 0.58f;
        using var win = new SolidBrush(Color.FromArgb(0, 0, 0, 0));
        for (int i = 0; i < 28; i++)
        {
            float x = cityL + (float)rnd.NextDouble() * (_w * 0.38f);
            float y = _groundTop - 4 - (float)rnd.NextDouble() * (_h * 0.32f);
            if (y < 4) continue;
            bool on = rnd.NextDouble() > 0.22;
            // a few flicker
            if (on && rnd.NextDouble() > 0.85)
                on = MathF.Sin(time * 3f + i) > -0.4f;
            if (!on) continue;
            int a = (int)(200 * lk);
            win.Color = Color.FromArgb(a, 255, 206, 120);
            g.FillRectangle(win, x, y, 1.6f, 1.8f);
        }

        using var lampPole = new Pen(Color.FromArgb(180, 20, 18, 16), 1.2f);
        using var lampHead = new SolidBrush(SceneClock.Mix(Color.FromArgb(220, 70, 66, 60), Color.FromArgb(220, 255, 220, 150), lk));
        for (int i = 0; i < _lamps.Length; i++)
        {
            var l = _lamps[i];
            float y1 = _groundTop + 2;
            g.DrawLine(lampPole, l.X, y1, l.X, y1 - l.H);
            g.FillEllipse(lampHead, l.X - 1.5f, y1 - l.H - 2.2f, 3.2f, 2.6f);
            using var cone = new GraphicsPath();
            cone.AddPolygon(new[]
            {
                new PointF(l.X, y1 - l.H),
                new PointF(l.X - 9f, y1 + 2),
                new PointF(l.X + 9f, y1 + 2)
            });
            using var pgb = new PathGradientBrush(cone);
            int ca = (int)(70 * lk);
            if (mood.IsRainy) ca = (int)(ca * 1.2f);
            if (mood.IsSnow) ca = (int)(ca * 1.35f); // lamp light scatters in the falling snow
            pgb.CenterColor = Color.FromArgb(ca, 255, 214, 140);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 214, 140) };
            pgb.CenterPoint = new PointF(l.X, y1 - l.H + 2);
            g.FillPath(pgb, cone);
        }
    }

    private void DrawGrass(Graphics g, SceneMood mood, float time)
    {
        using var pen = new Pen(SceneClock.WithA(mood.Silhouette, 180), 1f);
        float wind = MathF.Sin(time * 1.4f) * (mood.IsStorm ? 2.4f : 1.1f);
        for (int i = 0; i < _grassX.Length; i++)
        {
            float x = _grassX[i];
            float h = 3.2f + (i % 3);
            float sway = wind + MathF.Sin(time * 2f + i) * 0.6f;
            g.DrawLine(pen, x, _h - 3.5f, x + sway, _h - 3.5f - h);
        }
    }

    #endregion

    #region actors

    /// <summary>
    /// One train with a full stop-over: it comes in from the left and slows down, stops at the little platform, the doors open,
    /// passengers get off and others get on, then it departs, speeds up and leaves to the right. One lifecycle = the configured
    /// period. Position is a pure function of time (see <see cref="TrainCycle"/>), seeded so it is at the platform right after launch.
    /// </summary>
    private void DrawTrain(Graphics g, SceneMood mood, float time, AppConfig cfg)
    {
        float trainW = TrainW;
        double period = Math.Max(1.0, cfg.TrainCrossingMinutes) * 60.0;
        var spans = EffectiveSpans();
        var st0 = TrainCycle.StationSpan(spans, _w);
        _platL = st0.L + 2; _platR = st0.R - 2;
        if (_platR - _platL > 168) { float mid = (_platL + _platR) / 2; _platL = mid - 84; _platR = mid + 84; }
        if (_platR - _platL < 40) { float mid = (_platL + _platR) / 2; _platL = mid - 20; _platR = mid + 20; }
        float stopLeft = (_platL + _platR) / 2 - trainW / 2;

        if (!_trainSeeded || (!_trainSeededWithSpans && _spans.Count > 0 && time - _trainSeedTime < 6f))
        {
            // Start with the train standing at the platform (doors just open) so something is happening right after launch.
            double seed = TrainCycle.StopStart + 0.25 * TrainCycle.Plan[(int)TrainPhase.Alight].Frac;
            _trainPhase0 = seed - time / period;
            _trainSeeded = true;
            _trainSeededWithSpans = _spans.Count > 0;
            _trainSeedTime = time;
        }
        double p = DebugTrainCycle ?? SceneMath.TrainProgress(_trainPhase0, time, period);
        var ts = TrainCycle.Evaluate(p, _w, trainW, stopLeft);
        _trainState = ts;
        if (!ts.Visible)
        {
            TrainProgress = -1f; TrainSpeed = 1f; _headX0 = -1; _frost = 0;
            return;
        }
        _trainX = ts.X;
        float y = _railY - 1.2f;
        float lk = mood.LightsK;
        bool night = lk > 0.5f;
        bool rain = mood.IsRainy;
        _trainLights = lk;
        bool covered = mood.SnowCover > 0.01f;
        _frost = covered ? 0.55f + 0.45f * mood.SnowCover : 0f;
        _frostCol = covered ? RegionScenery.SnowTint(mood, 230) : Color.Empty;
        TrainProgress = Math.Clamp((_trainX + trainW) / (_w + trainW), 0f, 1f);
        TrainSpeed = ts.Speed;
        _headX0 = _trainX + trainW - 3; _headX1 = _trainX + trainW + 60; _headY = y;

        // Headlight beam (fades in with dusk)
        if (lk > 0.03f)
        {
            using var cone = new GraphicsPath();
            cone.AddPolygon(new[]
            {
                new PointF(_trainX + trainW - 3, y - 6.5f),
                new PointF(_trainX + trainW + 60, y - 13),
                new PointF(_trainX + trainW + 60, y + 3)
            });
            using var pgb = new PathGradientBrush(cone);
            pgb.CenterColor = Color.FromArgb((int)(70 * lk), 255, 236, 190);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 236, 190) };
            pgb.CenterPoint = new PointF(_trainX + trainW, y - 6f);
            g.FillPath(pgb, cone);
        }

        // 3 carriages then the locomotive at the right (LTR), drawn at 80 % so
        // the regional landmarks stay visible above the roofline.
        var st = g.Save();
        g.TranslateTransform(_trainX, y);
        g.ScaleTransform(TrainScale, TrainScale);
        float x = 0, ly = 0;
        float spin = _trainX / (3.1f * TrainScale); // wheels roll exactly with distance travelled
        DrawCarriage(g, x, ly, 38, night, rain, 0, spin, ts.Doors); x += 40;
        DrawCarriage(g, x, ly, 38, night, rain, 1, spin, ts.Doors); x += 40;
        DrawCarriage(g, x, ly, 38, night, rain, 2, spin, ts.Doors); x += 40;
        DrawLoco(g, x, ly, night, rain, spin);
        g.Restore(st);

        DrawStation(g, mood, time, ts);

        // Gentle smoke: a puff every ~0.45 s drifting up and back (world coords); lazier while standing.
        float chimX = _trainX + (x + 30.5f) * TrainScale, chimY = y - 21 * TrainScale;
        float every = ts.Speed < 0.05f ? 1.1f : 0.45f;
        if (_lastSteamEmit < 0 || time - _lastSteamEmit > every || time < _lastSteamEmit)
        {
            _lastSteamEmit = time;
            EmitSteam(chimX, chimY, time);
        }
        else AgeSteam();
        DrawSteam(g, mood, time);
    }

    private IReadOnlyList<(float L, float R)> EffectiveSpans()
        => _spans.Count > 0 ? _spans : new List<(float L, float R)> { (0f, _w) };

    private void DrawLoco(Graphics g, float x, float y, bool night, bool rain, float time)
    {
        var body = Color.FromArgb(255, 48, 36, 40);
        var cabin = Color.FromArgb(255, 62, 42, 44);
        using var b = new SolidBrush(body);
        using var c = new SolidBrush(cabin);
        using var rim = new Pen(Color.FromArgb(80, 255, 255, 255), 0.8f);
        // boiler
        g.FillRectangle(b, x + 6, y - 13, 36, 11);
        g.FillEllipse(b, x + 34, y - 13, 12, 11);
        // cabin
        g.FillRectangle(c, x, y - 18, 16, 16);
        // roof
        using var roof = new SolidBrush(Color.FromArgb(255, 36, 24, 26));
        g.FillRectangle(roof, x - 1, y - 20, 18, 3);
        if (_frost > 0)
            using (var fr = new SolidBrush(Color.FromArgb((int)(_frostCol.A * _frost), _frostCol)))
            {
                g.FillRectangle(fr, x - 1, y - 20.6f, 18, 1.4f);
                g.FillRectangle(fr, x + 8, y - 13.6f, 24, 1.1f);
            }
        // chimney
        g.FillRectangle(roof, x + 28, y - 21, 5, 9);
        // cowcatcher
        using var catcher = new SolidBrush(Color.FromArgb(255, 70, 50, 44));
        g.FillPolygon(catcher, new[]
        {
            new PointF(x + 48, y - 4), new PointF(x + 58, y + 1), new PointF(x + 44, y + 1)
        });
        // windows
        int wa = (int)(160 + 70 * Math.Max(_trainLights, rain ? 1f : 0f));
        using var win = new SolidBrush(Color.FromArgb(wa, 255, 214, 130));
        g.FillRectangle(win, x + 3, y - 16, 5, 5);
        g.FillRectangle(win, x + 9, y - 16, 5, 5);
        // headlight
        using var hl = new SolidBrush(Color.FromArgb((int)(180 + 75 * _trainLights), 255, 236, 180));
        g.FillEllipse(hl, x + 46, y - 11, 4.2f, 4.2f);
        // wheels
        DrawWheels(g, x + 6, y, 3, time);
        DrawWheels(g, x + 28, y, 2, time);
        // brass boiler bands + rim light
        using var band = new Pen(Color.FromArgb(150, 200, 160, 90), 0.7f);
        g.DrawLine(band, x + 22, y - 13, x + 22, y - 2);
        g.DrawLine(band, x + 32, y - 13, x + 32, y - 2);
        g.DrawLine(rim, x + 6, y - 12.6f, x + 42, y - 12.6f);
    }

    private void DrawCarriage(Graphics g, float x, float y, float w, bool night, bool rain, int idx, float spin, float doors)
    {
        using var body = new SolidBrush(Color.FromArgb(255, 54 + idx * 2, 38, 42));
        using var roof = new SolidBrush(Color.FromArgb(255, 34, 24, 28));
        g.FillRectangle(body, x, y - 14, w, 12);
        g.FillRectangle(roof, x - 0.5f, y - 16, w + 1, 2.4f);
        if (_frost > 0)
            using (var fr = new SolidBrush(Color.FromArgb((int)(_frostCol.A * _frost), _frostCol)))
            {
                g.FillRectangle(fr, x - 0.5f, y - 16.7f, w + 1, 1.4f);
                for (int i = 0; i < 4; i++) g.FillEllipse(fr, x + 2 + i * 10 + idx * 3 % 5, y - 17.4f, 4.5f, 1.8f);
            }
        int wa = (int)(140 + 80 * Math.Max(_trainLights, rain ? 1f : 0f));
        using var win = new SolidBrush(Color.FromArgb(wa, 255, 210, 120));
        for (int i = 0; i < 3; i++)
        {
            if (i == 1) continue; // the middle bay is the door
            g.FillRectangle(win, x + 5 + i * 11, y - 12, 6, 5);
        }
        // sliding door: a lighter leaf when shut, a warm opening while passengers get on and off
        using (var leaf = new SolidBrush(Color.FromArgb(255, 78 + idx * 2, 54, 58)))
            g.FillRectangle(leaf, x + 16, y - 12.4f, 6, 9.6f);
        if (doors > 0.01f)
            using (var open = new SolidBrush(Color.FromArgb((int)(235 * doors), 255, 226, 160)))
                g.FillRectangle(open, x + 16.6f, y - 11.8f, 4.8f * (0.35f + 0.65f * doors) , 8.8f);
        using (var seam = new Pen(Color.FromArgb(120, 24, 18, 20), 0.5f))
            g.DrawLine(seam, x + 19, y - 12.4f, x + 19, y - 2.8f);
        // coupler
        using var coup = new SolidBrush(Color.FromArgb(200, 30, 24, 24));
        g.FillRectangle(coup, x + w, y - 7, 2, 2);
        using var trim = new Pen(Color.FromArgb(120, 210, 170, 100), 0.6f);
        g.DrawLine(trim, x + 0.5f, y - 5.6f, x + w - 0.5f, y - 5.6f);
        DrawWheels(g, x + 4, y, 2, spin);
        DrawWheels(g, x + w - 16, y, 2, spin);
    }

    private void DrawWheels(Graphics g, float x, float y, int n, float spin)
    {
        using var wh = new SolidBrush(Color.FromArgb(255, 22, 20, 20));
        using var spoke = new Pen(Color.FromArgb(180, 80, 72, 68), 0.8f);
        for (int i = 0; i < n; i++)
        {
            float cx = x + i * 12, cy = y - 1.5f, r = 3.1f;
            g.FillEllipse(wh, cx - r, cy - r, r * 2, r * 2);
            g.DrawLine(spoke, cx, cy, cx + MathF.Cos(spin) * r, cy + MathF.Sin(spin) * r);
        }
    }

    private void EmitSteam(float x, float y, float time)
    {
        for (int i = 0; i < _steam.Length; i++)
        {
            if (!_steam[i].On)
            {
                _steam[i] = new Puff
                {
                    X = x + (float)_rng.NextDouble() * 2,
                    Y = y,
                    Vx = -0.10f - (float)_rng.NextDouble() * 0.10f,
                    Vy = -0.20f - (float)_rng.NextDouble() * 0.12f,
                    Life = 1f, Max = 1f,
                    Size = 2.2f + (float)_rng.NextDouble() * 1.6f,
                    On = true
                };
                break;
            }
        }
        AgeSteam();
    }

    private void AgeSteam()
    {
        for (int i = 0; i < _steam.Length; i++)
        {
            if (!_steam[i].On) continue;
            ref var p = ref _steam[i];
            p.X += p.Vx; p.Y += p.Vy;
            p.Life -= 0.012f;
            p.Size += 0.05f;
            if (p.Life <= 0 || p.Y < -4) p.On = false;
        }
    }

    private void DrawSteam(Graphics g, SceneMood mood, float time)
    {
        for (int i = 0; i < _steam.Length; i++)
        {
            if (!_steam[i].On) continue;
            var p = _steam[i];
            int a = Math.Clamp((int)(p.Life * 90), 0, 90);
            using var br = new SolidBrush(Color.FromArgb(a, 230, 230, 232));
            g.FillEllipse(br, p.X, p.Y, p.Size, p.Size * 0.85f);
        }
    }

    private void DrawCyclist(Graphics g, SceneMood mood, float time)
    {
        // Always looping LTR, slower than the train, with a short wrap gap.
        const float period = 22f;
        const float travel = 18f;
        float t = (time * 0.85f + 6f) % period;
        if (t > travel) return;
        float bikeW = 16f;
        _bikeX = -bikeW + (t / travel) * (_w + bikeW + 30);
        float y = _h - 6.2f;
        bool night = mood.IsNight || mood.Phase == DayPhase.Dusk;
        float pedal = time * 8f;

        if (night)
        {
            using var cone = new GraphicsPath();
            cone.AddPolygon(new[]
            {
                new PointF(_bikeX + 14, y - 5),
                new PointF(_bikeX + 36, y - 8),
                new PointF(_bikeX + 36, y + 1)
            });
            using var pgb = new PathGradientBrush(cone);
            pgb.CenterColor = Color.FromArgb(55, 255, 236, 190);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 236, 190) };
            pgb.CenterPoint = new PointF(_bikeX + 14, y - 5);
            g.FillPath(pgb, cone);
        }

        using var frame = new Pen(Color.FromArgb(230, 24, 22, 26), 1.15f);
        using var tire = new Pen(Color.FromArgb(230, 16, 16, 18), 1.3f);
        using var body = new SolidBrush(Color.FromArgb(230, 28, 24, 30));
        // wheels
        float r = 3.3f;
        g.DrawEllipse(tire, _bikeX, y - r * 2, r * 2, r * 2);
        g.DrawEllipse(tire, _bikeX + 9, y - r * 2, r * 2, r * 2);
        // spokes
        using var sp = new Pen(Color.FromArgb(160, 60, 60, 64), 0.7f);
        for (int w = 0; w < 2; w++)
        {
            float cx = _bikeX + r + w * 9, cy = y - r;
            g.DrawLine(sp, cx + MathF.Cos(pedal) * r, cy + MathF.Sin(pedal) * r,
                       cx - MathF.Cos(pedal) * r, cy - MathF.Sin(pedal) * r);
        }
        // frame
        g.DrawLine(frame, _bikeX + r, y - r, _bikeX + 6, y - 7);          // seat tube
        g.DrawLine(frame, _bikeX + 6, y - 7, _bikeX + 9 + r, y - r);      // top
        g.DrawLine(frame, _bikeX + r, y - r, _bikeX + 9 + r, y - r);      // chainstay
        // rider
        float bob = MathF.Sin(pedal) * 0.5f;
        g.FillEllipse(body, _bikeX + 5.2f, y - 14 + bob, 3.4f, 3.4f);     // head
        g.DrawLine(frame, _bikeX + 6.8f, y - 10.5f + bob, _bikeX + 6.2f, y - 6.5f); // torso
        // legs pedaling
        g.DrawLine(frame, _bikeX + 6.2f, y - 6.5f, _bikeX + 6.2f + MathF.Cos(pedal) * 3.2f, y - 2 + MathF.Sin(pedal) * 2);
        g.DrawLine(frame, _bikeX + 6.2f, y - 6.5f, _bikeX + 6.2f + MathF.Cos(pedal + 3.14f) * 3.2f, y - 2 + MathF.Sin(pedal + 3.14f) * 2);
        // arm
        g.DrawLine(frame, _bikeX + 6.8f, y - 10 + bob, _bikeX + 12, y - 7);

        if (mood.IsRainy)
        {
            // tiny umbrella
            using var umb = new SolidBrush(Color.FromArgb(200, 40, 50, 90));
            using var stem = new Pen(Color.FromArgb(200, 30, 30, 36), 1f);
            g.FillPie(umb, _bikeX + 3, y - 20 + bob, 12, 8, 200, 140);
            g.DrawLine(stem, _bikeX + 9, y - 16 + bob, _bikeX + 9, y - 11 + bob);
        }
        if (night)
        {
            using var hl = new SolidBrush(Color.FromArgb(255, 255, 236, 180));
            g.FillEllipse(hl, _bikeX + 13.2f, y - 6.2f, 2.2f, 2.2f);
        }
    }

    private void DrawBirds(Graphics g, SceneMood mood, float time)
    {
        using var pen = new Pen(Color.FromArgb((int)(160 * mood.DayK), 30, 30, 32), 1.05f);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        for (int i = 0; i < _birds.Length; i++)
        {
            ref var b = ref _birds[i];
            if (!b.On)
            {
                // occasional flock
                if (_rng.NextDouble() < 0.002)
                {
                    b.On = true;
                    b.X = -10;
                    b.Y = 6 + (float)_rng.NextDouble() * (_h * 0.35f);
                    b.Speed = 0.7f + (float)_rng.NextDouble() * 0.6f;
                    b.Amp = 2 + (float)_rng.NextDouble() * 2;
                    b.Phase = (float)_rng.NextDouble() * 6;
                }
                continue;
            }
            b.X += b.Speed;
            b.Wing += 0.45f;
            if (b.X > _w + 12) { b.On = false; continue; }
            float y = b.Y + MathF.Sin(time * 1.5f + b.Phase) * b.Amp;
            float flap = 2.6f + MathF.Sin(b.Wing) * 1.6f;
            g.DrawLines(pen, new[]
            {
                new PointF(b.X - 4, y + flap * 0.15f),
                new PointF(b.X, y),
                new PointF(b.X + 4, y + flap * 0.15f)
            });
        }
    }

    private void DrawFirefliesAndShootingStar(Graphics g, SceneMood mood, float time)
    {
        for (int i = 0; i < _flies.Length; i++)
        {
            ref var f = ref _flies[i];
            f.X += MathF.Sin(time * f.Speed + f.Phase) * 0.15f;
            f.Y += MathF.Cos(time * f.Speed * 0.8f + f.Phase) * 0.08f;
            float pulse = 0.5f + 0.5f * MathF.Sin(time * 3.2f + f.Phase);
            int a = Math.Clamp((int)(pulse * 200 * mood.StarsK), 0, 200);
            if (a < 20) continue;
            using var br = new SolidBrush(Color.FromArgb(a, 196, 255, 120));
            g.FillEllipse(br, f.X, f.Y, f.Size, f.Size);
        }

        if (_shootLife <= 0 && mood.StarsK > 0.8f && _rng.NextDouble() < 0.0025)
        {
            _shootX = _w * (0.15f + (float)_rng.NextDouble() * 0.5f);
            _shootY = 3 + (float)_rng.NextDouble() * 8;
            _shootLife = 1f;
        }
        if (_shootLife > 0)
        {
            _shootX += 4.5f;
            _shootY += 1.1f;
            _shootLife -= 0.04f;
            int a = Math.Clamp((int)(_shootLife * 220), 0, 220);
            using var pen = new Pen(Color.FromArgb(a, 230, 236, 255), 1.1f);
            g.DrawLine(pen, _shootX, _shootY, _shootX - 10, _shootY - 2.5f);
        }
    }

    private void DrawBalloon(Graphics g, SceneMood mood, float time)
    {
        if (mood.IsRainy || mood.IsStorm || mood.Cloudiness > 0.55f) return;
        float x = (_balloonX * _w + MathF.Sin(time * 0.12f) * 30f + time * 2.2f) % (_w + 40) - 20;
        float y = 5 + MathF.Sin(time * 0.4f) * 2.5f;
        float bk = mood.DayK;
        using var shell = new SolidBrush(Color.FromArgb((int)(180 * bk), 220, 80, 64));
        using var stripe = new SolidBrush(Color.FromArgb((int)(160 * bk), 240, 200, 120));
        using var basket = new SolidBrush(Color.FromArgb((int)(180 * bk), 70, 48, 32));
        using var line = new Pen(Color.FromArgb((int)(140 * bk), 40, 30, 24), 0.8f);
        g.FillEllipse(shell, x, y, 8, 9);
        g.FillEllipse(stripe, x + 2, y + 1, 4, 7);
        g.DrawLine(line, x + 2, y + 8, x + 3, y + 12);
        g.DrawLine(line, x + 6, y + 8, x + 5, y + 12);
        g.FillRectangle(basket, x + 2.5f, y + 11.5f, 4, 2.2f);
    }

    private void DrawNightCar(Graphics g, SceneMood mood, float time)
    {
        const float period = 16f;
        float t = (time * 1.15f + 2) % period;
        if (t > 11) return;
        float x = -20 + (t / 11f) * (_w + 40);
        float y = _h - 5.5f;
        using var body = new SolidBrush(Color.FromArgb(210, 18, 20, 26));
        g.FillRectangle(body, x, y - 4.5f, 14, 4);
        g.FillRectangle(body, x + 3, y - 7, 8, 3);
        using var head = new SolidBrush(Color.FromArgb(230, 255, 230, 160));
        using var tail = new SolidBrush(Color.FromArgb(200, 255, 60, 50));
        g.FillRectangle(head, x + 13, y - 3.5f, 2, 1.6f);
        g.FillRectangle(tail, x, y - 3.5f, 1.6f, 1.4f);
        // headlight wash
        using var cone = new GraphicsPath();
        cone.AddPolygon(new[] { new PointF(x + 15, y - 3), new PointF(x + 36, y - 6), new PointF(x + 36, y + 1) });
        using var pgb = new PathGradientBrush(cone);
        pgb.CenterColor = Color.FromArgb(40, 255, 230, 160);
        pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 230, 160) };
        pgb.CenterPoint = new PointF(x + 15, y - 3);
        g.FillPath(pgb, cone);
    }

    #endregion

    private void DimBehindIcons(Graphics g, FrameContext ctx, SceneMood mood)
    {
        // Soft calm band where Win11 clusters icons, without punching holes
        // so the panorama still reads as continuous.
        var icons = ctx.Regions.IconCluster;
        if (icons.IsEmpty)
        {
            // fallback: center 42% of the bar
            float cx = _w * 0.5f, band = _w * 0.42f;
            using var br = new LinearGradientBrush(
                new RectangleF(cx - band / 2 - 40, 0, band + 80, _h),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Horizontal);
            var cb = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(0, 8, 10, 16),
                    Color.FromArgb(50, 8, 10, 16),
                    Color.FromArgb(58, 8, 10, 16),
                    Color.FromArgb(0, 8, 10, 16)
                },
                Positions = new[] { 0f, 0.22f, 0.78f, 1f }
            };
            br.InterpolationColors = cb;
            g.FillRectangle(br, cx - band / 2 - 40, 0, band + 80, _h);
            return;
        }
        float left = icons.Left - ctx.Regions.Bounds.X - 36;
        float right = icons.Right - ctx.Regions.Bounds.X + 36;
        float w = Math.Max(40, right - left);
        using (var br = new LinearGradientBrush(
                   new RectangleF(left - 30, 0, w + 60, _h),
                   Color.FromArgb(0, 8, 10, 16), Color.FromArgb(0, 8, 10, 16),
                   LinearGradientMode.Horizontal))
        {
            var cb = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(0, 8, 10, 16),
                    Color.FromArgb(48, 8, 10, 16),
                    Color.FromArgb(56, 8, 10, 16),
                    Color.FromArgb(0, 8, 10, 16)
                },
                Positions = new[] { 0f, 0.18f, 0.82f, 1f }
            };
            br.InterpolationColors = cb;
            g.FillRectangle(br, left - 30, 0, w + 60, _h);
        }
    }

    /// <summary>
    /// Compact two-line HUD (physical pixels). level 0 = temp + condition + place,
    /// 1 = temp + condition, 2 = temp only. Returns the box size.
    /// </summary>
    public static SizeF MeasureBadge(Graphics g, SceneMood mood, float barH, int level)
    {
        Layout(g, mood, barH, level, out var size, out _, out _, out _, out _, out _);
        return size;
    }

    private static void Layout(Graphics g, SceneMood mood, float barH, int level, out SizeF box,
        out Font big, out Font small, out string l1, out string l2, out float glyph)
    {
        float boxH = Math.Max(18f, barH - 2 * Math.Max(3f, barH * 0.12f));
        glyph = boxH * 0.62f;
        big = new Font("Segoe UI Semibold", Math.Max(8f, boxH * 0.40f), FontStyle.Regular, GraphicsUnit.Pixel);
        small = new Font("Segoe UI", Math.Max(7f, boxH * 0.27f), FontStyle.Regular, GraphicsUnit.Pixel);
        l1 = mood.Ok ? mood.TemperatureText : "…";
        l2 = level switch
        {
            0 => mood.Ok ? mood.ConditionVi + " · " + mood.Place : "thời tiết",
            1 => mood.Ok ? mood.ConditionVi : "",
            _ => ""
        };
        float pad = boxH * 0.22f;
        var s1 = g.MeasureString(l1, big);
        var s2 = string.IsNullOrEmpty(l2) ? SizeF.Empty : g.MeasureString(l2, small);
        float textW = Math.Max(s1.Width, s2.Width);
        box = new SizeF(pad + glyph + pad * 0.6f + textW + pad * 0.7f, boxH);
    }

    public static void DrawBadge(Graphics g, SceneMood mood, RectangleF box, float barH, int level)
    {
        Layout(g, mood, barH, level, out var size, out var big, out var small, out var l1, out var l2, out var glyph);
        using (big) using (small)
        {
            float x = box.X, y = box.Y;
            float boxH = size.Height, boxW = size.Width;
            using (var path = Rounded(x, y, boxW, boxH, Math.Min(10f, boxH * 0.3f)))
            using (var plate = new LinearGradientBrush(new RectangleF(x, y - 1, boxW, boxH + 2),
                       Color.FromArgb(178, 10, 16, 30), Color.FromArgb(150, 6, 10, 20), LinearGradientMode.Vertical))
            using (var rim = new Pen(Color.FromArgb(46, 255, 255, 255), 1f))
            {
                g.FillPath(plate, path);
                g.DrawPath(rim, path);
            }
            float pad = boxH * 0.22f;
            var glyphRect = new RectangleF(x + pad, y + (boxH - glyph) / 2f, glyph, glyph);
            DrawGlyph(g, mood, glyphRect);
            float tx = glyphRect.Right + pad * 0.6f;
            var s1 = g.MeasureString(l1, big);
            var s2 = string.IsNullOrEmpty(l2) ? SizeF.Empty : g.MeasureString(l2, small);
            float total = s1.Height * 0.86f + (s2.Height > 0 ? s2.Height * 0.9f : 0);
            float ty = y + (boxH - total) / 2f - s1.Height * 0.06f;
            using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.DrawString(l1, big, shadow, tx, ty + 1);
            using (var fg = new SolidBrush(Color.FromArgb(250, 248, 250, 255)))
                g.DrawString(l1, big, fg, tx, ty);
            if (!string.IsNullOrEmpty(l2))
            {
                using var fg2 = new SolidBrush(Color.FromArgb(225, 214, 226, 240));
                g.DrawString(l2, small, fg2, tx + 1, ty + s1.Height * 0.86f);
            }
        }
    }

    private static void DrawGlyph(Graphics g, SceneMood mood, RectangleF r)
    {
        switch (mood.Kind)
        {
            case WeatherKind.Clear:
                using (var sun = new SolidBrush(Color.FromArgb(240, 255, 208, 100)))
                    g.FillEllipse(sun, r.X + r.Width * 0.18f, r.Y + r.Height * 0.18f, r.Width * 0.64f, r.Height * 0.64f);
                break;
            case WeatherKind.Night:
                using (var moon = new SolidBrush(Color.FromArgb(230, 230, 228, 210)))
                    g.FillEllipse(moon, r.X + r.Width * 0.22f, r.Y + r.Height * 0.18f, r.Width * 0.58f, r.Height * 0.58f);
                using (var hole = new SolidBrush(Color.FromArgb(200, 10, 16, 32)))
                    g.FillEllipse(hole, r.X + r.Width * 0.42f, r.Y + r.Height * 0.12f, r.Width * 0.5f, r.Height * 0.5f);
                break;
            case WeatherKind.Rain:
            case WeatherKind.Storm:
                using (var cld = new SolidBrush(Color.FromArgb(220, 200, 210, 230)))
                    g.FillEllipse(cld, r.X + 1, r.Y + 1, r.Width * 0.72f, r.Height * 0.55f);
                using (var drop = new Pen(Color.FromArgb(230, 170, 210, 255), 1.3f))
                {
                    g.DrawLine(drop, r.X + r.Width * 0.3f, r.Y + r.Height * 0.58f, r.X + r.Width * 0.22f, r.Y + r.Height * 0.95f);
                    g.DrawLine(drop, r.X + r.Width * 0.55f, r.Y + r.Height * 0.58f, r.X + r.Width * 0.47f, r.Y + r.Height * 0.95f);
                }
                break;
            case WeatherKind.Snow:
            {
                // crisp six-armed snowflake
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, R = Math.Min(r.Width, r.Height) * 0.46f;
                using var arm = new Pen(Color.FromArgb(240, 226, 238, 255), Math.Max(1.1f, R * 0.16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var tw = new Pen(Color.FromArgb(230, 200, 224, 255), Math.Max(0.9f, R * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                for (int k = 0; k < 6; k++)
                {
                    float a = k * MathF.PI / 3f - MathF.PI / 2f;
                    float ex = cx + MathF.Cos(a) * R, ey = cy + MathF.Sin(a) * R;
                    g.DrawLine(arm, cx, cy, ex, ey);
                    float bx = cx + MathF.Cos(a) * R * 0.58f, by = cy + MathF.Sin(a) * R * 0.58f;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        float b = a + sgn * 0.75f;
                        g.DrawLine(tw, bx, by, bx + MathF.Cos(b) * R * 0.32f, by + MathF.Sin(b) * R * 0.32f);
                    }
                }
                using var hub = new SolidBrush(Color.FromArgb(250, 245, 250, 255));
                g.FillEllipse(hub, cx - R * 0.14f, cy - R * 0.14f, R * 0.28f, R * 0.28f);
                break;
            }
            case WeatherKind.Fog:
                using (var f = new SolidBrush(Color.FromArgb(180, 200, 208, 214)))
                {
                    g.FillEllipse(f, r.X, r.Y + r.Height * 0.25f, r.Width, r.Height * 0.35f);
                    g.FillEllipse(f, r.X + 2, r.Y + r.Height * 0.5f, r.Width - 4, r.Height * 0.3f);
                }
                break;
            default:
                using (var cld = new SolidBrush(Color.FromArgb(220, 210, 216, 228)))
                {
                    g.FillEllipse(cld, r.X, r.Y + r.Height * 0.22f, r.Width * 0.7f, r.Height * 0.5f);
                    g.FillEllipse(cld, r.X + r.Width * 0.3f, r.Y + r.Height * 0.1f, r.Width * 0.6f, r.Height * 0.55f);
                }
                break;
        }
    }

    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        r = Math.Max(0.5f, Math.Min(r, Math.Min(w, h) / 2f));
        var path = new GraphicsPath();
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    #region setup

    private void BuildHorizon()
    {
        _horizon?.Dispose();
        _trees?.Dispose();
        _city?.Dispose();
        var rnd = new Random(1000 + _w);

        var crest = new List<PointF> { new(0, _groundTop + 2) };
        float x = 0;
        while (x < _w)
        {
            float span = 80 + (float)rnd.NextDouble() * 140;
            float peak = _groundTop - (6 + (float)rnd.NextDouble() * 10);
            x += span;
            crest.Add(new PointF(Math.Min(_w, x - span * 0.5f), peak));
        }
        crest.Add(new PointF(_w, _groundTop + 2));
        var hz = new GraphicsPath();
        hz.AddLine(0, _h, 0, _groundTop + 2);
        if (crest.Count >= 2)
            hz.AddCurve(crest.ToArray(), 0.45f);
        hz.AddLine(_w, _groundTop + 2, _w, _h);
        hz.CloseFigure();
        hz.Dispose();
        _horizon = null; // v5: regional scenery replaces the generic hills

        // City blocks on the right
        var city = new GraphicsPath();
        float cx = _w * 0.58f;
        while (cx < _w - 8)
        {
            float bw = 7 + (float)rnd.NextDouble() * 14;
            float bh = 6 + (float)rnd.NextDouble() * (_h * 0.38f);
            city.AddRectangle(new RectangleF(cx, _groundTop + 2 - bh, bw, bh));
            cx += bw + 2 + (float)rnd.NextDouble() * 6;
        }
        // a small bridge mid-left
        float bx = _w * 0.28f;
        city.AddRectangle(new RectangleF(bx, _groundTop - 2, 70, 3));
        city.AddRectangle(new RectangleF(bx + 4, _groundTop, 2, 6));
        city.AddRectangle(new RectangleF(bx + 64, _groundTop, 2, 6));
        city.Dispose();
        _city = null;

        // Tree clusters
        var trees = new GraphicsPath();
        for (int i = 0; i < 14; i++)
        {
            float tx = (float)rnd.NextDouble() * _w * 0.55f;
            float th = 7 + (float)rnd.NextDouble() * 8;
            float tw = th * 0.9f;
            trees.AddEllipse(tx, _groundTop + 2 - th, tw, th);
            trees.AddRectangle(new RectangleF(tx + tw * 0.4f, _groundTop, 1.4f, 4));
        }
        trees.Dispose();
        _trees = null;

        _rail = new PointF[]
        {
            new(0, _railY), new(_w, _railY)
        };

        _lamps = new Lamp[Math.Max(3, _w / 280)];
        for (int i = 0; i < _lamps.Length; i++)
            _lamps[i] = new Lamp { X = 40 + i * (_w / (float)_lamps.Length) + 20, H = 9 + (i % 3) };

        _grassX = new float[Math.Max(20, _w / 28)];
        for (int i = 0; i < _grassX.Length; i++)
            _grassX[i] = (float)rnd.NextDouble() * _w;

        _puddles = new Puddle[6];
        for (int i = 0; i < _puddles.Length; i++)
            _puddles[i] = new Puddle { X = 30 + i * (_w / 6.5f) + (float)rnd.NextDouble() * 40, W = 16 + (float)rnd.NextDouble() * 22 };
    }

    private void InitActors()
    {
        _farClouds = new Cloud[5];
        _nearClouds = new Cloud[4];
        for (int i = 0; i < _farClouds.Length; i++)
            _farClouds[i] = RandCloud(far: true, i);
        for (int i = 0; i < _nearClouds.Length; i++)
            _nearClouds[i] = RandCloud(far: false, i + 10);

        _snowFar = MakeSnow(Math.Max(90, _w / 8), 0);
        _snowMid = MakeSnow(Math.Max(50, _w / 16), 1);
        _snowNear = MakeSnow(Math.Max(28, _w / 30), 2);
        _farRain = MakeRain(70, far: true);
        _nearRain = MakeRain(50, far: false);
        _ripples = new Ripple[12];
        _steam = new Puff[24];
        _birds = new Bird[5];
        _flies = new Firefly[8];
        for (int i = 0; i < _flies.Length; i++)
        {
            _flies[i] = new Firefly
            {
                X = (float)_rng.NextDouble() * _w,
                Y = _h * 0.35f + (float)_rng.NextDouble() * _h * 0.4f,
                Phase = (float)_rng.NextDouble() * 6,
                Speed = 0.6f + (float)_rng.NextDouble() * 1.2f,
                Size = 1.3f + (float)_rng.NextDouble() * 1.1f
            };
        }
        _stars = new Star[Math.Max(28, _w / 40)];
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = new Star
            {
                X = (float)_rng.NextDouble() * _w,
                Y = (float)_rng.NextDouble() * (_h * 0.55f),
                Size = 0.8f + (float)_rng.NextDouble() * 1.4f,
                Phase = (float)_rng.NextDouble() * 6,
                Speed = 0.6f + (float)_rng.NextDouble() * 1.8f
            };
        }
        _balloonX = 0.2f + (float)_rng.NextDouble() * 0.4f;
    }

    private Cloud RandCloud(bool far, int seed)
    {
        var r = new Random(seed * 97 + _w);
        return new Cloud
        {
            X = (float)r.NextDouble() * _w,
            Y = far ? -2 : _h * 0.02f + (float)r.NextDouble() * 4,
            W = (far ? 90 : 70) + (float)r.NextDouble() * (far ? 140 : 100),
            H = (far ? 14 : 12) + (float)r.NextDouble() * 10,
            Speed = (far ? 0.08f : 0.16f) + (float)r.NextDouble() * (far ? 0.08f : 0.14f),
            Alpha = 0.55f + (float)r.NextDouble() * 0.4f,
            Seed = seed
        };
    }

    private Flake[] MakeSnow(int n, int layer)
    {
        var a = new Flake[n];
        for (int i = 0; i < n; i++) a[i] = NewFlake(layer, (float)_rng.NextDouble() * (_h + 6) - 4);
        return a;
    }

    private Flake NewFlake(int layer, float y)
    {
        float r() => (float)_rng.NextDouble();
        return layer switch
        {
            0 => new Flake { X = r() * _w, Y = y, Vy = 2.6f + r() * 2.2f, Phase = r() * 6.28f, Freq = 0.35f + r() * 0.3f, Sway = 1.2f + r() * 1.4f, Size = 1.0f + r() * 0.45f, Alpha = 0.5f + r() * 0.25f },
            1 => new Flake { X = r() * _w, Y = y, Vy = 6f + r() * 3.5f, Phase = r() * 6.28f, Freq = 0.55f + r() * 0.45f, Sway = 2.2f + r() * 2.2f, Size = 1.6f + r() * 0.6f, Alpha = 0.72f + r() * 0.23f },
            _ => new Flake { X = r() * _w, Y = y, Vy = 10f + r() * 6f, Phase = r() * 6.28f, Freq = 0.7f + r() * 0.6f, Sway = 3.5f + r() * 3f, Size = 2.3f + r() * 0.9f, Alpha = 0.85f + r() * 0.15f, Glint = r() * 6.28f }
        };
    }

    #endregion

    #region snow (v5.2)

    private float SnowWind(float time) => 2.2f + 3.2f * MathF.Sin(time * 0.11f) + 1.2f * MathF.Sin(time * 0.37f + 1.3f);

    private void StepSnow(float time)
    {
        float dt = _snowLast < 0 || time < _snowLast ? 1f / 30f : Math.Min(0.25f, time - _snowLast);
        _snowLast = time;
        float wind = SnowWind(time);
        Step(_snowFar, 0, 0.45f); Step(_snowMid, 1, 0.75f); Step(_snowNear, 2, 1f);

        void Step(Flake[] arr, int layer, float windK)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                ref var f = ref arr[i];
                f.Y += f.Vy * dt;
                f.X += wind * windK * dt;
                if (f.Y > _h + 3) { var n = NewFlake(layer, -3 - (float)_rng.NextDouble() * 4); f = n; }
                if (f.X > _w + 6) f.X -= _w + 12;
                if (f.X < -6) f.X += _w + 12;
            }
        }
    }

    /// <summary>0..1 warm light on a flake from street lamps and the train headlight.</summary>
    private float WarmLightAt(SceneMood mood, float x, float y)
    {
        float lk = mood.LightsK;
        if (lk < 0.05f) return 0f;
        float best = 0f;
        float y1 = _groundTop + 2;
        for (int i = 0; i < _lamps.Length; i++)
        {
            var l = _lamps[i];
            float top = y1 - l.H - 2;
            if (y < top - 2 || y > y1 + 2) continue;
            float spread = 3f + (y - top) * 0.75f;
            float dx = MathF.Abs(x - l.X);
            if (dx < spread) best = Math.Max(best, 1f - dx / spread);
        }
        if (_headX0 >= 0 && x > _headX0 && x < _headX1)
        {
            float u = (x - _headX0) / (_headX1 - _headX0);
            float cy = _headY - 6 - u * 2;
            float half = 2 + u * 8;
            float dy = MathF.Abs(y - cy);
            if (dy < half) best = Math.Max(best, (1f - dy / half) * (1f - u * 0.6f));
        }
        return best * lk;
    }

    private void DrawSnowLayer(Graphics g, SceneMood mood, float time, Flake[] arr, int layer)
    {
        int n = (int)(arr.Length * (0.35f + 0.65f * mood.SnowAmt));
        // Day flakes are white but kept slightly translucent; night flakes are moonlit blue.
        var baseCol = SceneClock.Mix(Color.FromArgb(255, 186, 198, 232), Color.FromArgb(255, 246, 249, 255), mood.DayK);
        baseCol = SceneClock.Mix(baseCol, Color.FromArgb(255, 255, 222, 232), 0.35f * mood.WarmK);
        var warm = Color.FromArgb(255, 255, 214, 150);
        float layerA = layer switch { 0 => 0.7f, 1 => 0.9f, _ => 1f };
        using var br = new SolidBrush(baseCol);
        using var halo = new SolidBrush(baseCol);
        using var spark = new Pen(baseCol, 0.7f);
        var prevSmooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < n; i++)
        {
            ref var f = ref arr[i];
            float x = f.X + MathF.Sin(time * f.Freq + f.Phase) * f.Sway;
            float y = f.Y;
            // tiny flutter of the near flakes
            float s = f.Size * (layer == 2 ? 0.9f + 0.1f * MathF.Sin(time * 4f + f.Phase) : 1f);
            float wl = layer == 0 ? 0f : WarmLightAt(mood, x, y);
            var col = wl > 0.01f ? SceneClock.Mix(baseCol, warm, Math.Min(1f, wl * 1.1f)) : baseCol;
            int a = (int)(255 * f.Alpha * layerA * (1f + 0.6f * wl));
            br.Color = Color.FromArgb(Math.Clamp(a, 0, 255), col);
            if (layer == 0)
            {
                g.FillRectangle(br, x, y, s, s);
                continue;
            }
            if (layer == 2)
            {
                halo.Color = Color.FromArgb(Math.Clamp((int)(a * 0.22f), 0, 255), col);
                g.FillEllipse(halo, x - s * 0.7f, y - s * 0.7f, s * 2.4f, s * 2.4f);
            }
            g.FillEllipse(br, x, y, s, s);
            if (layer == 2)
            {
                // occasional soft glint, brighter in lamp / headlight light
                float gl = MathF.Sin(time * 2.3f + f.Glint * 5f);
                if (gl > 0.93f || wl > 0.45f)
                {
                    float k = Math.Max((gl - 0.93f) / 0.07f, wl);
                    spark.Color = Color.FromArgb(Math.Clamp((int)(200 * k), 0, 255), col);
                    float cx = x + s / 2f, cy = y + s / 2f, r = s * 1.25f;
                    g.DrawLine(spark, cx - r, cy, cx + r, cy);
                    g.DrawLine(spark, cx, cy - r, cx, cy + r);
                }
            }
        }
        g.SmoothingMode = prevSmooth;
    }

    /// <summary>Snow blanket on the ground line + soft winter haze above the horizon.</summary>
    private void DrawSnowGround(Graphics g, SceneMood mood, float time)
    {
        float amt = 0.55f + 0.45f * mood.SnowCover;
        // winter breath / haze hugging the horizon
        var hazeCol = SceneClock.Mix(Color.FromArgb(255, 120, 136, 190), Color.FromArgb(255, 226, 234, 246), mood.DayK);
        var hz = new RectangleF(0, _groundTop - 12, _w, 16);
        using (var hb = new LinearGradientBrush(hz, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
        {
            int ha = (int)((mood.DayK > 0.5f ? 46 : 34) * amt);
            hb.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, hazeCol), Color.FromArgb(ha, hazeCol), Color.FromArgb(ha / 2, hazeCol) },
                Positions = new[] { 0f, 0.7f, 1f }
            };
            g.FillRectangle(hb, hz);
        }
        // drifting haze puffs
        using (var pb = new SolidBrush(Color.FromArgb((int)(16 * amt), hazeCol)))
            for (int i = 0; i < 4; i++)
            {
                float x = ((time * (2.5f + i) + i * 333) % (_w + 300)) - 150;
                g.FillEllipse(pb, x, _groundTop - 8 + (i % 2) * 2, 220 + i * 30, 10);
            }
        // thin white blanket along the ground top with gentle drifts
        var snowCol = RegionScenery.SnowTint(mood, (int)(200 * amt));
        using var sb = new SolidBrush(snowCol);
        g.FillRectangle(sb, 0, _groundTop - 0.6f, _w, 1.6f);
        var rnd = new Random(77 + _w);
        for (float x = -10; x < _w; )
        {
            float dw = 14 + (float)rnd.NextDouble() * 30;
            float dh = 1.2f + (float)rnd.NextDouble() * 1.6f;
            g.FillEllipse(sb, x, _groundTop - dh * 0.6f, dw, dh + 0.8f);
            x += dw * (0.8f + (float)rnd.NextDouble() * 0.9f);
        }
        // snow along the road edge (bottom) — kept subtle so the running-app dashes stay visible
        using var edge = new SolidBrush(RegionScenery.SnowTint(mood, (int)(90 * amt)));
        g.FillRectangle(edge, 0, _h - 5.4f, _w, 0.9f);
    }

    #endregion

    #region setup2

    private Drop[] MakeRain(int n, bool far)
    {
        var a = new Drop[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = new Drop
            {
                X = (float)_rng.NextDouble() * _w,
                Y = (float)_rng.NextDouble() * _h,
                Vx = far ? 0.7f : 1.15f,
                Vy = far ? 3.2f : 5.0f + (float)_rng.NextDouble() * 1.6f,
                Len = far ? 5f : 7.5f + (float)_rng.NextDouble() * 4f,
                Alpha = far ? 0.35f : 0.5f + (float)_rng.NextDouble() * 0.4f
            };
        }
        return a;
    }

    #endregion
}

internal static class SceneMoodExt
{
    public static Color GlowOrSun(this SceneMood m) => m.HorizonGlow;
}
