using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace AudioTaskbar.Tests;

/// <summary>v5.4 world destinations (v5.4.2: Sydney and New York remain; Rome, Giza and Santorini were removed), seasons in the render, and the icon-padding switch (headless renders only).</summary>
[Collection("Statics")]
public class SceneryV54Tests
{
    private static readonly string[] NewIds = { "sydney", "newyork" };
    private static readonly string[] RemovedIds = { "rome", "giza", "santorini" };

    private static T Sta<T>(Func<T> f)
    {
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw new Exception("STA work failed: " + err.Message, err);
        return result;
    }

    internal static (string hash, int nonTransparent, string layout) Render(string regionId, string season = "summer", string weather = "sunny",
        float hour = 12f, bool padding = true, bool fused = false, string cutouts = "", int w = 1920, int h = 48, int frames = 4, float t0 = 30f)
        => Sta(() =>
        {
            var cfg = new AppConfig { RegionMode = regionId, Season = season, WeatherPreview = weather, ShowIconPadding = padding, TimeMode = "real", TrainCrossingMinutes = 240 };
            cfg.Normalize();
            SceneClock.HourOverride = hour;
            try
            {
                var regions = new TaskbarRegions { Bounds = new Rectangle(0, 0, w, h) };
                foreach (var part in cutouts.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var lr = part.Split('-');
                    regions.Cutouts.Add(Rectangle.FromLTRB(int.Parse(lr[0]), 0, int.Parse(lr[1]), h));
                }
                regions.Signature = cutouts;
                var region = RegionCatalog.All.First(r => r.Id == regionId);
                using var ow = new OverlayWindow();
                var an = new Analyzer();
                Bitmap? last = null;
                for (int i = 0; i < frames; i++)
                {
                    last?.Dispose();
                    last = ow.RenderOffscreen(w, h, new FrameContext { Cfg = cfg, Analyzer = an, Regions = regions, Time = t0 + i / 30f, Fused = fused, BehindOk = fused, Region = region });
                }
                var bd = last!.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var bytes = new byte[bd.Stride * h];
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length);
                last.UnlockBits(bd);
                int opaque = 0;
                for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] > 16) opaque++;
                var layout = ow.LastLayoutInfo;
                last.Dispose();
                return (Convert.ToHexString(SHA256.HashData(bytes)), opaque, layout);
            }
            finally { SceneClock.HourOverride = null; }
        });

    [Fact]
    public void Catalog_has_the_world_destinations_with_their_own_landmarks()
    {
        Assert.Equal(9, RegionCatalog.All.Length);
        foreach (var id in RemovedIds) Assert.DoesNotContain(RegionCatalog.All, r => r.Id == id);
        var all = RegionCatalog.All;
        foreach (var id in NewIds)
        {
            var r = all.Single(x => x.Id == id);
            Assert.True(r.Landmarks.Length >= 2, id);
            Assert.False(string.IsNullOrWhiteSpace(r.NameVi));
            Assert.False(string.IsNullOrWhiteSpace(r.Filler));
            Assert.NotEmpty(r.Road);
        }
        // no duplicated landmark art across regions that did not already share it
        var newLandmarks = NewIds.SelectMany(id => all.Single(x => x.Id == id).Landmarks).ToList();
        Assert.Equal(newLandmarks.Count, newLandmarks.Distinct().Count());
        var oldLandmarks = all.Where(r => !NewIds.Contains(r.Id)).SelectMany(r => r.Landmarks).ToHashSet();
        Assert.Empty(newLandmarks.Where(oldLandmarks.Contains));
        // distinct skyline fillers
        Assert.Equal(NewIds.Length, NewIds.Select(id => all.Single(x => x.Id == id).Filler).Distinct().Count());
    }

    [Theory]
    [InlineData("sydney", "Sydney")]
    [InlineData("newyork", "New York")]
    public void New_regions_are_found_by_name_and_mode(string id, string city)
    {
        Assert.Equal(id, RegionCatalog.NormalizeMode(id));
        Assert.Equal(id, RegionCatalog.MatchName(city)?.Id);
    }

    [Fact]
    public void New_regions_are_found_by_coordinates()
    {
        Assert.Equal("sydney", RegionCatalog.MatchCoords(-33.87, 151.21)?.Id);
        Assert.Equal("newyork", RegionCatalog.MatchCoords(40.71, -74.0)?.Id);
        // v5.4.2: the removed destinations are no longer detected (auto falls back to the default scene)
        Assert.Null(RegionCatalog.MatchCoords(41.9, 12.5));
        Assert.Null(RegionCatalog.MatchCoords(29.98, 31.13));
        Assert.Null(RegionCatalog.MatchCoords(36.4, 25.43));
        Assert.Null(RegionCatalog.MatchName("Rome"));
        Assert.Null(RegionCatalog.MatchName("Cairo"));
        Assert.Null(RegionCatalog.MatchName("Santorini"));
        Assert.Equal("hcmc", RegionCatalog.MatchCoords(10.77, 106.7)?.Id);   // old regions unchanged
    }

    [Fact]
    public void Every_region_renders_something_and_all_are_visually_distinct()
    {
        var hashes = new Dictionary<string, string>();
        foreach (var r in RegionCatalog.All)
        {
            var (hash, opaque, _) = Render(r.Id, w: 1600, h: 48);
            Assert.True(opaque > 1600 * 48 / 3, $"{r.Id} drew only {opaque}px");
            hashes[r.Id] = hash;
        }
        Assert.Equal(hashes.Count, hashes.Values.Distinct().Count());
    }

    [Fact]
    public void Every_region_survives_every_season_weather_and_time()
    {
        foreach (var r in RegionCatalog.All)
            foreach (var season in new[] { "spring", "summer", "autumn", "winter" })
                foreach (var weather in new[] { "sunny", "rain", "snow", "storm" })
                    Render(r.Id, season, weather, hour: weather == "storm" ? 21 : 7, frames: 1, w: 700);
    }

    [Fact]
    public void Narrow_and_odd_sized_bars_do_not_crash_with_the_new_regions()
    {
        foreach (var id in NewIds)
            foreach (var (w, h) in new[] { (300, 40), (1, 1), (5000, 56), (800, 120) })
                Render(id, w: w, h: h, frames: 1);
    }

    [Fact]
    public void Seasons_change_the_picture()
    {
        var hashes = new[] { "spring", "summer", "autumn", "winter" }.Select(s => Render("hcmc", s, "sunny", frames: 1).hash).ToList();
        Assert.Equal(4, hashes.Distinct().Count());
    }

    [Fact]
    public void Season_palette_changes_the_sky_and_winter_carries_snow_cover()
    {
        SceneClock.HourOverride = 12f;
        try
        {
            SceneMood M(string season, string weather)
            {
                var c = new AppConfig { Season = season, WeatherPreview = weather, TimeMode = "real" }; c.Normalize();
                return SceneClock.Compose(null, c, new DateTime(2026, 6, 15, 12, 0, 0));
            }
            static (Color, Color, Color) Sky(SceneMood m) => (m.SkyTop, m.SkyMid, m.SkyBot);
            var seasons = new[] { "spring", "summer", "autumn", "winter" }.Select(s => Sky(M(s, "sunny"))).ToList();
            Assert.Equal(4, seasons.Distinct().Count());
            Assert.True(M("winter", "sunny").SnowCover > 0.2f);
            Assert.Equal(0f, M("summer", "sunny").SnowCover);
            Assert.Equal(0f, M("autumn", "rain").SnowCover);
            Assert.True(M("winter", "snow").SnowCover >= M("winter", "sunny").SnowCover);
        }
        finally { SceneClock.HourOverride = null; }
    }

    [Fact]
    public void Icon_padding_switch_changes_the_render_and_keeps_icons_unobstructed()
    {
        const string cut = "300-900,1200-1600";
        var on = Render("hcmc", padding: true, cutouts: cut, fused: false);
        var off = Render("hcmc", padding: false, cutouts: cut, fused: false);
        Assert.NotEqual(on.hash, off.hash);
        // without padding the scenery sits right against the icons: the free spans are bigger
        static int Free(string layout)
        {
            int a = layout.IndexOf("spans=[") + 7, b = layout.IndexOf(']', a);
            return layout[a..b].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Split('-')).Sum(p => int.Parse(p[1]) - int.Parse(p[0]));
        }
        Assert.True(Free(off.layout) > Free(on.layout), $"on={on.layout} off={off.layout}");
        // fused mode (behind the taskbar) differs too (the grey wash under icons is only drawn with padding)
        Assert.NotEqual(Render("hcmc", padding: true, cutouts: cut, fused: true).hash, Render("hcmc", padding: false, cutouts: cut, fused: true).hash);
    }

    [Fact]
    public void Padding_defaults_to_on_so_existing_users_see_no_change()
    {
        var c = new AppConfig(); c.Normalize();
        Assert.True(c.ShowIconPadding);
        Assert.False(c.ShowWeatherHud);
    }
}
