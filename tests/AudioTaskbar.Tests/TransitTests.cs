using System.Drawing.Imaging;
using System.Text.Json;

namespace AudioTaskbar.Tests;

/// <summary>v5.4: lifecycle state machines of the train (stop at a platform) and the airplane (take off, cruise, descend, leave low), pure functions.</summary>
public class TransitCycleTests
{
    private const float BarW = 1200f, TrainW = 141f, StopLeft = 700f;
    private static double Mid(IEnumerable<(int Id, double Frac)> plan, int id)
    {
        double acc = 0;
        foreach (var r in plan) { if (r.Id == id) return acc + r.Frac / 2; acc += r.Frac; }
        throw new InvalidOperationException();
    }

    // ───────── train ─────────

    [Fact]
    public void Train_plan_covers_exactly_one_period_in_the_documented_order()
    {
        Assert.Equal(1.0, TrainCycle.Plan.Sum(r => r.Frac), 9);
        Assert.Equal(new[] { TrainPhase.Away, TrainPhase.Approach, TrainPhase.Alight, TrainPhase.Board, TrainPhase.Depart },
            TrainCycle.Plan.Select(r => (TrainPhase)r.Id));
        var seen = new List<TrainPhase>();
        for (double p = 0; p < 1; p += 0.001) { var ph = TrainCycle.PhaseAt(p).Phase; if (seen.Count == 0 || seen[^1] != ph) seen.Add(ph); }
        Assert.Equal(new[] { TrainPhase.Away, TrainPhase.Approach, TrainPhase.Alight, TrainPhase.Board, TrainPhase.Depart }, seen);
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(150.0)]
    public void Train_phase_durations_are_fractions_of_the_configured_period(double periodSeconds)
    {
        // timing in seconds: stopped (alight + board) takes 20 % of the period, approach 36 %, depart 40 %
        double Seconds(TrainPhase ph) => TrainCycle.Plan.First(r => r.Id == (int)ph).Frac * periodSeconds;
        Assert.Equal(0.36 * periodSeconds, Seconds(TrainPhase.Approach), 6);
        Assert.Equal(0.20 * periodSeconds, Seconds(TrainPhase.Alight) + Seconds(TrainPhase.Board), 6);
        Assert.Equal(0.40 * periodSeconds, Seconds(TrainPhase.Depart), 6);
        // a clock running at that period lands in the right phase at the right second
        double p = TransitMath.Cycle(0, 0.04 * periodSeconds + 0.5, periodSeconds);
        Assert.Equal(TrainPhase.Approach, TrainCycle.PhaseAt(p).Phase);
        p = TransitMath.Cycle(0, (0.40 + 0.01) * periodSeconds, periodSeconds);
        Assert.Equal(TrainPhase.Alight, TrainCycle.PhaseAt(p).Phase);
        p = TransitMath.Cycle(0, (0.60 + 0.01) * periodSeconds, periodSeconds);
        Assert.Equal(TrainPhase.Depart, TrainCycle.PhaseAt(p).Phase);
    }

    [Fact]
    public void Train_slows_down_stops_at_the_platform_and_then_speeds_up()
    {
        double a0 = TransitMath.StartOf(TrainCycle.Plan, 1), a1 = a0 + 0.36;
        float lastX = float.MinValue, lastSpeed = float.MaxValue;
        for (double p = a0 + 1e-6; p < a1; p += 0.002)
        {
            var s = TrainCycle.Evaluate(p, BarW, TrainW, StopLeft);
            Assert.Equal(TrainPhase.Approach, s.Phase);
            Assert.True(s.X >= lastX); Assert.True(s.Speed <= lastSpeed + 1e-6f);   // rolling forward, slowing down
            Assert.True(s.X <= StopLeft + 0.01f);
            lastX = s.X; lastSpeed = s.Speed;
        }
        Assert.True(lastSpeed < 0.02f);
        Assert.InRange(lastX, StopLeft - 3f, StopLeft + 0.01f);

        // standing: same place, speed 0, for the whole of alight + board
        foreach (var ph in new[] { TrainPhase.Alight, TrainPhase.Board })
            for (float t = 0.05f; t < 1; t += 0.2f)
            {
                var p = TransitMath.StartOf(TrainCycle.Plan, (int)ph) + TrainCycle.Plan[(int)ph].Frac * t;
                var s = TrainCycle.Evaluate(p, BarW, TrainW, StopLeft);
                Assert.Equal(ph, s.Phase); Assert.Equal(StopLeft, s.X); Assert.Equal(0f, s.Speed);
            }

        // departing: accelerating, ends fully off the right edge
        double d0 = TransitMath.StartOf(TrainCycle.Plan, 4);
        float prevX = StopLeft, prevV = 0, prevStep = 0;
        for (double p = d0 + 1e-6; p < 1; p += 0.004)
        {
            var s = TrainCycle.Evaluate(p, BarW, TrainW, StopLeft);
            Assert.Equal(TrainPhase.Depart, s.Phase);
            Assert.True(s.X >= prevX); Assert.True(s.Speed >= prevV);
            float step = s.X - prevX; Assert.True(step >= prevStep - 0.01f);   // each step at least as long as the one before
            prevX = s.X; prevV = s.Speed; prevStep = step;
        }
        Assert.True(prevX > BarW - 15);
        Assert.False(TrainCycle.Evaluate(0.01, BarW, TrainW, StopLeft).Visible);   // the gap before the next arrival
    }

