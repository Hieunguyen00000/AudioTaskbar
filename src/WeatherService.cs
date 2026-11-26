using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace AudioTaskbar;

public sealed class WeatherSnapshot
{
    public bool Ok { get; init; }
    public string Source { get; init; } = "";
    public string LocationName { get; init; } = "";
    public string Country { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public float TemperatureC { get; init; }
    public int WeatherCode { get; init; }
    public bool IsDay { get; init; } = true;
    public float Precipitation { get; init; }
    public float CloudCover { get; init; }
    public DateTime FetchedAt { get; init; } = DateTime.Now;
    public string? Error { get; init; }

    public string ConditionVi => WeatherCodes.DescribeVi(WeatherCode, IsDay);
    public string ConditionEn => WeatherCodes.DescribeEn(WeatherCode, IsDay);
    public WeatherKind Kind => WeatherCodes.KindOf(WeatherCode, IsDay);
    public string TemperatureText(bool fahrenheit)
        => fahrenheit
            ? $"{Math.Round(TemperatureC * 9f / 5f + 32f):0}°F"
            : $"{Math.Round(TemperatureC):0}°C";
}

public enum WeatherKind
{
    Clear, Cloudy, Fog, Rain, Snow, Storm, Night
}

public static class WeatherCodes
{
    public static WeatherKind KindOf(int code, bool isDay)
    {
        if (code is 95 or 96 or 99) return WeatherKind.Storm;
        if (code is 71 or 73 or 75 or 77 or 85 or 86) return WeatherKind.Snow;
        if (code is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82)
            return WeatherKind.Rain;
        if (code is 45 or 48) return WeatherKind.Fog;
        if (code is 1 or 2 or 3) return WeatherKind.Cloudy;
        if (!isDay) return WeatherKind.Night;
        return WeatherKind.Clear;
    }

    public static string DescribeVi(int code, bool isDay) => code switch
    {
        0 => isDay ? "Nắng" : "Đêm quang",
        1 => isDay ? "Ít mây" : "Đêm ít mây",
        2 => "Mây rải rác",
        3 => "Nhiều mây",
        45 or 48 => "Sương mù",
        51 or 53 or 55 or 56 or 57 => "Mưa phùn",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Mưa",
        71 or 73 or 75 or 77 or 85 or 86 => "Tuyết",
        95 or 96 or 99 => "Dông",
        _ => "Thời tiết"
    };

    public static string DescribeEn(int code, bool isDay) => code switch
    {
        0 => isDay ? "Clear" : "Clear night",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Rain",
        71 or 73 or 75 or 77 or 85 or 86 => "Snow",
        95 or 96 or 99 => "Storm",
        _ => "Weather"
    };
}

public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly object _lock = new();
    private WeatherSnapshot? _latest;
    private CancellationTokenSource? _loopCts;
    private int _refreshMinutes = 15;

    public event Action<WeatherSnapshot>? Updated;

    public WeatherSnapshot? Latest
    {
        get { lock (_lock) return _latest; }
    }

