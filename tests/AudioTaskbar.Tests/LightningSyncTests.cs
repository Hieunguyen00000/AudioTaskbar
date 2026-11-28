namespace AudioTaskbar.Tests;

/// <summary>v5.4.2: the flash drawn on the diorama and the thunder heard are one event (LightningStrike): flash first, crack after a distance delay.</summary>
[Collection("Statics")]
public class LightningSyncTests
{
    private const int Rate = 44100;

    private sealed class FakeClock { public double T = 100; public double Now() => T; }

    private static float[] Render(AmbientMixer m, double seconds)
    {
        int frames = (int)(seconds * Rate);
        var buf = new float[frames * 2];
        m.Read(buf, 0, buf.Length);
        return buf;
    }

    private static double Rms(float[] x, double fromS, double toS)
    {
        int a = (int)(fromS * Rate) * 2, b = Math.Min(x.Length, (int)(toS * Rate) * 2);
        double s = 0;
        for (int i = a; i < b; i++) s += x[i] * x[i];
        return Math.Sqrt(s / Math.Max(1, b - a));
    }

    private static double Rough(float[] x, double fromS, double toS)   // mean |first difference|: a treble measure
    {
        int a = (int)(fromS * Rate) * 2, b = Math.Min(x.Length, (int)(toS * Rate) * 2);
        double s = 0;
        for (int i = a + 2; i < b; i += 2) s += Math.Abs(x[i] - x[i - 2]);
        return s / Math.Max(1, (b - a) / 2);
    }

    private static AmbientMixer ThunderMixer()
    {
        var m = new AmbientMixer();
        var v = SoundEffects.Ids.ToDictionary(i => i, i => i == "thunder" ? 100 : 0);
        m.SetVolumes(100, v);
        m.SetScene(SoundEffects.Ids.ToDictionary(i => i, _ => new SoundTarget(1f, 0.5f)));
        return m;
    }

    private static T Sta<T>(Func<T> f)
    {
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw new Exception("STA work failed: " + err.Message, err);
        return result;
    }

    // ---- the strike model ----

    [Fact]
    public void Thunder_delay_grows_and_loudness_and_treble_fall_with_distance()
    {
        Assert.InRange(LightningStrike.ThunderDelayFor(0f), 0.0f, 0.1f);
        Assert.InRange(LightningStrike.ThunderDelayFor(0.1f), 0.0f, 0.3f);        // close strike: 0..0.3 s
        Assert.InRange(LightningStrike.ThunderDelayFor(1f), 2.5f, 3.0f);          // far: 2.5..3 s
        float pd = -1, pg = 2, pc = 1e9f, pf = 2;
        for (float d = 0; d <= 1.0001f; d += 0.05f)
        {
            Assert.True(LightningStrike.ThunderDelayFor(d) > pd); pd = LightningStrike.ThunderDelayFor(d);
            Assert.True(LightningStrike.ThunderGainFor(d) < pg); pg = LightningStrike.ThunderGainFor(d);
            Assert.True(LightningStrike.ThunderCutoffFor(d) < pc); pc = LightningStrike.ThunderCutoffFor(d);
            Assert.True(LightningStrike.FlashPowerFor(d) < pf); pf = LightningStrike.FlashPowerFor(d);
        }
        Assert.True(LightningStrike.ThunderGainFor(1f) > 0.2f);
        Assert.True(LightningStrike.ThunderCutoffFor(1f) >= 1000f);
    }

