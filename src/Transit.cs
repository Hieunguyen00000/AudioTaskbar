namespace AudioTaskbar;

// v5.4: lifecycle state machines of the two vehicles of the diorama (train and airplane).
// Everything here is a PURE function of the cycle position (0..1) and the scene geometry, so it is
// deterministic, testable without a screen, and the drawing code only has to look at the returned state.

internal static class TransitMath
{
    public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : (float.IsNaN(v) ? 0f : v);
    public static float Smooth(float v) { v = Clamp01(v); return v * v * (3f - 2f * v); }
    public static float EaseOut(float v) { v = Clamp01(v); return 1f - (1f - v) * (1f - v); }
    public static float EaseIn(float v) { v = Clamp01(v); return v * v; }
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Cycle position 0..1 (exclusive) after <paramref name="timeSeconds"/>; never NaN / Infinity.</summary>
    public static double Cycle(double phase0, double timeSeconds, double periodSeconds)
        => SceneMath.TrainProgress(phase0, timeSeconds, periodSeconds);

    /// <summary>Phase table lookup: which row of a (phase, fraction) table holds <paramref name="p"/>, and how far (0..1) into it.</summary>
    public static (int Index, float T) Locate((int Id, double Frac)[] table, double p)
    {
        if (!double.IsFinite(p)) p = 0;
        p -= Math.Floor(p);
        double acc = 0;
        for (int i = 0; i < table.Length; i++)
        {
            double end = acc + table[i].Frac;
            if (p < end || i == table.Length - 1)
                return (i, (float)Math.Clamp((p - acc) / table[i].Frac, 0, 1));
            acc = end;
        }
        return (table.Length - 1, 1f);
    }

    public static double StartOf((int Id, double Frac)[] table, int index)
    {
        double acc = 0;
        for (int i = 0; i < index; i++) acc += table[i].Frac;
        return acc;
    }
}

// ───────────────────────────────────────── train ─────────────────────────────────────────

internal enum TrainPhase { Away, Approach, Alight, Board, Depart }

/// <param name="X">Left edge of the whole train (logical px).</param>
/// <param name="Speed">0 (standing) .. 1 (fastest part of the run).</param>
/// <param name="Doors">0 closed .. 1 open.</param>
/// <param name="Alighting">0..1 how far the passengers getting off have got (0 before they start, 1 once they are gone).</param>
/// <param name="Boarding">0..1 the same for the passengers getting on.</param>
internal readonly record struct TrainState(TrainPhase Phase, float PhaseT, float X, float Speed, float Doors, float Alighting, float Boarding, bool Visible);

/// <summary>
/// Approach and slow down → stop at the platform → passengers get off → passengers get on → doors close, depart and speed up
/// → off the right edge → a short gap. One full lifecycle takes exactly the configured period.
/// </summary>
internal static class TrainCycle
{
    public static readonly (int Id, double Frac)[] Plan =
    {
        ((int)TrainPhase.Away, 0.04), ((int)TrainPhase.Approach, 0.36), ((int)TrainPhase.Alight, 0.10),
        ((int)TrainPhase.Board, 0.10), ((int)TrainPhase.Depart, 0.40)
    };

    /// <summary>Cycle position at which the train has just stopped, doors opening (used to seed a visible train at launch).</summary>
    public static double StopStart => TransitMath.StartOf(Plan, (int)TrainPhase.Alight);
    public static double BoardStart => TransitMath.StartOf(Plan, (int)TrainPhase.Board);

    public static (TrainPhase Phase, float T) PhaseAt(double p)
    {
        var (i, t) = TransitMath.Locate(Plan, p);
        return ((TrainPhase)Plan[i].Id, t);
    }

    /// <param name="barW">Logical width of the bar (the train leaves at this x).</param>
    /// <param name="trainW">Length of the train.</param>
    /// <param name="stopLeft">Left edge of the train while it stands at the platform.</param>
    public static TrainState Evaluate(double p, float barW, float trainW, float stopLeft)
    {
        var (ph, t) = PhaseAt(p);
        float x0 = -trainW, x1 = Math.Max(barW, stopLeft + 1);
        switch (ph)
        {
            case TrainPhase.Away:
                return new(ph, t, x0, 0f, 0f, 0f, 0f, false);
            case TrainPhase.Approach:
                return new(ph, t, TransitMath.Lerp(x0, stopLeft, TransitMath.EaseOut(t)), 1f - t, 0f, 0f, 0f, true);
            case TrainPhase.Alight:
                return new(ph, t, stopLeft, 0f, TransitMath.Clamp01(t / 0.18f), t, 0f, true);
            case TrainPhase.Board:
                return new(ph, t, stopLeft, 0f, TransitMath.Clamp01((1f - t) / 0.18f), 1f, t, true);
            default: // Depart
                return new(ph, t, TransitMath.Lerp(stopLeft, x1, TransitMath.EaseIn(t)), t, 0f, 1f, 1f, true);
        }
    }

