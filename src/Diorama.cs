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
    public readonly float SunX;         // 0..1 across the bar
    public readonly float MoonPhase;    // 0 new .. 1 full-ish

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
        IsRainy = kind is WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow;
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
    public static DayPhase PhaseAt(DateTime local)
    {
        float h = local.Hour + local.Minute / 60f + local.Second / 3600f;
        if (h < 5.15f || h >= 19.25f) return DayPhase.Night;
        if (h < 6.55f) return DayPhase.Dawn;
        if (h < 16.20f) return DayPhase.Day;
        if (h < 17.80f) return DayPhase.Golden;
        return DayPhase.Dusk;
    }

    public static float SunX(DateTime local)
    {
        float h = local.Hour + local.Minute / 60f;
        // Sunrise ~5.8, sunset ~17.6 in Bà Rịa, mid-season.
        float t = Math.Clamp((h - 5.6f) / 12.2f, 0f, 1f);
        return 0.10f + t * 0.80f;
    }

    public static SceneMood Compose(WeatherSnapshot? live, AppConfig cfg, DateTime local)
    {
        var preview = (cfg.WeatherPreview ?? "live").Trim().ToLowerInvariant();
        DayPhase phase = PhaseAt(local);
        WeatherKind kind = live is { Ok: true } ? live.Kind : WeatherKind.Cloudy;
        bool isDay = live?.IsDay ?? (phase is DayPhase.Day or DayPhase.Dawn or DayPhase.Golden);
        float cloud = live is { Ok: true } ? Math.Clamp(live.CloudCover / 100f, 0f, 1f) : 0.55f;
        float rain = 0f;
        if (live is { Ok: true })
            rain = kind is WeatherKind.Storm ? 1f : kind is WeatherKind.Rain ? 0.7f : Math.Clamp(live.Precipitation / 6f, 0f, 1f);

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
                kind = WeatherKind.Clear; isDay = true; phase = DayPhase.Day;
                cloud = 0.12f; rain = 0f; cond = "Nắng đẹp";
                if (local.Hour >= 16) { phase = DayPhase.Golden; }
                break;
            case "cloudy":
            case "may":
            case "mây trôi":
                kind = WeatherKind.Cloudy; isDay = true; phase = DayPhase.Day;
                cloud = 0.85f; rain = 0f; cond = "Mây trôi";
                break;
            case "rain":
            case "mua":
            case "mưa rào":
                kind = WeatherKind.Rain; isDay = phase is not DayPhase.Night;
                if (phase == DayPhase.Night) isDay = false;
                cloud = 0.9f; rain = 0.85f; cond = "Mưa rào";
                break;
            case "storm":
            case "giong":
            case "giông bão":
                kind = WeatherKind.Storm; isDay = false; phase = DayPhase.Dusk;
                cloud = 1f; rain = 1f; cond = "Giông bão";
                break;
            case "night":
            case "dem":
            case "đêm sao":
                kind = WeatherKind.Night; isDay = false; phase = DayPhase.Night;
                cloud = 0.15f; rain = 0f; cond = "Đêm sao";
                break;
            default:
                // live — if Open-Meteo says night, honour it
                if (live is { Ok: true, IsDay: false } && phase == DayPhase.Day)
                    phase = DayPhase.Dusk;
                break;
        }

        // Palettes
        Color top, mid, bot, glow, ground, groundHi, sil, accent;
        switch (phase)
        {
            case DayPhase.Dawn:
                top = C(255, 140, 110, 155);
                mid = C(255, 232, 170, 150);
                bot = C(255, 255, 214, 180);
                glow = C(255, 255, 190, 140);
                ground = C(255, 62, 48, 52);
                groundHi = C(255, 96, 70, 64);
                sil = C(255, 44, 32, 40);
                accent = C(255, 255, 186, 120);
                isDay = true;
                break;
            case DayPhase.Golden:
                top = C(255, 168, 72, 58);
                mid = C(255, 232, 122, 58);
                bot = C(255, 255, 188, 110);
                glow = C(255, 255, 200, 110);
                ground = C(255, 52, 32, 30);
                groundHi = C(255, 92, 52, 36);
                sil = C(255, 38, 22, 26);
                accent = C(255, 255, 196, 110);
                isDay = true;
                break;
            case DayPhase.Dusk:
                top = C(255, 28, 24, 62);
                mid = C(255, 74, 48, 96);
                bot = C(255, 176, 86, 78);
                glow = C(255, 255, 140, 90);
                ground = C(255, 22, 18, 28);
                groundHi = C(255, 48, 32, 42);
                sil = C(255, 12, 10, 20);
                accent = C(255, 255, 176, 110);
                isDay = false;
                break;
            case DayPhase.Night:
                top = C(255, 6, 8, 22);
                mid = C(255, 14, 18, 40);
                bot = C(255, 24, 30, 56);
                glow = C(255, 80, 90, 130);
                ground = C(255, 8, 10, 18);
                groundHi = C(255, 22, 26, 40);
                sil = C(255, 4, 6, 12);
                accent = C(255, 230, 226, 200);
                isDay = false;
                break;
            default: // Day
                top = C(255, 62, 132, 210);
                mid = C(255, 118, 186, 232);
                bot = C(255, 196, 226, 245);
                glow = C(255, 255, 236, 170);
                ground = C(255, 46, 58, 44);
                groundHi = C(255, 72, 92, 58);
                sil = C(255, 28, 36, 32);
                accent = C(255, 255, 220, 120);
                isDay = true;
                break;
        }

        // Weather tints
        if (kind is WeatherKind.Cloudy or WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Fog or WeatherKind.Snow)
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

        if (kind == WeatherKind.Night) isDay = false;

        float cloudiness = kind switch
        {
            WeatherKind.Clear or WeatherKind.Night => Math.Min(cloud, 0.25f),
            WeatherKind.Cloudy => Math.Max(cloud, 0.65f),
            WeatherKind.Fog => 0.8f,
            WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow => Math.Max(cloud, 0.8f),
            _ => cloud
        };
        float rainAmt = kind switch
        {
            WeatherKind.Storm => 1f,
            WeatherKind.Rain => Math.Max(0.55f, rain),
            WeatherKind.Snow => 0.4f,
            _ => 0f
        };

        return new SceneMood(phase, kind, isDay, cloudiness, rainAmt,
            top, mid, bot, glow, ground, groundHi, sil, accent,
            cond, temp, place, live is { Ok: true } || preview != "live",
            SunX(local), MoonAmount(local));
    }

    public static string ShortPlace(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Bà Rịa";
        var n = name.Trim();
        if (n.Contains("Ba Ria", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Bà Rịa", StringComparison.OrdinalIgnoreCase))
            return "Bà Rịa";
        if (n.StartsWith("Thanh Pho ", StringComparison.OrdinalIgnoreCase))
            return n["Thanh Pho ".Length..].Trim();
        if (n.StartsWith("Thành phố ", StringComparison.OrdinalIgnoreCase))
            return n["Thành phố ".Length..].Trim();
        return n;
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
internal sealed class DioramaRenderer
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

    private float _trainX = -400;
    private float _bikeX = 200;
    private float _balloonX = 0.35f;
    private float _flash;          // 0..1 lightning envelope
    private float _flashCool;
    private float _flashX;
    private float _shootX = -1, _shootY, _shootLife;
    private bool _ready;

    private struct Cloud { public float X, Y, W, H, Speed, Alpha; public int Seed; }
    private struct Drop { public float X, Y, Vx, Vy, Len, Alpha; }
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

    public void Draw(Graphics g, FrameContext ctx, SceneMood mood, float time)
    {
        DrawSky(g, mood, time);
        if (mood.IsNight) DrawStarsAndMoon(g, mood, time, ctx.Cfg.ShowWeatherEffects || ctx.Cfg.ShowAmbientDetails);
        else if (mood.IsClear && ctx.Cfg.ShowWeatherEffects) DrawSun(g, mood, time);

        if (ctx.Cfg.ShowWeatherEffects || mood.IsCloudy)
            DrawClouds(g, mood, time, far: true);

        if (mood.IsStorm && ctx.Cfg.ShowWeatherEffects)
            DrawLightning(g, mood, time);

        if (mood.IsRainy && ctx.Cfg.ShowWeatherEffects)
            DrawRain(g, mood, time, near: false);

        if (ctx.Cfg.ShowAmbientDetails && mood.IsClear && mood.IsDay)
            DrawBalloon(g, mood, time);

        DrawHorizon(g, mood, time);
        if (ctx.Cfg.ShowAmbientDetails)
            DrawLampsAndWindows(g, mood, time);

        if (ctx.Cfg.ShowWeatherEffects && mood.IsCloudy)
            DrawClouds(g, mood, time, far: false);

        if (ctx.Cfg.ShowTrain)
            DrawTrain(g, mood, time);

        if (mood.IsRainy && ctx.Cfg.ShowWeatherEffects)
        {
            DrawRain(g, mood, time, near: true);
            DrawRipplesAndPuddles(g, mood, time);
        }

        if (mood.IsFog && ctx.Cfg.ShowWeatherEffects)
            DrawFog(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails)
            DrawGrass(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails && mood.IsDay && !mood.IsStorm)
            DrawBirds(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails && mood.IsNight && mood.IsClear)
            DrawFirefliesAndShootingStar(g, mood, time);

        if (ctx.Cfg.ShowAmbientDetails && mood.IsNight)
            DrawNightCar(g, mood, time);

        if (ctx.Cfg.ShowCyclist)
            DrawCyclist(g, mood, time);

        DimBehindIcons(g, ctx, mood);
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
        int ga = mood.Phase switch
        {
            DayPhase.Golden => 160,
            DayPhase.Dawn => 130,
            DayPhase.Dusk => 110,
            DayPhase.Night => 40,
            _ => mood.IsClear ? 70 : 30
        };
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

    private void DrawSun(Graphics g, SceneMood mood, float time)
    {
        float cx = mood.SunX * _w;
        float cy = mood.Phase == DayPhase.Golden || mood.Phase == DayPhase.Dawn
            ? _h * 0.42f
            : _h * 0.18f;
        float r = mood.Phase is DayPhase.Golden or DayPhase.Dawn ? _h * 0.34f : _h * 0.26f;

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(cx - r * 3.2f, cy - r * 3.2f, r * 6.4f, r * 6.4f);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = SceneClock.WithA(mood.GlowOrSun(), 90);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 220, 140) };
            pgb.CenterPoint = new PointF(cx, cy);
            g.FillPath(pgb, path);
        }
        // Soft shafts
        if (mood.Phase is DayPhase.Day or DayPhase.Golden or DayPhase.Dawn)
        {
            using var shaft = new SolidBrush(Color.FromArgb(16, 255, 236, 190));
            for (int i = 0; i < 5; i++)
            {
                float ang = -0.55f + i * 0.28f + MathF.Sin(time * 0.15f) * 0.03f;
                var pts = new[]
                {
                    new PointF(cx, cy),
                    new PointF(cx + MathF.Cos(ang - 0.06f) * _h * 2.2f, cy + MathF.Sin(ang - 0.06f) * _h * 2.2f),
                    new PointF(cx + MathF.Cos(ang + 0.06f) * _h * 2.2f, cy + MathF.Sin(ang + 0.06f) * _h * 2.2f)
                };
                g.FillPolygon(shaft, pts);
            }
        }
        using var core = new SolidBrush(Color.FromArgb(240, 255, 236, 170));
        using var rim = new SolidBrush(Color.FromArgb(80, 255, 250, 220));
        g.FillEllipse(rim, cx - r * 1.25f, cy - r * 1.25f, r * 2.5f, r * 2.5f);
        g.FillEllipse(core, cx - r, cy - r, r * 2, r * 2);
    }

    private void DrawStarsAndMoon(Graphics g, SceneMood mood, float time, bool details)
    {
        if (details && _stars.Length > 0)
        {
            for (int i = 0; i < _stars.Length; i++)
            {
                ref var s = ref _stars[i];
                float tw = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(time * s.Speed + s.Phase));
                int a = Math.Clamp((int)(tw * 210 * (1f - mood.Cloudiness * 0.7f)), 0, 230);
                if (a < 8) continue;
                using var br = new SolidBrush(Color.FromArgb(a, 230, 236, 255));
                g.FillEllipse(br, s.X, s.Y, s.Size, s.Size);
            }
        }

        // Crescent / near-full moon in the upper right-of-center, nudged by phase
        float mx = _w * (0.62f + 0.12f * (mood.SunX - 0.5f));
        float my = _h * 0.22f;
        float mr = _h * 0.20f;
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(mx - mr * 2.8f, my - mr * 2.8f, mr * 5.6f, mr * 5.6f);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = Color.FromArgb((int)(50 + 40 * mood.MoonPhase), 200, 210, 255);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 200, 210, 255) };
            pgb.CenterPoint = new PointF(mx, my);
            g.FillPath(pgb, path);
        }
        using var moon = new SolidBrush(Color.FromArgb(230, 236, 234, 214));
        g.FillEllipse(moon, mx - mr, my - mr, mr * 2, mr * 2);
        // Crescent bite when not full
        if (mood.MoonPhase < 0.85f)
        {
            float bite = 0.35f + (1f - mood.MoonPhase) * 0.55f;
            using var hole = new SolidBrush(mood.SkyTop);
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
        var col = mood.IsNight || mood.Phase == DayPhase.Dusk
            ? Color.FromArgb(255, 90, 98, 120)
            : mood.Phase is DayPhase.Golden or DayPhase.Dawn
                ? Color.FromArgb(255, 255, 214, 196)
                : Color.FromArgb(255, 242, 246, 250);

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

    private void DrawLightning(Graphics g, SceneMood mood, float time)
    {
        _flashCool -= 1f / 30f;
        if (_flash > 0) _flash = Math.Max(0, _flash - 0.08f);
        if (_flashCool <= 0 && _rng.NextDouble() < 0.012)
        {
            _flash = 1f;
            _flashCool = 3.5f + (float)_rng.NextDouble() * 5f;
            _flashX = _w * (0.18f + 0.64f * (float)_rng.NextDouble());
        }
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
            using var rail = new Pen(Color.FromArgb(mood.IsNight ? 80 : 100, 40, 36, 32), 1.15f);
            g.DrawLines(rail, _rail);
            using var rail2 = new Pen(Color.FromArgb(mood.IsNight ? 60 : 80, 30, 28, 26), 1f);
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
        bool lit = mood.IsNight || mood.Phase is DayPhase.Dusk or DayPhase.Dawn;
        if (!lit) return;

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
            int a = mood.IsNight ? 200 : 140;
            win.Color = Color.FromArgb(a, 255, 206, 120);
            g.FillRectangle(win, x, y, 1.6f, 1.8f);
        }

        using var lampPole = new Pen(Color.FromArgb(180, 20, 18, 16), 1.2f);
        using var lampHead = new SolidBrush(Color.FromArgb(220, 255, 220, 150));
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
            int ca = mood.IsNight ? 70 : 40;
            if (mood.IsRainy) ca = (int)(ca * 1.2f);
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

    private void DrawTrain(Graphics g, SceneMood mood, float time)
    {
        // Period: 20s travel + 8s gap. Train length ~ 210px
        const float period = 28f;
        const float travel = 19f;
        float t = time % period;
        float trainW = 215f;
        if (t > travel)
        {
            // park off-screen, don't draw
            _trainX = -trainW;
            return;
        }
        _trainX = -trainW + (t / travel) * (_w + trainW + 20);
        float y = _railY - 1.2f;
        bool night = mood.IsNight || mood.Phase == DayPhase.Dusk;
        bool rain = mood.IsRainy;

        // Headlight beam
        if (night)
        {
            using var cone = new GraphicsPath();
            cone.AddPolygon(new[]
            {
                new PointF(_trainX + trainW - 4, y - 8),
                new PointF(_trainX + trainW + 70, y - 16),
                new PointF(_trainX + trainW + 70, y + 4)
            });
            using var pgb = new PathGradientBrush(cone);
            pgb.CenterColor = Color.FromArgb(70, 255, 236, 190);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 236, 190) };
            pgb.CenterPoint = new PointF(_trainX + trainW, y - 7);
            g.FillPath(pgb, cone);
        }

        // Carriages (4) then locomotive at the right (LTR)
        float x = _trainX;
        DrawCarriage(g, x, y, 38, night, rain, 0); x += 40;
        DrawCarriage(g, x, y, 38, night, rain, 1); x += 40;
        DrawCarriage(g, x, y, 38, night, rain, 2); x += 40;
        DrawCarriage(g, x, y, 36, night, rain, 3); x += 38;
        DrawLoco(g, x, y, night, rain, time);

        // Steam
        EmitSteam(_trainX + trainW - 28, y - 16, time);
        DrawSteam(g, mood, time);
    }

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
        // chimney
        g.FillRectangle(roof, x + 28, y - 21, 5, 9);
        // cowcatcher
        using var catcher = new SolidBrush(Color.FromArgb(255, 70, 50, 44));
        g.FillPolygon(catcher, new[]
        {
            new PointF(x + 48, y - 4), new PointF(x + 58, y + 1), new PointF(x + 44, y + 1)
        });
        // windows
        int wa = night || rain ? 230 : 160;
        using var win = new SolidBrush(Color.FromArgb(wa, 255, 214, 130));
        g.FillRectangle(win, x + 3, y - 16, 5, 5);
        g.FillRectangle(win, x + 9, y - 16, 5, 5);
        // headlight
        using var hl = new SolidBrush(Color.FromArgb(night ? 255 : 180, 255, 236, 180));
        g.FillEllipse(hl, x + 46, y - 11, 4.2f, 4.2f);
        // wheels
        DrawWheels(g, x + 6, y, 3, time * 8f);
        DrawWheels(g, x + 28, y, 2, time * 8f);
    }

    private void DrawCarriage(Graphics g, float x, float y, float w, bool night, bool rain, int idx)
    {
        using var body = new SolidBrush(Color.FromArgb(255, 54 + idx * 2, 38, 42));
        using var roof = new SolidBrush(Color.FromArgb(255, 34, 24, 28));
        g.FillRectangle(body, x, y - 14, w, 12);
        g.FillRectangle(roof, x - 0.5f, y - 16, w + 1, 2.4f);
        int wa = night || rain ? 220 : 140;
        using var win = new SolidBrush(Color.FromArgb(wa, 255, 210, 120));
        for (int i = 0; i < 3; i++)
            g.FillRectangle(win, x + 5 + i * 11, y - 12, 6, 5);
        // coupler
        using var coup = new SolidBrush(Color.FromArgb(200, 30, 24, 24));
        g.FillRectangle(coup, x + w, y - 7, 2, 2);
        DrawWheels(g, x + 6, y, 2, 0);
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
        // emit a couple puffs
        if ((int)(time * 10) % 3 != 0) { /* still age existing */ }
        else
        {
            for (int i = 0; i < _steam.Length; i++)
            {
                if (!_steam[i].On)
                {
                    _steam[i] = new Puff
                    {
                        X = x + (float)_rng.NextDouble() * 3,
                        Y = y,
                        Vx = -0.35f - (float)_rng.NextDouble() * 0.4f,
                        Vy = -0.45f - (float)_rng.NextDouble() * 0.3f,
                        Life = 1f, Max = 1f,
                        Size = 2.5f + (float)_rng.NextDouble() * 2.2f,
                        On = true
                    };
                    break;
                }
            }
        }
        for (int i = 0; i < _steam.Length; i++)
        {
            if (!_steam[i].On) continue;
            ref var p = ref _steam[i];
            p.X += p.Vx; p.Y += p.Vy;
            p.Life -= 0.025f;
            p.Size += 0.08f;
            if (p.Life <= 0) p.On = false;
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
        using var pen = new Pen(Color.FromArgb(160, 30, 30, 32), 1.05f);
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
            int a = Math.Clamp((int)(pulse * 200), 0, 200);
            if (a < 20) continue;
            using var br = new SolidBrush(Color.FromArgb(a, 196, 255, 120));
            g.FillEllipse(br, f.X, f.Y, f.Size, f.Size);
        }

        if (_shootLife <= 0 && _rng.NextDouble() < 0.0025)
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
        using var shell = new SolidBrush(Color.FromArgb(180, 220, 80, 64));
        using var stripe = new SolidBrush(Color.FromArgb(160, 240, 200, 120));
        using var basket = new SolidBrush(Color.FromArgb(180, 70, 48, 32));
        using var line = new Pen(Color.FromArgb(140, 40, 30, 24), 0.8f);
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

    public void DrawBadge(Graphics g, FrameContext ctx, SceneMood mood)
    {
        var slot = ctx.Regions.WeatherSlot;
        if (slot.Width < 40 || slot.Height < 16) return;
        string line = mood.Ok
            ? $"{mood.TemperatureText}  ·  {mood.ConditionVi}  ·  {mood.Place}"
            : "…  thời tiết";

        using var font = new Font("Segoe UI Semibold", Math.Clamp(_h * 0.34f, 8.0f, 10.8f), FontStyle.Regular, GraphicsUnit.Point);
        var size = g.MeasureString(line, font);
        float glyph = Math.Min(slot.Height - 4, 15);
        float padX = 7f, padY = 2f;
        float boxW = glyph + 5 + size.Width + padX * 2;
        float boxH = Math.Max(size.Height, glyph) + padY * 2;
        float x = slot.X + 4;
        if (ctx.Cfg.WeatherPosition == "right")
            x = slot.Right - boxW - 4;
        float y = slot.Y + Math.Max(0, (slot.Height - boxH) / 2f);

        using (var path = Rounded(x, y, boxW, boxH, Math.Min(8f, boxH / 2f)))
        using (var plate = new SolidBrush(Color.FromArgb(170, 8, 12, 22)))
        using (var rim = new Pen(Color.FromArgb(36, 255, 255, 255), 1f))
        {
            g.FillPath(plate, path);
            g.DrawPath(rim, path);
        }

        var glyphRect = new RectangleF(x + padX, y + (boxH - glyph) / 2f, glyph, glyph);
        DrawGlyph(g, mood, glyphRect);
        var textPos = new PointF(x + padX + glyph + 4, y + (boxH - size.Height) / 2f);
        using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.DrawString(line, font, shadow, textPos.X, textPos.Y + 1);
        using var fg = new SolidBrush(Color.FromArgb(240, 246, 248, 255));
        g.DrawString(line, font, fg, textPos);
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
        _horizon = hz;

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
        _city = city;

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
        _trees = trees;

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

        _farRain = MakeRain(70, far: true);
        _nearRain = MakeRain(50, far: false);
        _ripples = new Ripple[12];
        _steam = new Puff[14];
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
