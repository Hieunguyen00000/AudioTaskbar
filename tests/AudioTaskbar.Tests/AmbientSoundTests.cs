namespace AudioTaskbar.Tests;

/// <summary>v5.4 ambient sound: silent by default, deterministic, seamless, region/weather/season/time-aware.</summary>
[Collection("Statics")]
public class AmbientSoundTests
{
    private static readonly string[] Ids = SoundEffects.Ids.ToArray();

    private static Dictionary<string, int> All(int v) => Ids.ToDictionary(i => i, _ => v);
    private static Dictionary<string, int> Only(string id, int v) => Ids.ToDictionary(i => i, i => i == id ? v : 0);
    private static Dictionary<string, SoundTarget> Scene(float level = 1f, float aux = 0.5f) => Ids.ToDictionary(i => i, _ => new SoundTarget(level, aux));

    private static float[] Render(AmbientMixer m, int frames, int chunkFrames = 4096)
    {
        var all = new float[frames * 2];
        int done = 0;
        var buf = new float[chunkFrames * 2];
        while (done < frames)
        {
            int n = Math.Min(chunkFrames, frames - done);
            m.Read(buf, 0, n * 2);
            Array.Copy(buf, 0, all, done * 2, n * 2);
            done += n;
        }
        return all;
    }

    private static double Rms(float[] x, int from = 0)
    {
        double s = 0; int n = 0;
        for (int i = from; i < x.Length; i++) { s += x[i] * x[i]; n++; }
        return Math.Sqrt(s / Math.Max(1, n));
    }

    private static SceneMood Mood(string weather, float hour, string season = "summer")
    {
        var c = new AppConfig { WeatherPreview = weather, Season = season, TimeMode = "real" };
        c.Normalize();
        SceneClock.HourOverride = hour;
        try { return SceneClock.Compose(null, c, new DateTime(2026, 6, 15, (int)hour, 0, 0)); }
        finally { SceneClock.HourOverride = null; }
    }

    private static RegionProfile R(string id) => RegionCatalog.All.First(r => r.Id == id);

    // ---- silence by default ----

    [Fact]
    public void Mixer_is_exactly_silent_when_all_volumes_are_zero_and_does_no_work()
    {
        var m = new AmbientMixer();
        m.SetVolumes(0, All(0));
        m.SetScene(Scene());
        var x = Render(m, 48000 * 2);
        Assert.All(x, v => Assert.Equal(0f, v));
        foreach (var id in Ids) Assert.Equal(0, m.RenderCallsFor(id));
        Assert.False(m.WantsAudio);
        Assert.False(m.HasActivity);
    }