    /// <summary>
    /// Progress of one passenger 0..1 (walking away from / towards the door). Passengers start one after another so they do not
    /// move as a block: passenger <paramref name="i"/> of <paramref name="n"/> is idle for the first part of the phase.
    /// </summary>
    public static float Walk(float phaseProgress, int i, int n)
    {
        float delay = n <= 1 ? 0f : 0.45f * i / (n - 1);
        return TransitMath.Clamp01((phaseProgress - delay) / 0.55f);
    }
}

// ───────────────────────────────────────── airplane ─────────────────────────────────────────

internal enum PlanePhase { Away, Approach, Landing, TaxiIn, Deplane, Board, TaxiOut, Takeoff, ClimbOut, Cruise }

/// <param name="X">Centre of the fuselage (logical px).</param>
/// <param name="Alt">Height of the wheels above the runway (px). 0 on the ground.</param>
/// <param name="PitchDeg">Nose-up angle (negative = nose down).</param>
/// <param name="Thrust">0..1 engine effort (drives the engine sound).</param>
/// <param name="Stairs">0..1 passenger stairs lowered at the front door.</param>
internal readonly record struct PlaneState(PlanePhase Phase, float PhaseT, float X, float Alt, float PitchDeg, float Speed,
    float Thrust, float Stairs, float Deplaning, float Boarding, bool OnGround, bool Visible);

/// <summary>Where the little airport sits (terminal + stand) in the bar, or that it does not fit and the plane only crosses the sky.</summary>
internal readonly record struct AirportPlan(bool HasAirport, float StandX, float TermL, float TermR, float SpanL = 0, float SpanR = 0)
{
    public const float PlaneLen = 34f;
    /// <summary>Narrowest free span (px) that still holds the parked plane next to the terminal.</summary>
    public const float MinSpan = 84f;
    private const float AirplaneToTerminal = PlaneLen + 4f;

    /// <summary>Index of the widest span that fits an airport, or -1.</summary>
    public static int FindSpan(IReadOnlyList<(float L, float R)> spans, float barW)
    {
        int best = -1; float bw = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            float w = spans[i].R - spans[i].L;
            if (w > bw) { bw = w; best = i; }
        }
        return best >= 0 && bw >= MinSpan ? best : -1;
    }

    public static AirportPlan Plan(IReadOnlyList<(float L, float R)> spans, float barW)
    {
        if (spans.Count == 0) spans = new[] { (0f, barW) };
        int i = FindSpan(spans, barW);
        if (i < 0) return new(false, 0, 0, 0, 0, 0);
        var s = spans[i];
        float sw = s.R - s.L;
        float tw = Math.Clamp(sw * 0.34f, 34f, 52f);
        // Room left over once the parked plane and the terminal are in: ~45 % of it before them (landing roll), ~55 % after
        // them (take-off roll), so both can be watched when the span is wide.
        float slack = Math.Max(0f, sw - (tw + 44f));
        float termL = s.L + 2f + (AirplaneToTerminal) + 0.45f * slack, termR = termL + tw;
        float stand = termL - 4f - PlaneLen / 2f;
        return new(true, stand, termL, termR, s.L, s.R);
    }

    /// <summary>The span the train should use: widest span that is NOT the airport's, else the widest one.</summary>
    public static (float L, float R) StationSpan(IReadOnlyList<(float L, float R)> spans, float barW, int airportSpan)
    {
        if (spans.Count == 0) return (0, barW);
        var order = spans.Select((s, k) => (s, k)).OrderByDescending(v => v.s.R - v.s.L).ToList();
        var pick = order.FirstOrDefault(v => v.k != airportSpan && v.s.R - v.s.L >= 48);
        return (pick.s.R - pick.s.L >= 48 ? pick.s : order[0].s);
    }
}

/// <summary>
/// Fly in and descend → touch down and roll out → taxi to the stand → stop, stairs down, passengers get off → passengers get on
/// → taxi out → take-off roll → climb out and leave → a gap. Without an airport the plane only crosses the sky.
/// </summary>
internal static class PlaneCycle
{
    public const float CruiseAlt = 24f;

    public static readonly (int Id, double Frac)[] Plan =
    {
        ((int)PlanePhase.Away, 0.14), ((int)PlanePhase.Approach, 0.16), ((int)PlanePhase.Landing, 0.07),
        ((int)PlanePhase.TaxiIn, 0.07), ((int)PlanePhase.Deplane, 0.13), ((int)PlanePhase.Board, 0.13),
        ((int)PlanePhase.TaxiOut, 0.06), ((int)PlanePhase.Takeoff, 0.08), ((int)PlanePhase.ClimbOut, 0.16)
    };

    /// <summary>Sky-only variant: cross the sky once per period, then a gap.</summary>
    public static readonly (int Id, double Frac)[] FlyoverPlan = { ((int)PlanePhase.Cruise, 0.55), ((int)PlanePhase.Away, 0.45) };

