using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AudioTaskbar;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly System.Windows.Forms.Timer _layoutTimer;
    private readonly AudioCapture _capture = new();
    private readonly Analyzer _analyzer = new();
    private readonly WeatherService _weather = new();
    private readonly TaskbarTransparency _transparency = new();
    private readonly TaskbarTapHost _tap = new();
    private readonly EventWaitHandle _cmdFusionOn = new(false, EventResetMode.AutoReset, @"Local\AudioTaskbar.Cmd.FusionOn");
    private readonly EventWaitHandle _cmdFusionOff = new(false, EventResetMode.AutoReset, @"Local\AudioTaskbar.Cmd.FusionOff");
    private readonly EventWaitHandle _cmdToggle = new(false, EventResetMode.AutoReset, @"Local\AudioTaskbar.Cmd.Toggle");
    private readonly EventWaitHandle _cmdPreview = new(false, EventResetMode.AutoReset, @"Local\AudioTaskbar.Cmd.Preview");
    // stop.ps1 signals this so the app exits cleanly (TAP detached from Explorer) instead of being killed.
    private readonly EventWaitHandle _cmdExit = new(false, EventResetMode.AutoReset, @"Local\AudioTaskbar.Cmd.Exit");

    private readonly List<OverlayWindow> _overlays = new();
    private readonly Dictionary<IntPtr, TaskbarRegions> _regions = new();
    private readonly object _configLock = new();
    private readonly string _configPath;
    private readonly FileSystemWatcher _watcher;
    private readonly string _logPath;
    private readonly ShellNotifyWindow _shellNotify;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private AppConfig _cfg;
    private float[] _sampleBuf = new float[2048];
    private int _watchdogCounter;
    private int _zCounter;
    private int _probePhase;
    private Color? _stockPixel;
    private bool _paused;
    private bool _hotkeyRegistered;
    private string _hotkeyDisplay = "F8";
    private DateTime _lastToggleUtc = DateTime.MinValue;
    private Icon? _iconOff;
    private bool _exiting;
    private bool _fused;
    private bool _behindFailed;
    private int _behindFailStreak;
    private float _idle; // 0 active, 1 idle
    private Icon? _icon;
    private bool _savingConfig;
    // v5
    private readonly TaskbarLayoutProbe _layout;
    private readonly List<BackdropWindow> _backdrops = new();
    private int _tapFailedPid;
    private string? _tapBlockedReason;
    private bool _attachInFlight;
    private string _regionLogged = "";
    private volatile bool _layoutDirty;

    public TrayApp()
    {
        Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioTaskbar"));
        _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioTaskbar", "app.log");
        var loaded = ConfigStore.LoadPreferred();
        _configPath = loaded.path;
        _cfg = loaded.cfg;
        if (loaded.note == "migrated-fusion-opt-in")
            Log("Config migrated to " + _configPath + ". Fusion nền taskbar is opt-in (disabled by default after Explorer restarted at 10:54).");
        else if (loaded.note is not null)
            Log("Config: " + loaded.note + " path=" + _configPath);
        if (!string.IsNullOrWhiteSpace(_cfg.Preset) && _cfg.Preset != "custom")
        {
            if (_cfg.Preset == "windows-accent")
                Presets.Apply(_cfg, "windows-accent");
        }
        // v4: temporarily retire spectrum/waveform and the WASAPI engine.
        _cfg.ShowSpectrum = false;
        _cfg.ShowWaveform = false;
        _cfg.EnableAudio = false;
        if (string.IsNullOrWhiteSpace(_cfg.WeatherPreview)) _cfg.WeatherPreview = "live";
        _cfg.Normalize();
        _layout = new TaskbarLayoutProbe(Log);
        _layout.Changed += () => _layoutDirty = true;
        Log("Start v5.2.0. Config=" + _configPath + " preview=" + _cfg.WeatherPreview + " hotkey=" + _cfg.Hotkey
            + " blockWallpaper=" + _cfg.BlockWallpaper
            + " train=" + _cfg.ShowTrain + "(" + _cfg.TrainCrossingMinutes + " min) traffic=" + _cfg.ShowCyclist
            + " region=" + _cfg.RegionMode + " hud=" + _cfg.ShowWeatherHud
            + " time=" + _cfg.TimeMode + (_cfg.TimeMode == "cycle" ? "(" + _cfg.DayCycleMinutes + " min/day, now " + SceneClock.VirtualHour(_cfg, DateTime.Now).ToString("0.00") + "h)" : "")
            + " dpiCtx=PerMonitorV2 sac=" + (TaskbarTapHost.DetectTapBlocker() ?? "off"));
        _tap.ReadyTimeoutSeconds = _cfg.TapTimeoutSeconds;

        _icon = CreateIcon(on: true);
        _iconOff = CreateIcon(on: false);
        _tray = new NotifyIcon
        {
            Icon = _icon ?? SystemIcons.Application,
            Visible = true,
            Text = "Audio Taskbar (F8)"
        };
        _tray.ContextMenuStrip = BuildMenu();
        _tray.DoubleClick += (_, _) => ToggleEffect();

        _capture.StatusChanged += msg =>
        {
            Log(msg);
            UpdateTip();
        };
        if (_cfg.EnableAudio)
        {
            _capture.Start();
            _analyzer.Configure(_cfg, _capture.SampleRate);
        }
        else
            Log("Audio engine idle (v4 diorama — spectrum/waveform off).");

        _weather.Updated += snap =>
        {
            Log(snap.Ok
                ? $"Weather: {snap.LocationName} {snap.TemperatureText(false)} {snap.ConditionEn} (via {snap.Source})"
                : "Weather failed: " + snap.Error);
            UpdateTip();
        };
        _weather.Start(_cfg); // weather drives sky, scenery region and the (toggleable) HUD

        _frameTimer = new System.Windows.Forms.Timer { Interval = Math.Max(8, 1000 / _cfg.Fps) };
        _frameTimer.Tick += (_, _) => OnFrame();
        _frameTimer.Start();

        _layoutTimer = new System.Windows.Forms.Timer { Interval = 750 };
        _layoutTimer.Tick += (_, _) => SyncOverlays();
        _layoutTimer.Start();

        _shellNotify = new ShellNotifyWindow(
            () =>
            {
                Log("TaskbarCreated — re-syncing.");
                _behindFailed = false;
                _behindFailStreak = 0;
                _tapFailedPid = 0; // new Explorer instance: TAP may be retried once
                BeginInvoke(() =>
                {
                    if (_paused) return;
                    SyncOverlays();
                    if (WantsSealedTaskbar())
                        TryEnableFusion(initial: false);
                });
            },
            () => BeginInvoke(ToggleEffect));
        RegisterConfiguredHotkey();

        SyncOverlays();
        if (WantsSealedTaskbar())
            TryEnableFusion(initial: true);

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_configPath) ?? ".", Path.GetFileName(_configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        _watcher.Changed += (_, _) => { if (!_savingConfig) BeginReload(); };
        _watcher.Created += (_, _) => { if (!_savingConfig) BeginReload(); };
        _watcher.Renamed += (_, _) => { if (!_savingConfig) BeginReload(); };
        _watcher.EnableRaisingEvents = File.Exists(_configPath);

        SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            try { if (!_exiting) BeginInvoke(SyncOverlays); } catch { }
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var pause = new ToolStripMenuItem("Bật/tắt hiệu ứng (F8)");
        pause.Click += (_, _) => ToggleEffect();
        menu.Items.Add(pause);

        var scene = new ToolStripMenuItem("Hoạt ảnh");
        scene.DropDownItems.Add(MakeToggle("Tàu hỏa (" + _cfg.TrainCrossingMinutes.ToString("0.#") + " phút/lượt)", () => _cfg.ShowTrain, v => { _cfg.ShowTrain = v; Persist(); }));
        scene.DropDownItems.Add(MakeToggle("Giao thông đặc trưng khu vực", () => _cfg.ShowCyclist, v => { _cfg.ShowCyclist = v; Persist(); }));
        scene.DropDownItems.Add(MakeToggle("Chim, đèn phố, cánh hoa", () => _cfg.ShowAmbientDetails, v => { _cfg.ShowAmbientDetails = v; Persist(); }));
        scene.DropDownItems.Add(MakeToggle("Hiệu ứng thời tiết (mưa, mây, sấm, sương)", () => _cfg.ShowWeatherEffects, v => { _cfg.ShowWeatherEffects = v; Persist(); }));
        menu.Items.Add(scene);

        var timeMenu = new ToolStripMenuItem("Ngày & đêm (mặt trời / mặt trăng)");
        void addTime(string id, Func<string> label)
        {
            var it = new ToolStripMenuItem(label());
            it.Click += (_, _) =>
            {
                _cfg.TimeMode = id;
                _cfg.Normalize();
                Persist();
                Log("Time mode: " + _cfg.TimeMode + " (scene hour " + SceneClock.VirtualHour(_cfg, DateTime.Now).ToString("0.00") + ")");
            };
            it.Paint += (_, _) => { try { it.Checked = _cfg.TimeMode == id; } catch { } };
            timeMenu.DropDownItems.Add(it);
        }
        addTime("cycle", () => "Chu kỳ " + _cfg.DayCycleMinutes.ToString("0.#") + " phút (1 ngày)");
        addTime("real", () => "Theo giờ thật");
        menu.Items.Add(timeMenu);

        var region = new ToolStripMenuItem("Khu vực & Cảnh vật");
        void addRegion(string id, string label)
        {
            var it = new ToolStripMenuItem(label);
            it.Click += (_, _) =>
            {
                _cfg.RegionMode = id;
                _cfg.Normalize();
                Persist();
                Log("Region mode: " + _cfg.RegionMode);
                if (_cfg.WeatherFollowsRegion) _weather.RefreshNow(_cfg);
            };
            it.Paint += (_, _) => { try { it.Checked = _cfg.RegionMode == id; } catch { } };
            region.DropDownItems.Add(it);
        }
        addRegion("auto", "Tự động theo máy");
        region.DropDownItems.Add(new ToolStripSeparator());
        addRegion("hcmc", "TP. Hồ Chí Minh");
        addRegion("baria-vungtau", "Bà Rịa – Vũng Tàu");
        addRegion("hanoi", "Hà Nội");
        addRegion("danang-hoian", "Đà Nẵng – Hội An");
        addRegion("tokyo", "Tokyo / Nhật Bản");
        addRegion("paris", "Paris / Pháp");
        addRegion("default", "Mặc định (quê hương & đô thị)");
        region.DropDownItems.Add(new ToolStripSeparator());
        region.DropDownItems.Add(MakeToggle("Thời tiết theo khu vực đã chọn", () => _cfg.WeatherFollowsRegion, v =>
        {
            _cfg.WeatherFollowsRegion = v;
            Persist();
            _weather.RefreshNow(_cfg);
        }));
        menu.Items.Add(region);

        var preview = new ToolStripMenuItem("Xem trước thời tiết");
        void addPreview(string id, string label)
        {
            var it = new ToolStripMenuItem(label);
            it.Click += (_, _) => { _cfg.WeatherPreview = id; _cfg.Normalize(); Persist(); Log("Weather preview: " + _cfg.WeatherPreview); };
            it.Paint += (_, _) => { try { it.Checked = _cfg.WeatherPreview == id; } catch { } };
            preview.DropDownItems.Add(it);
        }
        addPreview("live", "Thực tế");
        addPreview("sunny", "Nắng đẹp");
        addPreview("cloudy", "Mây trôi");
        addPreview("rain", "Mưa rào");
        addPreview("storm", "Giông bão");
        addPreview("snow", "Tuyết rơi");
        addPreview("night", "Đêm sao");
        menu.Items.Add(preview);

        menu.Items.Add(MakeToggle("Hiện thông tin thời tiết & địa điểm", () => _cfg.ShowWeatherHud, v =>
        {
            _cfg.ShowWeatherHud = v;
            Persist();
            Log("Weather HUD: " + (v ? "on" : "off"));
        }));
        var refresh = new ToolStripMenuItem("Làm mới thời tiết & vị trí");
        refresh.Click += (_, _) =>
        {
            Log("Weather refresh requested.");
            _weather.RefreshNow(_cfg);
        };
        menu.Items.Add(refresh);

        var fusion = MakeToggle("Chặn ảnh nền desktop",
            () => _cfg.BlockWallpaper || (_cfg.FuseIntoTaskbar && _cfg.MakeTaskbarTransparent),
            v =>
            {
                _cfg.BlockWallpaper = v;
                _cfg.FuseIntoTaskbar = v;
                _cfg.MakeTaskbarTransparent = v;
                Persist();
                if (v) TryEnableFusion(initial: false, force: true);
                else DisableFusion();
            });
        menu.Items.Add(fusion);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tải lại cấu hình", null, (_, _) => ReloadConfig());
        menu.Items.Add("Mở thư mục cấu hình", null, (_, _) =>
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir is not null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => ExitApp());

        menu.Opening += (_, _) =>
        {
            pause.Text = _paused
                ? $"Bật hiệu ứng ({_hotkeyDisplay})"
                : $"Tắt hiệu ứng ({_hotkeyDisplay})";
            pause.Checked = !_paused;
        };
        return menu;
    }

    private ToolStripMenuItem MakeIntensity(string label, float value)
    {
        var item = new ToolStripMenuItem(label) { Tag = value };
        item.Click += (_, _) =>
        {
            _cfg.Intensity = value;
            _cfg.Normalize();
            Persist();
        };
        return item;
    }

    private ToolStripMenuItem MakeToggle(string label, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(label);
        item.Click += (_, _) => set(!get());
        item.DropDownOpening += (_, _) => { };
        // refresh check on parent opening is enough if we hook menu Opening for leaves:
        item.OwnerChanged += (_, _) => { };
        var parent = item;
        // We'll update in a paint/open via Click only; also set CheckOnClick style:
        item.CheckOnClick = false;
        item.Paint += (_, _) => { try { item.Checked = get(); } catch { } };
        return item;
    }

    private void ApplyPreset(string id)
    {
        lock (_configLock)
        {
            Presets.Apply(_cfg, id);
        }
        Persist();
        Log("Preset: " + id);
    }

    private void Persist()
    {
        try
        {
            _savingConfig = true;
            AppConfig copy;
            lock (_configLock)
            {
                _cfg.Normalize();
                copy = _cfg;
            }
            ConfigStore.Save(_configPath, copy);
        }
        catch (Exception ex) { Log("Save config failed: " + ex.Message); }
        finally
        {
            // Delay so the watcher ignores our own write.
            Task.Delay(400).ContinueWith(_ => _savingConfig = false);
        }
    }

    private void ToggleEffect()
    {
        // RegisterHotKey + synthesized key events can occasionally fire twice
        // in the same press (keydown/keyup). Ignore a second toggle within 350ms.
        var now = DateTime.UtcNow;
        if ((now - _lastToggleUtc).TotalMilliseconds < 350) return;
        _lastToggleUtc = now;
        SetEffectEnabled(_paused);
    }

    private void SetEffectEnabled(bool on)
    {
        if (_exiting) return;
        if (on && !_paused) { UpdateTip(); return; }
        if (!on && _paused) { UpdateTip(); return; }
        _paused = !on;
        if (!on)
        {
            foreach (var o in _overlays) o.ClearAndHide();
            HideBackdrops();
            // Do not RequestRestore / Detach on F8 — toggling TAP fills mid-session
            // has recycled Explorer on 26300. TAP stays patched and attached; we
            // only hide the visualizer. Icons remain; wallpaper may peek through
            // the transparent bar while the effect is off. Exit still restores.
            if (_tap.IsAttached || _fused)
            {
                Log("Effect OFF: hiding visualizer, TAP fills left in place (no restore).");
                _fused = false;
                _transparency.Effective = false;
            }
            ApplyTrayIcon(on: false);
            UpdateTip();
            NotifyBalloon("Đã tắt hiệu ứng (" + _hotkeyDisplay + ")");
            Log("Effect OFF via " + _hotkeyDisplay + ".");
        }
        else
        {
            ApplyTrayIcon(on: true);
            UpdateTip();
            _behindFailed = false;
            _behindFailStreak = 0;
            if (_tap.IsAttached)
            {
                // Fills were left patched on OFF, so just slip back under the taskbar.
                _fused = true;
                _transparency.Effective = true;
                Log("Effect ON: TAP still attached — showing visualizer behind the taskbar.");
                foreach (var o in _overlays) o.ReassertZ(true);
                WatchBehindZ();
            }
            else if (WantsSealedTaskbar())
                TryEnableFusion(initial: false);
            SyncOverlays();
            NotifyBalloon("Đã bật hiệu ứng (" + _hotkeyDisplay + ")");
            Log("Effect ON via " + _hotkeyDisplay + ".");
        }
    }

    private void NotifyBalloon(string text)
    {
        try
        {
            _tray.BalloonTipTitle = "Audio Taskbar";
            _tray.BalloonTipText = text;
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(1600);
        }
        catch { }
    }

    private void ApplyTrayIcon(bool on)
    {
        try
        {
            var ic = on ? _icon : _iconOff;
            if (ic is not null) _tray.Icon = ic;
        }
        catch { }
    }

    private void RegisterConfiguredHotkey()
    {
        _hotkeyRegistered = false;
        _hotkeyDisplay = _cfg.Hotkey ?? "F8";
        try { _shellNotify.UnregisterToggleHotkey(); } catch { }
        if (!Hotkey.TryParse(_cfg.Hotkey, out uint mods, out uint vk, out string display))
        {
            Log("Hotkey: cannot parse '" + _cfg.Hotkey + "'. Use e.g. F8 or Ctrl+Alt+F9.");
            NotifyBalloon("Không hiểu phím tắt '" + _cfg.Hotkey + "'. Sửa hotkey trong config.json.");
            return;
        }
        _hotkeyDisplay = display;
        if (!_shellNotify.RegisterToggleHotkey(mods, vk, out int err))
        {
            Log($"Hotkey: RegisterHotKey({display}) failed win32={err}. Change 'hotkey' in config.json.");
            NotifyBalloon("Không đăng ký được " + display + " (có thể đang dùng bởi app khác). Đổi hotkey trong config.json.");
            return;
        }
        _hotkeyRegistered = true;
        Log("Hotkey registered: " + display + " (RegisterHotKey ok).");
    }

    private void BeginReload()
    {
        try
        {
            if (_exiting || _tray.ContextMenuStrip is null) return;
            _tray.ContextMenuStrip.BeginInvoke(new Action(ReloadConfig));
        }
        catch { }
    }

    // Hidden control owned by the UI thread, used to marshal background work
    // (TAP attach, layout probe, file watchers) back onto the UI thread. The tray
    // menu's handle only exists once the menu has been opened, so it is not usable.
    private readonly Control _invoker = CreateInvoker();

    private static Control CreateInvoker()
    {
        var c = new Control();
        _ = c.Handle; // force handle creation on the constructing (UI) thread
        return c;
    }

    private void BeginInvoke(Action action)
    {
        try
        {
            if (_invoker.IsHandleCreated && !_invoker.IsDisposed)
                _invoker.BeginInvoke(action);
        }
        catch { }
    }


    private void TryApplyPreviewCommand()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AudioTaskbar", "preview-cmd.txt");
            if (!File.Exists(path)) return;
            var raw = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return;
            _cfg.WeatherPreview = raw;
            _cfg.Normalize();
            Persist();
            Log("Weather preview via command: " + _cfg.WeatherPreview);
        }
        catch (Exception ex) { Log("Preview command: " + ex.Message); }
    }

    private void ReloadConfig()
    {
        try
        {
            Thread.Sleep(80);
            var cfg = ConfigStore.Load(_configPath);
            bool weatherOn;
            lock (_configLock)
            {
                cfg.ShowSpectrum = false;
                cfg.ShowWaveform = false;
                cfg.EnableAudio = false;
                cfg.Normalize();
                _cfg = cfg;
                weatherOn = cfg.ShowWeather;
            }
            if (cfg.EnableAudio)
                _analyzer.Configure(cfg, _capture.SampleRate);
            _frameTimer.Interval = Math.Max(8, 1000 / cfg.Fps);
            if (_sampleBuf.Length != cfg.FftSize)
                _sampleBuf = new float[cfg.FftSize];
            _weather.Start(cfg);
            _tap.ReadyTimeoutSeconds = cfg.TapTimeoutSeconds;
            SyncOverlays();
            if (WantsSealedTaskbar(cfg))
                TryEnableFusion(initial: false);
            else
                DisableFusion();
            RegisterConfiguredHotkey();
            Log("Config reloaded. preview=" + cfg.WeatherPreview + " hotkey=" + cfg.Hotkey);
            _tray.ShowBalloonTip(1200, "Audio Taskbar", "Đã tải lại cấu hình.", ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            Log("Config reload failed: " + ex.Message);
        }
    }

    private void OnFrame()
    {
        if (_exiting) return;
        if (_cmdExit.WaitOne(0))
        {
            Log("Exit requested (stop script) — detaching cleanly.");
            ExitApp();
            return;
        }
        if (_cmdToggle.WaitOne(0))
            ToggleEffect();
        if (_cmdPreview.WaitOne(0))
            TryApplyPreviewCommand();

        _watchdogCounter++;
        if (_watchdogCounter >= Math.Max(1, _cfg.Fps * 2))
        {
            _watchdogCounter = 0;
            if (_cfg.EnableAudio)
            {
                _capture.TickWatchdog();
                _analyzer.Configure(_cfg, _capture.SampleRate);
            }
        }

        if (_paused) return;

        AppConfig cfg;
        lock (_configLock) cfg = _cfg;

        if (cfg.EnableAudio)
        {
            if (_sampleBuf.Length != cfg.FftSize)
                _sampleBuf = new float[cfg.FftSize];
            bool have = _capture.TryGetMono(_sampleBuf, cfg.FftSize);
            if (have) _analyzer.Process(_sampleBuf, cfg);
        }

        _idle = 0f;

        float time = (float)(DateTime.UtcNow - _startedAt).TotalSeconds;
        var weather = _weather.Latest;

        _zCounter++;
        bool checkZ = _zCounter >= Math.Max(4, cfg.Fps / 2);
        if (checkZ) _zCounter = 0;

        if (_layoutDirty)
        {
            _layoutDirty = false;
            RefreshRegions();
        }
        var region = RegionCatalog.Resolve(cfg, weather);
        string regionKey = region.Id + "|" + cfg.RegionMode + "|" + (weather?.LocationName ?? "");
        if (regionKey != _regionLogged)
        {
            _regionLogged = regionKey;
            Log($"Region: {region.Id} ({region.NameVi}) mode={cfg.RegionMode} location='{weather?.LocationName}'");
        }
        var moodNow = SceneClock.Compose(weather, cfg, DateTime.Now);

        foreach (var overlay in _overlays)
        {
            var backdrop = _backdrops.FirstOrDefault(b => b.TaskbarHwnd == overlay.TaskbarHwnd);
            if (cfg.HideWhenFullscreen && TaskbarService.IsFullscreenOnBounds(overlay.Bounds))
            {
                if (overlay.Visible) overlay.ClearAndHide();
                backdrop?.HideNow();
                continue;
            }
            if (_regions.TryGetValue(overlay.TaskbarHwnd, out var regions) && regions.AutoHidden)
            {
                if (overlay.Visible) overlay.ClearAndHide();
                backdrop?.HideNow();
                continue;
            }
            if (!overlay.Visible)
            {
                overlay.Show();
                // Show() can lift a TOPMOST window above the taskbar — slip back immediately.
                overlay.ReassertZ(_fused);
            }

            if (!_regions.TryGetValue(overlay.TaskbarHwnd, out regions))
                regions = new TaskbarRegions { Bounds = overlay.Bounds };

            bool showWeather = cfg.ShowWeatherHud && (overlay.IsPrimaryTaskbar || !cfg.WeatherOnPrimaryOnly);
            overlay.DrawFrame(new FrameContext
            {
                Cfg = cfg,
                Analyzer = _analyzer,
                Wave = cfg.ShowWaveform ? _sampleBuf : null,
                Weather = weather,
                Regions = regions,
                Idle = _idle,
                Time = time,
                Fused = _fused,
                BehindOk = overlay.BehindOk,
                ShowWeather = showWeather,
                Region = region
            });
            if (checkZ) overlay.ReassertZ(_fused);

            // Safe mode: a solid scene-tinted layer *behind* Shell_TrayWnd keeps the
            // wallpaper out from under the (untouched) icons.
            if (backdrop is not null && (checkZ || !backdrop.Placed))
            {
                if (!_fused && cfg.BlockWallpaper)
                {
                    var top = SceneClock.Mix(moodNow.SkyMid, Color.FromArgb(255, 8, 10, 18), 0.35f);
                    var bot = SceneClock.Mix(moodNow.Ground, Color.FromArgb(255, 6, 8, 12), 0.35f);
                    backdrop.ShowBehind(overlay.Bounds,
                        Color.FromArgb(255, top.R / 8 * 8, top.G / 8 * 8, top.B / 8 * 8),
                        Color.FromArgb(255, bot.R / 8 * 8, bot.G / 8 * 8, bot.B / 8 * 8));
                }
                else backdrop.HideNow();
            }
        }

        if (checkZ && _fused && !_paused)
            WatchBehindZ();

        // Named events let us toggle fusion during unattended verification.
        if (_cmdFusionOff.WaitOne(0))
        {
            _cfg.FuseIntoTaskbar = false;
            _cfg.MakeTaskbarTransparent = false;
            Persist();
            DisableFusion();
        }
        else if (_cmdFusionOn.WaitOne(0))
        {
            _cfg.FuseIntoTaskbar = true;
            _cfg.MakeTaskbarTransparent = true;
            Persist();
            TryEnableFusion(initial: false);
        }

        // v5: the old pixel-probe "fusion" guess is retired — _fused is only ever
        // true when the TAP really attached, so the icon cut-out mask can't be skipped by mistake.

        if (_tap.IsAttached && _watchdogCounter == 0 && !_tap.ExplorerStillAlive())
        {
            int? live = TaskbarTapHost.FindTaskbarExplorerPid();
            _fused = false;
            _transparency.Effective = false;
            try { _tap.Detach(_ => { }); } catch { }
            foreach (var o in _overlays) o.ReassertZ(false);
            if (live is int pid)
            {
                // A new shell is already up (crash or user restart). Never kill it.
                Log("TAP: explorer PID changed to " + pid + " — detached without restarting the new shell.");
                if ((DateTime.Now - _tapAttachedAt).TotalSeconds < 90)
                {
                    // Explorer died soon after we attached: assume the TAP was involved and never
                    // retry automatically this session (no crash loops).
                    _tapBlockedReason = "explorer-exited-soon-after-attach";
                    Log("Fusion: Explorer exited " + (int)(DateTime.Now - _tapAttachedAt).TotalSeconds + " s after TAP attach — TAP disabled for this session; UIA icon-safe mode.");
                }
                SafeMode();
            }
            else
            {
                Log("TAP: explorer exited and no taskbar is present — restarting explorer.");
                TaskbarTapHost.RestartExplorer(Log);
            }
        }
    }

    private bool WantsSealedTaskbar(AppConfig? cfg = null)
    {
        cfg ??= _cfg;
        return cfg.BlockWallpaper || (cfg.MakeTaskbarTransparent && cfg.FuseIntoTaskbar);
    }

    private void TryEnableFusion(bool initial, bool force = false)
    {
        if (_behindFailed && initial)
        {
            Log("Fusion: skipped (earlier z-order fallback this session).");
            return;
        }
        if (!initial)
        {
            _behindFailed = false;
            _behindFailStreak = 0;
        }
        if (_tap.IsAttached)
        {
            _tap.RequestApply(Log);
            _fused = true;
            _transparency.Effective = true;
            foreach (var o in _overlays) o.ReassertZ(true);
            HideBackdrops();
            WatchBehindZ();
            Log("Fusion: TAP already attached — reapplied fills.");
            return;
        }
        if (_cfg.TapMode == "off")
        {
            Log("Fusion: tapMode=off — UIA icon-safe mode + wallpaper backdrop (Explorer untouched).");
            SafeMode();
            return;
        }
        if (_tapBlockedReason is not null && !force)
        {
            Log("Fusion: TAP unavailable (" + _tapBlockedReason + ") — staying in UIA icon-safe mode (no wait).");
            SafeMode();
            return;
        }
        int pidNow = TaskbarTapHost.FindTaskbarExplorerPid() ?? 0;
        if (!force && pidNow != 0 && pidNow == _tapFailedPid)
        {
            Log("Fusion: TAP already failed for explorer PID " + pidNow + " — not retrying on F8 (instant safe mode).");
            SafeMode();
            return;
        }
        var blocker = TaskbarTapHost.DetectTapBlocker();
        if (blocker is not null)
        {
            _tapBlockedReason = blocker;
            Log("Fusion: Smart App Control is ON — Windows blocks unsigned DLLs inside explorer.exe; using UIA icon-safe mode + backdrop.");
            SafeMode();
            return;
        }
        if (_attachInFlight) return;
        _attachInFlight = true;
        SafeMode(); // icon-safe immediately while we try, never a blank/blocked bar
        Log(initial ? "Fusion: attempting TAP attach (background)…" : "Fusion: re-attaching TAP (background)…");
        int pidBefore = pidNow;
        Task.Run(() =>
        {
            bool ok;
            try { ok = _tap.Attach(Log); }
            catch (Exception ex) { Log("Fusion attach error: " + ex.Message); ok = false; }
            BeginInvoke(() => FinishAttach(ok, pidBefore));
        });
    }

    private DateTime _tapAttachedAt = DateTime.MinValue;

    private void FinishAttach(bool ok, int pidBefore)
    {
        _attachInFlight = false;
        if (_exiting) return;
        bool explorerAlive = TaskbarTapHost.FindTaskbarExplorerPid() is not null;
        if (pidBefore != 0)
        {
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(pidBefore);
                explorerAlive = explorerAlive && !p.HasExited;
            }
            catch { explorerAlive = TaskbarTapHost.FindTaskbarExplorerPid() is not null; }
        }
        if (!explorerAlive)
        {
            Log("Fusion: explorer is gone after attach — restarting.");
            TaskbarTapHost.RestartExplorer(Log);
            ok = false;
            try { _tap.Detach(_ => { }); } catch { }
        }

        if (ok && !_paused)
        {
            _fused = true;
            _tapAttachedAt = DateTime.Now;
            _transparency.Effective = true;
            Log("Fusion: TAP " + (_tap.Reused ? "re-enabled (reused in-Explorer service, explorer PID " + _tap.LastExplorerPid + ")" : "attached (first injection, explorer PID " + _tap.LastExplorerPid + ")") + " — visualizer sits behind the taskbar.");
            foreach (var o in _overlays) o.ReassertZ(true);
            HideBackdrops();
            WatchBehindZ();
        }
        else
        {
            if (!ok)
            {
                _tapFailedPid = pidBefore;
                if (_tap.Status.StartsWith("blocked-")) _tapBlockedReason = _tap.Status["blocked-".Length..];
            }
            Log("Fusion: TAP unsuccessful (" + _tap.Status + ") — UIA icon-safe mode (exact cut-outs) + wallpaper backdrop.");
            SafeMode();
        }
    }

    private void SafeMode()
    {
        _fused = false;
        _transparency.Effective = false;
        foreach (var o in _overlays) o.ReassertZ(false);
    }

    private void HideBackdrops()
    {
        foreach (var b in _backdrops) b.HideNow();
    }

    private void RefreshRegions()
    {
        AppConfig cfg;
        lock (_configLock) cfg = _cfg;
        foreach (var o in _overlays)
        {
            if (o.TaskbarHwnd == IntPtr.Zero) continue;
            if (!_layout.TryGet(o.TaskbarHwnd, out var snap)) continue;
            var info = new TaskbarInfo(o.TaskbarHwnd, snap.Bounds, o.IsPrimaryTaskbar);
            var reg = TaskbarService.GetRegions(info, cfg, snap);
            if (!_regions.TryGetValue(o.TaskbarHwnd, out var prev) || prev.Signature != reg.Signature)
                Log("Layout " + (o.IsPrimaryTaskbar ? "primary" : "secondary") + " source=" + snap.Source
                    + " bounds=" + snap.Bounds.Width + "x" + snap.Bounds.Height + " cutouts="
                    + string.Join(",", reg.Cutouts.Select(c => (c.Left - snap.Bounds.Left) + "-" + (c.Right - snap.Bounds.Left))));
            _regions[o.TaskbarHwnd] = reg;
            if (snap.Bounds != o.Bounds && snap.Bounds.Width > 4 && snap.Bounds.Height > 4)
                o.MoveTo(snap.Bounds);
        }
    }

    private void WatchBehindZ()
    {
        if (!_fused)
        {
            _behindFailStreak = 0;
            return;
        }

        bool allOk = true;
        foreach (var o in _overlays)
        {
            if (!o.Visible) continue;
            if (!o.PlaceBehindTaskbar())
                allOk = false;
        }

        if (allOk)
        {
            if (_behindFailStreak > 0)
                Log("Fusion: visualizer is back under the taskbar.");
            _behindFailStreak = 0;
            return;
        }

        _behindFailStreak++;
        Log("Fusion: visualizer was above the taskbar (streak=" + _behindFailStreak + ") — re-slipped under Shell_TrayWnd.");
        if (_behindFailStreak >= 3)
            FallBackToIconSafe("could not keep visualizer behind the taskbar");
    }

    private void FallBackToIconSafe(string reason)
    {
        Log("Fusion fallback: " + reason + " — restoring taskbar fill and using icon-safe overlay so icons stay visible.");
        if (_tap.IsAttached)
            _tap.RequestRestore(Log);
        _fused = false;
        _transparency.Effective = false;
        _behindFailed = true;
        _behindFailStreak = 0;
        foreach (var o in _overlays) o.ReassertZ(false);
    }

    private void DisableFusion()
    {
        Log("Fusion: disabling, restoring taskbar background.");
        try { _tap.Detach(Log); } catch { }
        _transparency.Restore();
        _fused = false;
        _transparency.Effective = false;
        foreach (var o in _overlays) o.ReassertZ(false);
    }

    private void MaybeProbeFusion(AppConfig cfg)
    {
        var primary = _overlays.FirstOrDefault(o => o.IsPrimaryTaskbar);
        if (primary is null || !_regions.TryGetValue(primary.TaskbarHwnd, out var regions)) return;
        if (!TryEmptySamplePoint(regions, out var pt)) return;

        if (_probePhase == 0)
        {
            foreach (var o in _overlays) o.ClearAndHide();
            _stockPixel = SampleScreen(pt);
            Log($"Fusion probe: stock empty pixel={Fmt(_stockPixel)} at {pt.X},{pt.Y}");
            _probePhase = 1;
            return;
        }

        if (_probePhase == 1)
        {
            foreach (var o in _overlays)
            {
                if (!o.Visible) o.Show();
                o.PlaceBehindTaskbar();
            }
            _probePhase = 2;
            return;
        }

        var now = SampleScreen(pt);
        bool behind = primary.IsBelowTaskbar();
        int dist = _stockPixel is { } s && now is { } n ? ColorDist(s, n) : 0;
        bool changed = dist >= 18;
        _fused = behind && changed;
        _transparency.Effective = _fused;
        _probePhase = 3;
        Log($"Fusion probe: fused={_fused} behind={behind} delta={dist} behindPixel={Fmt(now)} stock={Fmt(_stockPixel)} status={_transparency.Status}");
        foreach (var o in _overlays) o.ReassertZ(_fused);
    }

    private static bool TryEmptySamplePoint(TaskbarRegions regions, out Point pt)
    {
        int x = regions.Bounds.X + 24;
        int y = regions.Bounds.Y + Math.Max(2, regions.Bounds.Height / 2);
        var icon = regions.IconCluster;
        if (!icon.IsEmpty && x >= icon.Left - 8 && x <= icon.Right + 8)
            x = Math.Max(regions.Bounds.X + 8, icon.Left - 40);
        if (!regions.Tray.IsEmpty && x >= regions.Tray.Left)
            x = regions.Bounds.X + 16;
        pt = new Point(x, y);
        return regions.Bounds.Contains(pt);
    }

    private static Color? SampleScreen(Point pt)
    {
        try
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(pt, Point.Empty, new Size(1, 1));
            return bmp.GetPixel(0, 0);
        }
        catch { return null; }
    }

    private static int ColorDist(Color a, Color b)
        => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static string Fmt(Color? c)
        => c is { } x ? $"#{x.R:X2}{x.G:X2}{x.B:X2}" : "null";

    private void SyncOverlays()
    {
        if (_exiting) return;
        if (_paused)
        {
            foreach (var o in _overlays) o.ClearAndHide();
            HideBackdrops();
            return;
        }
        var bars = TaskbarService.GetTaskbars();
        bool allMonitors;
        AppConfig cfg;
        lock (_configLock)
        {
            cfg = _cfg;
            allMonitors = cfg.ShowOnAllMonitors;
        }
        if (!allMonitors)
            bars = bars.Where(b => b.IsPrimary).ToList();

        _layout.SetTargets(bars.Select(b => b.Hwnd));
        _regions.Clear();
        foreach (var b in bars)
        {
            if (!_layout.TryGet(b.Hwnd, out var snap) || snap.Bounds != b.Bounds)
                snap = _layout.ProbeNow(b.Hwnd); // first frame is already icon-safe
            _regions[b.Hwnd] = TaskbarService.GetRegions(b, cfg, snap);
        }
        while (_backdrops.Count > bars.Count)
        {
            var last = _backdrops[^1];
            _backdrops.RemoveAt(_backdrops.Count - 1);
            try { last.HideNow(); last.Close(); last.Dispose(); } catch { }
        }
        while (_backdrops.Count < bars.Count) _backdrops.Add(new BackdropWindow());
        for (int i = 0; i < bars.Count; i++) _backdrops[i].TaskbarHwnd = bars[i].Hwnd;

        while (_overlays.Count > bars.Count)
        {
            var last = _overlays[^1];
            _overlays.RemoveAt(_overlays.Count - 1);
            last.Close();
            last.Dispose();
        }
        while (_overlays.Count < bars.Count)
        {
            var w = new OverlayWindow();
            w.Show();
            _overlays.Add(w);
        }
        for (int i = 0; i < bars.Count; i++)
        {
            _overlays[i].TaskbarHwnd = bars[i].Hwnd;
            _overlays[i].IsPrimaryTaskbar = bars[i].IsPrimary;
            _overlays[i].MoveTo(bars[i].Bounds);
            _overlays[i].ReassertZ(_fused);
        }
    }

    private void UpdateTip()
    {
        try
        {
            var w = _weather.Latest;
            string weather = w is { Ok: true }
                ? $" | {w.LocationName} {w.TemperatureText(false)}"
                : "";
            string state = _paused ? "TẮT" : "BẬT";
            string hk = _hotkeyRegistered ? _hotkeyDisplay : (_hotkeyDisplay + "?");
            _tray.Text = TrimTip($"Audio Taskbar {state} ({hk})" + weather + ( _cfg.WeatherPreview != "live" ? " [" + _cfg.WeatherPreview + "]" : ""));
        }
        catch { }
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _frameTimer.Stop();
        _layoutTimer.Stop();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        try { _shellNotify.UnregisterToggleHotkey(); } catch { }
        _shellNotify.DestroyHandle();
        foreach (var o in _overlays)
        {
            try { o.Close(); o.Dispose(); } catch { }
        }
        _overlays.Clear();
        foreach (var b in _backdrops) { try { b.HideNow(); b.Close(); b.Dispose(); } catch { } }
        _backdrops.Clear();
        try { _layout.Dispose(); } catch { }
        try { _tap.Detach(Log); } catch { }
        _transparency.Restore();
        Log("Taskbar appearance restored.");
        _weather.Dispose();
        try { _cmdFusionOn.Dispose(); _cmdFusionOff.Dispose(); _cmdToggle.Dispose(); _cmdPreview.Dispose(); _cmdExit.Dispose(); } catch { }

        _capture.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        try { _invoker.Dispose(); } catch { }
        _icon?.Dispose();
        _iconOff?.Dispose();
        Log("Exit.");
        ExitThread();
    }

    private void Log(string message)
    {
        try
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
            File.AppendAllText(_logPath, line);
        }
        catch { }
    }

    private static string TrimTip(string s) => s.Length <= 63 ? s : s[..63];

    private static Icon? CreateIcon(bool on = true)
    {
        try
        {
            using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var bgc = on ? Color.FromArgb(255, 7, 21, 38) : Color.FromArgb(255, 50, 50, 55);
                var fgc = on ? Color.FromArgb(255, 46, 230, 166) : Color.FromArgb(255, 140, 140, 145);
                using (var bg = new SolidBrush(bgc))
                    g.FillEllipse(bg, 1, 1, 30, 30);
                using var pen = new Pen(fgc, 2.2f);
                var pts = new[]
                {
                    new PointF(6, 20), new PointF(10, 16), new PointF(14, 22),
                    new PointF(18, 10), new PointF(22, 18), new PointF(26, 12)
                };
                g.DrawLines(pen, pts);
            }
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally { Native.DestroyIcon(hIcon); }
        }
        catch { return null; }
    }
}