    [Fact]
    public void Train_doors_open_while_stopped_and_passengers_get_off_before_others_get_on()
    {
        var approach = TrainCycle.Evaluate(Mid(TrainCycle.Plan, (int)TrainPhase.Approach), BarW, TrainW, StopLeft);
        Assert.Equal(0f, approach.Doors); Assert.Equal(0f, approach.Alighting); Assert.Equal(0f, approach.Boarding);
        var alight = TrainCycle.Evaluate(Mid(TrainCycle.Plan, (int)TrainPhase.Alight), BarW, TrainW, StopLeft);
        Assert.Equal(1f, alight.Doors); Assert.InRange(alight.Alighting, 0.4f, 0.6f); Assert.Equal(0f, alight.Boarding);
        var board = TrainCycle.Evaluate(Mid(TrainCycle.Plan, (int)TrainPhase.Board), BarW, TrainW, StopLeft);
        Assert.Equal(1f, board.Doors); Assert.Equal(1f, board.Alighting); Assert.InRange(board.Boarding, 0.4f, 0.6f);
        var depart = TrainCycle.Evaluate(Mid(TrainCycle.Plan, (int)TrainPhase.Depart), BarW, TrainW, StopLeft);
        Assert.Equal(0f, depart.Doors); Assert.Equal(1f, depart.Boarding);
        // the doors close at the very end of boarding and open at the start of alighting
        double b0 = TransitMath.StartOf(TrainCycle.Plan, (int)TrainPhase.Board);
        Assert.True(TrainCycle.Evaluate(b0 + 0.099, BarW, TrainW, StopLeft).Doors < 0.2f);
        Assert.True(TrainCycle.Evaluate(TrainCycle.StopStart + 0.0005, BarW, TrainW, StopLeft).Doors < 0.2f);
    }

    [Fact]
    public void Passengers_walk_one_after_another_and_every_walk_finishes()
    {
        for (int n = 1; n <= 6; n++)
        {
            Assert.Equal(0f, TrainCycle.Walk(0f, 0, n));
            for (int i = 0; i < n; i++) Assert.Equal(1f, TrainCycle.Walk(1f, i, n));
        }
        Assert.True(TrainCycle.Walk(0.2f, 0, 6) > TrainCycle.Walk(0.2f, 5, 6));
    }

    // ───────── airplane ─────────

    private static TakeoffStrip Strip() => TakeoffStrip.Plan(new[] { (100f, 640f) }, BarW);

    [Fact]
    public void Airplane_plan_covers_one_period_in_lifecycle_order()
    {
        Assert.Equal(1.0, PlaneCycle.Plan.Sum(r => r.Frac), 9);
        Assert.Equal(new[] { PlanePhase.Away, PlanePhase.Roll, PlanePhase.Liftoff, PlanePhase.Climb,
                             PlanePhase.Cruise, PlanePhase.Descent, PlanePhase.Exit },
            PlaneCycle.Plan.Select(r => (PlanePhase)r.Id));
        var seen = new List<PlanePhase>();
        for (double p = 0; p < 1; p += 0.0005) { var ph = PlaneCycle.PhaseAt(p).Phase; if (seen.Count == 0 || seen[^1] != ph) seen.Add(ph); }
        Assert.Equal(new[] { PlanePhase.Away, PlanePhase.Roll, PlanePhase.Liftoff, PlanePhase.Climb,
                             PlanePhase.Cruise, PlanePhase.Descent, PlanePhase.Exit }, seen);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(10.0)]
    public void Airplane_phase_timing_follows_the_configured_period(double minutes)
    {
        double period = minutes * 60;
        double Seconds(PlanePhase ph) => PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac * period;
        Assert.Equal(0.12 * period, Seconds(PlanePhase.Roll), 6);
        Assert.Equal(0.26 * period, Seconds(PlanePhase.Cruise), 6);
        Assert.Equal(0.12 * period, Seconds(PlanePhase.Descent), 6);
        Assert.Equal(period, PlaneCycle.Plan.Sum(r => r.Frac * period), 6);
        // the visible flight (everything except the away gap) is 82 % of the cycle
        Assert.Equal(0.82 * period, period - Seconds(PlanePhase.Away), 6);
        double p = TransitMath.Cycle(0.25, 0, period);
        Assert.Equal(0.25, p, 9);
        Assert.Equal(0.5, TransitMath.Cycle(0.25, 0.25 * period, period), 9);
        Assert.Equal(0.25, TransitMath.Cycle(0.25, period, period), 6);
    }

    [Fact]
    public void Takeoff_strip_sits_at_the_left_of_the_leftmost_wide_enough_span()
    {
        var st = Strip();
        Assert.True(st.HasStrip);
        Assert.Equal(100f, st.L);
        Assert.Equal(100f + TakeoffStrip.DefaultLen, st.R);
        Assert.True(st.R <= 640f);
        // nothing wide enough: no strip is drawn (the plane still flies)
        Assert.False(TakeoffStrip.Plan(new[] { (0f, 50f), (200f, 250f) }, BarW).HasStrip);
        Assert.True(TakeoffStrip.Plan(new[] { (10f, 10f + TakeoffStrip.MinSpan) }, BarW).HasStrip);
        var whole = TakeoffStrip.Plan(Array.Empty<(float, float)>(), BarW);
        Assert.True(whole.HasStrip); Assert.Equal(0f, whole.L); Assert.Equal(TakeoffStrip.DefaultLen, whole.R);
        // the train station goes into a different span than the strip when there is one
        var spans = new[] { (100f, 640f), (760f, 960f) };
        Assert.Equal((760f, 960f), TakeoffStrip.StationSpan(spans, BarW, st));
        // one span only: the platform starts just after the strip
        var one = TakeoffStrip.StationSpan(new[] { (100f, 640f) }, BarW, st);
        Assert.True(one.L >= st.R); Assert.Equal(640f, one.R);
    }

