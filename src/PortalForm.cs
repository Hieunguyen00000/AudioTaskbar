using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioTaskbar;

internal enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>
/// v5.4 control panel. By default it shows only the scenery, weather and season settings; "Âm thanh nền" and "Cài đặt"
/// (system + update) are collapsed until clicked. Edits preview live; <b>Lưu cấu hình</b> writes them to config.json,
/// and closing with unsaved edits asks whether to save, discard or keep editing.
/// "Đóng" / X hides to the tray (effect keeps running); "Kết thúc" exits and restores the taskbar.
/// </summary>
internal sealed class PortalForm : Form
{
    private readonly IPortalHost _host;
    private readonly Palette _p;
    private bool _syncing;
    private bool _forceClose;

    private readonly Label _status = new();
    private readonly Button _toggle = new();
    private readonly Label _info1 = new() { AutoSize = true, MaximumSize = new Size(CardWidth - 30, 0) };
    private readonly Label _info2 = new() { AutoSize = true, MaximumSize = new Size(CardWidth - 30, 0) };
    private readonly ComboBox _region = Combo();
    private readonly ComboBox _weather = Combo();
    private readonly ComboBox _weatherCycle = Combo();
    private readonly NumericUpDown _wcA = Num(0.5m, 1440m, 1, 1m, 66);
    private readonly NumericUpDown _wcB = Num(0.5m, 1440m, 1, 1m, 66);
    private readonly Label _wcDash = new() { Text = "–", AutoSize = true };
    private readonly Label _wcUnit = new() { AutoSize = true };
    private readonly ComboBox _season = Combo();
    private readonly CheckBox _seasonCycle = Check("Tự đổi mùa");
    private readonly NumericUpDown _seasonMin = Num((decimal)AppConfig.MinSeasonCycleMinutes, (decimal)AppConfig.MaxSeasonCycleMinutes, 1, 5m, 72);
    private readonly ComboBox _time = Combo();
    private readonly CheckBox _train = Check("Tàu hỏa");
    private readonly NumericUpDown _cycleMin = Num((decimal)AppConfig.MinDayCycleMinutes, (decimal)AppConfig.MaxDayCycleMinutes, 1, 5m, 72);
    private readonly NumericUpDown _trainMin = Num(0.25m, 240m, 2, 0.25m, 64);
    private readonly CheckBox _traffic = Check("Phương tiện khu vực");
    private readonly CheckBox _ambient = Check("Chi tiết nền (chim, đèn phố)");
    private readonly CheckBox _effects = Check("Hiệu ứng thời tiết");
    private readonly CheckBox _padding = Check("Vùng đệm xám quanh icon ứng dụng");
    private readonly ComboBox _hotkey = Combo();
    private readonly Label _hotkeyNote = new() { AutoSize = true };
    private readonly CheckBox _autostart = Check("Khởi động cùng Windows");
    private readonly CheckBox _autoUpdate = Check("Tự động kiểm tra và cập nhật khi mở app");
    private readonly CheckBox _startMin = Check("Mở ẩn xuống khay (không hiện cửa sổ này)");
    private readonly Label _updVersion = new() { AutoSize = true };
    private readonly Label _updStatus = new() { AutoSize = true, MaximumSize = new Size(400, 0) };
    private readonly Button _updCheck = new() { Text = UpdateButtonText, AutoSize = true };
    private readonly ProgressBar _updBar = new() { Height = 8, Dock = DockStyle.Fill, Visible = false, Style = ProgressBarStyle.Continuous };
    private readonly Button _save = new() { Text = SaveButtonText };
    private readonly Label _dirty = new() { AutoSize = true };
    private readonly DarkSlider _master = new() { Width = 230 };
    private readonly Label _masterVal = new() { AutoSize = true, Width = 40 };
    private readonly Dictionary<string, (DarkSlider Slider, Label Value)> _fx = new();
    private Expander? _soundExp, _settingsExp;
    private FlowLayoutPanel? _stack;
    private bool _scrolling;
    private readonly System.Windows.Forms.Timer _infoTimer = new() { Interval = 2000 };

    /// <summary>One button for check + update (the old separate "Cập nhật ngay" button is gone).</summary>
    internal const string UpdateButtonText = "Kiểm tra & cập nhật";
    internal const string SaveButtonText = "Lưu cấu hình";
    internal const string SettingsTitle = "Cài đặt";
    internal const string SoundTitle = "Âm thanh nền";

