namespace AudioTaskbar.Tests;

/// <summary>4. Cut-out / UIA math: overlapping or off-bar rects, DPI 100/125/150 %, HUD never on icons.</summary>
public class LayoutTests
{
    private static TaskbarRegions Bar(int x, int w, int h, params (int L, int R)[] icons)
    {
        var r = new TaskbarRegions { Bounds = new Rectangle(x, 1080 - h, w, h) };
        foreach (var (l, rr) in icons) r.Cutouts.Add(Rectangle.FromLTRB(x + l, 1080 - h, x + rr, 1080));
        return r;
    }

    private static void AssertNoOverlap(List<(int L, int R)> spans, TaskbarRegions r, int guard)
    {
        foreach (var s in spans)
        {
            Assert.True(s.L < s.R);
            Assert.InRange(s.L, 0, r.Bounds.Width);
            Assert.InRange(s.R, 0, r.Bounds.Width);
            foreach (var c in r.Cutouts)
            {
                int l = c.Left - r.Bounds.X - guard, rr = c.Right - r.Bounds.X + guard;
                Assert.True(s.R <= l || s.L >= rr, $"span {s.L}-{s.R} overlaps icon {l}-{rr}");
            }
        }
    }

    [Fact]
    public void No_icons_gives_one_full_span()
    {
        var r = Bar(0, 1920, 48);
        Assert.Equal(new[] { (0, 1920) }, TaskbarService.FreeSpans(r, 1920, 10));
    }

    [Fact]
    public void Spans_avoid_every_icon_with_guard()
    {
        var r = Bar(0, 1920, 48, (8, 198), (516, 1403), (1653, 1920));
        var spans = TaskbarService.FreeSpans(r, 1920, 12);
        AssertNoOverlap(spans, r, 12);
        Assert.Equal(2, spans.Count);
    }

    [Fact]
    public void Overlapping_and_duplicate_cutouts()
    {
        var r = Bar(0, 1920, 48, (500, 900), (800, 1000), (800, 1000), (950, 1100), (1090, 1095));
        var spans = TaskbarService.FreeSpans(r, 1920, 6);
        AssertNoOverlap(spans, r, 6);
        Assert.Equal(new[] { (0, 494), (1106, 1920) }, spans);
    }

    [Fact]
    public void Out_of_bounds_cutouts_are_safe()
    {
        var r = Bar(0, 1920, 48, (-300, 40), (1880, 2600), (5000, 6000), (-900, -800));
        var spans = TaskbarService.FreeSpans(r, 1920, 4);
        AssertNoOverlap(spans, r, 4);
        Assert.Single(spans);
        Assert.Equal((44, 1876), spans[0]);
    }

    [Fact]
    public void Icons_covering_the_whole_bar_leave_no_span()
    {
        var r = Bar(0, 1920, 48, (-10, 2000));
        Assert.Empty(TaskbarService.FreeSpans(r, 1920, 4));
    }

    [Fact]
    public void Secondary_monitor_offsets_are_relative_to_bar()
    {
        var r = Bar(1920, 1920, 48, (100, 300));
        var spans = TaskbarService.FreeSpans(r, 1920, 0);
        Assert.Equal(new[] { (0, 100), (300, 1920) }, spans);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Dpi_scaled_layouts_keep_icons_clear(double scale)
    {
        int S(double v) => (int)Math.Round(v * scale);
        int w = S(1920), h = S(48);
        var r = Bar(0, w, h, (S(8), S(198)), (S(516), S(1403)), (S(1653), w));
        int guard = S(28) + S(10);
        var spans = TaskbarService.FreeSpans(r, w, guard, 6);
        AssertNoOverlap(spans, r, guard);

        var vis = LayoutMath.ColumnVisibility(r.Cutouts, r.Bounds, w, S(28), S(10));
        Assert.Equal(w, vis.Length);
        foreach (var c in r.Cutouts)
            for (int x = Math.Max(0, c.Left - S(28)); x < Math.Min(w, c.Right + S(28)); x++)
                Assert.Equal(0f, vis[x]);
        Assert.All(vis, v => Assert.InRange(v, 0f, 1f));
        Assert.Equal(1f, vis[(S(198) + S(516)) / 2]);

        foreach (bool right in new[] { false, true })
        {
            var badge = LayoutMath.PlaceBadge(spans, new SizeF(S(110), S(30)), 4, h, right);
            Assert.False(badge.IsEmpty);
            Assert.True(badge.Top >= 0 && badge.Bottom <= h);
            foreach (var c in r.Cutouts)
            {
                var icon = RectangleF.FromLTRB(c.Left - r.Bounds.X, 0, c.Right - r.Bounds.X, h);
                Assert.False(badge.IntersectsWith(icon), $"HUD {badge} covers icon {icon} at {scale:P0}");
            }
        }
    }

    [Fact]
    public void Hud_is_hidden_when_it_cannot_fit()
    {
        var spans = new List<(int L, int R)> { (0, 50), (100, 160) };
        Assert.True(LayoutMath.PlaceBadge(spans, new SizeF(120, 30), 4, 48, false).IsEmpty);
        Assert.True(LayoutMath.PlaceBadge(new List<(int, int)>(), new SizeF(10, 10), 4, 48, true).IsEmpty);
        Assert.True(LayoutMath.PlaceBadge(spans, new SizeF(0, 30), 4, 48, true).IsEmpty);
        Assert.True(LayoutMath.PlaceBadge(spans, new SizeF(-5, 30), 4, 48, true).IsEmpty);
    }

    [Fact]
    public void Hud_left_and_right_choose_outer_spans()
    {
        var spans = new List<(int L, int R)> { (0, 200), (500, 700), (1500, 1900) };
        var left = LayoutMath.PlaceBadge(spans, new SizeF(100, 30), 4, 48, false);
        var right = LayoutMath.PlaceBadge(spans, new SizeF(100, 30), 4, 48, true);
        Assert.Equal(4f, left.X);
        Assert.Equal(1900f - 4 - 100, right.X);
        Assert.Equal(9f, left.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Column_visibility_with_zero_or_negative_width(int width)
    {
        var vis = LayoutMath.ColumnVisibility(new[] { new Rectangle(0, 0, 50, 48) }, new Rectangle(0, 0, 0, 48), width, 10, 5);
        Assert.Empty(vis);
    }

    [Fact]
    public void Column_visibility_with_offscreen_cutouts()
    {
        var vis = LayoutMath.ColumnVisibility(new[] { new Rectangle(-500, 0, 100, 48), new Rectangle(9000, 0, 100, 48) }, new Rectangle(0, 0, 1920, 48), 1920, 10, 5);
        Assert.All(vis, v => Assert.Equal(1f, v));
    }

    [Fact]
    public void Column_visibility_feather_is_monotonic()
    {
        var vis = LayoutMath.ColumnVisibility(new[] { Rectangle.FromLTRB(1000, 0, 1100, 48) }, new Rectangle(0, 0, 1920, 48), 1920, 10, 20);
        for (int x = 1110; x < 1140; x++) Assert.True(vis[x + 1] >= vis[x]);
        for (int x = 960; x < 990; x++) Assert.True(vis[x + 1] <= vis[x]);
    }
}