    public WeatherService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AudioTaskbar/2.0 (desktop weather)");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioTaskbar");
        Directory.CreateDirectory(dir);
        _cachePath = Path.Combine(dir, "weather-cache.json");
        _latest = LoadCache();
    }

    public void Start(AppConfig cfg)
    {
        _refreshMinutes = cfg.WeatherRefreshMinutes;
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        _ = Task.Run(() => LoopAsync(cfg, token), token);
    }

    public void RefreshNow(AppConfig cfg)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var snap = await FetchAsync(cfg, CancellationToken.None).ConfigureAwait(false);
                SetLatest(snap);
            }
            catch (Exception ex)
            {
                var stale = Latest;
                SetLatest(new WeatherSnapshot
                {
                    Ok = stale?.Ok ?? false,
                    Source = stale?.Source ?? "",
                    LocationName = stale?.LocationName ?? "",
                    Country = stale?.Country ?? "",
                    Latitude = stale?.Latitude ?? 0,
                    Longitude = stale?.Longitude ?? 0,
                    TemperatureC = stale?.TemperatureC ?? 0,
                    WeatherCode = stale?.WeatherCode ?? 0,
                    IsDay = stale?.IsDay ?? true,
                    Precipitation = stale?.Precipitation ?? 0,
                    CloudCover = stale?.CloudCover ?? 0,
                    FetchedAt = stale?.FetchedAt ?? DateTime.Now,
                    Error = ex.Message
                });
            }
        });
    }

    private async Task LoopAsync(AppConfig cfg, CancellationToken token)
    {
        // Always try once immediately (even if we have a cache).
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snap = await FetchAsync(cfg, token).ConfigureAwait(false);
                SetLatest(snap);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var stale = Latest;
                if (stale is null || !stale.Ok)
                {
                    SetLatest(new WeatherSnapshot { Ok = false, Error = ex.Message, FetchedAt = DateTime.Now });
                }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(5, _refreshMinutes)), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<WeatherSnapshot> FetchAsync(AppConfig cfg, CancellationToken token)
    {
        double lat, lon;
        string name, country, source;

        if (cfg.WeatherLatitude is { } clat && cfg.WeatherLongitude is { } clon)
        {
            lat = clat; lon = clon;
            name = string.IsNullOrWhiteSpace(cfg.WeatherCity) ? $"{lat:0.##}, {lon:0.##}" : cfg.WeatherCity;
            country = "";
            source = "config-coords";
        }
        else if (!string.IsNullOrWhiteSpace(cfg.WeatherCity))
        {
            var geo = await GeocodeAsync(cfg.WeatherCity, token).ConfigureAwait(false);
            lat = geo.lat; lon = geo.lon; name = geo.name; country = geo.country;
            source = "config-city";
        }
        else
        {
            var ip = await LocateByIpAsync(token).ConfigureAwait(false);
            lat = ip.lat; lon = ip.lon; name = ip.name; country = ip.country;
            source = ip.source;
        }

        var url =
            "https://api.open-meteo.com/v1/forecast?latitude=" +
            lat.ToString(CultureInfo.InvariantCulture) + "&longitude=" +
            lon.ToString(CultureInfo.InvariantCulture) +
            "&current=temperature_2m,weather_code,is_day,precipitation,cloud_cover,wind_speed_10m" +
            "&timezone=auto";
        using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var current = doc.RootElement.GetProperty("current");
        return new WeatherSnapshot
        {
            Ok = true,
            Source = source,
            LocationName = name,
            Country = country,
            Latitude = lat,
            Longitude = lon,
            TemperatureC = current.GetProperty("temperature_2m").GetSingle(),
            WeatherCode = current.GetProperty("weather_code").GetInt32(),
            IsDay = current.TryGetProperty("is_day", out var dayEl) && dayEl.GetInt32() == 1,
            Precipitation = current.TryGetProperty("precipitation", out var pr) ? pr.GetSingle() : 0,
            CloudCover = current.TryGetProperty("cloud_cover", out var cc) ? cc.GetSingle() : 0,
            FetchedAt = DateTime.Now
        };
    }

    private async Task<(double lat, double lon, string name, string country)> GeocodeAsync(string city, CancellationToken token)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(city) +
                  "&count=1&language=vi&format=json";
        using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new InvalidOperationException("Không tìm thấy thành phố: " + city);
        var r = results[0];
        var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? city : city;
        var admin = r.TryGetProperty("admin1", out var a) ? a.GetString() : null;
        var country = r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
        if (!string.IsNullOrWhiteSpace(admin) && !string.Equals(admin, name, StringComparison.OrdinalIgnoreCase))
            name = name + ", " + admin;
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name, country);
    }

    private async Task<(double lat, double lon, string name, string country, string source)> LocateByIpAsync(CancellationToken token)
    {
        Exception? last = null;
        foreach (var (url, source) in new[]
        {
            ("https://ipapi.co/json/", "ipapi.co"),
            ("https://get.geojs.io/v1/ip/geo.json", "geojs"),
            ("http://ip-api.com/json/?fields=status,country,regionName,city,lat,lon", "ip-api")
        })
        {
            try
            {
                using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                var e = doc.RootElement;
                if (e.TryGetProperty("error", out var err) && err.ValueKind is JsonValueKind.True or JsonValueKind.String)
                    continue;
                if (e.TryGetProperty("status", out var st) && st.GetString() is "fail")
                    continue;
                double lat = ReadDouble(e, "latitude", "lat");
                double lon = ReadDouble(e, "longitude", "lon");
                if (lat == 0 && lon == 0) continue;
                string city = ReadString(e, "city") ?? "";
                string region = ReadString(e, "region", "regionName", "region_name") ?? "";
                string country = ReadString(e, "country_name", "country") ?? "";
                var name = !string.IsNullOrEmpty(city) ? city : region;
                if (string.IsNullOrEmpty(name)) name = $"{lat:0.##}, {lon:0.##}";
                return (lat, lon, name, country, source);
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("Không xác định được vị trí: " + (last?.Message ?? "offline"));
    }

    private static double ReadDouble(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            if (e.TryGetProperty(n, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String &&
                    double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                    return s;
            }
        }
        return 0;
    }

    private static string? ReadString(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            if (e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        return null;
    }

    private void SetLatest(WeatherSnapshot snap)
    {
        lock (_lock) _latest = snap;
        try
        {
            if (snap.Ok)
                File.WriteAllText(_cachePath, JsonSerializer.Serialize(snap, ConfigStore.JsonOptions));
        }
        catch { /* cache is optional */ }
        try { Updated?.Invoke(snap); } catch { }
    }

    private WeatherSnapshot? LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var snap = JsonSerializer.Deserialize<WeatherSnapshot>(File.ReadAllText(_cachePath), ConfigStore.JsonOptions);
            return snap is { Ok: true } ? snap : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _http.Dispose();
    }
}