    internal static readonly (string Id, string Label)[] Regions = RegionCatalog.PickList().ToArray();
    internal static readonly (string Id, string Label)[] Weathers = TrayMenuModel.Weathers.ToArray();
    internal static readonly (string Id, string Label)[] Times = TrayMenuModel.Times.ToArray();
    internal static readonly (string Id, string Label)[] Seasons = TrayMenuModel.Seasons.ToArray();
    internal static readonly (string Id, string Label)[] WeatherCycles =
    {
        ("off", "Tắt (giữ thời tiết đã chọn)"), ("interval", "Theo khoảng thời gian"), ("random", "Ngẫu nhiên")
    };
    private static readonly string[] Hotkeys = { "F8", "F9", "Ctrl+Alt+F8", "Ctrl+Shift+F8" };

    /// <summary>Asked when the panel is closed or the app exited with unsaved edits. Tests replace it.</summary>
    internal Func<UnsavedChoice> PromptUnsaved { get; set; }

    public PortalForm(IPortalHost host)
    {
        _host = host;
        _p = Palette.Current();
        s_palette = _p;
        PromptUnsaved = DefaultPrompt;
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

        var stack = _stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = _p.Back, Margin = Padding.Empty,
            Location = new Point(Padding.Left, Padding.Top)
        };
        // Not Dock=Fill: a docked child of an AutoSize form makes the form size itself before the
        // cards finish their own layout.
        stack.Controls.Add(BuildHeader());
        stack.Controls.Add(BuildSceneCard());
        _soundExp = new Expander(_p, SoundTitle, BuildSoundCard(), expanded: false) { Margin = new Padding(0, 0, 0, 10) };
        _soundExp.Toggled += RefitSoon;
        stack.Controls.Add(_soundExp);
        _settingsExp = new Expander(_p, SettingsTitle, BuildSettingsBody(), expanded: false) { Margin = new Padding(0, 0, 0, 10) };
        _settingsExp.Toggled += RefitSoon;
        stack.Controls.Add(_settingsExp);
        stack.Controls.Add(BuildSaveRow());
        stack.Controls.Add(BuildFooter());
        Controls.Add(stack);

