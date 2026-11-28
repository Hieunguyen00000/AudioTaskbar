using System.Drawing.Imaging;

namespace AudioTaskbar;

/// <summary>
/// AudioTaskbar.exe --render-portal out.png [--expand none|settings|sound|both] [--season autumn|cycle|real] [--weather rain|random]
/// [--region sydney] [--dirty 1] [--volumes 1] [--scale 1.5] [--screen-height 1032]. Draws the control panel off-screen with a fake host:
/// no tray, no taskbar, no real config. --scale lays the panel out for that display scale (1.25 = 125 %), exactly as on a monitor
/// with that DPI; --screen-height also fits the window into a work area that tall and draws the whole window (scroll bar included).
/// </summary>
internal static class PortalPreview
{
    private sealed class PreviewHost : IPortalHost
    {
        public PreviewHost(AppConfig cfg) { Config = cfg; }
        public AppConfig Config { get; }
        public bool EffectOn => true;
        public bool Fused => true;
        public string StatusText => "Đang chạy";
        public string HotkeyDisplay => "F8";
        public bool HotkeyOk => true;
        public UpdateCheckResult? LastUpdateResult => null;
        public bool UpdateBusy => false;
        public double UpdateProgress => 0;
        public string UpdateStage => "";
        public bool Dirty;
        public PortalInfo WeatherInfo
        {
            get
            {
                var w = new WeatherSnapshot { Ok = true, LocationName = "Ho Chi Minh City", TemperatureC = 29, WeatherCode = 3, IsDay = true, CloudCover = 60 };
                return PortalInfo.Build(w, Config, RegionCatalog.Resolve(Config, w));
            }
        }
        public bool HasUnsavedChanges => Dirty;
        public event Action? StateChanged { add { } remove { } }
        public void Mutate(Action<AppConfig> change, bool refreshWeather = false, bool hotkey = false, bool fusion = false) { change(Config); Config.Normalize(); Dirty = true; }
        public void SetEffect(bool on) { }
        public Task CheckAndUpdateAsync() => Task.CompletedTask;
        public void SaveConfig() { Dirty = false; }
        public void DiscardChanges() { Dirty = false; }
        public void ExitFromPortal() { }
    }

    public static void Run(string[] args)
    {
        string Get(string k, string d)
        {
            int i = Array.IndexOf(args, k);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : d;
        }
        var outPath = Get("--render-portal", "portal.png");
        var cfg = new AppConfig { Season = Get("--season", "summer"), WeatherPreview = Get("--weather", "live"), RegionMode = Get("--region", "hcmc") };
        if (Get("--volumes", "0") == "1") { cfg.SoundMasterVolume = 60; cfg.SoundVolumes["rain"] = 40; cfg.SoundVolumes["birds"] = 70; cfg.SoundVolumes["waves"] = 30; }
        cfg.Normalize();
        var host = new PreviewHost(cfg);
        using var f = new PortalForm(host)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false
        };
        string expand = Get("--expand", "none");
        f.SettingsExpanded = expand is "settings" or "both";
        f.SoundExpanded = expand is "sound" or "both";
        host.Dirty = Get("--dirty", "0") == "1";
        f.Show();
        f.SyncFromHost();
        float scale = float.Parse(Get("--scale", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (scale > 0) f.ApplyScale(scale);
        for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(30); }
        int screenH = int.Parse(Get("--screen-height", "0"));
        if (screenH > 0)
        {
            f.FitTo(new Rectangle(-32000, -32000, 4000, screenH), reposition: false);
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(30); }
            using var win = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(win, new Rectangle(0, 0, f.Width, f.Height));
            win.Save(outPath, ImageFormat.Png);
            File.WriteAllText(outPath + ".txt", $"scale={f.UiScale} window={f.Width}x{f.Height} client={f.ClientSize.Width}x{f.ClientSize.Height} " +
                $"content={f.ContentWidthNeeded} scrolling={f.Scrolling} card={f.CardWidthPx} labelCol={f.LabelColumnPx}\n");
            f.ForceClose();
            return;
        }
        // Draw the content root (not the window) so a tall panel is captured at full height, whatever the screen size.
        var root = f.Content;
        root.PerformLayout();
        using var bmp = new Bitmap(root.Width + 28, root.Height + 24);
        using (var g = Graphics.FromImage(bmp)) g.Clear(f.BackColor);
        using (var inner = new Bitmap(root.Width, root.Height))
        {
            root.DrawToBitmap(inner, new Rectangle(0, 0, root.Width, root.Height));
            using var g2 = Graphics.FromImage(bmp);
            g2.DrawImage(inner, 14, 12);
        }
        bmp.Save(outPath, ImageFormat.Png);
        File.WriteAllText(outPath + ".txt", $"scale={f.UiScale} content={root.Width}x{root.Height} window={f.Width}x{f.Height} card={f.CardWidthPx} labelCol={f.LabelColumnPx} expand={expand}\n");
        f.ForceClose();
    }
}
