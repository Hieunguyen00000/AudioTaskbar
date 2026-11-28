using System.Diagnostics;

namespace AudioTaskbar;

/// <summary>
/// One lightning strike. <see cref="Distance"/> 0 = right overhead, 1 = far away; <see cref="X01"/> is where it hits across the bar.
/// The flash drawn on the diorama and the thunder heard through the mixer are both made from the same strike.
/// </summary>
internal readonly record struct LightningStrike(long Id, double Time, float X01, float Distance)
{
    private static float C(float d) => Math.Clamp(d, 0f, 1f);

    /// <summary>Seconds from the flash to the crack: about 0.04 s when it is overhead, up to 2.8 s when it is far.</summary>
    public static float ThunderDelayFor(float distance) => 0.04f + 2.76f * MathF.Pow(C(distance), 1.2f);
    /// <summary>Thunder loudness: distant strikes are quieter.</summary>
    public static float ThunderGainFor(float distance) => 1f - 0.62f * C(distance);
    /// <summary>Distant thunder loses its treble: the low-pass goes from open (20 kHz) to 1.4 kHz.</summary>
    public static float ThunderCutoffFor(float distance) => 20000f - 18600f * C(distance);
    /// <summary>Distant thunder also sounds a little slower / deeper (playback rate 1 .. 0.93).</summary>
    public static float ThunderRateFor(float distance) => 1f - 0.07f * C(distance);
    /// <summary>Brightness of the flash on the diorama: 1 close, 0.55 far.</summary>
    public static float FlashPowerFor(float distance) => 1f - 0.45f * C(distance);

    public float ThunderDelay => ThunderDelayFor(Distance);
}

/// <summary>
/// The single source of lightning for the app (shared by every diorama window and by the thunder sound). During a storm it
/// plans strikes on a wall clock; a diorama that flashes one calls <see cref="MarkShown"/>, which is what the thunder
/// listens to: a strike is heard exactly when (and only when) it was flashed.
/// </summary>
internal sealed class LightningSchedule
{
    public const double MinGap = 4.0, MaxGap = 11.0;

    private readonly object _lock = new();
    private readonly FastRng _rng;
    private readonly Func<double> _clock;
    private readonly List<LightningStrike> _all = new();
    private double _next = double.NaN;
    private long _id, _shownUpTo;

    public static LightningSchedule Shared { get; } = new(Environment.TickCount);

    public LightningSchedule(int seed, Func<double>? clock = null)
    {
        _rng = new FastRng(seed);
        _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    /// <summary>Raised (on the caller's thread) the first time a strike is flashed on screen.</summary>
    public event Action<LightningStrike>? Shown;

    public double Now => _clock();

    /// <summary>Plans the strikes that are due by <paramref name="now"/>; outside a storm nothing is planned and the plan restarts.</summary>
    public void Advance(double now, bool storm)
    {
        lock (_lock)
        {
            if (!storm) { _next = double.NaN; return; }
            if (double.IsNaN(_next)) _next = now + _rng.Range(1.0f, 4.0f);
            int guard = 0;
            while (_next <= now && guard++ < 4)
            {
                // about a third are close, the rest spread over mid and far distances
                float d = _rng.Chance(0.3f) ? _rng.Range(0f, 0.2f) : _rng.Range(0.2f, 1f);
                _all.Add(new LightningStrike(++_id, _next, _rng.Range(0f, 1f), d));
                if (_all.Count > 64) _all.RemoveAt(0);
                _next += _rng.Range((float)MinGap, (float)MaxGap);
            }
            if (_next <= now) _next = now + _rng.Range((float)MinGap, (float)MaxGap);
        }
    }

    public LightningStrike[] StrikesSince(long afterId)
    {
        lock (_lock) return _all.Where(s => s.Id > afterId).ToArray();
    }

    public long LastId { get { lock (_lock) return _id; } }
    public long LastShownId { get { lock (_lock) return _shownUpTo; } }

    /// <summary>A diorama flashed this strike; tells the thunder. Returns false when another window already showed it.</summary>
    public bool MarkShown(LightningStrike s)
    {
        lock (_lock)
        {
            if (s.Id <= _shownUpTo) return false;
            _shownUpTo = s.Id;
        }
        Shown?.Invoke(s);
        return true;
    }
}