    [Fact]
    public void Schedule_plans_strikes_only_in_a_storm_with_sensible_gaps_and_distances()
    {
        var clock = new FakeClock();
        var s = new LightningSchedule(7, clock.Now);
        for (int i = 0; i < 600; i++) { clock.T += 0.1; s.Advance(clock.T, storm: false); }
        Assert.Equal(0, s.LastId);
        var seen = new List<LightningStrike>();
        long cursor = 0;
        for (int i = 0; i < 6000; i++)   // 10 minutes of storm
        {
            clock.T += 0.1; s.Advance(clock.T, storm: true);
            foreach (var k in s.StrikesSince(cursor)) { cursor = k.Id; seen.Add(k); }
        }
        Assert.InRange(seen.Count, 60, 160);
        for (int i = 0; i < seen.Count; i++)
        {
            Assert.InRange(seen[i].Distance, 0f, 1f);
            Assert.InRange(seen[i].X01, 0f, 1f);
            if (i > 0)
            {
                Assert.True(seen[i].Id == seen[i - 1].Id + 1);
                Assert.InRange(seen[i].Time - seen[i - 1].Time, LightningSchedule.MinGap - 1e-6, LightningSchedule.MaxGap + 1e-6);
            }
        }
        Assert.Contains(seen, k => k.Distance < 0.25f);
        Assert.Contains(seen, k => k.Distance > 0.75f);
        // the storm ends: nothing more is planned
        long last = s.LastId;
        for (int i = 0; i < 600; i++) { clock.T += 0.1; s.Advance(clock.T, storm: false); }
        Assert.Equal(last, s.LastId);
    }

    [Fact]
    public void Schedule_is_deterministic_for_a_seed()
    {
        List<(double, float, float)> Run()
        {
            var c = new FakeClock(); var s = new LightningSchedule(3, c.Now); var o = new List<(double, float, float)>();
            for (int i = 0; i < 2000; i++) { c.T += 0.1; s.Advance(c.T, true); }
            foreach (var k in s.StrikesSince(0)) o.Add((k.Time, k.X01, k.Distance));
            return o;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_strike_is_shown_once_even_with_several_windows()
    {
        var s = new LightningSchedule(1, new FakeClock().Now);
        int raised = 0;
        s.Shown += _ => raised++;
        var k = new LightningStrike(1, 0, 0.5f, 0.5f);
        Assert.True(s.MarkShown(k));
        Assert.False(s.MarkShown(k));
        Assert.Equal(1, raised);
        Assert.Equal(1, s.LastShownId);
    }

    // ---- the mixer: thunder follows the strike ----

    [Fact]
    public void No_strike_no_thunder()
    {
        var m = ThunderMixer();
        var x = Render(m, 30);
        Assert.All(x, v => Assert.Equal(0f, v));
        Assert.Equal(0, m.Thunder.Heard);
    }

    [Fact]
    public void A_close_strike_cracks_at_once()
    {
        var m = ThunderMixer();
        m.Strike(new LightningStrike(1, 0, 0.5f, 0f));
        var x = Render(m, 6);
        Assert.Equal(1, m.Thunder.Heard);
        Assert.True(Rms(x, 0.0, 0.5) > 0.02, "close thunder starts within half a second");
    }

    [Fact]
    public void A_far_strike_is_late_quiet_and_dull()
    {
        var close = Render(Primed(0f), 9);
        var far = Render(Primed(1f), 9);
        double delay = LightningStrike.ThunderDelayFor(1f);
        double lead = SampleBank.ThunderClips.Max(c => c.Onset) / LightningStrike.ThunderRateFor(1f) * 1.04;   // whatever the clip plays before its crack
        Assert.True(Rms(far, 0, delay - lead - 0.1) < 1e-4, "silence until the thunder (its lead-in) starts");
        Assert.True(Rms(far, delay, delay + 1.5) > 0.002, "then it does arrive");
        Assert.True(Rms(far, delay, delay + 3) < Rms(close, 0, 3) * 0.75, "quieter when far");
        Assert.True(Rough(far, delay, delay + 3) < Rough(close, 0, 3) * 0.8, "duller (low-passed) when far");

        static AmbientMixer Primed(float d) { var m = ThunderMixer(); m.Strike(new LightningStrike(1, 0, 0.5f, d)); return m; }
    }

    [Fact]
    public void The_bolt_position_pans_the_thunder()
    {
        double Side(float x01)
        {
            var m = ThunderMixer(); m.Strike(new LightningStrike(1, 0, x01, 0.05f));
            var x = Render(m, 5); double l = 0, r = 0;
            for (int i = 0; i + 1 < x.Length; i += 2) { l += x[i] * x[i]; r += x[i + 1] * x[i + 1]; }
            return l / Math.Max(1e-9, r);
        }
        Assert.True(Side(0f) > 1.3);
        Assert.True(Side(1f) < 0.77);
    }

    [Fact]
    public void A_strike_is_ignored_when_thunder_is_muted_paused_or_there_is_no_storm()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, SoundEffects.Ids.ToDictionary(i => i, _ => 0));
        m.SetScene(SoundEffects.Ids.ToDictionary(i => i, _ => new SoundTarget(1f, 0.5f)));
        m.Strike(new LightningStrike(1, 0, 0.5f, 0f));
        Assert.All(Render(m, 2), v => Assert.Equal(0f, v));
        Assert.Equal(0, m.RenderCallsFor("thunder"));
        Assert.Equal(0, m.Thunder.Heard);

        var p = ThunderMixer(); p.Paused = true;
        p.Strike(new LightningStrike(1, 0, 0.5f, 0f));
        Assert.Equal(0, p.Thunder.Heard);

        var n = ThunderMixer();
        n.SetScene(SoundEffects.Ids.ToDictionary(i => i, _ => new SoundTarget(0f, 0f)));      // fair weather
        n.Strike(new LightningStrike(1, 0, 0.5f, 0f));
        Assert.All(Render(n, 4), v => Assert.Equal(0f, v));
        Assert.Equal(0, n.Thunder.Heard);
    }

