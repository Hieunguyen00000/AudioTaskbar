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
    /// <summary>
    /// v5.4.2: longer running, shorter stop. Running (approach + depart) 84 % of the cycle (was 76 %), standing at the platform
    /// (alight + board) 12 % (was 20 %). With the default 2-minute cycle: 100.8 s running (was 91.2 s), 14.4 s stop (was 24 s).
    /// </summary>
    public static readonly (int Id, double Frac)[] Plan =
    {
        ((int)TrainPhase.Away, 0.04), ((int)TrainPhase.Approach, 0.42), ((int)TrainPhase.Alight, 0.06),
        ((int)TrainPhase.Board, 0.06), ((int)TrainPhase.Depart, 0.42)
    };

    /// <summary>Span that holds the platform: the widest free span between the taskbar icons (v5.4.2: no runway to avoid).</summary>
    public static (float L, float R) StationSpan(IReadOnlyList<(float L, float R)> spans, float barW)
    {
        if (spans.Count == 0) return (0, barW);
        return spans.OrderByDescending(s => s.R - s.L).First();
    }

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

/// <summary>v5.4.2: three airborne phases only (no runway, nothing on the ground).</summary>
internal enum PlanePhase { Away, Climb, Cruise, Descent }

/// <param name="X">Centre of the fuselage (logical px).</param>
/// <param name="Alt">Height of the plane above the bottom of the bar (px). Always above 0: it never touches the ground.</param>
/// <param name="PitchDeg">Nose-up angle.</param>
/// <param name="Thrust">0..1 engine effort (drives the engine sound).</param>
internal readonly record struct PlaneState(PlanePhase Phase, float PhaseT, float X, float Alt, float PitchDeg, float Speed,
    float Thrust, bool Visible);

/// <summary>
/// v5.4.2 flight: appear in the air, low, at the left end of the bar and climb slowly → hold the cruise height across the
/// middle → descend slowly and disappear low at the right end → a gap. One cycle is how often a plane passes.
/// There is no runway: road traffic and the train never make room for the plane.
/// </summary>
internal static class PlaneCycle
{
    public const float PlaneLen = 34f;
    public const float CruiseAlt = 24f;
    /// <summary>Height at which the plane appears at the left and disappears at the right: low, but flying.</summary>
    public const float EdgeAlt = 4.5f;

    public static readonly (int Id, double Frac)[] Plan =
    {
        ((int)PlanePhase.Away, 0.20), ((int)PlanePhase.Climb, 0.28), ((int)PlanePhase.Cruise, 0.24), ((int)PlanePhase.Descent, 0.28)
    };

    public static double StartOf(PlanePhase ph)
    {
        for (int i = 0; i < Plan.Length; i++) if (Plan[i].Id == (int)ph) return TransitMath.StartOf(Plan, i);
        return 0;
    }

    public static (PlanePhase Phase, float T) PhaseAt(double p)
    {
        var (i, t) = TransitMath.Locate(Plan, p);
        return ((PlanePhase)Plan[i].Id, t);
    }

    /// <summary>x where the plane appears (just off the left edge), starts cruising, starts descending, and leaves (just off the right edge).</summary>
    public static (float Enter, float CruiseStart, float DescentStart, float Exit) Marks(float barW)
    {
        barW = Math.Max(barW, PlaneLen * 3f);
        float enter = -PlaneLen * 0.5f, exit = barW + PlaneLen * 0.5f;
        return (enter, TransitMath.Lerp(enter, exit, 0.34f), TransitMath.Lerp(enter, exit, 0.66f), exit);
    }

    public static PlaneState Evaluate(double p, float barW)
    {
        var (ph, t) = PhaseAt(p);
        var (enter, cruiseStart, descentStart, exit) = Marks(barW);
        switch (ph)
        {
            case PlanePhase.Away:
                return new(ph, t, enter, EdgeAlt, 0f, 0f, 0f, false);
            case PlanePhase.Climb:
            {
                float alt = TransitMath.Lerp(EdgeAlt, CruiseAlt, TransitMath.Smooth(t));
                float pitch = 5f * MathF.Sin(MathF.PI * Math.Clamp(t * 1.15f, 0f, 1f)) + 1f;  // gentle nose-up, level at the top
                return new(ph, t, TransitMath.Lerp(enter, cruiseStart, t), alt, pitch, 1f, 0.92f - 0.12f * t, true);
            }
            case PlanePhase.Cruise:
            {
                float bob = MathF.Sin(t * 6.28f * 2f) * 0.6f;
                return new(ph, t, TransitMath.Lerp(cruiseStart, descentStart, t), CruiseAlt + bob, 1f, 1f, 0.62f, true);
            }
            default: // Descent: slowly down, still flying when it leaves the bar
            {
                float alt = TransitMath.Lerp(CruiseAlt, EdgeAlt, TransitMath.Smooth(t));
                float pitch = -4f * MathF.Sin(MathF.PI * Math.Clamp(t * 1.15f, 0f, 1f));
                return new(ph, t, TransitMath.Lerp(descentStart, exit, t), alt, pitch, 1f, 0.48f - 0.08f * t, true);
            }
        }
    }
}
