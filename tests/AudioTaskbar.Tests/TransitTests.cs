using System.Drawing.Imaging;
using System.Text.Json;

namespace AudioTaskbar.Tests;

/// <summary>v5.4: lifecycle state machines of the train (stop at a platform) and the airplane (land, gate, take off), pure functions.</summary>
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

    private static AirportPlan Airport() => AirportPlan.Plan(new[] { (100f, 640f) }, BarW);

    [Fact]
    public void Airplane_plan_covers_one_period_in_lifecycle_order()
    {
        Assert.Equal(1.0, PlaneCycle.Plan.Sum(r => r.Frac), 9);
        Assert.Equal(1.0, PlaneCycle.FlyoverPlan.Sum(r => r.Frac), 9);
        var seen = new List<PlanePhase>();
        for (double p = 0; p < 1; p += 0.0005) { var ph = PlaneCycle.PhaseAt(p, true).Phase; if (seen.Count == 0 || seen[^1] != ph) seen.Add(ph); }
        Assert.Equal(new[] { PlanePhase.Away, PlanePhase.Approach, PlanePhase.Landing, PlanePhase.TaxiIn, PlanePhase.Deplane,
                             PlanePhase.Board, PlanePhase.TaxiOut, PlanePhase.Takeoff, PlanePhase.ClimbOut }, seen);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(10.0)]
    public void Airplane_phase_timing_follows_the_configured_period(double minutes)
    {
        double period = minutes * 60;
        double Seconds(PlanePhase ph) => PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac * period;
        Assert.Equal(0.13 * period, Seconds(PlanePhase.Deplane), 6);
        Assert.Equal(0.13 * period, Seconds(PlanePhase.Board), 6);
        Assert.Equal(period, PlaneCycle.Plan.Sum(r => r.Frac * period), 6);
        // stand time at the gate (deplane + board) is a quarter of the cycle
        Assert.Equal(0.26 * period, Seconds(PlanePhase.Deplane) + Seconds(PlanePhase.Board), 6);
        double p = TransitMath.Cycle(0.25, 0, period);
        Assert.Equal(0.25, p, 9);
        // time moves the cycle forward and wraps
        Assert.Equal(0.5, TransitMath.Cycle(0.25, 0.25 * period, period), 9);
        Assert.Equal(0.25, TransitMath.Cycle(0.25, period, period), 6);
    }

    [Fact]
    public void Airport_fits_in_a_wide_free_span_and_not_in_a_narrow_one()
    {
        var ap = Airport();
        Assert.True(ap.HasAirport);
        Assert.InRange(ap.TermL, 100f, 640f); Assert.InRange(ap.TermR, 100f, 640f);
        Assert.True(ap.StandX - AirportPlan.PlaneLen / 2 >= 100f);                       // the parked plane is inside the span
        Assert.True(ap.StandX + AirportPlan.PlaneLen / 2 < ap.TermL);                    // and stands beside the terminal, not in it
        Assert.False(AirportPlan.Plan(new[] { (0f, 60f), (200f, 270f) }, BarW).HasAirport);   // nothing wide enough → the plane just crosses the sky
        Assert.True(AirportPlan.Plan(new[] { (10f, 10f + AirportPlan.MinSpan) }, BarW).HasAirport);
        Assert.True(AirportPlan.Plan(Array.Empty<(float, float)>(), BarW).HasAirport);        // no measured spans = the whole bar
        // the train station goes into a different span than the airport when there is one
        var spans = new[] { (100f, 640f), (760f, 960f) };
        int ai = AirportPlan.FindSpan(spans, BarW);
        Assert.Equal(0, ai);
        Assert.Equal((760f, 960f), AirportPlan.StationSpan(spans, BarW, ai));
        Assert.Equal((100f, 640f), AirportPlan.StationSpan(new[] { (100f, 640f) }, BarW, 0));
    }

    [Fact]
    public void Airplane_runs_through_the_whole_lifecycle_in_order()
    {
        var ap = Airport();
        PlaneState S(double p) => PlaneCycle.Evaluate(p, BarW, ap);
        double Start(PlanePhase ph) => PlaneCycle.StartOf(ph);
        double Dur(PlanePhase ph) => PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac;
        PlaneState At(PlanePhase ph, float t) => S(Start(ph) + Dur(ph) * t);

        Assert.False(At(PlanePhase.Away, 0.5f).Visible);

        // fly across at cruise height, then descend to the runway
        var a0 = At(PlanePhase.Approach, 0.05f); var a1 = At(PlanePhase.Approach, 0.5f); var a2 = At(PlanePhase.Approach, 0.99f);
        Assert.True(a0.Visible); Assert.Equal(PlaneCycle.CruiseAlt, a0.Alt, 1);
        Assert.True(a1.Alt < a0.Alt); Assert.True(a2.Alt < 0.5f); Assert.True(a2.X > a1.X && a1.X > a0.X);

        // touch down and roll out, slowing down
        var l0 = At(PlanePhase.Landing, 0.02f); var l1 = At(PlanePhase.Landing, 0.98f);
        Assert.True(l0.OnGround && l1.OnGround); Assert.Equal(0f, l0.Alt);
        Assert.True(l1.X > l0.X); Assert.True(l1.Speed < l0.Speed);

        // taxi to the stand, then stop there
        var t1 = At(PlanePhase.TaxiIn, 0.999f);
        Assert.Equal(ap.StandX, t1.X, 0); Assert.True(t1.Speed < 0.05f);
        var d = At(PlanePhase.Deplane, 0.5f);
        Assert.Equal(ap.StandX, d.X); Assert.Equal(0f, d.Speed); Assert.Equal(1f, d.Stairs); Assert.InRange(d.Deplaning, 0.45f, 0.55f); Assert.Equal(0f, d.Boarding);
        var b = At(PlanePhase.Board, 0.5f);
        Assert.Equal(ap.StandX, b.X); Assert.Equal(0f, b.Speed); Assert.Equal(1f, b.Stairs); Assert.Equal(1f, b.Deplaning); Assert.InRange(b.Boarding, 0.45f, 0.55f);
        Assert.True(At(PlanePhase.Board, 0.995f).Stairs < 0.2f);       // stairs are taken away before pushing out
        Assert.True(At(PlanePhase.Deplane, 0.005f).Stairs < 0.2f);     // and brought up when it stops

        // taxi out, take-off roll (nose comes up at the end), climb out and leave
        var o0 = At(PlanePhase.TaxiOut, 0.01f); var o1 = At(PlanePhase.TaxiOut, 0.99f);
        Assert.True(o1.X > o0.X && o0.X >= ap.StandX);
        var k0 = At(PlanePhase.Takeoff, 0.1f); var k1 = At(PlanePhase.Takeoff, 0.99f);
        Assert.True(k0.OnGround && k1.OnGround); Assert.True(k1.X > k0.X); Assert.True(k1.Speed > k0.Speed);
        Assert.True(k1.PitchDeg > 5f); Assert.True(k1.Thrust > 0.8f);
        var c0 = At(PlanePhase.ClimbOut, 0.05f); var c1 = At(PlanePhase.ClimbOut, 0.5f); var c2 = At(PlanePhase.ClimbOut, 0.999f);
        Assert.False(c0.OnGround); Assert.True(c0.Alt < c1.Alt && c1.Alt < c2.Alt); Assert.Equal(PlaneCycle.CruiseAlt, c2.Alt, 0);
        Assert.True(c2.X >= BarW);                                     // gone off the right edge
    }

    [Fact]
    public void Airplane_position_has_no_jumps_between_phases_and_never_moves_backwards()
    {
        var ap = Airport();
        float prevX = float.MinValue; double prevP = 0;
        PlaneState? prev = null;
        for (double p = PlaneCycle.StartOf(PlanePhase.Approach); p < 1; p += 0.0004)
        {
            var s = PlaneCycle.Evaluate(p, BarW, ap);
            Assert.True(s.X >= prevX - 0.001f, $"moves backwards at p={p:F4}");
            if (prev is { } q) Assert.True(MathF.Abs(s.X - q.X) < 14f && MathF.Abs(s.Alt - q.Alt) < 3f, $"jump at p={p:F4} (prev {prevP:F4}): dx={s.X - q.X}, dalt={s.Alt - q.Alt}");
            prevX = s.X; prevP = p; prev = s;
        }
        // all numbers are finite and on the ground phases the plane is on the ground
        for (double p = 0; p < 1; p += 0.001)
        {
            var s = PlaneCycle.Evaluate(p, BarW, ap);
            Assert.True(float.IsFinite(s.X) && float.IsFinite(s.Alt) && float.IsFinite(s.PitchDeg));
            if (s.Phase is PlanePhase.Landing or PlanePhase.TaxiIn or PlanePhase.Deplane or PlanePhase.Board or PlanePhase.TaxiOut or PlanePhase.Takeoff)
            { Assert.Equal(0f, s.Alt); Assert.True(s.OnGround); }
        }
    }

    [Fact]
    public void Without_an_airport_the_plane_only_crosses_the_sky()
    {
        var none = new AirportPlan(false, 0, 0, 0);
        var seen = new HashSet<PlanePhase>();
        float prevX = float.MinValue;
        for (double p = 0; p < 1; p += 0.002)
        {
            var s = PlaneCycle.Evaluate(p, BarW, none);
            seen.Add(s.Phase);
            if (!s.Visible) continue;
            Assert.False(s.OnGround); Assert.True(s.Alt > 10f);
            Assert.True(s.X >= prevX); prevX = s.X;
        }
        Assert.Equal(new[] { PlanePhase.Away, PlanePhase.Cruise }.OrderBy(x => x), seen.OrderBy(x => x));
        Assert.True(prevX >= BarW);   // it crosses the whole bar
    }

    [Fact]
    public void Evaluate_survives_odd_input()
    {
        foreach (var p in new[] { double.NaN, double.PositiveInfinity, -3.7, 5.25 })
        {
            var s = PlaneCycle.Evaluate(p, BarW, Airport());
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
        foreach (var (ph, t) in new[] { (PlanePhase.Approach, 0.12), (PlanePhase.Approach, 0.9), (PlanePhase.Landing, 0.5), (PlanePhase.TaxiIn, 0.6),
                                        (PlanePhase.Deplane, 0.5), (PlanePhase.Board, 0.5), (PlanePhase.TaxiOut, 0.8), (PlanePhase.Takeoff, 0.9), (PlanePhase.ClimbOut, 0.3) })
            yield return new object[] { ph.ToString() + t, PlaneCycle.StartOf(ph) + PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac * t };
    }

    [Theory]
    [MemberData(nameof(PlanePhases))]
    public void Every_airplane_phase_draws_the_plane_and_keeps_icon_cutouts_clear(string name, double p)
    {
        // a single wide free span so the whole run is visible
        const string cut = "800-1200";
        var without = Render(true, false, 0.05, null, cut);     // same airport strip, plane away
        var with = Render(true, false, p, null, cut);
        Assert.True(Diff(without, with, 0, 800) > 8, $"{name}: nothing drawn for the airplane");
        // icon cut-out: every pixel stays fully transparent, with the plane on or off
        int stride = with.Length / H;
        for (int y = 0; y < H; y++) for (int x = 800; x < W; x++) Assert.Equal(0, with[y * stride + x * 4 + 3]);
    }

    [Fact]
    public void The_airplane_is_hidden_in_the_away_phase_and_when_switched_off()
    {
        var off = Render(false, false, null, null);
        var away = Render(true, false, 0.05, null);
        // away: only the (static) airport strip differs from "off", and it is the same as the strip alone
        var strip = Render(true, false, 0.0, null);
        Assert.Equal(0, Diff(away, strip));
        var gate = Render(true, false, PlaneCycle.StartOf(PlanePhase.Deplane) + 0.05, null);
        Assert.True(Diff(away, gate) > 20);
        Assert.True(Diff(off, away) > 50);    // the runway and terminal exist while the plane is away
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
    public void A_narrow_bar_without_room_for_an_airport_still_shows_a_plane_crossing_the_sky()
    {
        const string cut = "0-90,150-300,360-700";   // free spans 44 px wide: the airport cannot fit
        var ap = AirportPlan.Plan(new[] { (98f, 142f), (308f, 352f) }, 700);
        Assert.False(ap.HasAirport);
        var off = Render(false, false, null, null, cut, 700);
        var on = Render(true, false, 0.275, null, cut, 700);   // mid-cruise
        Assert.True(Diff(off, on, 0, 700) > 10);
        // no runway when there is no airport: a plane-less cycle moment is identical to the switched-off scene
        var awayOn = Render(true, false, 0.9, null, cut, 700);
        Assert.Equal(0, Diff(off, awayOn, 0, 700));
    }

    [Fact]
    public void Night_scenes_light_up_the_terminal_and_the_train()
    {
        var day = Render(true, true, PlaneCycle.StartOf(PlanePhase.Deplane) + 0.05, TrainCycle.StopStart + 0.02, hour: 12f);
        var night = Render(true, true, PlaneCycle.StartOf(PlanePhase.Deplane) + 0.05, TrainCycle.StopStart + 0.02, hour: 22f, weather: "night");
        Assert.True(Diff(day, night) > 2000);
    }
}