    [Fact]
    public void Airplane_runs_through_the_whole_lifecycle_in_order()
    {
        var st = Strip();
        PlaneState S(double p) => PlaneCycle.Evaluate(p, BarW, st);
        double Start(PlanePhase ph) => PlaneCycle.StartOf(ph);
        double Dur(PlanePhase ph) => PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac;
        PlaneState At(PlanePhase ph, float t) => S(Start(ph) + Dur(ph) * t);

        Assert.False(At(PlanePhase.Away, 0.5f).Visible);

        // roll: on the short left strip, accelerating, still on the ground
        var r0 = At(PlanePhase.Roll, 0.05f); var r1 = At(PlanePhase.Roll, 0.98f);
        Assert.True(r0.OnGround && r1.OnGround); Assert.Equal(0f, r0.Alt);
        Assert.True(r1.X > r0.X); Assert.True(r1.Speed > r0.Speed); Assert.True(r1.Thrust > r0.Thrust);
        Assert.InRange(r0.X, st.L, st.R); Assert.InRange(r1.X, st.L, st.R);
        Assert.True(r0.X < st.L + TakeoffStrip.PlaneLen);   // starts at the left end

        // lift off at the end of the strip: leaves the ground, nose comes up
        var k0 = At(PlanePhase.Liftoff, 0.05f); var k1 = At(PlanePhase.Liftoff, 0.99f);
        Assert.True(k1.X > k0.X); Assert.True(k1.Alt > k0.Alt); Assert.True(k1.PitchDeg > 8f);
        Assert.False(k1.OnGround); Assert.True(k1.Thrust > 0.9f);

        // climb to cruise height
        var c0 = At(PlanePhase.Climb, 0.05f); var c1 = At(PlanePhase.Climb, 0.5f); var c2 = At(PlanePhase.Climb, 0.999f);
        Assert.True(c0.Alt < c1.Alt && c1.Alt < c2.Alt); Assert.Equal(PlaneCycle.CruiseAlt, c2.Alt, 1);
        Assert.False(c0.OnGround);

        // cruise high across the middle of the bar
        var u0 = At(PlanePhase.Cruise, 0.05f); var u1 = At(PlanePhase.Cruise, 0.5f); var u2 = At(PlanePhase.Cruise, 0.99f);
        Assert.True(u1.X > u0.X && u2.X > u1.X);
        Assert.InRange(u0.Alt, PlaneCycle.CruiseAlt - 1.2f, PlaneCycle.CruiseAlt + 1.2f);
        Assert.InRange(u1.Alt, PlaneCycle.CruiseAlt - 1.2f, PlaneCycle.CruiseAlt + 1.2f);
        Assert.True(u1.X > BarW * 0.25f && u1.X < BarW * 0.9f);
        Assert.False(u0.OnGround);

        // descend near the right: loses altitude, still flying
        var d0 = At(PlanePhase.Descent, 0.02f); var d1 = At(PlanePhase.Descent, 0.5f); var d2 = At(PlanePhase.Descent, 0.999f);
        Assert.True(d0.Alt > d1.Alt && d1.Alt > d2.Alt);
        Assert.Equal(PlaneCycle.ExitAlt, d2.Alt, 1);
        Assert.False(d0.OnGround || d1.OnGround || d2.OnGround);
        Assert.True(d0.Alt > 0f && d2.Alt > 0f);
        Assert.True(d2.X > d0.X);

        // leave the view low at the right, without landing
        var e0 = At(PlanePhase.Exit, 0.05f); var e2 = At(PlanePhase.Exit, 0.999f);
        Assert.Equal(PlaneCycle.ExitAlt, e0.Alt, 1); Assert.Equal(PlaneCycle.ExitAlt, e2.Alt, 1);
        Assert.False(e0.OnGround || e2.OnGround);
        Assert.True(e2.X >= BarW);
        Assert.True(e2.X > e0.X);
    }

    [Fact]
    public void Airplane_position_has_no_jumps_between_phases_and_never_moves_backwards()
    {
        var st = Strip();
        float prevX = float.MinValue; double prevP = 0;
        PlaneState? prev = null;
        for (double p = PlaneCycle.StartOf(PlanePhase.Roll); p < 1; p += 0.0004)
        {
            var s = PlaneCycle.Evaluate(p, BarW, st);
            Assert.True(s.X >= prevX - 0.001f, $"moves backwards at p={p:F4}");
            if (prev is { } q) Assert.True(MathF.Abs(s.X - q.X) < 14f && MathF.Abs(s.Alt - q.Alt) < 3f, $"jump at p={p:F4} (prev {prevP:F4}): dx={s.X - q.X}, dalt={s.Alt - q.Alt}");
            prevX = s.X; prevP = p; prev = s;
        }
        for (double p = 0; p < 1; p += 0.001)
        {
            var s = PlaneCycle.Evaluate(p, BarW, st);
            Assert.True(float.IsFinite(s.X) && float.IsFinite(s.Alt) && float.IsFinite(s.PitchDeg));
            if (s.Phase == PlanePhase.Roll) { Assert.Equal(0f, s.Alt); Assert.True(s.OnGround); }
            // after leaving the ground it never lands again
            if (s.Phase is PlanePhase.Climb or PlanePhase.Cruise or PlanePhase.Descent or PlanePhase.Exit)
            { Assert.True(s.Alt > 0f); Assert.False(s.OnGround); }
        }
    }

