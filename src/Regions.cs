using System.Globalization;
using System.Text;

namespace AudioTaskbar;

/// <summary>Vehicles that can roll along the road / float on the water lane.</summary>
public enum VehicleKind
{
    Motorbike, CityBus, Cyclo, Taxi, Bicycle, FlowerBike,
    FishingBoat, Sailboat, LanternBoat,
    Mamachari, ClassicTaxi, KeiTruck,
    VintageCar, TourBus, Car
}

public enum WaterKind { None, Sea, River, Lake }

/// <summary>A regional look: landmarks on the horizon, filler skyline, local traffic.</summary>
public sealed class RegionProfile
{
    public required string Id { get; init; }
    public required string NameVi { get; init; }
    /// <summary>Representative city (used when weather follows a manually picked region).</summary>
    public required string WeatherCity { get; init; }
    public double Lat { get; init; }
    public double Lon { get; init; }
    /// <summary>Landmark ids in priority order (most iconic first).</summary>
    public required string[] Landmarks { get; init; }
    public required string Filler { get; init; }
    public required (VehicleKind kind, float weight)[] Road { get; init; }
    public (VehicleKind kind, float weight)[] Water { get; init; } = Array.Empty<(VehicleKind, float)>();
    public WaterKind Water2 { get; init; } = WaterKind.None;
    public bool Petals { get; init; }
    public bool Lanterns { get; init; }
    public bool Palms { get; init; }
}

public static class RegionCatalog
{
    public static readonly RegionProfile Hcmc = new()
    {
        Id = "hcmc", NameVi = "TP. Hồ Chí Minh", WeatherCity = "Ho Chi Minh City", Lat = 10.7769, Lon = 106.7009,
        Landmarks = new[] { "landmark81", "bitexco", "notredame-sg", "baSonBridge" },
        Filler = "metro",
        Road = new[] { (VehicleKind.Motorbike, 6f), (VehicleKind.CityBus, 1.4f), (VehicleKind.Cyclo, 0.9f), (VehicleKind.Taxi, 1.6f) },
        Water = new[] { (VehicleKind.FishingBoat, 1f) },
        Water2 = WaterKind.River
    };

    public static readonly RegionProfile BariaVungTau = new()
    {
        Id = "baria-vungtau", NameVi = "Bà Rịa – Vũng Tàu", WeatherCity = "Vung Tau", Lat = 10.3460, Lon = 107.0843,
        Landmarks = new[] { "christ-vt", "lighthouse-vt", "palms-baisau" },
        Filler = "coast",
        Road = new[] { (VehicleKind.Motorbike, 5f), (VehicleKind.Bicycle, 2f), (VehicleKind.Car, 1f) },
        Water = new[] { (VehicleKind.FishingBoat, 2f), (VehicleKind.Sailboat, 1.4f) },
        Water2 = WaterKind.Sea, Palms = true
    };

    public static readonly RegionProfile Hanoi = new()
    {
        Id = "hanoi", NameVi = "Hà Nội", WeatherCity = "Hanoi", Lat = 21.0285, Lon = 105.8542,
        Landmarks = new[] { "turtle-tower", "longbien", "one-pillar", "thehuc" },
        Filler = "oldquarter",
        Road = new[] { (VehicleKind.Motorbike, 6f), (VehicleKind.Cyclo, 1.4f), (VehicleKind.FlowerBike, 1.4f), (VehicleKind.Taxi, 0.8f) },
        Water2 = WaterKind.Lake
    };

    public static readonly RegionProfile DanangHoiAn = new()
    {
        Id = "danang-hoian", NameVi = "Đà Nẵng – Hội An", WeatherCity = "Da Nang", Lat = 16.0544, Lon = 108.2022,
        Landmarks = new[] { "dragon-bridge", "golden-bridge", "hoian-lanterns" },
        Filler = "hoian",
        Road = new[] { (VehicleKind.Motorbike, 5f), (VehicleKind.Bicycle, 2f), (VehicleKind.Car, 0.8f) },
        Water = new[] { (VehicleKind.LanternBoat, 2f), (VehicleKind.FishingBoat, 0.6f) },
        Water2 = WaterKind.River, Lanterns = true
    };

    public static readonly RegionProfile Tokyo = new()
    {
        Id = "tokyo", NameVi = "Tokyo / Nhật Bản", WeatherCity = "Tokyo", Lat = 35.6762, Lon = 139.6503,
        Landmarks = new[] { "fuji", "tokyo-tower", "torii", "sakura" },
        Filler = "tokyo",
        Road = new[] { (VehicleKind.Mamachari, 2.4f), (VehicleKind.ClassicTaxi, 2f), (VehicleKind.KeiTruck, 1.6f), (VehicleKind.Car, 0.8f) },
        Petals = true
    };

    public static readonly RegionProfile Paris = new()
    {
        Id = "paris", NameVi = "Paris / Pháp", WeatherCity = "Paris", Lat = 48.8566, Lon = 2.3522,
        Landmarks = new[] { "eiffel", "arc-triomphe", "dome" },
        Filler = "haussmann",
        Road = new[] { (VehicleKind.Bicycle, 2.4f), (VehicleKind.VintageCar, 1.8f), (VehicleKind.TourBus, 1f), (VehicleKind.Car, 1.2f) },
        Water = new[] { (VehicleKind.Sailboat, 0.5f) },
        Water2 = WaterKind.River
    };

    public static readonly RegionProfile Default = new()
    {
        Id = "default", NameVi = "Mặc định – Quê hương & Đô thị", WeatherCity = "",
        Landmarks = new[] { "hills-bridge", "cozy-skyline" },
        Filler = "village",
        Road = new[] { (VehicleKind.Bicycle, 2f), (VehicleKind.Car, 2f), (VehicleKind.Motorbike, 1.5f) }
    };