    [Fact]
    public void Thunder_output_does_not_depend_on_the_read_chunk()
    {
        float[] Run(int chunk)
        {
            var m = ThunderMixer();
            m.Strike(new LightningStrike(1, 0, 0.3f, 0.5f));
            var all = new float[Rate * 2 * 8]; var buf = new float[chunk * 2]; int done = 0;
            while (done < Rate * 8) { int n = Math.Min(chunk, Rate * 8 - done); m.Read(buf, 0, n * 2); Array.Copy(buf, 0, all, done * 2, n * 2); done += n; }
            return all;
        }
        Assert.Equal(Run(4096), Run(777));
    }

    // ---- the engine: a flashed strike reaches the mixer, and only a flashed one ----

    [Fact]
    public void Engine_turns_flashed_strikes_into_thunder_and_stops_listening_when_disposed()
    {
        var s = new LightningSchedule(1, new FakeClock().Now);
        var eng = new AmbientEngine(null, s);
        eng.Mixer.SetVolumes(100, SoundEffects.Ids.ToDictionary(i => i, i => i == "thunder" ? 100 : 0));
        eng.Mixer.SetScene(SoundEffects.Ids.ToDictionary(i => i, _ => new SoundTarget(1f, 0.5f)));
        Assert.Equal(0, eng.Mixer.Thunder.Heard);
        s.Advance(1000, storm: true);                       // planning a strike alone is not enough ...
        Render(eng.Mixer, 1);
        Assert.Equal(0, eng.Mixer.Thunder.Heard);
        s.MarkShown(new LightningStrike(1, 0, 0.5f, 0.2f)); // ... it has to be flashed
        Render(eng.Mixer, 3);
        Assert.Equal(1, eng.Mixer.Thunder.Heard);
        eng.Dispose();
        s.MarkShown(new LightningStrike(2, 0, 0.5f, 0.2f));
        Render(eng.Mixer, 1);
        Assert.Equal(1, eng.Mixer.Thunder.Heard);
    }

    // ---- diorama + engine together: every flash has thunder, every thunder has a flash ----

    private sealed record Run((int Frame, long Id, float Dist)[] Flashes, (long Id, double CrackAt)[] Plays, long Heard, long Shown, double[] Energy);

