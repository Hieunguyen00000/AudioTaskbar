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

internal enum PlanePhase { Away, Roll, Liftoff, Climb, Cruise, Descent, Exit }

/// <param name="X">Centre of the fuselage (logical px).</param>
/// <param name="Alt">Height of the wheels above the runway (px). 0 on the ground.</param>
/// <param name="PitchDeg">Nose-up angle.</param>
/// <param name="Thrust">0..1 engine effort (drives the take-off / climb sound).</param>
internal readonly record struct PlaneState(PlanePhase Phase, float PhaseT, float X, float Alt, float PitchDeg, float Speed,
    float Thrust, bool OnGround, bool Visible);

/// <summary>
/// A short take-off strip at the left of the leftmost free span that can hold it.
/// The plane always rolls, lifts off, climbs and leaves at the right of the bar; when no span is
/// wide enough the strip is not drawn and road traffic is left alone.
/// </summary>
internal readonly record struct TakeoffStrip(bool HasStrip, float L, float R)
{
    public const float PlaneLen = 34f;
    /// <summary>Narrowest free span (px) that still holds a short take-off strip.</summary>
    public const float MinSpan = 56f;
    public const float DefaultLen = 96f;
    public const float MaxLen = 120f;

    public static TakeoffStrip Plan(IReadOnlyList<(float L, float R)> spans, float barW)
    {
        if (spans.Count == 0) spans = new[] { (0f, barW) };
        foreach (var s in spans)
        {
            float w = s.R - s.L;
            if (w < MinSpan) continue;
            float len = Math.Min(DefaultLen, Math.Min(MaxLen, w));
            return new(true, s.L, s.L + len);
        }
        return new(false, 0, Math.Min(DefaultLen, Math.Max(0f, barW)));
    }

    /// <summary>
    /// Span the train should use: the widest one, preferring a span that is not the strip's when
    /// another is at least 48 px. If the chosen span also holds the strip, the returned left edge
    /// starts just after the strip so the platform does not sit on the runway.
    /// </summary>
    public static (float L, float R) StationSpan(IReadOnlyList<(float L, float R)> spans, float barW, TakeoffStrip strip)
    {
        if (spans.Count == 0) return (0, barW);
        var order = spans.Select((s, k) => (s, k)).OrderByDescending(v => v.s.R - v.s.L).ToList();
        int stripSpan = -1;
        if (strip.HasStrip)
        {
            for (int i = 0; i < spans.Count; i++)
                if (strip.L >= spans[i].L - 0.5f && strip.R <= spans[i].R + 0.5f) { stripSpan = i; break; }
        }
        var pick = order.FirstOrDefault(v => v.k != stripSpan && v.s.R - v.s.L >= 48);
        var s = pick.s.R - pick.s.L >= 48 ? pick.s : order[0].s;
        if (strip.HasStrip && s.L <= strip.L + 1f && s.R >= strip.R - 1f)
            return (Math.Min(strip.R + 6f, Math.Max(s.L, s.R - 8f)), s.R);
        return s;
    }
}

/// <summary>
/// Take-off roll from the left → lift off → climb to cruise height → cruise high across the middle
/// → descend near the right and leave the view low (it does not land) → a gap. One cycle is how often a take-off happens.
/// </summary>
internal static class PlaneCycle
{
    public const float CruiseAlt = 24f;
    /// <summary>Height of the wheels as the plane leaves the view: low, but still flying (it never lands).</summary>
    public const float ExitAlt = 3.2f;
    public const float LiftAlt = 5.5f;

