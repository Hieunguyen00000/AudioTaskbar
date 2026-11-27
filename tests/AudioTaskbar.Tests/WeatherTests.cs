using System.Net;

namespace AudioTaskbar.Tests;

/// <summary>2. Weather: offline / timeout / HTTP 500 / bad JSON, WMO codes, anti-jump, region matching.</summary>
public class WeatherTests
{
    private const string GoodForecast = "{\"current\":{\"temperature_2m\":27.5,\"weather_code\":73,\"is_day\":1,\"precipitation\":0.4,\"cloud_cover\":88}}";

    private static AppConfig CoordsCfg() => new() { WeatherLatitude = 10.77, WeatherLongitude = 106.7, WeatherCity = "Ho Chi Minh City" };

    [Theory]
    [InlineData(71, WeatherKind.Snow)]
    [InlineData(73, WeatherKind.Snow)]
    [InlineData(75, WeatherKind.Snow)]
    [InlineData(77, WeatherKind.Snow)]
    [InlineData(85, WeatherKind.Snow)]
    [InlineData(86, WeatherKind.Snow)]
    [InlineData(51, WeatherKind.Rain)]
    [InlineData(61, WeatherKind.Rain)]
    [InlineData(65, WeatherKind.Rain)]
    [InlineData(80, WeatherKind.Rain)]
    [InlineData(82, WeatherKind.Rain)]
    [InlineData(95, WeatherKind.Storm)]
    [InlineData(96, WeatherKind.Storm)]
    [InlineData(99, WeatherKind.Storm)]
    [InlineData(45, WeatherKind.Fog)]
    [InlineData(2, WeatherKind.Cloudy)]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(-1, WeatherKind.Clear)]
    [InlineData(1234, WeatherKind.Clear)]
    public void Wmo_codes_map_to_kind_by_day(int code, WeatherKind kind)
        => Assert.Equal(kind, WeatherCodes.KindOf(code, true));

    [Theory]
    [InlineData(0)]
    [InlineData(1234)]
    public void Clear_or_unknown_codes_at_night_are_night(int code)
        => Assert.Equal(WeatherKind.Night, WeatherCodes.KindOf(code, false));

    [Theory]
    [InlineData(71, "Tuyết nhẹ")]
    [InlineData(77, "Tuyết nhẹ")]
    [InlineData(85, "Tuyết nhẹ")]
    [InlineData(73, "Tuyết rơi")]
    [InlineData(75, "Tuyết dày")]
    [InlineData(86, "Tuyết dày")]
    public void Snow_codes_have_vietnamese_labels(int code, string vi)
        => Assert.Equal(vi, WeatherCodes.DescribeVi(code, true));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(61)]
    [InlineData(95)]
    [InlineData(9999)]
    [InlineData(-7)]
    public void Every_code_has_a_label_and_no_snow_outside_snow_codes(int code)
    {
        Assert.False(string.IsNullOrWhiteSpace(WeatherCodes.DescribeVi(code, true)));
        Assert.False(string.IsNullOrWhiteSpace(WeatherCodes.DescribeEn(code, false)));
        Assert.Equal(0f, WeatherCodes.SnowAmount(code));
    }

    [Fact]
    public void Snow_amount_grows_with_intensity()
    {
        Assert.True(WeatherCodes.SnowAmount(71) < WeatherCodes.SnowAmount(73));
        Assert.True(WeatherCodes.SnowAmount(73) < WeatherCodes.SnowAmount(75));
        Assert.All(new[] { 71, 73, 75, 77, 85, 86 }, c => Assert.InRange(WeatherCodes.SnowAmount(c), 0.01f, 1f));
    }

    [Fact]
    public async Task Good_response_is_parsed()
    {
        using var d = new TempDir();
        using var ws = new WeatherService(new MockHandler(_ => MockHandler.Json(GoodForecast)), d.Path);
        var s = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        Assert.True(s.Ok);
        Assert.Null(s.Error);
        Assert.Equal(73, s.WeatherCode);
        Assert.Equal(27.5f, s.TemperatureC);
        Assert.Equal(WeatherKind.Snow, s.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Http_errors_never_throw(HttpStatusCode code)
    {
        using var d = new TempDir();
        using var ws = new WeatherService(new MockHandler(_ => MockHandler.Status(code)), d.Path);
        var s = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        Assert.False(s.Ok);
        Assert.NotNull(s.Error);
    }

    [Fact]
    public async Task Offline_never_throws()
    {
        using var d = new TempDir();
        using var ws = new WeatherService(new MockHandler(_ => throw new HttpRequestException("No such host is known.")), d.Path);
        var s = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        Assert.False(s.Ok);
        Assert.Contains("host", s.Error);
    }

    [Fact]
    public async Task Timeout_never_throws()
    {
        using var d = new TempDir();
        var handler = new MockHandler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return MockHandler.Json(GoodForecast); });
        using var ws = new WeatherService(handler, d.Path, TimeSpan.FromMilliseconds(200));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        Assert.False(s.Ok);
        Assert.NotNull(s.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("{}")]
    [InlineData("{\"current\":{}}")]
    [InlineData("{\"current\":{\"temperature_2m\":\"hot\",\"weather_code\":1}}")]
    [InlineData("")]
    public async Task Invalid_json_never_throws(string body)
    {
        using var d = new TempDir();
        using var ws = new WeatherService(new MockHandler(_ => MockHandler.Json(body)), d.Path);
        var s = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        Assert.False(s.Ok);
        Assert.NotNull(s.Error);
    }

    [Fact]
    public async Task Failure_after_success_keeps_last_good_weather()
    {
        using var d = new TempDir();
        bool fail = false;
        using var ws = new WeatherService(new MockHandler(_ => fail ? MockHandler.Status(HttpStatusCode.InternalServerError) : MockHandler.Json(GoodForecast)), d.Path);
        var good = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        ws.SetLatest(good);
        fail = true;
        var s = await ws.FetchSafeAsync(CoordsCfg(), CancellationToken.None);
        Assert.True(s.Ok);
        Assert.Equal(73, s.WeatherCode);
        Assert.NotNull(s.Error);
    }

    [Fact]
    public void Corrupted_weather_cache_is_ignored()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("weather-cache.json"), "{{{ broken");
        using var ws = new WeatherService(new MockHandler(_ => MockHandler.Json(GoodForecast)), d.Path);
        Assert.Null(ws.Latest); // broken cache = no snapshot, never an exception
    }

    // --- anti-jump (IP geolocation hopping between provinces) ---
    private static readonly (double, double) Hcmc = (10.82, 106.63), Baria = (10.50, 107.17), Danang = (16.05, 108.20), Q7 = (10.73, 106.72);

    [Fact]
    public void First_location_is_accepted()
    {
        var s = new LocationStabilizer();
        var (p, kept) = s.Apply(Hcmc.Item1, Hcmc.Item2, "HCMC", "VN");
        Assert.False(kept);
        Assert.Equal("HCMC", p.Name);
    }

    [Fact]
    public void Small_move_is_accepted_immediately()
    {
        var s = new LocationStabilizer();
        s.Apply(Hcmc.Item1, Hcmc.Item2, "HCMC", "VN");
        var (p, kept) = s.Apply(Q7.Item1, Q7.Item2, "Quan 7", "VN");
        Assert.False(kept);
        Assert.Equal("Quan 7", p.Name);
    }

    [Fact]
    public void Single_far_jump_is_ignored()
    {
        var s = new LocationStabilizer();
        s.Apply(Baria.Item1, Baria.Item2, "Ba Ria", "VN");
        var (p, kept) = s.Apply(Danang.Item1, Danang.Item2, "Da Nang", "VN");
        Assert.True(kept);
        Assert.Equal("Ba Ria", p.Name);
    }

    [Fact]
    public void Two_consecutive_far_hits_switch_location()
    {
        var s = new LocationStabilizer();
        s.Apply(Baria.Item1, Baria.Item2, "Ba Ria", "VN");
        s.Apply(Danang.Item1, Danang.Item2, "Da Nang", "VN");
        var (p, kept) = s.Apply(Danang.Item1 + 0.01, Danang.Item2, "Da Nang", "VN");
        Assert.False(kept);
        Assert.Equal("Da Nang", p.Name);
    }

    [Fact]
    public void Flapping_far_hits_never_switch()
    {
        var s = new LocationStabilizer();
        s.Apply(Baria.Item1, Baria.Item2, "Ba Ria", "VN");
        for (int i = 0; i < 5; i++)
        {
            var (p1, _) = s.Apply(Danang.Item1, Danang.Item2, "Da Nang", "VN");
            Assert.Equal("Ba Ria", p1.Name);
            var (p2, _) = s.Apply(21.03, 105.85, "Ha Noi", "VN");
            Assert.Equal("Ba Ria", p2.Name);
        }
    }

    [Fact]
    public void Seed_from_cache_is_respected()
    {
        var s = new LocationStabilizer();
        var (p, kept) = s.Apply(Danang.Item1, Danang.Item2, "Da Nang", "VN", (Baria.Item1, Baria.Item2, "Ba Ria", "VN"));
        Assert.True(kept);
        Assert.Equal("Ba Ria", p.Name);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0, 0)]
    public void Distance_with_bad_coordinates_is_huge_not_nan(double a, double b, double c, double e)
        => Assert.Equal(double.MaxValue, LocationStabilizer.DistanceKm(a, b, c, e));

    [Fact]
    public void Distance_hcmc_to_vungtau_is_about_80km()
        => Assert.InRange(LocationStabilizer.DistanceKm(10.82, 106.63, 10.35, 107.08), 60, 80);

    // --- region matching ---
    [Theory]
    [InlineData("Ho Chi Minh City", "hcmc")]
    [InlineData("Thành phố Hồ Chí Minh", "hcmc")]
    [InlineData("Sài Gòn", "hcmc")]
    [InlineData("Thu Duc", "hcmc")]
    [InlineData("Ba Ria", "baria-vungtau")]
    [InlineData("Bà Rịa - Vũng Tàu", "baria-vungtau")]
    [InlineData("Vung Tau", "baria-vungtau")]
    [InlineData("Hà Nội", "hanoi")]
    [InlineData("Đà Nẵng", "danang-hoian")]
    [InlineData("Hoi An", "danang-hoian")]
    [InlineData("Tokyo", "tokyo")]
    [InlineData("Paris, France", "paris")]
    public void Region_names_match(string name, string id)
        => Assert.Equal(id, RegionCatalog.MatchName(name)?.Id);

    [Theory]
    [InlineData("Atlantis")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Unknown_names_do_not_match(string? name) => Assert.Null(RegionCatalog.MatchName(name));

    [Fact]
    public void Unknown_place_resolves_to_default_region()
    {
        var cfg = new AppConfig(); cfg.Normalize();
        var w = new WeatherSnapshot { Ok = true, LocationName = "Reykjavik", Latitude = 64.1, Longitude = -21.9 };
        Assert.Equal(RegionCatalog.Default.Id, RegionCatalog.Resolve(cfg, w).Id);
        Assert.Equal(RegionCatalog.Default.Id, RegionCatalog.Resolve(cfg, null).Id);
        Assert.Equal(RegionCatalog.Default.Id, RegionCatalog.Resolve(cfg, new WeatherSnapshot { Ok = false, LocationName = "Ho Chi Minh City" }).Id);
    }

    [Fact]
    public void Coordinates_resolve_when_name_is_unknown()
    {
        var cfg = new AppConfig(); cfg.Normalize();
        var w = new WeatherSnapshot { Ok = true, LocationName = "Phuong 12", Latitude = 10.80, Longitude = 106.65 };
        Assert.Equal("hcmc", RegionCatalog.Resolve(cfg, w).Id);
        var br = new WeatherSnapshot { Ok = true, LocationName = "?", Latitude = 10.50, Longitude = 107.17 };
        Assert.Equal("baria-vungtau", RegionCatalog.Resolve(cfg, br).Id);
    }

    [Fact]
    public void Manual_region_overrides_detected_place()
    {
        var cfg = new AppConfig { RegionMode = "tokyo" }; cfg.Normalize();
        var w = new WeatherSnapshot { Ok = true, LocationName = "Ho Chi Minh City" };
        Assert.Equal("tokyo", RegionCatalog.Resolve(cfg, w).Id);
    }
}
