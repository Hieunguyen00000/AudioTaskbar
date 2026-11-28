using System.Drawing.Imaging;
using System.Globalization;

namespace AudioTaskbar;

/// <summary>
/// Headless preview: renders exactly the overlay pixels a machine would show,
/// for a given bar size + measured cut-outs, without touching the taskbar.
/// (--train-phase / --plane-phase 0..1 pin the lifecycle of the train / airplane; --airplane 0 hides it)
/// AudioTaskbar.exe --render out.png --size 1920x60 --cutouts 8-198,516-1403,1653-1920
///     --region hcmc --temp 27 --code 3 --day 0 --place "Ho Chi Minh City" --season autumn --padding 0 --t 40 --preview live
/// </summary>
internal static class RenderPreview
{
    public static void Run(string[] args)
    {
        string Get(string k, string d)
        {
            int i = Array.IndexOf(args, k);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : d;
        }
        var outPath = Get("--render", "preview.png");
        var size = Get("--size", "1920x48").Split('x');
        int w = int.Parse(size[0]), h = int.Parse(size[1]);
        var cfg = new AppConfig
        {
            RegionMode = Get("--region", "auto"),
            WeatherPreview = Get("--preview", "live"),
            Season = Get("--season", "summer"),
            ShowIconPadding = Get("--padding", "1") == "1",
            TrainCrossingMinutes = float.Parse(Get("--train", "1"), CultureInfo.InvariantCulture),
            ShowAirplane = Get("--airplane", "1") == "1",
            AirplaneCycleMinutes = float.Parse(Get("--airplane-min", "3"), CultureInfo.InvariantCulture),
            TimeMode = Get("--timemode", "cycle")
        };
        cfg.Normalize();
        // --hour 17.75 pins the scene clock (sun/moon position, sky palette) for a preview frame.
        var hourArg = Get("--hour", "");
        if (hourArg.Length > 0) SceneClock.HourOverride = float.Parse(hourArg, CultureInfo.InvariantCulture);
        // --train-phase 0.42 / --plane-phase 0.5 pin the lifecycle position (0..1) of the vehicles, to preview each phase.
        var tp = Get("--train-phase", ""); var pp = Get("--plane-phase", "");
        DioramaRenderer.DebugTrainCycle = tp.Length > 0 ? double.Parse(tp, CultureInfo.InvariantCulture) : null;
        DioramaRenderer.DebugPlaneCycle = pp.Length > 0 ? double.Parse(pp, CultureInfo.InvariantCulture) : null;
        var weather = new WeatherSnapshot
        {
            Ok = true,
            Source = "preview",
            LocationName = Get("--place", "Ho Chi Minh City"),
            TemperatureC = float.Parse(Get("--temp", "27"), CultureInfo.InvariantCulture),
            WeatherCode = int.Parse(Get("--code", "3")),
            IsDay = Get("--day", "1") == "1",
            CloudCover = float.Parse(Get("--cloud", "60"), CultureInfo.InvariantCulture),
            Latitude = 10.77, Longitude = 106.7
        };
        var regions = new TaskbarRegions { Bounds = new Rectangle(0, 0, w, h) };
        foreach (var part in Get("--cutouts", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var lr = part.Split('-');
            int l = int.Parse(lr[0]), r = int.Parse(lr[1]);
            regions.Cutouts.Add(Rectangle.FromLTRB(l, 0, r, h));
        }
        regions.Signature = Get("--cutouts", "");
        var region = RegionCatalog.Resolve(cfg, weather);
        float t0 = float.Parse(Get("--t", "30"), CultureInfo.InvariantCulture);
        int frames = int.Parse(Get("--frames", "240"));
        using var ow = new OverlayWindow();
        var analyzer = new Analyzer();
        Bitmap? last = null;
        for (int i = 0; i < frames; i++)
        {
            var ctx = new FrameContext
            {
                Cfg = cfg, Analyzer = analyzer, Weather = weather, Regions = regions,
                Time = t0 + i / 30f, Fused = Get("--fused", "0") == "1", BehindOk = Get("--fused", "0") == "1",
                ShowWeather = Get("--hud", "0") == "1", Region = region
            };
            last?.Dispose();
            last = ow.RenderOffscreen(w, h, ctx);
        }
        last!.Save(outPath, ImageFormat.Png);
        File.WriteAllText(outPath + ".txt", $"region={region.Id} layout={ow.LastLayoutInfo}\n");
    }
}
