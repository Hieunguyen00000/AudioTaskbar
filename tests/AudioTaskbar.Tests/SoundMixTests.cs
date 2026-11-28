namespace AudioTaskbar.Tests;

/// <summary>v5.4.2: standard per-effect volumes that follow the scene ("Tự phối theo cảnh"); the master volume is the user's alone.</summary>
[Collection("Statics")]
public class SoundMixTests
{
    private static SceneMood Mood(string weather, float hour, string season = "summer")
    {
        var c = new AppConfig { WeatherPreview = weather, Season = season, TimeMode = "real" };
        c.Normalize();
        SceneClock.HourOverride = hour;
        try { return SceneClock.Compose(null, c, new DateTime(2026, 6, 15, (int)hour, 0, 0)); }
        finally { SceneClock.HourOverride = null; }
    }

    private static RegionProfile R(string id) => RegionCatalog.All.First(r => r.Id == id);

    [Fact]
    public void Every_effect_has_a_standard_volume_and_presets_stay_in_range()
    {
        Assert.Equal(SoundEffects.Ids.OrderBy(x => x), SoundMix.Base.Keys.OrderBy(x => x));
        foreach (var r in RegionCatalog.All)
            foreach (var w in new[] { "sunny", "rain", "storm", "snow", "cloudy" })
                foreach (var h in new[] { 3f, 12f, 21f })
                    foreach (var s in new[] { SeasonId.Spring, SeasonId.Summer, SeasonId.Autumn, SeasonId.Winter })
                    {
                        var p = SoundMix.Preset(r, Mood(w, h), s);
                        Assert.Equal(SoundEffects.Ids.OrderBy(x => x), p.Keys.OrderBy(x => x));
                        Assert.All(p.Values, v => Assert.InRange(v, 0, 100));
                    }
    }

    [Fact]
    public void Storms_raise_rain_and_thunder()
    {
        var calm = SoundMix.Preset(R("hanoi"), Mood("sunny", 14), SeasonId.Summer);
        var storm = SoundMix.Preset(R("hanoi"), Mood("storm", 14), SeasonId.Summer);
        Assert.True(storm[SoundEffects.Rain] > calm[SoundEffects.Rain]);
        Assert.True(storm[SoundEffects.Thunder] > calm[SoundEffects.Thunder]);
        Assert.True(storm[SoundEffects.Wind] > calm[SoundEffects.Wind]);
        Assert.True(storm[SoundEffects.Birds] < calm[SoundEffects.Birds]);
    }

    [Fact]
    public void Nights_bring_crickets_and_quiet_the_birds_and_traffic()
    {
        var day = SoundMix.Preset(R("default"), Mood("sunny", 13), SeasonId.Summer);
        var night = SoundMix.Preset(R("default"), Mood("sunny", 23), SeasonId.Summer);
        Assert.True(night[SoundEffects.Insects] > day[SoundEffects.Insects]);
        Assert.True(night[SoundEffects.Birds] < day[SoundEffects.Birds]);
        Assert.True(night[SoundEffects.Traffic] < day[SoundEffects.Traffic]);
        // winter nights are quieter than summer nights
        Assert.True(SoundMix.Preset(R("default"), Mood("sunny", 23, "winter"), SeasonId.Winter)[SoundEffects.Insects] < night[SoundEffects.Insects]);
    }

    [Fact]
    public void Coasts_get_waves_and_cities_get_traffic()
    {
        var mood = Mood("sunny", 11);
        var coast = SoundMix.Preset(R("baria-vungtau"), mood, SeasonId.Summer);
        var inland = SoundMix.Preset(R("hanoi"), mood, SeasonId.Summer);
        Assert.True(coast[SoundEffects.Waves] >= inland[SoundEffects.Waves] + 20);
        var city = SoundMix.Preset(R("newyork"), mood, SeasonId.Summer);
        var country = SoundMix.Preset(R("default"), mood, SeasonId.Summer);
        Assert.True(city[SoundEffects.Traffic] >= country[SoundEffects.Traffic] + 15);
        Assert.True(city[SoundEffects.Horns] > country[SoundEffects.Horns]);
    }

