using System.Globalization;

namespace AudioTaskbar;

/// <summary>v5.4 seasons. Summer is the look of every earlier version (so old configs look unchanged).</summary>
internal enum SeasonId { Spring, Summer, Autumn, Winter }

internal static class Seasons
{
    public static readonly SeasonId[] All = { SeasonId.Spring, SeasonId.Summer, SeasonId.Autumn, SeasonId.Winter };

    /// <summary>v5.4.2 season modes stored in <see cref="AppConfig.Season"/> next to the four fixed seasons.</summary>
    public const string Cycle = "cycle", Real = "real";

    public static string Id(SeasonId s) => s switch
    {
        SeasonId.Spring => "spring", SeasonId.Autumn => "autumn", SeasonId.Winter => "winter", _ => "summer"
    };

    public static string NameVi(SeasonId s) => s switch
    {
        SeasonId.Spring => "Mùa xuân", SeasonId.Autumn => "Mùa thu", SeasonId.Winter => "Mùa đông", _ => "Mùa hè"
    };

    /// <summary>Accepts ids, Vietnamese names (with or without diacritics) and English names; anything else is summer.</summary>
    public static SeasonId Parse(string? text)
    {
        var f = RegionCatalog.Fold((text ?? "").Trim());
        return f switch
        {
            "spring" or "xuan" or "mua xuan" => SeasonId.Spring,
            "autumn" or "fall" or "thu" or "mua thu" => SeasonId.Autumn,
            "winter" or "dong" or "mua dong" => SeasonId.Winter,
            _ => SeasonId.Summer
        };
    }

    public static string Normalize(string? text) => Id(Parse(text));

    /// <summary>
    /// The season setting: one of the four fixed seasons, <see cref="Cycle"/> ("Tự đổi mùa") or <see cref="Real"/>
    /// ("Thực tế": the real season for today's date at the scene's location).
    /// </summary>
    public static string NormalizeMode(string? text)
    {
        var f = RegionCatalog.Fold((text ?? "").Trim());
        return f switch
        {
            "cycle" or "auto" or "tu doi mua" or "tu doi" => Cycle,
            "real" or "live" or "thuc te" => Real,
            _ => Normalize(text)
        };
    }

    public static bool IsFixed(string? mode) => NormalizeMode(mode) is not (Cycle or Real);

    public static SeasonId Next(SeasonId s) => All[((int)s + 1) % All.Length];

    /// <summary>Pure season clock: the season that is current <paramref name="elapsedMinutes"/> after <paramref name="start"/>.</summary>
    public static SeasonId At(SeasonId start, double elapsedMinutes, float periodMinutes)
    {
        if (!(periodMinutes > 0f) || !float.IsFinite(periodMinutes)) return start;
        if (!double.IsFinite(elapsedMinutes) || elapsedMinutes < 0) elapsedMinutes = 0;
        long steps = (long)Math.Floor(elapsedMinutes / periodMinutes);
        return All[(int)(((int)start + steps) % All.Length)];
    }

    /// <summary>Latitude used for "Thực tế" when the scene has no known location: TP. Hồ Chí Minh.</summary>
    public const double FallbackLatitude = 10.7769;
    /// <summary>From this latitude (north or south) the four meteorological seasons apply; nearer the equator the tropical mapping.</summary>
    public const double TemperateFromLatitude = 18.0;

    /// <summary>Latitude of the scene for "Thực tế": the picked region's city, else the detected location, else TP.HCM.</summary>
    public static double SceneLatitude(RegionProfile region, WeatherSnapshot? weather)
    {
        if (region.Id != RegionCatalog.Default.Id && region.Lat != 0) return region.Lat;
        if (weather is { Ok: true } w && w.Latitude != 0 && double.IsFinite(w.Latitude)) return w.Latitude;
        return FallbackLatitude;
    }

    /// <summary>
    /// v5.4.2 "Thực tế": the real season on <paramref name="date"/> at <paramref name="latitude"/>.
    /// <list type="bullet">
    /// <item>|lat| ≥ 18° (Hà Nội, Tokyo, Paris, New York, Sydney …): meteorological seasons. North: Mar–May spring, Jun–Aug summer,
    /// Sep–Nov autumn, Dec–Feb winter. South: shifted by six months (Sydney: Dec–Feb summer, Jun–Aug winter).</item>
    /// <item>|lat| &lt; 18° (TP.HCM, Vũng Tàu, Đà Nẵng): tropical, never winter (there is no snow). North: Jan–Feb spring (Tết, hoa mai),
    /// Mar–Jul summer (hot season, first rains), Aug–Dec autumn (late rainy season, then the cool dry wind). South: shifted by six months.</item>
    /// </list>
    /// </summary>
    public static SeasonId RealAt(DateTime date, double latitude)
    {
        if (!double.IsFinite(latitude)) latitude = FallbackLatitude;
        int m = date.Month;
        if (latitude < 0) m = (m + 5) % 12 + 1;   // southern hemisphere: same table, six months later
        if (Math.Abs(latitude) >= TemperateFromLatitude)
            return m switch
            {
                3 or 4 or 5 => SeasonId.Spring,
                6 or 7 or 8 => SeasonId.Summer,
                9 or 10 or 11 => SeasonId.Autumn,
                _ => SeasonId.Winter
            };
        return m switch
        {
            1 or 2 => SeasonId.Spring,
            >= 3 and <= 7 => SeasonId.Summer,
            _ => SeasonId.Autumn
        };
    }
}