        WireEvents();
        SyncFromHost();
        _host.StateChanged += OnHostChanged;
        _infoTimer.Tick += (_, _) => { if (Visible && !IsDisposed) UpdateInfo(); };
        _infoTimer.Start();
        FormClosed += (_, _) => { _host.StateChanged -= OnHostChanged; _infoTimer.Stop(); _infoTimer.Dispose(); };
    }

    internal const int CardWidth = 440;
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

    /// <summary>Test / preview hooks.</summary>
    internal bool SettingsExpanded { get => _settingsExp!.Expanded; set => _settingsExp!.Expanded = value; }
    internal bool SoundExpanded { get => _soundExp!.Expanded; set => _soundExp!.Expanded = value; }
    /// <summary>Texts of every control the user can see by default: everything except the content of collapsed sections.</summary>
    internal IEnumerable<string> VisibleTexts() => Descendants(this).Where(c => c.Text.Length > 0).Select(c => c.Text);
    internal IEnumerable<string> AllTexts() => Descendants(this, includeCollapsed: true).Where(c => c.Text.Length > 0).Select(c => c.Text);
    internal IEnumerable<Control> AllControls() => Descendants(this, includeCollapsed: true);
    internal bool IsInCollapsedSection(Control c)
    {
        for (var p = c.Parent; p is not null; p = p.Parent) if (p is Expander e && !e.Expanded) return true;
        return false;
    }
    internal Button SaveButton => _save;
    internal Button UpdateButton => _updCheck;
    internal ComboBox WeatherBox => _weather;
    internal ComboBox SeasonBox => _season;
    internal CheckBox PaddingBox => _padding;
    internal DarkSlider MasterSlider => _master;
    internal IReadOnlyDictionary<string, DarkSlider> EffectSliders => _fx.ToDictionary(k => k.Key, k => k.Value.Slider);

    private static IEnumerable<Control> Descendants(Control root, bool includeCollapsed = false)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            if (!includeCollapsed && c is Expander { Expanded: false } e) { yield return e.Controls[0]; continue; }
            foreach (var d in Descendants(c, includeCollapsed)) yield return d;
        }
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

    private Card NewCard(string title, out TableLayoutPanel grid, bool showTitle = true, int width = CardWidth)
    {
        // Fixed width: an AutoSize panel would otherwise shrink to its padding around the Dock=Top grid.
        var card = new Card(_p) { Width = width, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, 10) };
        grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, BackColor = _p.Card, Padding = new Padding(12, 8, 12, 10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumn));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (showTitle)
        {
            var head = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = _p.Accent, Margin = new Padding(0, 0, 0, 8) };
            grid.Controls.Add(head, 0, 0); grid.SetColumnSpan(head, 2);
        }
        card.Controls.Add(grid);
        // The card follows the grid's real height (an AutoSize Panel mis-measures a Dock=Top child).
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

    private FlowLayoutPanel Flow(params Control[] items)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = _p.Card, Margin = Padding.Empty };
        foreach (var i in items) f.Controls.Add(i);
        return f;
    }

    private Label Hint(string text, int width = CardWidth - 40) => new() { Text = text, AutoSize = true, ForeColor = _p.Sub, MaximumSize = new Size(width, 0) };

    private Control BuildSceneCard()
    {
        var card = NewCard("Cảnh vật && Thời tiết && Mùa", out var g);
        g.RowCount = 1;
        foreach (var r in Regions) _region.Items.Add(r.Label);
        foreach (var w in Weathers) _weather.Items.Add(w.Label);
        foreach (var w in WeatherCycles) _weatherCycle.Items.Add(w.Label);
        foreach (var t in Times) _time.Items.Add(t.Label);
        foreach (var s in Seasons) _season.Items.Add(s.Label);

        // Weather and location info is shown here (and only here) — handy while setting up.
        _info1.ForeColor = _p.Text; _info2.ForeColor = _p.Sub;
        var infoBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = _p.Card, Margin = Padding.Empty };
        infoBox.Controls.Add(_info1); infoBox.Controls.Add(_info2);
        Full(g, infoBox);

        Row(g, "Khu vực", _region);
        Row(g, "Thời tiết", _weather);
        Row(g, "Tự đổi thời tiết", _weatherCycle);
        _wcUnit.ForeColor = _p.Sub; _wcDash.ForeColor = _p.Sub;
        _wcDash.Margin = new Padding(4, 6, 4, 0); _wcUnit.Margin = new Padding(8, 6, 0, 0);
        Row(g, "Chu kỳ đổi", Flow(Framed(_wcA, 80), _wcDash, Framed(_wcB, 80), _wcUnit));
        Row(g, "Mùa", _season);
        var seasonCell = Flow(Framed(_seasonMin, 86), new Label { Text = "phút/mùa  (0,5 – 10080)", AutoSize = true, ForeColor = _p.Sub, Margin = new Padding(8, 6, 0, 0) });
        int sr = g.RowCount++;
        _seasonCycle.Anchor = AnchorStyles.Left; _seasonCycle.Margin = Padding.Empty;
        g.Controls.Add(_seasonCycle, 0, sr);
        seasonCell.Margin = new Padding(0, 3, 0, 3);
        g.Controls.Add(seasonCell, 1, sr);
        Row(g, "Ngày && đêm", _time);
        Row(g, "Độ dài chu kỳ", Flow(Framed(_cycleMin, 86), new Label { Text = "phút/ngày  (0,5 – 10080)", AutoSize = true, ForeColor = _p.Sub, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 0, 0) }));
        // Same label/control grid as the rows above: checkbox in the label column, value in the control column.
        _train.Margin = Padding.Empty;
        var trainCell = Flow(Framed(_trainMin, 86), new Label { Text = "phút/lượt", AutoSize = true, ForeColor = _p.Sub, Margin = new Padding(8, 6, 0, 0) });
        int tr = g.RowCount++;
        _train.Anchor = AnchorStyles.Left;
        g.Controls.Add(_train, 0, tr);
        trainCell.Margin = new Padding(0, 3, 0, 3);
        g.Controls.Add(trainCell, 1, tr);
        var two = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, BackColor = _p.Card, Margin = Padding.Empty };
        two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var cb in new Control[] { _traffic, _effects, _ambient, _padding }) cb.Margin = new Padding(0, 3, 0, 3);
        two.Controls.Add(_traffic, 0, 0); two.Controls.Add(_effects, 1, 0);
        two.Controls.Add(_ambient, 0, 1); two.SetColumnSpan(_ambient, 2);
        two.Controls.Add(_padding, 0, 2); two.SetColumnSpan(_padding, 2);
        Full(g, two);
        return card;
    }

    private Control BuildSoundCard()
    {
        var card = NewCard(SoundTitle, out var g, showTitle: false);
        g.RowCount = 1;
        Full(g, Hint("Tất cả âm lượng mặc định là 0 (im lặng). Kéo âm lượng tổng, rồi từng hiệu ứng. " +
                     "Âm thanh do máy tự tạo, hòa theo khu vực, thời tiết, mùa và giờ trong ngày."));
        Row(g, "Âm lượng tổng", Flow(_master, _masterVal));
        _masterVal.ForeColor = _p.Text; _masterVal.Margin = new Padding(6, 3, 0, 0);
        foreach (var (id, name) in SoundEffects.All)
        {
            var slider = new DarkSlider { Width = 230 };
            var val = new Label { AutoSize = true, Width = 40, ForeColor = _p.Sub, Margin = new Padding(6, 3, 0, 0) };
            _fx[id] = (slider, val);
            Row(g, name, Flow(slider, val));
        }
        return card;
    }

    private Control BuildSettingsBody()
    {
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = _p.Back, Margin = Padding.Empty };
        body.Controls.Add(BuildSystemCard());
        body.Controls.Add(BuildUpdateCard());
        return body;
    }

    private Control BuildSystemCard()
    {
        var card = NewCard("Hệ thống", out var g);
        g.RowCount = 1;
        foreach (var h in Hotkeys) _hotkey.Items.Add(h);
        _hotkey.Width = 150;
        _hotkey.Margin = Padding.Empty;
        _hotkeyNote.Margin = new Padding(10, 5, 0, 0);
        Row(g, "Phím tắt bật/tắt", Flow(_hotkey, _hotkeyNote));
        Full(g, _autostart);
        // v5.3: wallpaper blocking / behind-icon blending is always on — no user toggle.
        // Store edition: GitHub auto-update does not apply (updates come from Microsoft Store).
        if (!StoreEdition.UsesStoreUpdates)
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
        if (StoreEdition.UsesStoreUpdates)
        {
            _updCheck.Visible = false;
            _updBar.Visible = false;
        }
        else
        {
            StyleButton(_updCheck, primary: true);
            Full(g, Flow(_updCheck));
            Full(g, _updBar);
        }
        return card;
    }

    private Control BuildSaveRow()
    {
        var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = false, Size = new Size(CardWidth, 40), BackColor = _p.Back, Margin = new Padding(0, 0, 0, 8), Padding = Padding.Empty };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        _dirty.Anchor = AnchorStyles.Left; _dirty.Margin = new Padding(2, 0, 0, 0); _dirty.Font = new Font("Segoe UI Semibold", 9.5f);
        StyleButton(_save, primary: true);
        _save.AutoSize = false; _save.Dock = DockStyle.Fill; _save.Margin = new Padding(0, 1, 0, 1); _save.Font = new Font("Segoe UI Semibold", 9.5f);
        t.Controls.Add(_dirty, 0, 0);
        t.Controls.Add(_save, 1, 0);
        return t;
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
        _close.Click += (_, _) => { if (ConfirmLeave()) Hide(); };
        _exit.Click += (_, _) => { if (ConfirmLeave()) _host.ExitFromPortal(); };
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

    private static NumericUpDown Num(decimal min, decimal max, int decimals, decimal step, int width)
        => new() { BorderStyle = BorderStyle.None, Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step, Width = width, TextAlign = HorizontalAlignment.Right };

    private static ComboBox Combo() => new DarkCombo { Width = 260 };
    private static CheckBox Check(string text) => new DarkCheck { Text = text, AutoSize = true };

    #endregion

    #region sync

    private void WireEvents()
    {
        _toggle.Click += (_, _) => _host.SetEffect(!_host.EffectOn);
        _save.Click += (_, _) => { _host.SaveConfig(); SyncFromHost(); };
        _region.SelectedIndexChanged += (_, _) => Apply(c => c.RegionMode = Regions[Math.Max(0, _region.SelectedIndex)].Id, refreshWeather: true);
        _weather.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            var id = Weathers[Math.Max(0, _weather.SelectedIndex)].Id;
            _host.Mutate(c => c.ApplyWeatherOverrideSelection(id)); // "Đêm sao" → Cố định ban đêm; "Tuyết rơi" → Mùa đông
            SyncFromHost();
        };
        _weatherCycle.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            var id = WeatherCycles[Math.Max(0, _weatherCycle.SelectedIndex)].Id;
            _host.Mutate(c => c.WeatherCycleMode = id);
            SyncFromHost();
        };
        _wcA.ValueChanged += (_, _) => { if (!_syncing) _host.Mutate(c => { if (c.WeatherCycleMode == "random") c.WeatherCycleRandomMinMinutes = (float)_wcA.Value; else c.WeatherCycleMinutes = (float)_wcA.Value; }); };
        _wcB.ValueChanged += (_, _) => { if (!_syncing) _host.Mutate(c => c.WeatherCycleRandomMaxMinutes = (float)_wcB.Value); };
        _season.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            var id = Seasons[Math.Max(0, _season.SelectedIndex)].Id;
            _host.Mutate(c => c.ApplySeasonSelection(id)); // leaving winter while snow is chosen puts the weather back to "Thực tế"
            SyncFromHost();
        };
        _seasonCycle.CheckedChanged += (_, _) => Apply(c => c.SeasonCycleEnabled = _seasonCycle.Checked);
        _seasonMin.ValueChanged += (_, _) => Apply(c => c.SeasonCycleMinutes = (float)_seasonMin.Value);
        _time.SelectedIndexChanged += (_, _) => Apply(c => c.TimeMode = Times[Math.Max(0, _time.SelectedIndex)].Id);
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
        _padding.CheckedChanged += (_, _) => Apply(c => c.ShowIconPadding = _padding.Checked);
        _master.ValueChanged += (_, _) => { _masterVal.Text = _master.Value + "%"; Apply(c => c.SoundMasterVolume = _master.Value); };
        foreach (var (id, pair) in _fx)
        {
            var (slider, val) = pair;
            string key = id;
            slider.ValueChanged += (_, _) => { val.Text = slider.Value + "%"; Apply(c => c.SoundVolumes[key] = slider.Value); };
        }
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
        _updCheck.Click += async (_, _) => await _host.CheckAndUpdateAsync();
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

    private void UpdateInfo()
    {
        var info = _host.WeatherInfo;
        if (_info1.Text != info.Line1) _info1.Text = info.Line1;
        if (_info2.Text != info.Line2) _info2.Text = info.Line2;
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

            UpdateInfo();
            SetIndex(_region, IndexOf(Regions, c.RegionMode));
            SetIndex(_weather, IndexOf(Weathers, c.WeatherPreview));
            SetIndex(_weatherCycle, IndexOf(WeatherCycles, c.WeatherCycleMode));
            bool rnd = c.WeatherCycleMode == "random", cyc = c.WeatherCycleMode != "off";
            SetNum(_wcA, rnd ? c.WeatherCycleRandomMinMinutes : c.WeatherCycleMinutes);
            SetNum(_wcB, c.WeatherCycleRandomMaxMinutes);
            _wcA.Enabled = cyc; _wcB.Enabled = cyc && rnd;
            _wcB.Parent!.Visible = rnd; _wcDash.Visible = rnd;
            _wcUnit.Text = rnd ? "phút" : "phút/lần đổi";
            SetIndex(_season, IndexOf(Seasons, c.Season));
            _seasonCycle.Checked = c.SeasonCycleEnabled;
            SetNum(_seasonMin, c.SeasonCycleMinutes);
            _seasonMin.Enabled = c.SeasonCycleEnabled;
            SetIndex(_time, IndexOf(Times, c.TimeMode));
            _train.Checked = c.ShowTrain;
            SetNum(_trainMin, c.TrainCrossingMinutes);
            _trainMin.Enabled = c.ShowTrain;
            SetNum(_cycleMin, c.DayCycleMinutes);
            _cycleMin.Enabled = c.TimeMode == "cycle";
            _traffic.Checked = c.ShowCyclist;
            _ambient.Checked = c.ShowAmbientDetails;
            _effects.Checked = c.ShowWeatherEffects;
            _padding.Checked = c.ShowIconPadding;

            _master.Value = c.SoundMasterVolume; _masterVal.Text = c.SoundMasterVolume + "%";
            foreach (var (id, pair) in _fx)
            {
                int v = c.SoundVolumes.TryGetValue(id, out var x) ? x : 0;
                pair.Slider.Value = v; pair.Value.Text = v + "%";
            }

            if (!_hotkey.Items.Contains(c.Hotkey)) _hotkey.Items.Add(c.Hotkey);
            _hotkey.SelectedItem = c.Hotkey;
            _hotkeyNote.Text = _host.HotkeyOk ? "✓ đang dùng" : "⚠ bị app khác chiếm";
            _hotkeyNote.ForeColor = _host.HotkeyOk ? _p.Ok : _p.Danger;

            _autostart.Checked = AutoStart.IsEnabled();
            _autoUpdate.Checked = c.AutoUpdateEnabled;
            _startMin.Checked = c.StartMinimized;

            bool unsaved = _host.HasUnsavedChanges;
            _dirty.Text = unsaved ? "● Có thay đổi chưa lưu" : "✓ Đã lưu";
            _dirty.ForeColor = unsaved ? _p.Warn : _p.Ok;
            _save.Enabled = unsaved;
            _save.BackColor = unsaved ? _p.Accent : _p.Card;
            _save.ForeColor = unsaved ? Color.White : _p.Sub;

            _updVersion.Text = "Phiên bản hiện tại: v" + AutoUpdater.AppVersion();
            if (StoreEdition.UsesStoreUpdates)
            {
                _updStatus.Text = StoreEdition.StoreUpdateMessage + ". Windows sẽ cài bản mới khi có trên Store.";
                _updStatus.ForeColor = _p.Ok;
                _updCheck.Visible = false;
                _updBar.Visible = false;
                return;
            }
            var last = _host.LastUpdateResult;
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
            _updCheck.Enabled = !_host.UpdateBusy;
            double p = _host.UpdateProgress;
            _updBar.Visible = _host.UpdateBusy && p >= 0;
            if (p >= 0) _updBar.Value = (int)Math.Round(Math.Clamp(p, 0, 1) * 100);
        }
        finally { _syncing = false; }
    }

    private static void SetNum(NumericUpDown n, float value)
    {
        var v = Math.Clamp((decimal)value, n.Minimum, n.Maximum);
        if (n.Value != v) n.Value = v;
    }

    private static void SetIndex(ComboBox cb, int i)
    {
        if (cb.SelectedIndex != i && i >= 0 && i < cb.Items.Count) cb.SelectedIndex = i;
    }

    #endregion

    #region unsaved changes

    private UnsavedChoice DefaultPrompt()
    {
        var r = MessageBox.Show(this,
            "Bạn có thay đổi chưa lưu.\n\n• Có — lưu vào cấu hình\n• Không — bỏ thay đổi (trở về cấu hình đã lưu)\n• Hủy — tiếp tục chỉnh sửa",
            "Audio Taskbar", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r switch { DialogResult.Yes => UnsavedChoice.Save, DialogResult.No => UnsavedChoice.Discard, _ => UnsavedChoice.Cancel };
    }

    /// <summary>True when it is fine to leave (nothing unsaved, or the user chose to save / discard).</summary>
    internal bool ConfirmLeave()
    {
        if (!_host.HasUnsavedChanges) return true;
        switch (PromptUnsaved())
        {
            case UnsavedChoice.Save: _host.SaveConfig(); return true;
            case UnsavedChoice.Discard: _host.DiscardChanges(); return true;
            default: return false;
        }
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

    /// <summary>Content root of the panel (used by the headless preview, which draws it at full height).</summary>
    internal Control Content => _stack!;

    private void RefitSoon()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(new Action(() => FitToScreen(reposition: true))); } catch { }
    }

    /// <summary>High DPI / small screens: never taller than the work area — scroll instead. Re-evaluated when a section opens or closes.</summary>
    internal void FitToScreen(bool reposition = true)
    {
        try
        {
            var wa = Screen.FromControl(this).WorkingArea;
            if (_scrolling)
            {
                // back to natural size first, so closing a section shrinks the window again
                AutoScroll = false;
                AutoSize = true;
                PerformLayout();
                _scrolling = false;
            }
            if (Height > wa.Height - 16)
            {
                AutoSize = false;
                AutoScroll = true;
                Size = new Size(Width + SystemInformation.VerticalScrollBarWidth, wa.Height - 16);
                _scrolling = true;
            }
            if (!reposition) return;
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
        // X behaves like UniKey's "Đóng": hide to the tray, keep the effect running — after asking about unsaved edits.
        if (!_forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (ConfirmLeave()) Hide();
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


    /// <summary>Collapsible section: a clickable header (chevron + title) that shows or hides its content.</summary>
    private sealed class Expander : FlowLayoutPanel
    {
        private readonly Control _content;
        private readonly ExpanderHeader _header;
        public Expander(Palette p, string title, Control content, bool expanded)
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = p.Back; Margin = Padding.Empty;
            _content = content;
            _header = new ExpanderHeader(p, title) { Width = CardWidth, Height = 34, Margin = new Padding(0, 0, 0, 6) };
            _header.Click += (_, _) => Expanded = !Expanded;
            Controls.Add(_header);
            Controls.Add(_content);
            Expanded = expanded;
        }

        private bool _expanded;
        public event Action? Toggled;
        public bool Expanded
        {
            get => _expanded;
            set
            {
                bool changed = _expanded != value;
                _expanded = value; _content.Visible = value; _header.Open = value; _header.Margin = new Padding(0, 0, 0, value ? 6 : 0);
                if (changed) Toggled?.Invoke();
            }
        }
    }

    private sealed class ExpanderHeader : Control
    {
        private readonly Palette _p;
        private bool _hover;
        public bool Open { get; set; }
        public ExpanderHeader(Palette p, string title)
        {
            _p = p; Text = title; Cursor = Cursors.Hand; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(_p.Back)) g.FillRectangle(bg, ClientRectangle);
            using var path = Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
            using (var fill = new SolidBrush(_hover ? _p.Hover : _p.Card)) g.FillPath(fill, path);
            using (var pen = new Pen(Focused ? _p.Accent : _p.Border)) g.DrawPath(pen, path);
            // chevron: ▸ closed, ▾ open
            float cx = 18, cy = Height / 2f;
            using var chev = new Pen(_p.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (Open) g.DrawLines(chev, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
            else g.DrawLines(chev, new[] { new PointF(cx - 2, cy - 4), new PointF(cx + 2, cy), new PointF(cx - 2, cy + 4) });
            TextRenderer.DrawText(g, Text, new Font("Segoe UI Semibold", 10.5f), new Rectangle(34, 0, Width - 40, Height), _p.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Flat themed slider (0..100): grey track, accent fill, round thumb. Mouse and arrow keys.</summary>
    internal sealed class DarkSlider : Control
    {
        private int _value;
        private bool _drag;
        public int Minimum => 0;
        public int Maximum => 100;
        public event EventHandler? ValueChanged;
        private Palette P => s_palette ?? Palette.Current();

        public DarkSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 24; TabStop = true; Cursor = Cursors.Hand;
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Clamp(value, 0, 100);
                if (v == _value) return;
                _value = v; Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private int FromX(int x) => (int)Math.Round(Math.Clamp((x - 9f) / Math.Max(1f, Width - 18f), 0f, 1f) * 100f);
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _drag = true; Focus(); Value = FromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) Value = FromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; }
        protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left or Keys.Down: Value -= 5; break;
                case Keys.Right or Keys.Up: Value += 5; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = 100; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = P; var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? p.Card)) g.FillRectangle(bg, ClientRectangle);
            float cy = Height / 2f, x0 = 9, x1 = Width - 9, xv = x0 + (x1 - x0) * _value / 100f;
            using var track = new Pen(p.Border, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(track, x0, cy, x1, cy);
            if (_value > 0)
            {
                using var fill = new Pen(Enabled ? p.Accent : p.Border, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(fill, x0, cy, xv, cy);
            }
            using var thumb = new SolidBrush(Enabled ? (Focused ? ControlPaint.Light(p.Accent, 0.2f) : Color.White) : p.Border);
            using var rim = new Pen(p.Accent, 1.4f);
            g.FillEllipse(thumb, xv - 7, cy - 7, 14, 14);
            g.DrawEllipse(rim, xv - 7, cy - 7, 14, 14);
        }
    }
    #endregion
}