    [Fact]
    public void Automatic_mix_follows_the_scene_but_respects_hand_set_effects_and_never_touches_the_master()
    {
        var cfg = new AppConfig { SoundMasterVolume = 37 }; cfg.Normalize();
        Assert.True(cfg.SoundAutoMix);                              // on by default
        var preset = SoundMix.Preset(R("hanoi"), Mood("storm", 14), SeasonId.Summer);
        Assert.Equal(preset, SoundMix.Effective(cfg, preset));
        SoundMix.SetManual(cfg, SoundEffects.Rain, 12);
        var eff = SoundMix.Effective(cfg, preset);
        Assert.Equal(12, eff[SoundEffects.Rain]);
        Assert.Equal(preset[SoundEffects.Thunder], eff[SoundEffects.Thunder]);
        // a different scene moves the automatic effects only
        var calm = SoundMix.Preset(R("hanoi"), Mood("sunny", 14), SeasonId.Summer);
        Assert.Equal(12, SoundMix.Effective(cfg, calm)[SoundEffects.Rain]);
        Assert.Equal(calm[SoundEffects.Thunder], SoundMix.Effective(cfg, calm)[SoundEffects.Thunder]);
        // switching it off keeps what is heard, as the user's own values
        SoundMix.SetAutoMix(cfg, false, calm);
        Assert.Equal(SoundMix.Effective(cfg, preset), SoundMix.Effective(cfg, calm));
        Assert.Equal(calm[SoundEffects.Birds], cfg.SoundVolumes[SoundEffects.Birds]);
        SoundMix.ResetToScene(cfg);
        Assert.Equal(calm, SoundMix.Effective(cfg, calm));
        Assert.Equal(37, cfg.SoundMasterVolume);
    }

    [Fact]
    public void Ambient_mixer_gets_the_effective_mix_and_the_users_master()
    {
        var src = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "AmbientMixer.cs"));
        Assert.Contains("SetVolumes(cfg.SoundMasterVolume, SoundMix.Effective(", src);
        Assert.DoesNotContain("SoundMasterVolume =", File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "Sounds.cs")));
    }

    [Theory]
    [InlineData(0, new int[0], "")]                       // never touched: everything follows the scene, still silent
    [InlineData(0, new[] { 0, 40 }, "thunder")]           // master off but one effect raised: that one is kept
    [InlineData(65, new[] { 0, 0 }, "*")]                 // user listened with their own mix: all of it is kept
    public void Migration_to_schema_56_keeps_the_users_mix_and_master(int master, int[] rainThunder, string expectManual)
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        string vols = rainThunder.Length == 0 ? "" : $", \"soundVolumes\": {{ \"rain\": {rainThunder[0]}, \"thunder\": {rainThunder[1]} }}";
        File.WriteAllText(p, $"{{ \"configVersion\": 55, \"soundMasterVolume\": {master}{vols} }}");
        var c = ConfigStore.Load(p);
        Assert.Equal(AppConfig.CurrentConfigVersion, c.ConfigVersion);
        Assert.Equal(56, AppConfig.CurrentConfigVersion);
        Assert.Equal(master, c.SoundMasterVolume);                  // never changed by migration
        Assert.True(c.SoundAutoMix);
        if (expectManual == "*") Assert.Equal(SoundEffects.Ids.OrderBy(x => x), c.SoundManual.OrderBy(x => x));
        else if (expectManual == "") Assert.Empty(c.SoundManual);
        else Assert.Equal(new[] { expectManual }, c.SoundManual);
        // idempotent: loading again changes nothing
        var again = ConfigStore.Load(p);
        Assert.Equal(c.SoundManual.OrderBy(x => x), again.SoundManual.OrderBy(x => x));
        Assert.Equal(master, again.SoundMasterVolume);
    }
}