    [Fact]
    public void Without_a_strip_the_plane_still_takes_off_from_the_left_and_leaves_low_at_the_right()
    {
        var none = new TakeoffStrip(false, 0, 0);
        var seen = new List<PlanePhase>();
        float prevX = float.MinValue; float minExit = float.MaxValue; float maxCruise = 0;
        for (double p = 0; p < 1; p += 0.002)
        {
            var s = PlaneCycle.Evaluate(p, BarW, none);
            if (seen.Count == 0 || seen[^1] != s.Phase) seen.Add(s.Phase);
            if (!s.Visible) continue;
            Assert.True(s.X >= prevX - 0.001f); prevX = s.X;
            if (s.Phase == PlanePhase.Cruise) maxCruise = Math.Max(maxCruise, s.Alt);
            if (s.Phase == PlanePhase.Exit) minExit = Math.Min(minExit, s.Alt);
            if (s.Phase is PlanePhase.Descent or PlanePhase.Exit) Assert.False(s.OnGround);
        }
        Assert.Equal(new[] { PlanePhase.Away, PlanePhase.Roll, PlanePhase.Liftoff, PlanePhase.Climb,
                             PlanePhase.Cruise, PlanePhase.Descent, PlanePhase.Exit }, seen);
        Assert.True(prevX >= BarW);
        Assert.True(maxCruise > 15f);
        Assert.True(minExit < 6f && minExit > 0f);   // leaves low, but does not land
    }

    [Fact]
    public void Evaluate_survives_odd_input()
    {
        foreach (var p in new[] { double.NaN, double.PositiveInfinity, -3.7, 5.25 })
        {
            var s = PlaneCycle.Evaluate(p, BarW, Strip());
            Assert.True(float.IsFinite(s.X));
            var t = TrainCycle.Evaluate(p, BarW, TrainW, StopLeft);
            Assert.True(float.IsFinite(t.X));
        }
        Assert.Equal(0.0, TransitMath.Cycle(double.NaN, double.NaN, double.NaN), 9);
        Assert.InRange(TransitMath.Cycle(0, 12345.6, 0), 0, 0.999999);
    }
}

/// <summary>v5.4 config: airplane fields, defaults, migration and the airplane sound slot (silent by default).</summary>
[Collection("Statics")]
public class TransitConfigTests
{
    [Fact]
    public void Train_default_is_two_minutes_and_a_saved_value_is_never_overwritten()
    {
        Assert.Equal(2f, new AppConfig().TrainCrossingMinutes);
        Assert.Equal(2f, AppConfig.DefaultTrainCycleMinutes);
        using var d = new TempDir();
        // brand-new config on disk
        Assert.Equal(2f, ConfigStore.Load(d.File("none.json")).TrainCrossingMinutes);
        // a file that has no train value at all (fresh / very old) gets the default
        var p0 = d.File("a.json"); File.WriteAllText(p0, "{ \"configVersion\": 54 }");
        Assert.Equal(2f, ConfigStore.Load(p0).TrainCrossingMinutes);
        // values the user saved stay exactly as they are, including the old default of 1 and any other number
        foreach (var (name, ver, val) in new[] { ("b.json", 54, 1f), ("c.json", 55, 1f), ("d.json", 53, 1f), ("e.json", 54, 0.5f), ("f.json", 55, 7.5f), ("g.json", 51, 25f) })
        {
            var p = d.File(name);
            File.WriteAllText(p, "{ \"configVersion\": " + ver + ", \"trainCrossingMinutes\": " + val.ToString(System.Globalization.CultureInfo.InvariantCulture) + " }");
            Assert.Equal(val, ConfigStore.Load(p).TrainCrossingMinutes);
            Assert.Equal(val, ConfigStore.Load(p).TrainCrossingMinutes);   // and still after the migrated file is read again
        }
        // pre-v5.1 files that still carry the old 25-minute default move to the new default (that was never a user choice)
        var po = d.File("old.json"); File.WriteAllText(po, "{ \"configVersion\": 0, \"trainCrossingMinutes\": 25 }");
        Assert.Equal(2f, ConfigStore.Load(po).TrainCrossingMinutes);
    }

    [Fact]
    public void Defaults_airplane_on_three_minutes_silent()
    {
        var c = new AppConfig(); c.Normalize();
        Assert.True(c.ShowAirplane);
        Assert.Equal(3f, c.AirplaneCycleMinutes);
        Assert.Equal(55, AppConfig.CurrentConfigVersion);
        Assert.Contains(SoundEffects.Airplane, SoundEffects.Ids);
        Assert.Equal(0, c.SoundVolumes[SoundEffects.Airplane]);
        Assert.All(c.SoundVolumes.Values, v => Assert.Equal(0, v));
    }