    public static readonly RegionProfile[] All = { Hcmc, BariaVungTau, Hanoi, DanangHoiAn, Tokyo, Paris, Default };

    public static RegionProfile Get(string? id)
        => All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    public static string NormalizeMode(string? mode)
    {
        var m = (mode ?? "auto").Trim().ToLowerInvariant();
        if (m is "" or "auto" or "tu-dong" or "tự động") return "auto";
        m = m switch
        {
            "hochiminh" or "ho-chi-minh" or "saigon" or "sai-gon" or "tphcm" => "hcmc",
            "vungtau" or "vung-tau" or "baria" or "ba-ria" or "brvt" => "baria-vungtau",
            "ha-noi" => "hanoi",
            "danang" or "da-nang" or "hoian" or "hoi-an" => "danang-hoian",
            "japan" or "nhat-ban" => "tokyo",
            "france" or "phap" => "paris",
            _ => m
        };
        return All.Any(r => r.Id == m) ? m : "auto";
    }

    /// <summary>Resolve the active profile from config + detected weather location.</summary>
    public static RegionProfile Resolve(AppConfig cfg, WeatherSnapshot? weather)
    {
        var mode = NormalizeMode(cfg.RegionMode);
        if (mode != "auto") return Get(mode);
        // Auto: a custom weather city wins, then the detected location.
        if (!string.IsNullOrWhiteSpace(cfg.WeatherCity))
        {
            var byCity = MatchName(cfg.WeatherCity);
            if (byCity is not null) return byCity;
        }
        if (weather is { Ok: true })
        {
            var byName = MatchName(weather.LocationName);
            if (byName is not null) return byName;
            var byCoord = MatchCoords(weather.Latitude, weather.Longitude);
            if (byCoord is not null) return byCoord;
        }
        return Default;
    }

    public static RegionProfile? MatchName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Fold(name);
        if (n.Contains("ho chi minh") || n.Contains("saigon") || n.Contains("sai gon") || n.Contains("thu duc")) return Hcmc;
        if (n.Contains("vung tau") || n.Contains("ba ria") || n.Contains("phu my") || n.Contains("long hai")) return BariaVungTau;
        if (n.Contains("ha noi") || n.Contains("hanoi")) return Hanoi;
        if (n.Contains("da nang") || n.Contains("danang") || n.Contains("hoi an") || n.Contains("hoian")) return DanangHoiAn;
        if (n.Contains("tokyo") || n.Contains("japan") || n.Contains("nhat ban")) return Tokyo;
        if (n.Contains("paris") || n.Contains("france")) return Paris;
        return null;
    }

    public static RegionProfile? MatchCoords(double lat, double lon)
    {
        if (lat == 0 && lon == 0) return null;
        if (lat is >= 10.25 and <= 10.75 && lon is >= 107.0 and <= 107.6) return BariaVungTau;
        if (lat is >= 10.55 and <= 11.2 && lon is >= 106.35 and <= 107.0) return Hcmc;
        if (lat is >= 20.75 and <= 21.35 && lon is >= 105.45 and <= 106.1) return Hanoi;
        if (lat is >= 15.75 and <= 16.25 && lon is >= 107.85 and <= 108.45) return DanangHoiAn;
        if (lat is >= 35.3 and <= 36.0 && lon is >= 139.2 and <= 140.1) return Tokyo;
        if (lat is >= 48.6 and <= 49.1 && lon is >= 2.0 and <= 2.7) return Paris;
        return null;
    }

    // Vietnamese / Latin diacritics → ASCII. Explicit table because the app runs with
    // InvariantGlobalization, where string.Normalize(FormD) does not decompose (found by v5.3 tests).
    private const string FoldPairs = "àaáaảaãaạaăaằaắaẳaẵaặaâaầaấaẩaẫaậaäaåaÀaÁaẢaÃaẠaĂaẰaẮaẲaẴaẶaÂaẦaẤaẨaẪaẬaÄaÅaèeéeẻeẽeẹeêeềeếeểeễeệeëeÈeÉeẺeẼeẸeÊeỀeẾeỂeỄeỆeËeìiíiỉiĩiịiîiïiÌiÍiỈiĨiỊiÎiÏiòoóoỏoõoọoôoồoốoổoỗoộoơoờoớoởoỡoợoöoøoÒoÓoỎoÕoỌoÔoỒoỐoỔoỖoỘoƠoỜoỚoỞoỠoỢoÖoØoùuúuủuũuụuưuừuứuửuữuựuûuüuÙuÚuỦuŨuỤuƯuỪuỨuỬuỮuỰuÛuÜuỳyýyỷyỹyỵyÿyỲyÝyỶyỸyỴyŸyđdðdĐdÐdçcÇcñnÑn";
    private static readonly Dictionary<char, char> FoldMap = BuildFoldMap();

    private static Dictionary<char, char> BuildFoldMap()
    {
        var m = new Dictionary<char, char>();
        for (int i = 0; i + 1 < FoldPairs.Length; i += 2) m[FoldPairs[i]] = FoldPairs[i + 1];
        return m;
    }

    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch0 in s)
        {
            char ch = FoldMap.TryGetValue(ch0, out var f) ? f : ch0;
            if (ch is >= 'A' and <= 'Z') ch = (char)(ch + 32);
            if (ch > 127)
            {
                // anything else: try canonical decomposition, drop combining marks
                var d = ch.ToString().Normalize(NormalizationForm.FormD);
                foreach (var c in d)
                    if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString().Replace('-', ' ').Replace(',', ' ');
    }
}
