namespace AudioTaskbar;

/// <summary>What the control panel needs from the running app (also implemented by a fake for headless previews).</summary>
internal interface IPortalHost
{
    /// <summary>The live configuration (edits preview immediately; they are written to disk only by <see cref="SaveConfig"/>).</summary>
    AppConfig Config { get; }
    bool EffectOn { get; }
    bool Fused { get; }
    string StatusText { get; }
    string HotkeyDisplay { get; }
    bool HotkeyOk { get; }
    UpdateCheckResult? LastUpdateResult { get; }
    bool UpdateBusy { get; }
    double UpdateProgress { get; }
    string UpdateStage { get; }
    PortalInfo WeatherInfo { get; }
    bool HasUnsavedChanges { get; }
    event Action? StateChanged;

    void Mutate(Action<AppConfig> change, bool refreshWeather = false, bool hotkey = false, bool fusion = false);
    void SetEffect(bool on);
    /// <summary>The single update command: check, and install when a newer version exists.</summary>
    Task CheckAndUpdateAsync();
    /// <summary>Write the current settings to config.json.</summary>
    void SaveConfig();
    /// <summary>Throw away unsaved edits and go back to what is in config.json.</summary>
    void DiscardChanges();
    void ExitFromPortal();
}

/// <summary>Weather and location as shown in the control panel (the taskbar badge was removed in v5.4).</summary>
internal sealed record PortalInfo(string Location, string Temperature, string Condition, string Region, string Season, string Showing)
{
    public static PortalInfo Build(WeatherSnapshot? w, AppConfig cfg, RegionProfile region)
    {
        bool ok = w is { Ok: true };
        string location = ok ? SceneClock.ShortPlace(w!.LocationName) : "Chưa xác định vị trí";
        string temp = ok ? w!.TemperatureText(cfg.WeatherUnits == "fahrenheit") : "—";
        string cond = ok ? w!.ConditionVi : "chưa có dữ liệu";
        string season = Seasons.NameVi(Seasons.Parse(cfg.EffectiveSeason)) + (cfg.SeasonCycleEnabled ? " (tự đổi)" : "");
        string showing = NameOfWeather(cfg.EffectiveWeather, cond) + (cfg.WeatherCycleMode != WeatherCycle.Off ? " (tự đổi)" : "");
        return new PortalInfo(location, temp, cond, region.NameVi, season, showing);
    }

    public static string NameOfWeather(string id, string liveCondition) => id switch
    {
        "sunny" => "Nắng đẹp", "cloudy" => "Mây trôi", "rain" => "Mưa rào", "storm" => "Giông bão",
        "snow" => "Tuyết rơi", "night" => "Đêm sao", _ => "Thực tế (" + liveCondition + ")"
    };

    public string Line1 => $"Vị trí: {Location}  ·  {Temperature}  ·  {Condition}";
    public string Line2 => $"Cảnh vật: {Region}  ·  {Season}  ·  Đang hiển thị: {Showing}";
}