    [Theory]
    [InlineData(0f, 3f)]
    [InlineData(-5f, 3f)]
    [InlineData(float.NaN, 3f)]
    [InlineData(0.1f, 0.5f)]
    [InlineData(2f, 2f)]
    [InlineData(9999f, 240f)]
    public void Cycle_minutes_are_clamped(float input, float expected)
    {
        var c = new AppConfig { AirplaneCycleMinutes = input }; c.Normalize();
        Assert.Equal(expected, c.AirplaneCycleMinutes);
    }

    [Fact]
    public void A_v54_file_from_before_the_airplane_gets_it_on_and_keeps_everything_else()
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        File.WriteAllText(p, "{ \"configVersion\": 54, \"region" + "Mode\": \"paris\", \"trainCrossingMinutes\": 2, \"soundMasterVolume\": 70, \"soundVolumes\": { \"rain\": 40, \"train\": 25 }, \"season\": \"autumn\" }");
        var c = ConfigStore.Load(p);
        Assert.Equal(AppConfig.CurrentConfigVersion, c.ConfigVersion);
        Assert.True(c.ShowAirplane); Assert.Equal(3f, c.AirplaneCycleMinutes);
        // what the user had chosen survives (no reset of volumes, season, region, train minutes)
        Assert.Equal("paris", c.RegionMode); Assert.Equal(2f, c.TrainCrossingMinutes); Assert.Equal("autumn", c.Season);
        Assert.Equal(70, c.SoundMasterVolume); Assert.Equal(40, c.SoundVolumes["rain"]); Assert.Equal(25, c.SoundVolumes["train"]);
        Assert.Equal(0, c.SoundVolumes["airplane"]);
        var json = File.ReadAllText(p);
        Assert.Contains("showAirplane", json); Assert.Contains("airplaneCycleMinutes", json); Assert.Contains("\"airplane\": 0", json);
    }

    [Fact]
    public void A_pre_v54_file_is_silent_and_gets_the_airplane()
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        File.WriteAllText(p, "{ \"configVersion\": 51, \"trainCrossingMinutes\": 4 }");
        var c = ConfigStore.Load(p);
        Assert.True(c.ShowAirplane); Assert.Equal(3f, c.AirplaneCycleMinutes); Assert.Equal(4f, c.TrainCrossingMinutes);
        Assert.Equal(0, c.SoundMasterVolume);
        Assert.All(c.SoundVolumes.Values, v => Assert.Equal(0, v));
        Assert.True(c.SoundVolumes.ContainsKey("airplane"));
    }

    [Fact]
    public void A_user_who_switched_the_airplane_off_keeps_it_off_after_a_later_load()
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        var c = new AppConfig { ShowAirplane = false, AirplaneCycleMinutes = 7.5f, ConfigVersion = AppConfig.CurrentConfigVersion };
        ConfigStore.Save(p, c);
        var back = ConfigStore.Load(p);
        Assert.False(back.ShowAirplane); Assert.Equal(7.5f, back.AirplaneCycleMinutes);
    }

    [Fact]
    public void Airplane_sound_is_silent_when_far_and_swells_with_thrust_and_proximity()
    {
        double Rms(float prox, float thrust)
        {
            var src = new AirplaneSource { Proximity = prox, Thrust = thrust, Pan = 0 };
            var l = new float[512]; var r = new float[512]; double e = 0; int n = 0;
            for (int k = 0; k < 400; k++)
            {
                Array.Clear(l); Array.Clear(r);
                src.Render(l, r, 512, 1f, 0f);
                if (k >= 200) { foreach (var v in l) { e += v * v; n++; } }
            }
            return Math.Sqrt(e / n);
        }
        Assert.True(Rms(0f, 1f) < 1e-6);
        Assert.True(Rms(1f, 1f) > Rms(1f, 0.1f));
        Assert.True(Rms(1f, 0.6f) > Rms(0.4f, 0.6f));
        Assert.True(Rms(1f, 1f) is > 0.001 and < 1.5);
        // the scene lowers it a little in a storm and never mutes it by itself
        Assert.Contains(SoundEffects.Airplane, SoundScape.Targets(RegionCatalog.Default, SceneClock.Compose(null, new AppConfig { WeatherPreview = "storm" }, DateTime.Now), SeasonId.Summer).Keys);
    }
}

/// <summary>Headless renders of every phase: the vehicles really are drawn, and the icon cut-outs stay clear.</summary>
[Collection("Statics")]
public class TransitRenderTests
{
    private const int W = 1200, H = 48;
    private const string Cut = "0-90,420-760,980-1200";

    private static T Sta<T>(Func<T> f)
    {
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw new Exception("STA work failed: " + err.Message, err);
        return result;
    }