    private static Run Simulate(bool showEffects, bool storm)
    {
        return Sta(() =>
        {
            var clock = new FakeClock();
            var sched = new LightningSchedule(5, clock.Now);
            int shown = 0;
            sched.Shown += _ => shown++;
            var cfg = new AppConfig { WeatherPreview = storm ? "storm" : "sunny", TimeMode = "real", ShowWeatherEffects = showEffects };
            cfg.Normalize();
            SceneClock.HourOverride = 12f;
            try
            {
                var mood = SceneClock.Compose(null, cfg, DateTime.Now);
                Assert.Equal(storm, mood.IsStorm);
                var d = new DioramaRenderer { Lightning = sched };
                d.Ensure(800, 48);
                using var bmp = new Bitmap(800, 48, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(bmp);
                var ctx = new FrameContext { Cfg = cfg, Analyzer = new Analyzer(), Regions = new TaskbarRegions { Bounds = new Rectangle(0, 0, 800, 48) }, Region = RegionCatalog.Default };
                var eng = new AmbientEngine(null, sched);
                eng.Mixer.SetVolumes(100, SoundEffects.Ids.ToDictionary(i => i, i => i == "thunder" ? 100 : 0));
                eng.Mixer.SetScene(SoundEffects.Ids.ToDictionary(i => i, _ => new SoundTarget(1f, 0.5f)));

                var flashes = new List<(int, long, float)>();
                var energy = new List<double>();
                long lastShown = 0;
                const double dt = 1.0 / 30; int frame = (int)(Rate * dt);
                var buf = new float[frame * 2];
                for (int f = 0; f < 30 * 150; f++)   // 150 s of storm, one video frame at a time: draw, then the audio for that frame
                {
                    clock.T += dt;
                    d.Draw(g, ctx, mood, (float)(f * dt));
                    if (sched.LastShownId != lastShown)
                    {
                        lastShown = sched.LastShownId;
                        var k = sched.StrikesSince(lastShown - 1)[0];
                        flashes.Add((f, k.Id, k.Distance));
                    }
                    eng.Mixer.Read(buf, 0, buf.Length);
                    double e = 0; for (int i = 0; i < buf.Length; i++) e += buf[i] * buf[i];
                    energy.Add(Math.Sqrt(e / buf.Length));
                }
                var run = new Run(flashes.ToArray(), eng.Mixer.Thunder.Plays, eng.Mixer.Thunder.Heard, shown, energy.ToArray());
                eng.Dispose();
                return run;
            }
            finally { SceneClock.HourOverride = null; }
        });
    }

    [Fact]
    public void Every_flash_has_its_thunder_and_every_thunder_has_a_flash()
    {
        var r = Simulate(showEffects: true, storm: true);
        Assert.True(r.Flashes.Length >= 12, $"flashes: {r.Flashes.Length}");
        Assert.Equal(r.Flashes.Length, r.Shown);
        Assert.Equal(r.Shown, r.Heard);                              // one thunder per flash
        Assert.Equal(r.Flashes.Select(f => f.Id).ToArray(), r.Plays.Select(p => p.Id).ToArray());   // the same strikes, none extra
        double dt = 1.0 / 30;
        foreach (var (frame, id, dist) in r.Flashes)
        {
            var crack = r.Plays.Single(p => p.Id == id).CrackAt;
            double wait = crack - frame * dt;                        // from the start of the flash frame to the crack
            double expect = LightningStrike.ThunderDelayFor(dist);
            Assert.InRange(wait, expect - dt - 0.012, expect + 0.012);   // flash first, crack after the distance delay
            Assert.True(wait >= -0.012, "thunder before its flash");
            // and the crack is really audible there: the half second after it has energy (the strike may overlap an earlier rumble)
            int a = (int)Math.Round(crack / dt), b = a + 15;
            if (b >= r.Energy.Length) continue;
            Assert.True(r.Energy[a..(b + 1)].Max() > 0.02, $"silence at the crack of strike {id}");
        }
        // close and distant strikes both occurred, so both ends of the delay range were exercised
        Assert.Contains(r.Flashes, f => f.Dist < 0.25f);
        Assert.Contains(r.Flashes, f => f.Dist > 0.75f);
    }

    [Fact]
    public void Without_weather_effects_there_is_no_flash_and_so_no_thunder()
    {
        var r = Simulate(showEffects: false, storm: true);
        Assert.Empty(r.Flashes);
        Assert.Equal(0, r.Shown);
        Assert.Equal(0, r.Heard);
        Assert.All(r.Energy, e => Assert.Equal(0.0, e));
    }

    [Fact]
    public void Fair_weather_has_neither_flash_nor_thunder()
    {
        var r = Simulate(showEffects: true, storm: false);
        Assert.Empty(r.Flashes);
        Assert.Equal(0, r.Heard);
        Assert.All(r.Energy, e => Assert.Equal(0.0, e));
    }
}