/// <summary>
/// Runtime season. A fixed season is shown as is. "Thực tế" follows the date and the scene's latitude
/// (<see cref="Seasons.RealAt"/>). "Tự đổi mùa" starts at the real season and advances every
/// <see cref="AppConfig.SeasonCycleMinutes"/> real minutes (spring → summer → autumn → winter → spring …);
/// changing the period or switching modes restarts it.
/// </summary>
internal sealed class SeasonCycler
{
    private SeasonId _start = SeasonId.Summer;
    private DateTime _anchorUtc;
    private bool _active;
    private float _period;

    public SeasonId Current(DateTime nowUtc, AppConfig cfg, double latitude = Seasons.FallbackLatitude)
    {
        var mode = Seasons.NormalizeMode(cfg.Season);
        if (mode == Seasons.Real) { _active = false; return Seasons.RealAt(nowUtc.ToLocalTime(), latitude); }
        if (mode != Seasons.Cycle) { _active = false; return Seasons.Parse(mode); }
        if (!_active || Math.Abs(_period - cfg.SeasonCycleMinutes) > 1e-4f)
        {
            _active = true; _start = Seasons.RealAt(nowUtc.ToLocalTime(), latitude); _period = cfg.SeasonCycleMinutes; _anchorUtc = nowUtc;
        }
        return Seasons.At(_start, (nowUtc - _anchorUtc).TotalMinutes, _period);
    }
}

/// <summary>
/// Weather that changes by itself. v5.4.2: it is the "Ngẫu nhiên" entry of the weather list (<see cref="AppConfig.WeatherPreview"/> =
/// <see cref="Random"/>): a random, season-aware weather after a random wait between the two "Chu kỳ đổi" values.
/// The v5.4 modes ("interval" / "random" of the removed "Tự đổi thời tiết" setting) are only read to migrate old files.
/// </summary>
internal static class WeatherCycle
{
    public const string Off = "off", Interval = "interval", Random = "random";

    /// <summary>Legacy v5.4 <c>weatherCycleMode</c> values.</summary>
    public static string NormalizeMode(string? mode)
    {
        var m = RegionCatalog.Fold((mode ?? "").Trim());
        return m switch
        {
            "interval" or "fixed" or "time" or "theo thoi gian" or "theo khoang thoi gian" => Interval,
            "random" or "ngau nhien" => Random,
            _ => Off
        };
    }

    /// <summary>Weather ids the cycle may show in a season, with relative weights (snow only in winter).</summary>
    public static (string id, float weight)[] Pool(SeasonId season) => season switch
    {
        SeasonId.Winter => new[] { ("sunny", 2f), ("cloudy", 3f), ("rain", 1f), ("snow", 3f) },
        SeasonId.Spring => new[] { ("sunny", 3f), ("cloudy", 2f), ("rain", 2f), ("storm", 0.5f) },
        SeasonId.Autumn => new[] { ("sunny", 2f), ("cloudy", 3f), ("rain", 2.5f), ("storm", 0.6f) },
        _ => new[] { ("sunny", 4f), ("cloudy", 2f), ("rain", 2f), ("storm", 1.2f) }
    };

    public static bool InPool(SeasonId season, string id) => Pool(season).Any(p => p.id == id);

    public static string PickRandom(SeasonId season, string? exclude, System.Random rng)
    {
        var pool = Pool(season).Where(p => p.id != exclude).ToArray();
        if (pool.Length == 0) pool = Pool(season);
        float total = pool.Sum(p => p.weight);
        float r = (float)rng.NextDouble() * total;
        foreach (var (id, w) in pool) { r -= w; if (r <= 0) return id; }
        return pool[^1].id;
    }
}

/// <summary>Stateful scheduler for <see cref="WeatherCycle"/>; time and randomness are injected so it is unit-testable.</summary>
internal sealed class WeatherScheduler
{
    private readonly System.Random _rng;
    private bool _active;
    private string? _current;
    private DateTime _nextUtc;

    public WeatherScheduler(System.Random? rng = null) => _rng = rng ?? new System.Random();

    public DateTime NextChangeUtc => _nextUtc;

    /// <summary>Forget the current weather so the cycle restarts.</summary>
    public void Reset() => _current = null;

    /// <summary>The randomly chosen weather id, or null when the weather is not "Ngẫu nhiên" (the user's own pick applies).</summary>
    public string? Current(DateTime nowUtc, AppConfig cfg, SeasonId season)
    {
        if (cfg.WeatherPreview != WeatherCycle.Random) { _active = false; _current = null; return null; }
        if (_current is null || !_active)
        {
            _active = true;
            _current = WeatherCycle.PickRandom(season, null, _rng);
            _nextUtc = nowUtc + NextSpan(cfg);
            return _current;
        }
        // The season moved on and the current weather no longer belongs to it (e.g. snow after winter).
        if (!WeatherCycle.InPool(season, _current))
        {
            _current = WeatherCycle.PickRandom(season, _current, _rng);
            _nextUtc = nowUtc + NextSpan(cfg);
            return _current;
        }
        while (nowUtc >= _nextUtc)
        {
            _current = WeatherCycle.PickRandom(season, _current, _rng);
            _nextUtc += NextSpan(cfg);
            if (_nextUtc <= nowUtc) _nextUtc = nowUtc + NextSpan(cfg);
        }
        return _current;
    }

    private TimeSpan NextSpan(AppConfig cfg)
    {
        double lo = cfg.WeatherCycleRandomMinMinutes, hi = Math.Max(lo, cfg.WeatherCycleRandomMaxMinutes);
        return TimeSpan.FromMinutes(lo + _rng.NextDouble() * (hi - lo));
    }
}