    private static byte[] Render(bool plane, bool train, double? planeP, double? trainP, string cutouts = Cut, int w = W, float hour = 12f, string weather = "sunny")
        => Sta(() =>
        {
            var cfg = new AppConfig { RegionMode = "hcmc", WeatherPreview = weather, TimeMode = "real", ShowAirplane = plane, ShowTrain = train, ShowCyclist = false, ShowAmbientDetails = false };
            cfg.Normalize();
            SceneClock.HourOverride = hour;
            DioramaRenderer.DebugPlaneCycle = planeP; DioramaRenderer.DebugTrainCycle = trainP;
            try
            {
                var regions = new TaskbarRegions { Bounds = new Rectangle(0, 0, w, H), Signature = cutouts };
                foreach (var part in cutouts.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var lr = part.Split('-');
                    regions.Cutouts.Add(Rectangle.FromLTRB(int.Parse(lr[0]), 0, int.Parse(lr[1]), H));
                }
                using var ow = new OverlayWindow(); var an = new Analyzer();
                Bitmap? last = null;
                for (int i = 0; i < 3; i++)
                {
                    last?.Dispose();
                    last = ow.RenderOffscreen(w, H, new FrameContext { Cfg = cfg, Analyzer = an, Regions = regions, Time = 30f + i / 30f, Fused = false, BehindOk = false, Region = RegionCatalog.All.First(r => r.Id == "hcmc") });
                }
                var bd = last!.LockBits(new Rectangle(0, 0, w, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var bytes = new byte[bd.Stride * H];
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length);
                last.UnlockBits(bd); last.Dispose();
                return bytes;
            }
            finally { SceneClock.HourOverride = null; DioramaRenderer.DebugPlaneCycle = null; DioramaRenderer.DebugTrainCycle = null; }
        });

    private static int Diff(byte[] a, byte[] b, int x0 = 0, int x1 = W)
    {
        int n = 0, stride = a.Length / H;
        for (int y = 0; y < H; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * stride + x * 4;
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) n++;
            }
        return n;
    }

    public static IEnumerable<object[]> PlanePhases()
    {
        foreach (var (ph, t) in new[] { (PlanePhase.Roll, 0.3), (PlanePhase.Roll, 0.9), (PlanePhase.Liftoff, 0.5),
                                        (PlanePhase.Climb, 0.6), (PlanePhase.Cruise, 0.5), (PlanePhase.Descent, 0.6), (PlanePhase.Exit, 0.12) })
            yield return new object[] { ph.ToString() + t, PlaneCycle.StartOf(ph) + PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac * t };
    }

    [Theory]
    [MemberData(nameof(PlanePhases))]
    public void Every_airplane_phase_draws_the_plane_and_keeps_icon_cutouts_clear(string name, double p)
    {
        // a wide free span with only a narrow tray cut-out on the right, so the whole flight is visible
        const string cut = "1180-1200";
        var without = Render(true, false, 0.05, null, cut);     // same take-off strip, plane away
        var with = Render(true, false, p, null, cut);
        Assert.True(Diff(without, with, 0, 1180) > 8, $"{name}: nothing drawn for the airplane");
        // icon cut-out: every pixel stays fully transparent, with the plane on or off
        int stride = with.Length / H;
        for (int y = 0; y < H; y++) for (int x = 1180; x < W; x++) Assert.Equal(0, with[y * stride + x * 4 + 3]);
    }

    [Fact]
    public void The_airplane_is_hidden_in_the_away_phase_and_when_switched_off()
    {
        var off = Render(false, false, null, null);
        var away = Render(true, false, 0.05, null);
        // away: only the (static) take-off strip differs from "off", and it is the same as the strip alone
        var strip = Render(true, false, 0.0, null);
        Assert.Equal(0, Diff(away, strip));
        var rolling = Render(true, false, PlaneCycle.StartOf(PlanePhase.Roll) + 0.05, null);
        Assert.True(Diff(away, rolling) > 20);
        Assert.True(Diff(off, away) > 20);    // the short take-off strip exists while the plane is away
    }

    public static IEnumerable<object[]> TrainPhases()
    {
        foreach (var (ph, t) in new[] { (TrainPhase.Approach, 0.35), (TrainPhase.Approach, 0.95), (TrainPhase.Alight, 0.4), (TrainPhase.Alight, 0.8),
                                        (TrainPhase.Board, 0.4), (TrainPhase.Board, 0.8), (TrainPhase.Depart, 0.2), (TrainPhase.Depart, 0.55) })
            yield return new object[] { ph.ToString() + t, TransitMath.StartOf(TrainCycle.Plan, (int)ph) + TrainCycle.Plan[(int)ph].Frac * t };
    }

    [Theory]
    [MemberData(nameof(TrainPhases))]
    public void Every_train_phase_draws_the_train_and_keeps_icon_cutouts_clear(string name, double p)
    {
        var without = Render(false, false, null, null);
        var with = Render(false, true, null, p);
        Assert.True(Diff(without, with) > 30, $"{name}: nothing drawn for the train");
        int stride = with.Length / H;
        foreach (var (a, b) in new[] { (0, 90), (420, 760), (980, 1200) })
            for (int y = 0; y < H; y++) for (int x = a; x < b; x++) Assert.Equal(0, with[y * stride + x * 4 + 3]);
    }

    [Fact]
    public void The_train_stands_at_a_platform_with_passengers_that_move()
    {
        double a0 = TransitMath.StartOf(TrainCycle.Plan, (int)TrainPhase.Alight);
        var early = Render(false, true, null, a0 + 0.10 * 0.2);
        var mid = Render(false, true, null, a0 + 0.10 * 0.5);
        var late = Render(false, true, null, a0 + 0.10 * 0.9);
        Assert.True(Diff(early, mid) > 3);       // people step out of the doors: pixels change, the train itself does not move
        Assert.True(Diff(mid, late) > 3);
        // doors open vs closed
        var closed = Render(false, true, null, TransitMath.StartOf(TrainCycle.Plan, (int)TrainPhase.Depart) + 0.01);
        Assert.True(Diff(mid, closed) > 40);
    }

