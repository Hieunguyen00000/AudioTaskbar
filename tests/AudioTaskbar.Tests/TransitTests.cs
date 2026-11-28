using System.Drawing.Imaging;
using System.Text.Json;

namespace AudioTaskbar.Tests;

/// <summary>Lifecycle state machines of the train (stop at a platform) and the airplane (v5.4.2: climb in, cruise, descend out — airborne only), pure functions.</summary>
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
        // v5.4.2 timing in seconds: stopped (alight + board) takes 12 % of the period (was 20 %), approach 42 % (was 36 %), depart 42 % (was 40 %)
        double Seconds(TrainPhase ph) => TrainCycle.Plan.First(r => r.Id == (int)ph).Frac * periodSeconds;
        Assert.Equal(0.42 * periodSeconds, Seconds(TrainPhase.Approach), 6);
        Assert.Equal(0.12 * periodSeconds, Seconds(TrainPhase.Alight) + Seconds(TrainPhase.Board), 6);
        Assert.Equal(0.42 * periodSeconds, Seconds(TrainPhase.Depart), 6);
        // a clock running at that period lands in the right phase at the right second
        double p = TransitMath.Cycle(0, 0.04 * periodSeconds + 0.5, periodSeconds);
        Assert.Equal(TrainPhase.Approach, TrainCycle.PhaseAt(p).Phase);
        p = TransitMath.Cycle(0, (0.46 + 0.01) * periodSeconds, periodSeconds);
        Assert.Equal(TrainPhase.Alight, TrainCycle.PhaseAt(p).Phase);
        p = TransitMath.Cycle(0, (0.58 + 0.01) * periodSeconds, periodSeconds);
        Assert.Equal(TrainPhase.Depart, TrainCycle.PhaseAt(p).Phase);
    }

    [Fact]
    public void Train_runs_longer_and_stops_shorter_than_in_v541()
    {
        // default 2-minute cycle: running (approach + depart) 100.8 s (was 91.2 s), standing at the platform 14.4 s (was 24 s)
        const double period = 120;
        double Frac(TrainPhase ph) => TrainCycle.Plan.First(r => r.Id == (int)ph).Frac;
        Assert.Equal(100.8, (Frac(TrainPhase.Approach) + Frac(TrainPhase.Depart)) * period, 6);
        Assert.Equal(14.4, (Frac(TrainPhase.Alight) + Frac(TrainPhase.Board)) * period, 6);
        Assert.True((Frac(TrainPhase.Approach) + Frac(TrainPhase.Depart)) * period > 91.2);
        Assert.True((Frac(TrainPhase.Alight) + Frac(TrainPhase.Board)) * period < 24);
    }

    [Fact]
    public void Train_slows_down_stops_at_the_platform_and_then_speeds_up()
    {
        double a0 = TransitMath.StartOf(TrainCycle.Plan, 1), a1 = a0 + TrainCycle.Plan[1].Frac;
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
        Assert.True(TrainCycle.Evaluate(b0 + TrainCycle.Plan[(int)TrainPhase.Board].Frac * 0.99, BarW, TrainW, StopLeft).Doors < 0.2f);
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

    private static readonly PlanePhase[] Order = { PlanePhase.Away, PlanePhase.Climb, PlanePhase.Cruise, PlanePhase.Descent };

    [Fact]
    public void Airplane_plan_has_only_three_airborne_phases_after_the_gap()
    {
        Assert.Equal(1.0, PlaneCycle.Plan.Sum(r => r.Frac), 9);
        Assert.Equal(Order, PlaneCycle.Plan.Select(r => (PlanePhase)r.Id));
        Assert.Equal(Order, Enum.GetValues<PlanePhase>());   // no roll, lift-off or exit phase exists any more
        var seen = new List<PlanePhase>();
        for (double p = 0; p < 1; p += 0.0005) { var ph = PlaneCycle.PhaseAt(p).Phase; if (seen.Count == 0 || seen[^1] != ph) seen.Add(ph); }
        Assert.Equal(Order, seen);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(10.0)]
    public void Airplane_phase_timing_follows_the_configured_period(double minutes)
    {
        double period = minutes * 60;
        double Seconds(PlanePhase ph) => PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac * period;
        Assert.Equal(0.28 * period, Seconds(PlanePhase.Climb), 6);
        Assert.Equal(0.24 * period, Seconds(PlanePhase.Cruise), 6);
        Assert.Equal(0.28 * period, Seconds(PlanePhase.Descent), 6);
        Assert.Equal(0.80 * period, period - Seconds(PlanePhase.Away), 6);
        Assert.Equal(0.5, TransitMath.Cycle(0.25, 0.25 * period, period), 9);
    }

    [Theory]
    [InlineData(1200f)]
    [InlineData(700f)]
    [InlineData(3840f)]
    public void The_plane_appears_low_at_the_left_climbs_holds_and_descends_out_at_the_right(float barW)
    {
        var seen = new List<PlanePhase>();
        float prevX = float.MinValue, maxCruise = 0f, minCruise = float.MaxValue;
        PlaneState first = default, last = default; bool any = false;
        for (double p = 0; p < 1; p += 0.0005)
        {
            var s = PlaneCycle.Evaluate(p, barW);
            if (seen.Count == 0 || seen[^1] != s.Phase) seen.Add(s.Phase);
            if (!s.Visible) { Assert.Equal(PlanePhase.Away, s.Phase); continue; }
            if (!any) { first = s; any = true; }
            last = s;
            Assert.True(s.Alt > 0f, "the plane never touches the ground");
            Assert.True(s.X >= prevX - 0.001f, "it never moves backwards");
            if (prevX > float.MinValue) Assert.True(s.X - prevX < barW * 0.01f, "no jumps");
            prevX = s.X;
            if (s.Phase == PlanePhase.Cruise) { maxCruise = Math.Max(maxCruise, s.Alt); minCruise = Math.Min(minCruise, s.Alt); }
        }
        Assert.Equal(Order, seen);
        // appears in the air, low, right at the left edge …
        Assert.Equal(PlanePhase.Climb, first.Phase);
        Assert.True(first.X <= 0f && first.X >= -PlaneCycle.PlaneLen);
        Assert.InRange(first.Alt, 3f, 7f);
        // … holds its height across the middle …
        Assert.True(minCruise > PlaneCycle.CruiseAlt - 1f && maxCruise < PlaneCycle.CruiseAlt + 1f);
        // … and leaves low, still flying, past the right edge
        Assert.Equal(PlanePhase.Descent, last.Phase);
        Assert.True(last.X >= barW);
        Assert.InRange(last.Alt, 3f, 7f);
    }

    [Fact]
    public void Climb_rises_and_descent_sinks_slowly_and_monotonically()
    {
        float prev = 0f;
        for (float t = 0.01f; t < 1f; t += 0.01f)
        {
            var s = PlaneCycle.Evaluate(PlaneCycle.StartOf(PlanePhase.Climb) + 0.28 * t, 1200f);
            Assert.True(s.Alt >= prev - 1e-4f); prev = s.Alt;
            Assert.InRange(s.PitchDeg, 0f, 8f);   // gentle nose-up, never a steep take-off
        }
        prev = float.MaxValue;
        for (float t = 0.01f; t < 1f; t += 0.01f)
        {
            var s = PlaneCycle.Evaluate(PlaneCycle.StartOf(PlanePhase.Descent) + 0.28 * t, 1200f);
            Assert.True(s.Alt <= prev + 1e-4f); prev = s.Alt;
            Assert.InRange(s.PitchDeg, -6f, 0.01f);
        }
        // the middle third of the bar is cruise
        var (enter, cruiseStart, descentStart, exit) = PlaneCycle.Marks(1200f);
        Assert.True(cruiseStart > 1200f * 0.25f && descentStart < 1200f * 0.75f && descentStart > cruiseStart);
        Assert.True(enter < 0f && exit > 1200f);
    }

    [Fact]
    public void Evaluate_survives_odd_input()
    {
        foreach (var p in new[] { double.NaN, double.PositiveInfinity, -3.7, 5.25 })
        {
            var s = PlaneCycle.Evaluate(p, BarW);
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
        Assert.Equal(56, AppConfig.CurrentConfigVersion);
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
        foreach (var (ph, t) in new[] { (PlanePhase.Climb, 0.08), (PlanePhase.Climb, 0.6), (PlanePhase.Cruise, 0.5),
                                        (PlanePhase.Descent, 0.4), (PlanePhase.Descent, 0.85) })
            yield return new object[] { ph.ToString() + t, PlaneCycle.StartOf(ph) + PlaneCycle.Plan.First(r => r.Id == (int)ph).Frac * t };
    }

    [Theory]
    [MemberData(nameof(PlanePhases))]
    public void Every_airplane_phase_draws_the_plane_and_keeps_icon_cutouts_clear(string name, double p)
    {
        // a wide free span with only a narrow tray cut-out on the right, so the whole flight is visible
        const string cut = "1180-1200";
        var without = Render(true, false, 0.05, null, cut);     // plane away
        var with = Render(true, false, p, null, cut);
        Assert.True(Diff(without, with, 0, 1180) > 8, $"{name}: nothing drawn for the airplane");
        // icon cut-out: every pixel stays fully transparent, with the plane on or off
        int stride = with.Length / H;
        for (int y = 0; y < H; y++) for (int x = 1180; x < W; x++) Assert.Equal(0, with[y * stride + x * 4 + 3]);
    }

    [Fact]
    public void The_airplane_is_hidden_in_the_away_phase_and_when_switched_off()
    {
        var off = Render(false, false, null, null, "1180-1200");
        var away = Render(true, false, 0.05, null, "1180-1200");
        // v5.4.2: no runway — with the plane away the scene is pixel-identical to the airplane switched off
        Assert.Equal(0, Diff(off, away));
        var climbing = Render(true, false, PlaneCycle.StartOf(PlanePhase.Climb) + 0.1, null, "1180-1200");
        Assert.True(Diff(away, climbing) > 20);
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
        double fr = TrainCycle.Plan[(int)TrainPhase.Alight].Frac;
        var early = Render(false, true, null, a0 + fr * 0.2);
        var mid = Render(false, true, null, a0 + fr * 0.5);
        var late = Render(false, true, null, a0 + fr * 0.9);
        Assert.True(Diff(early, mid) > 3);       // people step out of the doors: pixels change, the train itself does not move
        Assert.True(Diff(mid, late) > 3);
        // doors open vs closed
        var closed = Render(false, true, null, TransitMath.StartOf(TrainCycle.Plan, (int)TrainPhase.Depart) + 0.01);
        Assert.True(Diff(mid, closed) > 40);
    }

    [Fact]
    public void A_narrow_bar_with_small_gaps_still_shows_the_plane()
    {
        const int w = 700;
        const string cut = "0-80,130-280,330-700";
        var off = Render(false, false, null, null, cut, w);
        double pVis = -1;
        for (double p = 0; p < 1; p += 0.002)
        {
            var s = PlaneCycle.Evaluate(p, w);
            if (s.Visible && ((s.X > 95 && s.X < 115) || (s.X > 295 && s.X < 315))) { pVis = p; break; }
        }
        Assert.True(pVis > 0, "the flight path never crosses a free gap");
        var on = Render(true, false, pVis, null, cut, w);
        Assert.True(Diff(off, on, 0, w) > 10);
        var awayOn = Render(true, false, 0.05, null, cut, w);
        Assert.Equal(0, Diff(off, awayOn, 0, w));
    }

    [Fact]
    public void Night_scenes_light_up_the_plane_and_the_train()
    {
        var day = Render(true, true, PlaneCycle.StartOf(PlanePhase.Cruise) + 0.4, TrainCycle.StopStart + 0.02, hour: 12f);
        var night = Render(true, true, PlaneCycle.StartOf(PlanePhase.Cruise) + 0.4, TrainCycle.StopStart + 0.02, hour: 22f, weather: "night");
        Assert.True(Diff(day, night) > 2000);
    }
}

/// <summary>v5.4.2: no take-off strip — road vehicles and the train ignore the airplane completely.</summary>
[Collection("Statics")]
public class NoRunwayRoadTests
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

    /// <summary>Draws many frames and returns every road-vehicle extent seen after each frame, plus the train's platform span.</summary>
    private static (List<(float L, float R, int Lane)> seen, int frames) Run(string region, bool airplane, List<(float L, float R)> spans, float seconds = 120f)
        => Sta(() =>
        {
            var cfg = new AppConfig { RegionMode = region, WeatherPreview = "sunny", TimeMode = "real", ShowAirplane = airplane, ShowCyclist = true };
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
                return (seen, n);
            }
            finally { SceneClock.HourOverride = null; }
        });

    [Theory]
    [InlineData("hcmc")]
    [InlineData("newyork")]
    [InlineData("default")]
    public void Road_traffic_is_identical_with_the_airplane_on_or_off(string region)
    {
        var spans = new List<(float L, float R)> { (98f, 412f), (768f, 972f) };
        var (on, frames) = Run(region, true, spans);
        var (off, _) = Run(region, false, spans);
        Assert.True(frames > 400);
        Assert.NotEmpty(on);
        Assert.Equal(off, on);
        // vehicles use the far left of the first span too (where the old take-off strip used to be)
        Assert.Contains(on, e => e.R > 98f && e.L < 98f + 96f);
    }

    [Fact]
    public void The_train_platform_uses_the_widest_span_without_a_runway_gap()
    {
        var spans = new List<(float L, float R)> { (98f, 412f), (768f, 972f) };
        Assert.Equal((98f, 412f), TrainCycle.StationSpan(spans, W));
        Assert.Equal((0f, (float)W), TrainCycle.StationSpan(new List<(float L, float R)>(), W));
    }
}