    [Fact]
    public void Master_up_with_every_effect_at_zero_is_silent()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, All(0));
        m.SetScene(Scene());
        Assert.All(Render(m, 48000), v => Assert.Equal(0f, v));
        Assert.False(m.WantsAudio);
    }

    [Fact]
    public void Effects_up_with_master_at_zero_is_silent()
    {
        var m = new AmbientMixer();
        m.SetVolumes(0, All(100));
        m.SetScene(Scene());
        Assert.All(Render(m, 48000), v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Missing_volume_entries_count_as_zero()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, new Dictionary<string, int> { ["rain"] = 100 });
        m.SetScene(Scene());
        Render(m, 48000 * 3);
        Assert.True(m.RenderCallsFor("rain") > 0);
        Assert.Equal(0, m.RenderCallsFor("birds"));
        m.SetVolumes(100, null);
        Assert.False(m.WantsAudio);
    }

    [Fact]
    public void Only_the_effect_with_volume_is_rendered()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, Only("waves", 100));
        m.SetScene(Scene());
        Render(m, 48000 * 3);
        foreach (var id in Ids) Assert.Equal(id == "waves", m.RenderCallsFor(id) > 0);
    }

    // ---- every source makes sound, is finite and bounded ----

    [Theory]
    [InlineData("rain")] [InlineData("thunder")] [InlineData("wind")] [InlineData("traffic")] [InlineData("horns")]
    [InlineData("birds")] [InlineData("insects")] [InlineData("animals")] [InlineData("waves")]
    public void Each_effect_is_audible_finite_and_within_range(string id)
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, Only(id, 100));
        m.SetScene(Scene(1f, 0.5f));
        var x = Render(m, 48000 * 25);   // long enough for sparse events (thunder, horns, animals)
        Assert.All(x, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
        Assert.True(Rms(x, 48000 * 2) > 0.002, $"{id} is silent");
    }

    [Fact]
    public void Train_is_audible_only_while_it_passes_and_pans()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, Only("train", 100));
        m.SetScene(Scene());
        m.Train.Proximity = 0f; m.Train.Pan = 0f;
        var far = Render(m, 48000 * 4);
        m.Train.Proximity = 1f; m.Train.Pan = -0.85f;
        var left = Render(m, 48000 * 3);
        double l = 0, r = 0;
        for (int i = 48000 * 2; i < left.Length / 2; i++) { l += left[i * 2] * left[i * 2]; r += left[i * 2 + 1] * left[i * 2 + 1]; }
        Assert.True(Rms(left, 48000 * 2) > Rms(far, 48000 * 2) * 1.5 || Rms(left, 48000 * 2) > 0.003);
        Assert.True(l > r * 1.5, "train panned left should be louder in the left channel");
    }

    [Fact]
    public void Train_proximity_is_bell_shaped()
    {
        Assert.Equal(0f, AmbientEngine.TrainProximity(-0.1f));
        Assert.Equal(0f, AmbientEngine.TrainProximity(1.1f));
        Assert.Equal(0f, AmbientEngine.TrainProximity(0f), 3);
        Assert.Equal(0f, AmbientEngine.TrainProximity(1f), 3);
        Assert.Equal(1f, AmbientEngine.TrainProximity(0.5f), 3);
        Assert.True(AmbientEngine.TrainProximity(0.25f) < AmbientEngine.TrainProximity(0.4f));
        Assert.True(AmbientEngine.TrainProximity(0.75f) < AmbientEngine.TrainProximity(0.6f));
    }

    // ---- determinism and seamlessness ----

    [Fact]
    public void Output_does_not_depend_on_how_the_device_asks_for_it()
    {
        float[] Run(int chunk)
        {
            var m = new AmbientMixer();
            m.SetVolumes(80, All(70));
            m.SetScene(Scene(0.8f, 0.4f));
            return Render(m, 48000 * 6, chunk);
        }
        var a = Run(8000);
        var b = Run(333);
        var c = Run(1);
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i]);
            if (i < 40000) Assert.Equal(a[i], c[i]);
        }
    }

    [Theory]
    [InlineData("rain")] [InlineData("wind")] [InlineData("waves")] [InlineData("traffic")]
    public void Continuous_beds_have_no_clicks_or_loop_seams(string id)
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, Only(id, 100));
        m.SetScene(Scene(1f, 0.5f));
        var x = Render(m, 48000 * 40, 1000);
        float maxJump = 0;
        for (int i = 48000 * 3; i < x.Length / 2 - 1; i++)
            maxJump = Math.Max(maxJump, Math.Abs(x[(i + 1) * 2] - x[i * 2]));
        Assert.True(maxJump < (id == "rain" ? 1.2f : 0.6f), $"{id}: sample jump {maxJump}");   // rain has drop transients by design
        // Slow loudness changes only: compare 1 s windows
        var w = new List<double>();
        for (int s = 3; s < 39; s++) w.Add(Rms(x[(s * 96000)..((s + 1) * 96000)]));
        Assert.True(w.Max() / Math.Max(1e-6, w.Min()) < 10.0, $"{id}: loudness varies too much ({w.Min():F3}..{w.Max():F3})");
    }

    [Fact]
    public void Pausing_fades_everything_to_silence_and_resuming_brings_it_back()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, All(100));
        m.SetScene(Scene());
        Render(m, 48000 * 5);
        Assert.True(m.WantsAudio);
        m.Paused = true;
        Assert.False(m.WantsAudio);
        var tail = Render(m, 48000 * 3);
        Assert.True(Rms(tail, tail.Length - 9600) < 1e-4, "silent after the fade");
        Assert.False(m.HasActivity);
        var calls = Ids.Sum(i => m.RenderCallsFor(i));
        Render(m, 48000);
        Assert.Equal(calls, Ids.Sum(i => m.RenderCallsFor(i)));   // no synthesis while paused
        m.Paused = false;
        var back = Render(m, 48000 * 4);
        Assert.True(Rms(back, back.Length - 48000) > 0.002);
    }

    [Fact]
    public void Volume_changes_are_smoothed_not_stepped()
    {
        var m = new AmbientMixer();
        m.SetVolumes(100, Only("wind", 100));
        m.SetScene(Scene());
        Render(m, 48000 * 4);
        m.SetVolumes(100, Only("wind", 0));
        var x = Render(m, 4800, 100);
        float maxJump = 0;
        for (int i = 0; i < 4799; i++) maxJump = Math.Max(maxJump, Math.Abs(x[(i + 1) * 2] - x[i * 2]));
        Assert.True(maxJump < 0.3f, $"jump {maxJump}");
    }

    // ---- the soundscape follows region, weather, season and time of day ----

    [Fact]
    public void Soundscape_always_lists_every_effect_within_0_to_1()
    {
        foreach (var region in RegionCatalog.All)
            foreach (var w in new[] { "sunny", "cloudy", "rain", "storm", "snow", "night" })
                foreach (var season in Seasons.All)
                    foreach (var h in new[] { 3f, 7f, 12f, 18f, 22f })
                    {
                        var t = SoundScape.Targets(region, Mood(w, h, Seasons.Id(season)), season);
                        Assert.Equal(Ids.OrderBy(x => x), t.Keys.OrderBy(x => x));
                        Assert.All(t.Values, v => { Assert.InRange(v.Level, 0f, 1f); Assert.InRange(v.Aux, 0f, 1f); });
                    }
    }

    [Fact]
    public void Rain_and_thunder_follow_the_weather()
    {
        var hcmc = R("hcmc");
        Assert.Equal(0f, SoundScape.Targets(hcmc, Mood("sunny", 12), SeasonId.Summer)["rain"].Level);
        Assert.True(SoundScape.Targets(hcmc, Mood("rain", 12), SeasonId.Summer)["rain"].Level > 0.3f);
        Assert.Equal(0f, SoundScape.Targets(hcmc, Mood("rain", 12), SeasonId.Summer)["thunder"].Level);
        Assert.True(SoundScape.Targets(hcmc, Mood("storm", 12), SeasonId.Summer)["thunder"].Level > 0.5f);
        Assert.True(SoundScape.Targets(hcmc, Mood("storm", 12), SeasonId.Summer)["wind"].Level >
                    SoundScape.Targets(hcmc, Mood("sunny", 12), SeasonId.Summer)["wind"].Level);
    }

    [Fact]
    public void Waves_belong_to_the_coast_and_traffic_to_the_city()
    {
        var m = Mood("sunny", 12);
        Assert.True(SoundScape.Targets(R("baria-vungtau"), m, SeasonId.Summer)["waves"].Level > 0.4f);
        Assert.True(SoundScape.Targets(R("sydney"), m, SeasonId.Summer)["waves"].Level > 0.4f);
        Assert.True(SoundScape.Targets(R("santorini"), m, SeasonId.Summer)["waves"].Level > 0.4f);
        Assert.True(SoundScape.Targets(R("hanoi"), m, SeasonId.Summer)["waves"].Level < 0.2f);
        Assert.Equal(0f, SoundScape.Targets(R("giza"), m, SeasonId.Summer)["waves"].Level);
        Assert.True(SoundScape.Targets(R("newyork"), m, SeasonId.Summer)["traffic"].Level >
                    SoundScape.Targets(R("santorini"), m, SeasonId.Summer)["traffic"].Level + 0.3f);
        // honking: Ho Chi Minh City and New York honk, Tokyo hardly ever does
        Assert.True(SoundScape.Targets(R("hcmc"), Mood("sunny", 8), SeasonId.Summer)["horns"].Level > 0.5f);
        Assert.True(SoundScape.Targets(R("tokyo"), Mood("sunny", 8), SeasonId.Summer)["horns"].Level < 0.15f);
    }

    [Fact]
    public void Traffic_peaks_at_rush_hour_and_sleeps_at_night()
    {
        Assert.True(SoundScape.TrafficOfDay(8f) > 0.6f);
        Assert.True(SoundScape.TrafficOfDay(17.5f) > 0.8f);
        Assert.True(SoundScape.TrafficOfDay(3f) < 0.3f);
        var hn = R("hanoi");
        Assert.True(SoundScape.Targets(hn, Mood("sunny", 17.5f), SeasonId.Summer)["traffic"].Level >
                    SoundScape.Targets(hn, Mood("sunny", 3f), SeasonId.Summer)["traffic"].Level * 2f);
    }

    [Fact]
    public void Birds_by_day_insects_by_night_and_seasons_change_the_mix()
    {
        var def = R("default");
        var day = SoundScape.Targets(def, Mood("sunny", 9), SeasonId.Summer);
        var night = SoundScape.Targets(def, Mood("night", 23), SeasonId.Summer);
        Assert.True(day["birds"].Level > night["birds"].Level * 2f);
        Assert.True(night["insects"].Level > day["insects"].Level);
        Assert.True(day["insects"].Aux > night["insects"].Aux);   // cicadas by day, crickets by night
        var winter = SoundScape.Targets(def, Mood("sunny", 9, "winter"), SeasonId.Winter);
        var spring = SoundScape.Targets(def, Mood("sunny", 9, "spring"), SeasonId.Spring);
        Assert.True(spring["birds"].Level > winter["birds"].Level * 2f);
        Assert.True(winter["insects"].Level < 0.1f);
        // rain keeps the birds quiet
        Assert.True(SoundScape.Targets(def, Mood("rain", 9), SeasonId.Summer)["birds"].Level < day["birds"].Level * 0.5f);
        // roosters and frogs are country sounds
        Assert.True(SoundScape.Targets(def, Mood("night", 23), SeasonId.Summer)["animals"].Level >
                    SoundScape.Targets(R("newyork"), Mood("night", 23), SeasonId.Summer)["animals"].Level);
    }

    [Fact]
    public void Trains_follow_the_region_trait()
    {
        var m = Mood("sunny", 12);
        Assert.Equal(0f, SoundScape.Targets(R("giza"), m, SeasonId.Summer)["train"].Level);
        Assert.True(SoundScape.Targets(R("tokyo"), m, SeasonId.Summer)["train"].Level > 0.5f);
    }

    [Fact]
    public void Every_region_has_a_distinct_sound_profile_entry()
    {
        foreach (var r in RegionCatalog.All.Where(x => x.Id != "default"))
        {
            var t = SoundScape.Traits(r.Id);
            Assert.NotEqual(SoundScape.Traits("__unknown__"), t);
        }
    }

    // ---- engine / device ----

    [Fact]
    public void Engine_with_zero_volumes_never_opens_an_audio_device()
    {
        using var e = new AmbientEngine();
        var cfg = new AppConfig(); cfg.Normalize();
        for (int i = 0; i < 50; i++) e.Update(cfg, R("hcmc"), Mood("rain", 12), SeasonId.Summer, true, 0.5f);
        Assert.False(e.DeviceOpen);
        Assert.False(e.Mixer.WantsAudio);
    }

    [Fact]
    public void Engine_is_inert_after_dispose_and_dispose_is_idempotent()
    {
        var e = new AmbientEngine();
        var cfg = new AppConfig { SoundMasterVolume = 100 }; cfg.SoundVolumes["rain"] = 100; cfg.Normalize();
        e.Dispose();
        e.Dispose();
        e.Update(cfg, R("hcmc"), Mood("rain", 12), SeasonId.Summer, true, -1f);
        Assert.False(e.DeviceOpen);
    }

    [Fact]
    public void Disabling_effects_pauses_the_mixer()
    {
        using var e = new AmbientEngine();
        var cfg = new AppConfig { SoundMasterVolume = 0 }; cfg.Normalize();
        e.Update(cfg, R("hcmc"), Mood("rain", 12), SeasonId.Summer, false, -1f);
        Assert.True(e.Mixer.Paused);
        e.Update(cfg, R("hcmc"), Mood("rain", 12), SeasonId.Summer, true, -1f);
        Assert.False(e.Mixer.Paused);
    }

    [Fact]
    public void No_third_party_audio_assets_are_shipped()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var src = Path.Combine(root, "src");
        if (!Directory.Exists(src)) return;
        var proj = File.ReadAllText(Path.Combine(src, "AudioTaskbar.csproj"));
        Assert.DoesNotContain(".wav", proj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mp3", proj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".ogg", proj, StringComparison.OrdinalIgnoreCase);
        var audio = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => new[] { ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac" }.Contains(Path.GetExtension(f).ToLowerInvariant()));
        Assert.Empty(audio);
    }
}