    [Fact]
    public void A_narrow_bar_without_room_for_a_strip_still_shows_the_plane()
    {
        // free spans of 50 px: too narrow for a take-off strip, but wide enough to see the plane fly through
        const int w = 700;
        const string cut = "0-80,130-280,330-700";
        Assert.False(TakeoffStrip.Plan(new[] { (80f, 130f), (280f, 330f) }, w).HasStrip);
        var off = Render(false, false, null, null, cut, w);
        // pick a cycle position whose fuselage centre sits in a free gap (the plane still flies without a strip)
        var none = new TakeoffStrip(false, 0, 0);
        double pVis = -1;
        for (double p = 0; p < 1; p += 0.002)
        {
            var s = PlaneCycle.Evaluate(p, w, none);
            if (s.Visible && ((s.X > 95 && s.X < 115) || (s.X > 295 && s.X < 315))) { pVis = p; break; }
        }
        Assert.True(pVis > 0, "the flight path never crosses a free gap");
        var on = Render(true, false, pVis, null, cut, w);
        Assert.True(Diff(off, on, 0, w) > 10);
        // no strip is drawn: a plane-less cycle moment is identical to the switched-off scene
        var awayOn = Render(true, false, 0.05, null, cut, w);
        Assert.Equal(0, Diff(off, awayOn, 0, w));
    }

    [Fact]
    public void Night_scenes_light_up_the_strip_and_the_train()
    {
        var day = Render(true, true, PlaneCycle.StartOf(PlanePhase.Cruise) + 0.4, TrainCycle.StopStart + 0.02, hour: 12f);
        var night = Render(true, true, PlaneCycle.StartOf(PlanePhase.Cruise) + 0.4, TrainCycle.StopStart + 0.02, hour: 22f, weather: "night");
        Assert.True(Diff(day, night) > 2000);
    }
}

/// <summary>v5.4: the short take-off strip is at the left, and road vehicles never drive onto it.</summary>
[Collection("Statics")]
public class TakeoffStripRoadTests
{
    private const int W = 1200;

    private static T Sta<T>(Func<T> f)
    {
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw new Exception("STA work failed: " + err.Message, err);
        return result;
    }