    public static readonly (int Id, double Frac)[] Plan =
    {
        ((int)PlanePhase.Away, 0.18), ((int)PlanePhase.Roll, 0.12), ((int)PlanePhase.Liftoff, 0.08),
        ((int)PlanePhase.Climb, 0.16), ((int)PlanePhase.Cruise, 0.26), ((int)PlanePhase.Descent, 0.12),
        ((int)PlanePhase.Exit, 0.08)
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

    /// <summary>
    /// Key x positions along the flight: start of the roll, lift-off, start of the climb, start of the cruise,
    /// start of the descent (near the right), start of the low exit, and leaving the bar.
    /// </summary>
    public static (float Start, float Lift, float ClimbStart, float CruiseStart, float DescentStart, float ExitStart, float Exit) Marks(float barW, TakeoffStrip strip)
    {
        float L = TakeoffStrip.PlaneLen;
        float sl = strip.HasStrip ? strip.L : 0f;
        float sr = strip.HasStrip ? strip.R : Math.Min(TakeoffStrip.DefaultLen, Math.Max(L + 8f, barW * 0.12f));
        float start = sl + L * 0.5f;
        float lift = Math.Max(start + 8f, sr - 4f);
        float exit = barW + L * 0.5f;
        if (lift > exit - 40f) lift = Math.Max(start + 4f, exit - 40f);
        float remain = Math.Max(40f, exit - lift);
        // After lift-off: a short climb, a long high cruise across the middle, then a descent close to the right edge.
        float climbStart = lift + Math.Min(remain * 0.08f, 22f);
        float cruiseStart = lift + remain * 0.28f;
        float descentStart = lift + remain * 0.78f;
        float exitStart = lift + remain * 0.92f;
        return (start, lift, climbStart, cruiseStart, descentStart, exitStart, exit);
    }

    public static PlaneState Evaluate(double p, float barW, TakeoffStrip strip = default)
    {
        var (ph, t) = PhaseAt(p);
        var (start, lift, climbStart, cruiseStart, descentStart, exitStart, exit) = Marks(barW, strip);
        switch (ph)
        {
            case PlanePhase.Away:
                return new(ph, t, start, 0, 0, 0, 0, true, false);
            case PlanePhase.Roll:
            {
                float pitch = t > 0.85f ? 8f * (t - 0.85f) / 0.15f : 0f;
                return new(ph, t, TransitMath.Lerp(start, lift, TransitMath.EaseIn(t)), 0, pitch,
                    0.25f + 0.75f * t, 0.35f + 0.65f * t, true, true);
            }
            case PlanePhase.Liftoff:
            {
                float alt = LiftAlt * TransitMath.EaseIn(t);
                return new(ph, t, TransitMath.Lerp(lift, climbStart, t), alt, 8f + 6f * t, 1f, 1f, alt < 0.4f, true);
            }
            case PlanePhase.Climb:
            {
                float fx = 0.4f * t + 0.6f * TransitMath.EaseOut(t);
                float alt = TransitMath.Lerp(LiftAlt, CruiseAlt, TransitMath.EaseOut(t));
                return new(ph, t, TransitMath.Lerp(climbStart, cruiseStart, fx), alt, 14f - 11f * t, 1f, 0.95f, false, true);
            }
            case PlanePhase.Cruise:
            {
                float bob = MathF.Sin(t * 6.28f * 2f) * 0.6f;
                return new(ph, t, TransitMath.Lerp(cruiseStart, descentStart, t), CruiseAlt + bob, 3f, 1f, 0.62f, false, true);
            }
            case PlanePhase.Descent:
            {
                float alt = TransitMath.Lerp(CruiseAlt, ExitAlt, TransitMath.Smooth(t));
                float pitch = t < 0.75f
                    ? TransitMath.Lerp(3f, -6f, TransitMath.Smooth(t / 0.75f))
                    : TransitMath.Lerp(-6f, -1.5f, (t - 0.75f) / 0.25f);
                return new(ph, t, TransitMath.Lerp(descentStart, exitStart, t), alt, pitch, 1f, 0.45f, false, true);
            }
            default: // Exit: already low, leaves the view without touching down
            {
                return new(ph, t, TransitMath.Lerp(exitStart, exit, t), ExitAlt, -1.5f, 1f, 0.38f - 0.1f * t, false, true);
            }
        }
    }
}
