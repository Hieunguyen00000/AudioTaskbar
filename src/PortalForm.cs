using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace AudioTaskbar;

/// <summary>
/// v5.3 UniKey-style control portal: one compact window with grouped cards. Every change applies
/// live and persists to %LOCALAPPDATA%\AudioTaskbar\config.json; the tray menu, F8 and this
/// window stay in sync through <see cref="TrayApp.StateChanged"/>.
/// "Đóng" / X hides to the tray (effect keeps running); "Kết thúc" exits and restores the taskbar.
/// </summary>
internal sealed class PortalForm : Form
{
    private readonly TrayApp _host;
    private readonly Palette _p;
    private bool _syncing;
    private bool _forceClose;

    private readonly Label _status = new();
    private readonly Button _toggle = new();
    private readonly ComboBox _region = Combo();
    private readonly ComboBox _weather = Combo();
    private readonly ComboBox _time = Combo();
    private readonly CheckBox _hud = Check("Hiện thông tin thời tiết && địa điểm");
    private readonly CheckBox _train = Check("Tàu hỏa");
    private readonly NumericUpDown _trainMin = new() { Minimum = 0.25m, Maximum = 240m, DecimalPlaces = 2, Increment = 0.25m, Width = 64, TextAlign = HorizontalAlignment.Right };
    private readonly CheckBox _traffic = Check("Phương tiện khu vực");
    private readonly CheckBox _ambient = Check("Chi tiết nền (chim, đèn phố)");
    private readonly CheckBox _effects = Check("Hiệu ứng thời tiết");
    private readonly ComboBox _hotkey = Combo();
    private readonly Label _hotkeyNote = new() { AutoSize = true };
    private readonly CheckBox _autostart = Check("Khởi động cùng Windows");
    private readonly CheckBox _tap = Check("Hòa nền phía sau icon (TAP)");
    private readonly Label _tapNote = new() { AutoSize = true, MaximumSize = new Size(400, 0) };
    private readonly CheckBox _autoUpdate = Check("Tự động kiểm tra cập nhật");
    private readonly CheckBox _startMin = Check("Mở ẩn xuống khay (không hiện cửa sổ này)");
    private readonly Label _updVersion = new() { AutoSize = true };
    private readonly Label _updStatus = new() { AutoSize = true, MaximumSize = new Size(400, 0) };
    private readonly Button _updCheck = new() { Text = "Kiểm tra cập nhật", AutoSize = true };
    private readonly Button _updNow = new() { Text = "Cập nhật ngay", AutoSize = true, Visible = false };
    private readonly ProgressBar _updBar = new() { Height = 8, Dock = DockStyle.Fill, Visible = false, Style = ProgressBarStyle.Continuous };

    private static readonly (string Id, string Label)[] Regions =
    {
        ("auto", "Tự động theo máy"), ("hcmc", "TP. Hồ Chí Minh"), ("baria-vungtau", "Bà Rịa – Vũng Tàu"),
        ("hanoi", "Hà Nội"), ("danang-hoian", "Đà Nẵng – Hội An"), ("tokyo", "Tokyo"), ("paris", "Paris"), ("default", "Mặc định")
    };
    private static readonly (string Id, string Label)[] Weathers =
    {
        ("live", "Thực tế"), ("sunny", "Nắng đẹp"), ("cloudy", "Mây trôi"), ("rain", "Mưa rào"),
        ("storm", "Giông bão"), ("snow", "Tuyết rơi"), ("night", "Đêm sao")
    };
    private static readonly (string Id, string Label)[] Times = { ("cycle", "Chu kỳ 30 phút"), ("real", "Theo giờ thật") };
    private static readonly string[] Hotkeys = { "F8", "F9", "Ctrl+Alt+F8", "Ctrl+Shift+F8" };

