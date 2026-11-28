namespace AudioTaskbar.Tests;

/// <summary>
/// v5.4.2: the control panel at 100–200 % display scale (headless). It is laid out exactly as on a monitor with that DPI
/// (<see cref="PortalForm.ApplyScale"/> is the same code the window runs on DPI changes): nothing may reach past the visible
/// client area, nothing may be cut or wrapped, and on a short screen only a vertical scroll bar appears, beside the content.
/// </summary>
[Collection("Statics")]
public class PortalDpiTests
{
    public static IEnumerable<object[]> Scales() => new[] { 1f, 1.25f, 1.5f, 1.75f, 2f }.Select(s => new object[] { s });

    private static void Sta(Action a)
    {
        Exception? err = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw new Exception("STA test failed: " + err, err);
    }

    private static void WithPanel(float scale, Action<PortalForm> body, AppConfig? cfg = null) => Sta(() =>
    {
        cfg ??= new AppConfig { Season = "cycle", WeatherPreview = "random" };   // every conditional row shown
        var host = new FakePortalHost(cfg);
        using var f = new PortalForm(host) { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
        f.Show();
        f.SettingsExpanded = true; f.SoundExpanded = true;
        f.ApplyScale(scale);
        f.FitTo(new Rectangle(-32000, -32000, 6000, 6000), reposition: false);   // natural size, no scroll bar
        for (int i = 0; i < 3; i++) Application.DoEvents();
        try { body(f); }
        finally { f.ForceClose(); }
    });

    private static IEnumerable<Control> Shown(PortalForm f) => f.AllControls().Where(c => c.Visible);

    private static Rectangle InForm(PortalForm f, Control c) => f.RectangleToClient(c.Parent!.RectangleToScreen(c.Bounds));

    private static void AssertNothingClipped(PortalForm f, float scale)
    {
        int clientW = f.ClientSize.Width;
        foreach (var c in Shown(f))
        {
            var r = InForm(f, c);
            Assert.True(r.Left >= 0 && r.Right <= clientW, $"{scale:0.##}×: {Describe(c)} spans {r.Left}..{r.Right}, client is {clientW} wide");
            if (c.Parent is not Form)
            {
                Assert.True(c.Right <= c.Parent!.ClientSize.Width, $"{scale:0.##}×: {Describe(c)} ends at {c.Right}, its container is {c.Parent.ClientSize.Width} wide");
                Assert.True(c.Bottom <= c.Parent.ClientSize.Height, $"{scale:0.##}×: {Describe(c)} ends at y={c.Bottom}, its container {Describe(c.Parent)} is {c.Parent.ClientSize.Height} tall");
            }
            if (c is Label { AutoSize: true } or CheckBox { AutoSize: true })
                Assert.True(c.Width >= c.GetPreferredSize(Size.Empty).Width - 1 || c.MaximumSize.Width > 0,
                    $"{scale:0.##}×: {Describe(c)} is cut ({c.Width} < {c.GetPreferredSize(Size.Empty).Width})");
            if (c is ComboBox cb)
                foreach (var item in cb.Items)
                    Assert.True(TextRenderer.MeasureText(cb.GetItemText(item), cb.Font).Width + 20 * scale <= cb.Width,
                        $"{scale:0.##}×: drop-down entry '{item}' does not fit ({cb.Width} px)");
        }
    }

    private static string Describe(Control c) => c.GetType().Name + (c.Text.Length > 0 ? " '" + c.Text + "'" : "");

    [Theory]
    [MemberData(nameof(Scales))]
    public void Every_control_fits_inside_the_visible_client_width(float scale)
    {
        WithPanel(scale, f =>
        {
            Assert.Equal(scale, f.UiScale, 3);
            AssertNothingClipped(f, scale);
            Assert.True(f.ClientSize.Width >= f.ContentWidthNeeded);
            Assert.True(f.CardWidthPx >= (int)(PortalForm.CardWidth * scale) - 1);
        });
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Row_labels_stay_on_one_line_and_the_long_check_boxes_are_whole(float scale)
    {
        WithPanel(scale, f =>
        {
            var labels = f.AllControls().OfType<Label>().Where(l => l.Parent is TableLayoutPanel t && t.Parent?.GetType().Name == "Card"
                                                                     && t.GetColumn(l) == 0 && t.GetColumnSpan(l) == 1 && l.Visible).ToList();
            Assert.True(labels.Count > 15);
            foreach (var l in labels)
            {
                Assert.True(l.Height <= l.Font.Height * 1.6, $"{scale:0.##}×: label '{l.Text}' wraps ({l.Height} px tall)");
                int pref = l.GetPreferredSize(Size.Empty).Width;
                Assert.True(l.Width >= pref, $"{scale:0.##}×: label '{l.Text}' is cut ({l.Width} < {pref})");
                Assert.True(l.Right <= f.LabelColumnPx, $"{scale:0.##}×: label '{l.Text}' runs into the value column");
            }
            foreach (var text in new[] { "Chim, đèn phố, đom đóm", "Hiện quanh icon ứng dụng", "Xe, thuyền theo khu vực", "Mưa, tuyết, sấm chớp", "Tự phối theo cảnh" })
            {
                var cb = f.AllControls().OfType<CheckBox>().Single(c => c.Text == text);
                Assert.True(cb.Width >= cb.GetPreferredSize(Size.Empty).Width, $"{scale:0.##}×: '{text}' is cut");
                Assert.True(InForm(f, cb).Right <= f.ClientSize.Width);
            }
            foreach (var unit in f.AllControls().OfType<Label>().Where(l => l.Text is "phút" or "phút / vòng" or "phút / ngày"))
                Assert.True(InForm(f, unit).Right <= f.ClientSize.Width, $"{scale:0.##}×: unit '{unit.Text}' is cut");
        });
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Header_status_line_is_whole_and_without_the_removed_text(float scale)
    {
        WithPanel(scale, f =>
        {
            var status = f.AllControls().OfType<Label>().Single(l => l.Text.StartsWith("● "));
            Assert.DoesNotContain("Hòa nền", status.Text);
            int pref = status.GetPreferredSize(Size.Empty).Width;
            Assert.True(status.Width >= pref, $"{scale:0.##}×: status cut ({status.Width} < {pref})");
            Assert.True(InForm(f, status).Right <= f.ClientSize.Width);
            var dirty = f.AllControls().OfType<Label>().Single(l => l.Text is "✓ Đã lưu" or "● Chưa lưu");
            Assert.True(InForm(f, status).Right <= InForm(f, dirty).Left, $"{scale:0.##}×: status line {InForm(f, status)} runs into the saved hint {InForm(f, dirty)}");
        });
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Fonts_and_fixed_sizes_follow_the_scale(float scale)
    {
        WithPanel(scale, f =>
        {
            Assert.InRange(f.Font.Size, 9.5f * 96f / 72f * scale - 0.01f, 9.5f * 96f / 72f * scale + 0.01f);
            Assert.Equal(GraphicsUnit.Pixel, f.Font.Unit);
            Assert.InRange(f.MasterSlider.Width, (int)(230 * scale) - 1, (int)(230 * scale) + 1);
            foreach (var n in f.AllControls().OfType<NumericUpDown>())
                Assert.True(n.Parent!.Height >= n.PreferredHeight, "number box cut vertically");
        });
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void A_short_screen_scrolls_vertically_only_and_the_bar_never_covers_the_content(float scale)
    {
        WithPanel(scale, f =>
        {
            int screenH = 600;
            f.FitTo(new Rectangle(-32000, -32000, 3000, screenH), reposition: false);
            for (int i = 0; i < 3; i++) Application.DoEvents();
            Assert.True(f.Scrolling);
            Assert.True(f.Height <= screenH);
            Assert.True(f.AutoScroll);
            Assert.False(f.HorizontalScroll.Visible, "no horizontal scroll bar");
            Assert.True(f.ClientSize.Width >= f.ContentWidthNeeded, $"client {f.ClientSize.Width} < content {f.ContentWidthNeeded}");
            AssertNothingClipped(f, scale);
            // closing the sections and fitting a tall screen goes back to the natural size without a scroll bar
            f.SettingsExpanded = false; f.SoundExpanded = false;
            f.FitTo(new Rectangle(-32000, -32000, 3000, 4000), reposition: false);
            Assert.False(f.Scrolling);
        });
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Every_card_is_as_tall_as_its_content(float scale)
    {
        WithPanel(scale, f =>
        {
            var cards = f.AllControls().Where(c => c.GetType().Name == "Card").ToList();
            Assert.True(cards.Count >= 6);
            foreach (var card in cards)
            {
                var grid = card.Controls.OfType<TableLayoutPanel>().Single();
                Assert.True(card.Height >= grid.Height + card.Padding.Vertical, $"{scale:0.##}×: card '{f.CardTitleOf(grid.Controls[0])}' is {card.Height} tall, content {grid.Height}");
                Assert.True(grid.Height > 30 * scale, $"{scale:0.##}×: card grid collapsed ({grid.Height})");
            }
        });
    }

    [Fact]
    public void Scaling_twice_never_compounds()
    {
        WithPanel(1.5f, f =>
        {
            int card = f.CardWidthPx, label = f.LabelColumnPx;
            string why = f.WidthDriver;
            string Dump() => string.Join("; ", new Control[] { f.AutoMixBox, f.RemixButton }.Select(c =>
                $"{c.GetType().Name} pref={c.GetPreferredSize(Size.Empty).Width} w={c.Width} m={c.Margin.Horizontal} pad={c.Padding.Horizontal} font={c.Font.Size}{c.Font.Unit} vis={c.Visible}"));
            string before = Dump();
            f.ApplyScale(2f);
            f.ApplyScale(1.5f);
            Assert.True(card == f.CardWidthPx, $"card {card} ({why}) → {f.CardWidthPx} ({f.WidthDriver}) | {before} || {Dump()}");
            Assert.Equal(label, f.LabelColumnPx);
            f.ApplyScale(1f);
            Assert.True(f.CardWidthPx < card);
        });
    }
}
