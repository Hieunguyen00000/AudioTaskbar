using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
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
    private readonly NumericUpDown _cycleMin = new() { BorderStyle = BorderStyle.None, Minimum = (decimal)AppConfig.MinDayCycleMinutes, Maximum = (decimal)AppConfig.MaxDayCycleMinutes, DecimalPlaces = 1, Increment = 5m, Width = 72, TextAlign = HorizontalAlignment.Right };
    private readonly NumericUpDown _trainMin = new() { BorderStyle = BorderStyle.None, Minimum = 0.25m, Maximum = 240m, DecimalPlaces = 2, Increment = 0.25m, Width = 64, TextAlign = HorizontalAlignment.Right };
    private readonly CheckBox _traffic = Check("Phương tiện khu vực");
    private readonly CheckBox _ambient = Check("Chi tiết nền (chim, đèn phố)");
    private readonly CheckBox _effects = Check("Hiệu ứng thời tiết");
    private readonly ComboBox _hotkey = Combo();
    private readonly Label _hotkeyNote = new() { AutoSize = true };
    private readonly CheckBox _autostart = Check("Khởi động cùng Windows");
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
    private static readonly (string Id, string Label)[] Times = { ("cycle", "Theo chu kỳ tùy chỉnh"), ("real", "Theo giờ thật"), ("day", "Cố định ban ngày"), ("night", "Cố định ban đêm") };
    private static readonly string[] Hotkeys = { "F8", "F9", "Ctrl+Alt+F8", "Ctrl+Shift+F8" };

    public PortalForm(TrayApp host)
    {
        _host = host;
        _p = Palette.Current();
        s_palette = _p;
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
    private const int LabelColumn = 118;
    [ThreadStatic] private static Palette? s_palette;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_p.Dark) return;
        // Windows 10 20H1+ / 11: dark title bar so the frame matches the dark panel.
        try { int on = 1; if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
    }

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
        t.Margin = new Padding(0, 0, 0, 12);
        return t;
    }


    private Card NewCard(string title, out TableLayoutPanel grid)
    {
        // Fixed width: an AutoSize panel would otherwise shrink to its padding around the Dock=Top grid.
        var card = new Card(_p) { Width = CardWidth, MinimumSize = new Size(CardWidth, 0), MaximumSize = new Size(CardWidth, 0), Margin = new Padding(0, 0, 0, 10) };
        grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, BackColor = _p.Card, Padding = new Padding(12, 8, 12, 10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumn));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var head = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = _p.Accent, Margin = new Padding(0, 0, 0, 8) };
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
        var l = new Label { Text = label, AutoSize = true, ForeColor = _p.Sub, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
        c.Anchor = c is ComboBox ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        c.Margin = new Padding(0, 3, 0, 3);
        g.Controls.Add(l, 0, r);
        g.Controls.Add(c, 1, r);
    }

    private void Full(TableLayoutPanel g, Control c)
    {
        int r = g.RowCount++;
        c.Margin = new Padding(0, 3, 0, 3);
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
        var cycleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _p.Card, Margin = Padding.Empty };
        cycleRow.Controls.Add(Framed(_cycleMin, 86));
        cycleRow.Controls.Add(new Label { Text = "phút/ngày  (0,5 – 10080)", AutoSize = true, ForeColor = _p.Sub, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 0, 0) });
        Row(g, "Độ dài chu kỳ", cycleRow);
        Full(g, _hud);
        // Same label/control grid as the rows above: checkbox in the label column, value in the control column.
        _train.Margin = new Padding(0, 0, 0, 0);
        var trainCell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _p.Card, Margin = Padding.Empty };
        trainCell.Controls.Add(Framed(_trainMin, 86));
        trainCell.Controls.Add(new Label { Text = "phút/lượt", AutoSize = true, ForeColor = _p.Sub, Margin = new Padding(8, 6, 0, 0) });
        int tr = g.RowCount++;
        _train.Anchor = AnchorStyles.Left;
        g.Controls.Add(_train, 0, tr);
        trainCell.Margin = new Padding(0, 3, 0, 3);
        g.Controls.Add(trainCell, 1, tr);
        var two = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, BackColor = _p.Card, Margin = Padding.Empty };
        two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var cb in new Control[] { _traffic, _effects, _ambient }) cb.Margin = new Padding(0, 3, 0, 3); // flush with the rows above
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
        _hotkey.Margin = Padding.Empty;
        hk.Controls.Add(_hotkey);
        _hotkeyNote.Margin = new Padding(10, 5, 0, 0);
        hk.Controls.Add(_hotkeyNote);
        Row(g, "Phím tắt bật/tắt", hk);
        Full(g, _autostart);
        // v5.3: wallpaper blocking / behind-icon blending is always on — no user toggle.
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

    // v5.3.2: the on/off button lives in the footer (right), next to "Đóng" (left) and "Kết thúc" (middle).
    private const int FooterHeight = 36;
    private const int FooterGap = 8;
    private readonly Button _close = new() { Text = "Đóng (Ẩn xuống khay)" };
    private readonly Button _exit = new() { Text = "Kết thúc (Thoát)" };
    private TableLayoutPanel? _footer;

    private Control BuildFooter()
    {
        var t = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = false, Size = new Size(CardWidth, FooterHeight + 2), BackColor = _p.Back, Margin = new Padding(0, 2, 0, 0), Padding = Padding.Empty };
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight + 2));
        for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CardWidth / 3f));
        StyleButton(_close, primary: false);
        StyleButton(_exit, primary: false, danger: true);
        foreach (var b in new[] { _close, _exit, _toggle }) { b.AutoSize = false; b.Dock = DockStyle.Fill; b.Height = FooterHeight; b.Padding = new Padding(4, 0, 4, 0); b.TextAlign = ContentAlignment.MiddleCenter; b.UseMnemonic = false; }
        _toggle.FlatStyle = FlatStyle.Flat;
        _toggle.Font = new Font("Segoe UI Semibold", 9.5f);
        _toggle.Cursor = Cursors.Hand;
        _close.Margin = new Padding(0, 1, FooterGap / 2, 1);
        _exit.Margin = new Padding(FooterGap / 2, 1, FooterGap / 2, 1);
        _toggle.Margin = new Padding(FooterGap / 2, 1, 0, 1);
        _close.Click += (_, _) => Hide();
        _exit.Click += (_, _) => _host.ExitFromPortal();
        t.Controls.Add(_close, 0, 0);
        t.Controls.Add(_exit, 1, 0);
        t.Controls.Add(_toggle, 2, 0);
        CancelButton = _close;
        _footer = t;
        LayoutFooter();
        return t;
    }

    internal static string ToggleText(bool on, string hotkey) => (on ? "Tắt hiệu ứng (" : "Bật hiệu ứng (") + hotkey + ")";

    /// <summary>
    /// Column widths for the footer: each button gets at least its text width (+ padding); the space left over
    /// is shared equally, so the three buttons look even and never clip (also with long hotkeys like Ctrl+Alt+F8).
    /// </summary>
    internal static int[] FooterColumns(int total, int gap, int[] needed)
    {
        int n = needed.Length, avail = total - gap * (n - 1);
        int sum = needed.Sum();
        var w = new int[n];
        if (sum <= avail)
        {
            // Prefer equal widths when they all fit; otherwise grow the narrow ones until they meet.
            int equal = avail / n;
            if (needed.All(x => x <= equal)) { for (int i = 0; i < n; i++) w[i] = equal; }
            else { int extra = (avail - sum) / n; for (int i = 0; i < n; i++) w[i] = needed[i] + extra; }
        }
        else
        {
            for (int i = 0; i < n; i++) w[i] = (int)Math.Floor(needed[i] * (double)avail / sum);
        }
        w[n - 1] += avail - w.Sum(); // rounding remainder → last column, so the right edge lines up with the cards
        for (int i = 0; i < n; i++) w[i] += i == 0 || i == n - 1 ? gap / 2 : gap; // margins live inside the cells
        return w;
    }

    private void LayoutFooter()
    {
        if (_footer is null) return;
        string hk = _host.HotkeyDisplay;
        int Need(Button b, params string[] texts) => texts.Max(x => TextRenderer.MeasureText(x, b.Font).Width) + b.Padding.Horizontal + 14;
        var need = new[] { Need(_close, _close.Text), Need(_exit, _exit.Text), Need(_toggle, ToggleText(true, hk), ToggleText(false, hk)) };
        var cols = FooterColumns(CardWidth, FooterGap, need);
        _footer.SuspendLayout();
        for (int i = 0; i < 3; i++) _footer.ColumnStyles[i] = new ColumnStyle(SizeType.Absolute, cols[i]);
        _footer.ResumeLayout(true);
    }

    private void StyleButton(Button b, bool primary, bool danger = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = danger ? _p.Danger : primary ? _p.Accent : _p.Border;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = primary ? _p.Accent : _p.Card;
        b.ForeColor = primary ? Color.White : danger ? _p.Danger : _p.Text;
        b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(_p.Accent, 0.15f) : _p.Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(_p.Accent, 0.05f) : _p.Input;
        b.Padding = new Padding(10, 4, 10, 4);
        b.Margin = new Padding(0, 3, 8, 3);
        b.Cursor = Cursors.Hand;
        b.UseMnemonic = false;
    }

    /// <summary>A borderless NumericUpDown inside a 1-px themed frame, with dark up/down arrows.</summary>
    private Control Framed(NumericUpDown n, int width)
    {
        n.BackColor = _p.Input;
        n.ForeColor = _p.Text;
        n.Font = Font;
        var frame = new InputFrame(_p) { Size = new Size(width, n.PreferredHeight + 8), Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        n.Location = new Point(6, 4);
        n.Width = width - 8;
        frame.Controls.Add(n);
        n.EnabledChanged += (_, _) => { n.BackColor = n.Enabled ? _p.Input : _p.Card; n.ForeColor = n.Enabled ? _p.Text : _p.Sub; frame.Invalidate(); };
        n.GotFocus += (_, _) => frame.Focused2 = true;
        n.LostFocus += (_, _) => frame.Focused2 = false;
        if (n.Controls.Count > 0 && n.Controls[0] is Control spin)
        {
            // Overpaint the classic light spinner after it draws itself.
            spin.Paint += (_, e) =>
            {
                var r = spin.ClientRectangle;
                using var bg = new SolidBrush(n.Enabled ? _p.Input : _p.Card);
                e.Graphics.FillRectangle(bg, r);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var arrow = new SolidBrush(n.Enabled ? _p.Sub : _p.Border);
                float cx = r.Width / 2f, h = r.Height / 2f;
                e.Graphics.FillPolygon(arrow, new[] { new PointF(cx - 3.5f, h - 2.5f), new PointF(cx + 3.5f, h - 2.5f), new PointF(cx, h - 6.5f) });
                e.Graphics.FillPolygon(arrow, new[] { new PointF(cx - 3.5f, h + 2.5f), new PointF(cx + 3.5f, h + 2.5f), new PointF(cx, h + 6.5f) });
            };
        }
        return frame;
    }

    private static ComboBox Combo() => new DarkCombo { Width = 260 };
    private static CheckBox Check(string text) => new DarkCheck { Text = text, AutoSize = true };

    #endregion

    #region sync

    private void WireEvents()
    {
        _toggle.Click += (_, _) => _host.SetEffect(!_host.EffectOn);
        _region.SelectedIndexChanged += (_, _) => Apply(c => c.RegionMode = Regions[Math.Max(0, _region.SelectedIndex)].Id, refreshWeather: true);
        _weather.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            var id = Weathers[Math.Max(0, _weather.SelectedIndex)].Id;
            _host.Mutate(c => c.ApplyWeatherOverrideSelection(id)); // "Đêm sao" → Cố định ban đêm
            SyncFromHost();
        };
        _time.SelectedIndexChanged += (_, _) => Apply(c => c.TimeMode = Times[Math.Max(0, _time.SelectedIndex)].Id);
        _hud.CheckedChanged += (_, _) => Apply(c => c.ShowWeatherHud = _hud.Checked);
        _train.CheckedChanged += (_, _) => Apply(c => c.ShowTrain = _train.Checked);
        _cycleMin.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            var v = (float)_cycleMin.Value;
            _host.Mutate(c => SceneClock.SetCycleMinutes(c, v, DateTime.Now));
        };
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
            var tt = ToggleText(on, _host.HotkeyDisplay);
            if (_toggle.Text != tt) { _toggle.Text = tt; LayoutFooter(); }
            // Primary accent either way so it is easy to spot: filled green when on, green outline when off.
            _toggle.BackColor = on ? _p.Accent : _p.Card;
            _toggle.ForeColor = on ? Color.White : _p.Accent;
            _toggle.FlatAppearance.BorderSize = 1;
            _toggle.FlatAppearance.BorderColor = _p.Accent;
            _toggle.FlatAppearance.MouseOverBackColor = on ? ControlPaint.Light(_p.Accent, 0.15f) : _p.Hover;
            _toggle.FlatAppearance.MouseDownBackColor = on ? ControlPaint.Dark(_p.Accent, 0.05f) : _p.Input;

            SetIndex(_region, IndexOf(Regions, c.RegionMode));
            SetIndex(_weather, IndexOf(Weathers, c.WeatherPreview));
            SetIndex(_time, IndexOf(Times, c.TimeMode));
            _hud.Checked = c.ShowWeatherHud;
            _train.Checked = c.ShowTrain;
            var m = (decimal)Math.Clamp(c.TrainCrossingMinutes, 0.25f, 240f);
            if (_trainMin.Value != m) _trainMin.Value = m;
            _trainMin.Enabled = c.ShowTrain;
            var cm = (decimal)Math.Clamp(c.DayCycleMinutes, AppConfig.MinDayCycleMinutes, AppConfig.MaxDayCycleMinutes);
            if (_cycleMin.Value != cm) _cycleMin.Value = cm;
            _cycleMin.Enabled = c.TimeMode == "cycle";
            _traffic.Checked = c.ShowCyclist;
            _ambient.Checked = c.ShowAmbientDetails;
            _effects.Checked = c.ShowWeatherEffects;

            if (!_hotkey.Items.Contains(c.Hotkey)) _hotkey.Items.Add(c.Hotkey);
            _hotkey.SelectedItem = c.Hotkey;
            _hotkeyNote.Text = _host.HotkeyOk ? "✓ đang dùng" : "⚠ bị app khác chiếm";
            _hotkeyNote.ForeColor = _host.HotkeyOk ? _p.Ok : _p.Danger;

            _autostart.Checked = AutoStart.IsEnabled();
            _autoUpdate.Checked = c.AutoUpdateEnabled;
            _startMin.Checked = c.StartMinimized;

            _updVersion.Text = "Phiên bản hiện tại: v" + AutoUpdater.AppVersion();
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

        public bool Dark { get; init; }
        public Color Input { get; init; }
        public Color Hover { get; init; }

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
                    Dark = true, Input = Color.FromArgb(53, 56, 65), Hover = Color.FromArgb(62, 66, 76),
                    Back = Color.FromArgb(32, 33, 38), Card = Color.FromArgb(43, 45, 52), Border = Color.FromArgb(70, 74, 84),
                    Text = Color.FromArgb(236, 238, 242), Sub = Color.FromArgb(168, 174, 186), Accent = Color.FromArgb(22, 160, 133),
                    Ok = Color.FromArgb(64, 200, 140), Warn = Color.FromArgb(240, 180, 70), Danger = Color.FromArgb(240, 100, 96)
                }
                : new Palette
                {
                    Dark = false, Input = Color.White, Hover = Color.FromArgb(234, 238, 243),
                    Back = Color.FromArgb(242, 244, 247), Card = Color.White, Border = Color.FromArgb(214, 219, 226),
                    Text = Color.FromArgb(28, 32, 40), Sub = Color.FromArgb(96, 104, 118), Accent = Color.FromArgb(14, 138, 116),
                    Ok = Color.FromArgb(20, 140, 80), Warn = Color.FromArgb(190, 120, 10), Danger = Color.FromArgb(200, 50, 50)
                };
        }
    }

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Card with 8-px rounded corners and a hairline border (the grid sits inside the 4-px inset).</summary>
    private sealed class Card : Panel
    {
        private readonly Palette _p;
        public Card(Palette p)
        {
            _p = p;
            AutoSize = false;
            BackColor = p.Back;
            Padding = new Padding(4);
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
            using var fill = new SolidBrush(_p.Card);
            using var pen = new Pen(_p.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
        }
    }

    /// <summary>Rounded 1-px frame hosting an input; accent border while focused.</summary>
    private sealed class InputFrame : Panel
    {
        private readonly Palette _p;
        private bool _focused;
        public bool Focused2 { get => _focused; set { _focused = value; Invalidate(); } }
        public InputFrame(Palette p) { _p = p; BackColor = p.Card; DoubleBuffered = true; ResizeRedraw = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            bool en = Controls.Count == 0 || Controls[0].Enabled;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4);
            using var fill = new SolidBrush(en ? _p.Input : _p.Card);
            using var pen = new Pen(_focused && en ? _p.Accent : _p.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// Owner-drawn drop-down list: dark slate field, light text, chevron, accent border on hover/focus,
    /// and a matching dark item list (no bright-white system combo on the dark card).
    /// </summary>
    private sealed class DarkCombo : ComboBox
    {
        private bool _hover;
        private Palette P => s_palette ?? Palette.Current();

        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 22;
            IntegralHeight = false;
            MaxDropDownItems = 10;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BackColor = P.Input; ForeColor = P.Text;
            if (P.Dark) { try { SetWindowTheme(Handle, "DarkMode_CFD", null); } catch { } }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var p = P;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = !edit && (e.State & DrawItemState.Selected) != 0;
            using var bg = new SolidBrush(sel ? Color.FromArgb(p.Dark ? 70 : 40, p.Accent) : p.Input);
            if (sel) { using var baseBg = new SolidBrush(p.Input); e.Graphics.FillRectangle(baseBg, e.Bounds); }
            e.Graphics.FillRectangle(bg, e.Bounds);
            var r = Rectangle.Inflate(e.Bounds, -8, 0);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, Enabled ? p.Text : p.Sub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private const int WM_PAINT = 0x000F;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT) PaintField();
        }

        /// <summary>Repaint the whole closed field (border, background, text, chevron) over the system frame.</summary>
        private void PaintField()
        {
            var p = P;
            using var g = CreateGraphics();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = ClientRectangle;
            using (var parentBg = new SolidBrush(Parent?.BackColor ?? p.Card)) g.FillRectangle(parentBg, rc);
            using var path = Round(new RectangleF(0.5f, 0.5f, rc.Width - 1.5f, rc.Height - 1.5f), 4);
            using (var fill = new SolidBrush(Enabled ? (_hover && !DroppedDown ? p.Hover : p.Input) : p.Card)) g.FillPath(fill, path);
            using (var pen = new Pen(Focused || DroppedDown ? p.Accent : p.Border)) g.DrawPath(pen, path);
            var text = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : "";
            TextRenderer.DrawText(g, text, Font, new Rectangle(8, 0, rc.Width - 34, rc.Height), Enabled ? p.Text : p.Sub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            float cx = rc.Width - 15, cy = rc.Height / 2f;
            using var chev = new Pen(Enabled ? p.Sub : p.Border, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(chev, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    /// <summary>Flat themed check box: rounded 16-px box, accent fill + white tick when checked.</summary>
    private sealed class DarkCheck : CheckBox
    {
        private bool _hover;
        private Palette P => s_palette ?? Palette.Current();

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            UseMnemonic = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var t = TextRenderer.MeasureText(Text.Replace("&&", "&"), Font, Size.Empty, TextFormatFlags.NoPrefix);
            return new Size(16 + 8 + t.Width + 2, Math.Max(22, t.Height + 4));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = P;
            var g = e.Graphics;
            using (var bg = new SolidBrush(Parent?.BackColor ?? p.Card)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = (Height - 16) / 2f;
            using var box = Round(new RectangleF(0.5f, y + 0.5f, 15f, 15f), 3.5f);
            if (Checked)
            {
                using var fill = new SolidBrush(Enabled ? p.Accent : p.Border);
                g.FillPath(fill, box);
                using var tick = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[] { new PointF(4f, y + 8.2f), new PointF(7f, y + 11f), new PointF(12f, y + 5f) });
            }
            else
            {
                using var fill = new SolidBrush(_hover && Enabled ? p.Hover : p.Input);
                using var pen = new Pen(_hover && Enabled ? p.Accent : p.Border);
                g.FillPath(fill, box);
                g.DrawPath(pen, box);
            }
            if (Focused && ShowFocusCues)
            {
                using var fpen = new Pen(Color.FromArgb(140, p.Accent)) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(fpen, 23, 1, Width - 25, Height - 3);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height), Enabled ? p.Text : p.Sub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
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