    /// <summary>Draws many frames (about 3 minutes of scene time) and returns every road-vehicle extent seen after each frame.</summary>
    private static (List<(float L, float R, int Lane)> seen, (float L, float R)? zone, int frames) Run(string region, bool airplane, List<(float L, float R)> spans, bool traffic = true, float seconds = 180f)
        => Sta(() =>
        {
            var cfg = new AppConfig { RegionMode = region, WeatherPreview = "sunny", TimeMode = "real", ShowAirplane = airplane, ShowCyclist = traffic };
            cfg.Normalize();
            var reg = RegionCatalog.All.First(r => r.Id == region);
            var d = new DioramaRenderer();
            d.Ensure(W, 48);
            d.SetLayout(reg, spans);
            var seen = new List<(float, float, int)>();
            SceneClock.HourOverride = 12f;
            try
            {
                var mood = SceneClock.Compose(null, cfg, DateTime.Now);
                using var bmp = new Bitmap(W, 48, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(bmp);
                var ctx = new FrameContext { Cfg = cfg, Analyzer = new Analyzer(), Regions = new TaskbarRegions { Bounds = new Rectangle(0, 0, W, 48) }, Region = reg };
                int n = 0;
                for (float t = 10f; t < 10f + seconds; t += 0.25f, n++)
                {
                    d.Draw(g, ctx, mood, t);
                    seen.AddRange(d.Scenery.RoadExtents());
                }
                return (seen, d.Scenery.NoRoadZone, n);
            }
            finally { SceneClock.HourOverride = null; }
        });

    public static IEnumerable<object[]> Regions() => new[] { "hcmc", "hanoi", "newyork", "tokyo", "default" }.Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(Regions))]
    public void No_road_vehicle_is_ever_inside_the_takeoff_strip_while_it_exists(string region)
    {
        var spans = new List<(float L, float R)> { (98f, 412f), (768f, 972f) };
        var st = TakeoffStrip.Plan(spans, W);
        Assert.True(st.HasStrip);
        Assert.Equal(98f, st.L);
        Assert.Equal(98f + TakeoffStrip.DefaultLen, st.R);
        var (seen, zone, frames) = Run(region, true, spans);
        Assert.Equal((st.L, st.R), zone);
        Assert.True(frames > 600);
        Assert.NotEmpty(seen);   // there was traffic elsewhere, so the test is meaningful
        Assert.DoesNotContain(seen, e => e.R > st.L && e.L < st.R);
        // traffic still runs on the rest of the first span (past the short strip) and on the second span
        Assert.Contains(seen, e => e.R > st.R + 4f && e.L < 412f);
        Assert.Contains(seen, e => e.R > 768f && e.L < 972f);
    }

    [Fact]
    public void Traffic_that_is_already_on_the_strip_when_it_appears_is_cleared_at_once()
    {
        var spans = new List<(float L, float R)> { (98f, 412f), (768f, 972f) };
        var st = TakeoffStrip.Plan(spans, W);
        var (before, zone0, _) = Run("hcmc", false, spans, seconds: 60f);
        Assert.Null(zone0);
        Assert.Contains(before, e => e.R > st.L && e.L < st.R);
        var (after, zone1, _) = Run("hcmc", true, spans, seconds: 60f);
        Assert.Equal((st.L, st.R), zone1);
        Assert.DoesNotContain(after, e => e.R > st.L && e.L < st.R);
    }

    [Fact]
    public void No_strip_on_narrow_spans_leaves_road_traffic_unchanged()
    {
        // no span wide enough for a strip: nothing is blocked and the traffic is identical with the airplane on or off
        var narrow = new List<(float L, float R)> { (98f, 142f), (308f, 352f) };
        Assert.False(TakeoffStrip.Plan(narrow, W).HasStrip);
        var (on, zoneOn, _) = Run("hcmc", true, narrow, seconds: 60f);
        var (off, zoneOff, _) = Run("hcmc", false, narrow, seconds: 60f);
        Assert.Null(zoneOn); Assert.Null(zoneOff);
        Assert.Equal(off, on);
        Assert.Contains(on, e => e.R > 98f && e.L < 142f);
    }

    [Fact]
    public void Switching_the_airplane_off_lets_traffic_drive_through_again()
    {
        var spans = new List<(float L, float R)> { (98f, 412f) };
        var (_, zone, _) = Run("hcmc", false, spans, seconds: 5f);
        Assert.Null(zone);
    }

    [Fact]
    public void The_ground_run_stays_on_the_strip_and_the_plane_leaves_low_at_the_right()
    {
        foreach (var spans in new[] { new[] { (98f, 412f) }, new[] { (10f, 94f) }, new[] { (200f, 1000f) }, new[] { (0f, 1200f) } })
        {
            var st = TakeoffStrip.Plan(spans, W);
            Assert.True(st.HasStrip);
            Assert.True(st.R <= spans[0].Item2 && st.L >= spans[0].Item1);
            var (start, lift, climbStart, cruiseStart, descentStart, exitStart, exit) = PlaneCycle.Marks(W, st);
            Assert.InRange(start, st.L, st.R);
            Assert.InRange(lift, start, st.R + 0.01f);
            Assert.True(exit >= W);
            Assert.True(cruiseStart > climbStart && descentStart > cruiseStart && exitStart > descentStart);
            for (double p = 0; p < 1; p += 0.0005)
            {
                var s = PlaneCycle.Evaluate(p, W, st);
                if (s.Visible && s.OnGround && s.Phase == PlanePhase.Roll) Assert.InRange(s.X, st.L, st.R);
                if (s.Visible && s.OnGround) Assert.InRange(s.X, st.L, st.R + 20f);
                if (s.Phase is PlanePhase.Descent or PlanePhase.Exit)
                { Assert.False(s.OnGround); Assert.True(s.Alt > 0f); }
            }
            var last = PlaneCycle.Evaluate(PlaneCycle.StartOf(PlanePhase.Exit) + 0.08 * 0.999, W, st);
            Assert.True(last.X >= W); Assert.Equal(PlaneCycle.ExitAlt, last.Alt, 1); Assert.False(last.OnGround);
        }
    }

    [Fact]
    public void The_runway_is_drawn_only_on_the_short_left_strip()
    {
        // render with a cut-out layout: the dark strip appears under the short left take-off runway only
        var cfg = new AppConfig { RegionMode = "hcmc", WeatherPreview = "sunny", TimeMode = "real", ShowAirplane = true, ShowTrain = false, ShowCyclist = false, ShowAmbientDetails = false };
        cfg.Normalize();
        var bytes = Sta(() =>
        {
            SceneClock.HourOverride = 12f; DioramaRenderer.DebugPlaneCycle = 0.05;
            try
            {
                var regions = new TaskbarRegions { Bounds = new Rectangle(0, 0, W, 48), Signature = "420-760,980-1200" };
                regions.Cutouts.Add(Rectangle.FromLTRB(420, 0, 760, 48)); regions.Cutouts.Add(Rectangle.FromLTRB(980, 0, 1200, 48));
                using var ow = new OverlayWindow(); var an = new Analyzer();
                Bitmap? last = null;
                for (int i = 0; i < 3; i++) { last?.Dispose(); last = ow.RenderOffscreen(W, 48, new FrameContext { Cfg = cfg, Analyzer = an, Regions = regions, Time = 30f + i / 30f, Region = RegionCatalog.All.First(r => r.Id == "hcmc") }); }
                var bd = last!.LockBits(new Rectangle(0, 0, W, 48), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var b = new byte[bd.Stride * 48]; System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, b, 0, b.Length); last.UnlockBits(bd); last.Dispose();
                return b;
            }
            finally { SceneClock.HourOverride = null; DioramaRenderer.DebugPlaneCycle = null; }
        });
        int stride = bytes.Length / 48;
        // runway asphalt is the dark grey strip at y = 46 (below the centre-line dashes)
        bool Asphalt(int x) { int i = 46 * stride + x * 4; return bytes[i + 3] > 200 && Math.Abs(bytes[i] - bytes[i + 1]) < 14 && Math.Abs(bytes[i + 1] - bytes[i + 2]) < 18 && bytes[i] is > 50 and < 110; }
        int onStrip = Enumerable.Range(20, 70).Count(Asphalt);        // the short strip sits at the left of the first free span
        Assert.True(onStrip > 30, "take-off strip missing at the left");
        int pastStrip = Enumerable.Range(200, 180).Count(Asphalt);    // the rest of the first span is ordinary road, not runway
        Assert.True(pastStrip < 30, $"runway leaks past the short strip ({pastStrip} px)");
        int otherSpan = Enumerable.Range(790, 160).Count(Asphalt);
        Assert.True(otherSpan < 30, $"runway leaks into the second span ({otherSpan} px)");
    }
}