    public PortalForm(TrayApp host)
    {
        _host = host;
        _p = Palette.Current();
        Text = "Audio Taskbar";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        BackColor = _p.Back;
        ForeColor = _p.Text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 12, 14, 12);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = _p.Back, Margin = Padding.Empty,
            Location = new Point(Padding.Left, Padding.Top)
        };
        // Not Dock=Fill: a docked child of an AutoSize form makes the form size itself before the
        // cards finish their own layout (the update card ended up under the footer).
        stack.Controls.Add(BuildHeader());
        stack.Controls.Add(BuildToggle());
        stack.Controls.Add(BuildSceneCard());
        stack.Controls.Add(BuildSystemCard());
        stack.Controls.Add(BuildUpdateCard());
        stack.Controls.Add(BuildFooter());
        Controls.Add(stack);

        WireEvents();
        SyncFromHost();
        _host.StateChanged += OnHostChanged;
        FormClosed += (_, _) => _host.StateChanged -= OnHostChanged;
    }

    private const int CardWidth = 440;

    #region layout

    private Control BuildHeader()
    {
        var t = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, Width = CardWidth, Margin = new Padding(0, 0, 0, 8), BackColor = _p.Back };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var icon = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 2, 10, 0) };
        try { icon.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch { }
        t.Controls.Add(icon, 0, 0); t.SetRowSpan(icon, 2);
        var title = new Label { Text = "Audio Taskbar", AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), ForeColor = _p.Text, Margin = new Padding(0, 0, 0, 0) };
        t.Controls.Add(title, 1, 0);
        var ver = new Pill { Text = "v" + AutoUpdater.AppVersion(), Fill = _p.Accent, TextColor = Color.White, Margin = new Padding(6, 6, 0, 0) };
        t.Controls.Add(ver, 2, 0);
        _status.AutoSize = true;
        _status.Margin = new Padding(1, 2, 0, 0);
        _status.Font = new Font("Segoe UI Semibold", 9.5f);
        t.Controls.Add(_status, 1, 1); t.SetColumnSpan(_status, 2);
        return t;
    }

    private Control BuildToggle()
    {
        _toggle.Width = CardWidth;
        _toggle.Height = 46;
        _toggle.FlatStyle = FlatStyle.Flat;
        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.Font = new Font("Segoe UI Semibold", 12f);
        _toggle.Margin = new Padding(0, 0, 0, 10);
        _toggle.Cursor = Cursors.Hand;
        return _toggle;
    }

    private Card NewCard(string title, out TableLayoutPanel grid)
    {
        // Fixed width: an AutoSize panel would otherwise shrink to its padding around the Dock=Top grid.
        var card = new Card(_p) { Width = CardWidth, MinimumSize = new Size(CardWidth, 0), MaximumSize = new Size(CardWidth, 0), Margin = new Padding(0, 0, 0, 10) };
        grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, BackColor = _p.Card, Padding = new Padding(12, 6, 12, 8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var head = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = _p.Accent, Margin = new Padding(0, 0, 0, 6) };
        grid.Controls.Add(head, 0, 0); grid.SetColumnSpan(head, 2);
        card.Controls.Add(grid);
        // The card follows the grid's real height (an AutoSize Panel mis-measures a Dock=Top child,
        // which let the footer cover the bottom of the update card).
        var g = grid;
        void Fit() { int h = g.Height + card.Padding.Vertical; if (card.Height != h) card.Height = h; }
        g.SizeChanged += (_, _) => Fit();
        card.HandleCreated += (_, _) => Fit();
        Fit();
        return card;
    }

    private void Row(TableLayoutPanel g, string label, Control c)
    {
        int r = g.RowCount++;
        var l = new Label { Text = label, AutoSize = true, ForeColor = _p.Sub, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 4) };
        c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        c.Margin = new Padding(0, 3, 0, 3);
        g.Controls.Add(l, 0, r);
        g.Controls.Add(c, 1, r);
    }

    private void Full(TableLayoutPanel g, Control c)
    {
        int r = g.RowCount++;
        c.Margin = new Padding(0, 2, 0, 2);
        g.Controls.Add(c, 0, r);
        g.SetColumnSpan(c, 2);
    }

    private Control BuildSceneCard()
    {
        var card = NewCard("Cảnh vật && Thời tiết", out var g);
        g.RowCount = 1;
        foreach (var r in Regions) _region.Items.Add(r.Label);
        foreach (var w in Weathers) _weather.Items.Add(w.Label);
        foreach (var t in Times) _time.Items.Add(t.Label);
        Row(g, "Khu vực", _region);
        Row(g, "Thời tiết", _weather);
        Row(g, "Ngày && đêm", _time);
        Full(g, _hud);
        var trainRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _p.Card, Margin = Padding.Empty };
        _train.Margin = new Padding(0, 3, 6, 0);
        _trainMin.Margin = new Padding(0, 1, 4, 0);
        trainRow.Controls.Add(_train);
        trainRow.Controls.Add(_trainMin);
        trainRow.Controls.Add(new Label { Text = "phút/lượt", AutoSize = true, ForeColor = _p.Sub, Margin = new Padding(0, 4, 0, 0) });
        Full(g, trainRow);
        var two = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, BackColor = _p.Card, Margin = Padding.Empty };
        two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        two.Controls.Add(_traffic, 0, 0); two.Controls.Add(_effects, 1, 0);
        two.Controls.Add(_ambient, 0, 1); two.SetColumnSpan(_ambient, 2);
        Full(g, two);
        return card;
    }

    private Control BuildSystemCard()
    {
        var card = NewCard("Hệ thống", out var g);
        g.RowCount = 1;
        foreach (var h in Hotkeys) _hotkey.Items.Add(h);
        var hk = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _p.Card, Margin = Padding.Empty };
        _hotkey.Width = 150;
        hk.Controls.Add(_hotkey);
        _hotkeyNote.Margin = new Padding(6, 5, 0, 0);
        hk.Controls.Add(_hotkeyNote);
        Row(g, "Phím tắt bật/tắt", hk);
        Full(g, _autostart);
        Full(g, _tap);
        _tapNote.ForeColor = _p.Sub;
        _tapNote.Font = new Font("Segoe UI", 8.5f);
        Full(g, _tapNote);
        Full(g, _autoUpdate);
        Full(g, _startMin);
        return card;
    }

    private Control BuildUpdateCard()
    {
        var card = NewCard("Cập nhật", out var g);
        g.RowCount = 1;
        _updVersion.ForeColor = _p.Text;
        _updStatus.ForeColor = _p.Sub;
        Full(g, _updVersion);
        Full(g, _updStatus);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _p.Card, Margin = Padding.Empty };
        StyleButton(_updCheck, primary: false);
        StyleButton(_updNow, primary: true);
        buttons.Controls.Add(_updCheck);
        buttons.Controls.Add(_updNow);
        Full(g, buttons);
        Full(g, _updBar);
        return card;
    }

    private Control BuildFooter()
    {
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Width = CardWidth, BackColor = _p.Back, Margin = new Padding(0, 2, 0, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // v5.3: no "open config folder" link — end users get a clean UniKey-style panel.
        var spacer = new Label { AutoSize = false, Size = new Size(1, 1), Margin = Padding.Empty };
        var close = new Button { Text = "Đóng (Ẩn xuống khay)", AutoSize = true };
        var exit = new Button { Text = "Kết thúc (Thoát)", AutoSize = true };
        StyleButton(close, primary: false);
        StyleButton(exit, primary: false, danger: true);
        close.Click += (_, _) => Hide();
        exit.Click += (_, _) => _host.ExitFromPortal();
        t.Controls.Add(spacer, 0, 0);
        t.Controls.Add(close, 1, 0);
        t.Controls.Add(exit, 2, 0);
        CancelButton = close;
        return t;
    }

    private void StyleButton(Button b, bool primary, bool danger = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = danger ? _p.Danger : primary ? _p.Accent : _p.Border;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = primary ? _p.Accent : _p.Card;
        b.ForeColor = primary ? Color.White : danger ? _p.Danger : _p.Text;
        b.Padding = new Padding(8, 3, 8, 3);
        b.Margin = new Padding(6, 3, 0, 3);
        b.Cursor = Cursors.Hand;
    }

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    #endregion

    #region sync

    private void WireEvents()
    {
        _toggle.Click += (_, _) => _host.SetEffect(!_host.EffectOn);
        _region.SelectedIndexChanged += (_, _) => Apply(c => c.RegionMode = Regions[Math.Max(0, _region.SelectedIndex)].Id, refreshWeather: true);
        _weather.SelectedIndexChanged += (_, _) => Apply(c => c.WeatherPreview = Weathers[Math.Max(0, _weather.SelectedIndex)].Id);
        _time.SelectedIndexChanged += (_, _) => Apply(c => c.TimeMode = Times[Math.Max(0, _time.SelectedIndex)].Id);
        _hud.CheckedChanged += (_, _) => Apply(c => c.ShowWeatherHud = _hud.Checked);
        _train.CheckedChanged += (_, _) => Apply(c => c.ShowTrain = _train.Checked);
        _trainMin.ValueChanged += (_, _) => Apply(c => c.TrainCrossingMinutes = (float)_trainMin.Value);
        _traffic.CheckedChanged += (_, _) => Apply(c => c.ShowCyclist = _traffic.Checked);
        _ambient.CheckedChanged += (_, _) => Apply(c => c.ShowAmbientDetails = _ambient.Checked);
        _effects.CheckedChanged += (_, _) => Apply(c => c.ShowWeatherEffects = _effects.Checked);
        _hotkey.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _hotkey.SelectedItem is not string hk) return;
            _host.Mutate(c => c.Hotkey = hk, hotkey: true);
        };
        _autostart.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            try { AutoStart.Set(_autostart.Checked, Application.ExecutablePath); }
            catch (Exception ex) { MessageBox.Show(this, "Không ghi được khóa khởi động: " + ex.Message, "Audio Taskbar"); }
            SyncFromHost();
        };
        _tap.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _host.Mutate(c => c.TapMode = _tap.Checked ? "auto" : "off", fusion: true);
        };
        _autoUpdate.CheckedChanged += (_, _) => Apply(c => c.AutoUpdateEnabled = _autoUpdate.Checked);
        _startMin.CheckedChanged += (_, _) => Apply(c => c.StartMinimized = _startMin.Checked);
        _updCheck.Click += async (_, _) => await _host.CheckForUpdatesAsync(manual: true);
        _updNow.Click += async (_, _) => await _host.InstallLatestAsync();
    }

    private void Apply(Action<AppConfig> change, bool refreshWeather = false)
    {
        if (_syncing) return;
        _host.Mutate(change, refreshWeather: refreshWeather);
    }

    private void OnHostChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(SyncFromHost)); } catch { } return; }
        SyncFromHost();
    }

    private static int IndexOf((string Id, string Label)[] list, string? id)
    {
        for (int i = 0; i < list.Length; i++) if (list[i].Id == id) return i;
        return 0;
    }

    internal void SyncFromHost()
    {
        if (IsDisposed) return;
        _syncing = true;
        try
        {
            var c = _host.Config;
            bool on = _host.EffectOn;
            _status.Text = "● " + _host.StatusText;
            _status.ForeColor = !on ? _p.Sub : _host.Fused ? _p.Ok : _p.Warn;
            _toggle.Text = on ? $"Tắt hiệu ứng  ({_host.HotkeyDisplay})" : $"Bật hiệu ứng  ({_host.HotkeyDisplay})";
            _toggle.BackColor = on ? _p.Accent : _p.Card;
            _toggle.ForeColor = on ? Color.White : _p.Text;
            _toggle.FlatAppearance.BorderSize = on ? 0 : 1;
            _toggle.FlatAppearance.BorderColor = _p.Border;

            SetIndex(_region, IndexOf(Regions, c.RegionMode));
            SetIndex(_weather, IndexOf(Weathers, c.WeatherPreview));
            SetIndex(_time, IndexOf(Times, c.TimeMode));
            _hud.Checked = c.ShowWeatherHud;
            _train.Checked = c.ShowTrain;
            var m = (decimal)Math.Clamp(c.TrainCrossingMinutes, 0.25f, 240f);
            if (_trainMin.Value != m) _trainMin.Value = m;
            _trainMin.Enabled = c.ShowTrain;
            _traffic.Checked = c.ShowCyclist;
            _ambient.Checked = c.ShowAmbientDetails;
            _effects.Checked = c.ShowWeatherEffects;

            if (!_hotkey.Items.Contains(c.Hotkey)) _hotkey.Items.Add(c.Hotkey);
            _hotkey.SelectedItem = c.Hotkey;
            _hotkeyNote.Text = _host.HotkeyOk ? "✓ đang dùng" : "⚠ bị app khác chiếm";
            _hotkeyNote.ForeColor = _host.HotkeyOk ? _p.Ok : _p.Danger;

            _autostart.Checked = AutoStart.IsEnabled();
            var blocker = _host.TapBlocker;
            bool sac = blocker is not null && blocker.Contains("smart", StringComparison.OrdinalIgnoreCase);
            _tap.Checked = c.TapMode != "off" && !sac;
            _tap.Enabled = !sac;
            _tapNote.Text = sac
                ? "Smart App Control đang bật: Windows chặn DLL chưa ký trong Explorer, app dùng chế độ an toàn (khoét quanh icon)."
                : blocker is not null
                    ? "TAP tạm tắt cho phiên này (" + blocker + "). Bật lại sau khi khởi động lại app."
                    : "Cảnh vật nằm sau icon, nền taskbar trong suốt. Tắt để dùng chế độ an toàn.";
            _autoUpdate.Checked = c.AutoUpdateEnabled;
            _startMin.Checked = c.StartMinimized;

            _updVersion.Text = "Phiên bản hiện tại: v" + AutoUpdater.AppVersion()
                               + (c.UpdateFeedUrl.Length > 0 ? "  ·  feed riêng" : "  ·  github.com/" + c.UpdateRepo);
            var last = _host.Updater.LastResult;
            _updStatus.Text = _host.UpdateBusy || _host.UpdateStage.Length > 0 && last is null
                ? _host.UpdateStage
                : last is null ? "Chưa kiểm tra." : (_host.UpdateStage.Length > 0 ? _host.UpdateStage : last.Message) + "  (" + last.CheckedAt.ToString("HH:mm") + ")";
            _updStatus.ForeColor = last?.Status switch
            {
                UpdateStatus.UpToDate => _p.Ok,
                UpdateStatus.UpdateAvailable => _p.Accent,
                null => _p.Sub,
                _ => _p.Warn
            };
            _updNow.Visible = last?.Status == UpdateStatus.UpdateAvailable;
            if (last?.Info is { } info && last.Status == UpdateStatus.UpdateAvailable) _updNow.Text = "Cập nhật ngay (v" + info.Version + ")";
            _updCheck.Enabled = !_host.UpdateBusy;
            _updNow.Enabled = !_host.UpdateBusy;
            double p = _host.UpdateProgress;
            _updBar.Visible = _host.UpdateBusy && p >= 0;
            if (p >= 0) _updBar.Value = (int)Math.Round(Math.Clamp(p, 0, 1) * 100);
        }
        finally { _syncing = false; }
    }

    private static void SetIndex(ComboBox cb, int i)
    {
        if (cb.SelectedIndex != i && i >= 0 && i < cb.Items.Count) cb.SelectedIndex = i;
    }

    #endregion

    public void ShowAndActivate()
    {
        SyncFromHost();
        if (!Visible) Show();
        FitToScreen();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        // bring to front even when another app owns the foreground
        TopMost = true;
        Activate();
        TopMost = false;
        BringToFront();
    }

    /// <summary>High DPI / small screens: never taller than the work area — scroll instead.</summary>
    private void FitToScreen()
    {
        try
        {
            var wa = Screen.FromControl(this).WorkingArea;
            if (Height > wa.Height - 16)
            {
                AutoSize = false;
                AutoScroll = true;
                Size = new Size(Width + SystemInformation.VerticalScrollBarWidth, wa.Height - 16);
            }
            if (Bottom > wa.Bottom || Top < wa.Top) Top = Math.Max(wa.Top, wa.Bottom - Height);
            if (Right > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
            if (Left < wa.Left) Left = wa.Left;
        }
        catch { }
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X behaves like UniKey's "Đóng": hide to the tray, keep the effect running.
        if (!_forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    #region visuals

    internal sealed class Palette
    {
        public Color Back, Card, Border, Text, Sub, Accent, Ok, Warn, Danger;

        public static Palette Current()
        {
            bool dark = false;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                dark = k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { }
            return dark
                ? new Palette
                {
                    Back = Color.FromArgb(32, 33, 38), Card = Color.FromArgb(43, 45, 52), Border = Color.FromArgb(70, 74, 84),
                    Text = Color.FromArgb(236, 238, 242), Sub = Color.FromArgb(168, 174, 186), Accent = Color.FromArgb(22, 160, 133),
                    Ok = Color.FromArgb(64, 200, 140), Warn = Color.FromArgb(240, 180, 70), Danger = Color.FromArgb(240, 100, 96)
                }
                : new Palette
                {
                    Back = Color.FromArgb(242, 244, 247), Card = Color.White, Border = Color.FromArgb(214, 219, 226),
                    Text = Color.FromArgb(28, 32, 40), Sub = Color.FromArgb(96, 104, 118), Accent = Color.FromArgb(14, 138, 116),
                    Ok = Color.FromArgb(20, 140, 80), Warn = Color.FromArgb(190, 120, 10), Danger = Color.FromArgb(200, 50, 50)
                };
        }
    }

    private sealed class Card : Panel
    {
        private readonly Palette _p;
        public Card(Palette p)
        {
            _p = p;
            AutoSize = false;
            BackColor = p.Card;
            Padding = new Padding(1);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(_p.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private sealed class Pill : Control
    {
        public Color Fill { get; set; } = Color.Teal;
        public Color TextColor { get; set; } = Color.White;
        public Pill()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI Semibold", 8.5f);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var s = TextRenderer.MeasureText(Text, Font);
            return new Size(s.Width + 14, s.Height + 4);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Size = GetPreferredSize(Size.Empty); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = new GraphicsPath();
            float d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            using var br = new SolidBrush(Fill);
            e.Graphics.FillPath(br, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, TextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    #endregion
}
