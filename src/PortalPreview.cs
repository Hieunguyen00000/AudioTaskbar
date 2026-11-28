using System.Drawing.Imaging;

namespace AudioTaskbar;

/// <summary>
/// AudioTaskbar.exe --render-portal out.png [--expand none|settings|sound|both] [--season autumn] [--weather rain] [--region sydney]
/// [--dirty 1] [--volumes 1]. Draws the control panel off-screen with a fake host: no tray, no taskbar, no real config.
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
        if (Get("--cycle", "0") == "1") { cfg.WeatherCycleMode = "random"; cfg.SeasonCycleEnabled = true; }
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
        for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(30); }
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
        File.WriteAllText(outPath + ".txt", $"content={root.Width}x{root.Height} window={f.Width}x{f.Height} expand={expand}\n");
        f.ForceClose();
    }
}
