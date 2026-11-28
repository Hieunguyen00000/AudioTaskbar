using System.Globalization;

namespace AudioTaskbar;

/// <summary>v5.4 seasons. Summer is the look of every earlier version (so old configs look unchanged).</summary>
internal enum SeasonId { Spring, Summer, Autumn, Winter }

internal static class Seasons
{
    public static readonly SeasonId[] All = { SeasonId.Spring, SeasonId.Summer, SeasonId.Autumn, SeasonId.Winter };

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

    public static SeasonId Next(SeasonId s) => All[((int)s + 1) % All.Length];

    /// <summary>Pure season clock: the season that is current <paramref name="elapsedMinutes"/> after <paramref name="start"/>.</summary>
    public static SeasonId At(SeasonId start, double elapsedMinutes, float periodMinutes)
    {
        if (!(periodMinutes > 0f) || !float.IsFinite(periodMinutes)) return start;
        if (!double.IsFinite(elapsedMinutes) || elapsedMinutes < 0) elapsedMinutes = 0;
        long steps = (long)Math.Floor(elapsedMinutes / periodMinutes);
        return All[(int)(((int)start + steps) % All.Length)];
    }
}

/// <summary>
/// Runtime season cycle. The configured season is the starting point; with the cycle on, the season advances every
/// <see cref="AppConfig.SeasonCycleMinutes"/> real minutes (spring → summer → autumn → winter → spring …).
/// Changing the configured season, the period or the on/off switch restarts the cycle from the configured season.
/// </summary>
internal sealed class SeasonCycler
{
    private SeasonId _start = SeasonId.Summer;
    private DateTime _anchorUtc;
    private bool _active;
    private float _period;

    public SeasonId Current(DateTime nowUtc, AppConfig cfg)
    {
        var configured = Seasons.Parse(cfg.Season);
        if (!cfg.SeasonCycleEnabled) { _active = false; return configured; }
        if (!_active || _start != configured || Math.Abs(_period - cfg.SeasonCycleMinutes) > 1e-4f)
        {
            _active = true; _start = configured; _period = cfg.SeasonCycleMinutes; _anchorUtc = nowUtc;
        }
        return Seasons.At(_start, (nowUtc - _anchorUtc).TotalMinutes, _period);
    }
}

/// <summary>Weather that can change by itself (v5.4): fixed interval or random, season-aware.</summary>
internal static class WeatherCycle
{
    public const string Off = "off", Interval = "interval", Random = "random";

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

    /// <summary>The user's weather pick as a cycle starting point (null for live / night).</summary>
    public static string? NormalizeStart(string? preview)
        => preview is "sunny" or "cloudy" or "rain" or "storm" or "snow" ? preview : null;

    public static bool InPool(SeasonId season, string id) => Pool(season).Any(p => p.id == id);

    /// <summary>Order used by the fixed-interval mode.</summary>
    public static string NextInOrder(SeasonId season, string current)
    {
        var ids = Pool(season).Select(p => p.id).ToArray();
        int i = Array.IndexOf(ids, current);
        return ids[(i + 1) % ids.Length];
    }

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
    private string _mode = WeatherCycle.Off;
    private string? _current;
    private DateTime _nextUtc;

    public WeatherScheduler(System.Random? rng = null) => _rng = rng ?? new System.Random();

    public DateTime NextChangeUtc => _nextUtc;

    /// <summary>Forget the current weather so the cycle restarts from the user's (new) weather pick.</summary>
    public void Reset() => _current = null;

    /// <summary>The cycled weather id, or null when the cycle is off (the user's own weather choice applies).</summary>
    public string? Current(DateTime nowUtc, AppConfig cfg, SeasonId season)
    {
        string mode = WeatherCycle.NormalizeMode(cfg.WeatherCycleMode);
        if (mode == WeatherCycle.Off) { _mode = mode; _current = null; return null; }
        if (_current is null || mode != _mode)
        {
            _mode = mode;
            var start = WeatherCycle.NormalizeStart(cfg.WeatherPreview);
            _current = start is not null && WeatherCycle.InPool(season, start) ? start : WeatherCycle.PickRandom(season, null, _rng);
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
            _current = mode == WeatherCycle.Interval
                ? WeatherCycle.NextInOrder(season, _current)
                : WeatherCycle.PickRandom(season, _current, _rng);
            _nextUtc += NextSpan(cfg);
            if (_nextUtc <= nowUtc) _nextUtc = nowUtc + NextSpan(cfg);
        }
        return _current;
    }

    private TimeSpan NextSpan(AppConfig cfg)
    {
        if (_mode == WeatherCycle.Interval) return TimeSpan.FromMinutes(cfg.WeatherCycleMinutes);
        double lo = cfg.WeatherCycleRandomMinMinutes, hi = Math.Max(lo, cfg.WeatherCycleRandomMaxMinutes);
        return TimeSpan.FromMinutes(lo + _rng.NextDouble() * (hi - lo));
    }
}