/// <summary>Hidden window that listens for the TaskbarCreated broadcast.</summary>
internal sealed class ShellNotifyWindow : NativeWindow
{
    public const int ToggleHotkeyId = 1;
    private readonly Action _onTaskbarCreated;
    private readonly Action _onToggleHotkey;
    private readonly int _msg;

    public ShellNotifyWindow(Action onTaskbarCreated, Action onToggleHotkey)
    {
        _onTaskbarCreated = onTaskbarCreated;
        _onToggleHotkey = onToggleHotkey;
        _msg = Native.RegisterWindowMessage("TaskbarCreated");
        var cp = new CreateParams
        {
            Caption = "AudioTaskbar.ShellNotify",
            Style = 0,
            ExStyle = Native.WS_EX_TOOLWINDOW
        };
        CreateHandle(cp);
    }

    public bool RegisterToggleHotkey(uint modifiers, uint vk, out int win32Error)
    {
        UnregisterToggleHotkey();
        if (!Native.RegisterHotKey(Handle, ToggleHotkeyId, modifiers, vk))
        {
            win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            return false;
        }
        win32Error = 0;
        return true;
    }

    public void UnregisterToggleHotkey()
    {
        try { Native.UnregisterHotKey(Handle, ToggleHotkeyId); } catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _msg) _onTaskbarCreated();
        else if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == ToggleHotkeyId)
            _onToggleHotkey();
        base.WndProc(ref m);
    }
}