    public static double StartOf(PlanePhase ph)
    {
        for (int i = 0; i < Plan.Length; i++) if (Plan[i].Id == (int)ph) return TransitMath.StartOf(Plan, i);
        return 0;
    }

    public static (PlanePhase Phase, float T) PhaseAt(double p, bool airport)
    {
        var table = airport ? Plan : FlyoverPlan;
        var (i, t) = TransitMath.Locate(table, p);
        return ((PlanePhase)table[i].Id, t);
    }

    /// <summary>Key x positions of the ground run for a given stand.</summary>
    public static (float Touchdown, float TaxiStart, float TaxiOutEnd, float Liftoff) Marks(AirportPlan ap)
    {
        // The runway only exists inside the airport span, so touch-down, taxiing and lift-off all stay inside it.
        float standX = ap.StandX;
        float taxi = Math.Min(standX - 4f, Math.Max(standX - 36f, ap.SpanL + 4f));
        float td = Math.Max(taxi - 110f, ap.SpanL + 2f);
        float outEnd = standX + 40f;
        float lift = Math.Clamp(outEnd + 120f, outEnd + 8f, Math.Max(outEnd + 8f, ap.SpanR - 10f));
        return (td, taxi, outEnd, lift);
    }

    public static PlaneState Evaluate(double p, float barW, AirportPlan ap)
    {
        float L = AirportPlan.PlaneLen;
        var (ph, t) = PhaseAt(p, ap.HasAirport);
        float x0 = -L;
        if (!ap.HasAirport)
        {
            if (ph == PlanePhase.Away) return new(ph, t, x0, CruiseAlt, 0, 0, 0, 0, 0, 0, false, false);
            float bob = MathF.Sin(t * 6.28f * 3f) * 1.2f;
            return new(ph, t, TransitMath.Lerp(x0, barW + L, t), CruiseAlt + bob - 3f, 0, 1f, 0.6f, 0, 0, 0, false, true);
        }
        var (td, taxi, outEnd, lift) = Marks(ap);
        float stand = ap.StandX;
        switch (ph)
        {
            case PlanePhase.Away:
                return new(ph, t, x0, CruiseAlt, 0, 0, 0, 0, 0, 0, false, false);
            case PlanePhase.Approach:
            {
                float fx = 0.7f * t + 0.3f * TransitMath.EaseOut(t);
                float alt = CruiseAlt * (1f - TransitMath.Smooth((t - 0.30f) / 0.70f));
                float pitch = t < 0.30f ? 0f : (t < 0.88f ? -3f : 3f);
                return new(ph, t, TransitMath.Lerp(x0, td, fx), alt, pitch, 0.8f, 0.45f, 0, 0, 0, alt < 0.05f, true);
            }
            case PlanePhase.Landing:
                return new(ph, t, TransitMath.Lerp(td, taxi, TransitMath.EaseOut(t)), 0, 0, 1f - 0.8f * t, 0.5f * (1f - t) + 0.1f, 0, 0, 0, true, true);
            case PlanePhase.TaxiIn:
                return new(ph, t, TransitMath.Lerp(taxi, stand, TransitMath.EaseOut(t)), 0, 0, 0.2f * (1f - t), 0.12f, 0, 0, 0, true, true);
            case PlanePhase.Deplane:
                return new(ph, t, stand, 0, 0, 0, 0.04f, TransitMath.Clamp01(t / 0.12f), t, 0, true, true);
            case PlanePhase.Board:
                return new(ph, t, stand, 0, 0, 0, 0.04f + 0.08f * TransitMath.Smooth((t - 0.85f) / 0.15f),
                    TransitMath.Clamp01((1f - t) / 0.12f), 1f, t, true, true);
            case PlanePhase.TaxiOut:
                return new(ph, t, TransitMath.Lerp(stand, outEnd, TransitMath.EaseIn(t)), 0, 0, 0.2f * t, 0.15f + 0.1f * t, 0, 1f, 1f, true, true);
            case PlanePhase.Takeoff:
            {
                float pitch = t > 0.75f ? 10f * (t - 0.75f) / 0.25f : 0f;
                return new(ph, t, TransitMath.Lerp(outEnd, lift, TransitMath.EaseIn(t)), 0, pitch, 0.2f + 0.7f * t, 0.9f, 0, 1f, 1f, true, true);
            }
            default: // ClimbOut
            {
                float fx = 0.45f * t + 0.55f * t * t;
                float alt = CruiseAlt * TransitMath.EaseOut(t);
                return new(ph, t, TransitMath.Lerp(lift, Math.Max(barW + L, lift + 1), fx), alt, 12f * (1f - t) + 2f, 1f, 0.9f, 0, 1f, 1f, false, true);
            }
        }
    }

    /// <summary>Passenger walk progress, see <see cref="TrainCycle.Walk"/>.</summary>
    public static float Walk(float phaseProgress, int i, int n) => TrainCycle.Walk(phaseProgress, i, n);
}
