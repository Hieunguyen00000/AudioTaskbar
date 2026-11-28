namespace AudioTaskbar.Tests;

/// <summary>
/// Opening Start (Win key) makes the UIA probe of Shell_TrayWnd often return
/// zero items. The conservative fallback then covers almost the whole bar,
/// OverlayWindow treats free space as too small and lays landmarks across the
/// full width, and scenery jumps. These tests lock the hold/ignore policy.
/// </summary>
public class StartMenuLayoutTests
{
    private static readonly Rectangle Bar = new(0, 1032, 1920, 48);
    private static readonly Rectangle Task = new(452, 1032, 1017, 48);
    private static readonly Rectangle Tray = new(1706, 1032, 214, 48);

    private static TaskbarLayoutSnapshot GoodUia(int uia = 27)
    {
        var s = new TaskbarLayoutSnapshot
        {
            Bounds = Bar,
            TaskArea = Task,
            TrayArea = Tray,
            Source = $"uia({uia})+legacy(2)",
            UiaCount = uia,
            At = DateTime.UtcNow,
        };
        s.Items.Add(new Rectangle(0, 1032, 48, 48));
        s.Items.Add(Task);
        s.Items.Add(Tray);
        return s;
    }

    // Matches TaskbarLayoutProbe when uia==0: keep = min(width/8, height*3) = 144
    // and cutouts 144-1920, as seen in app.log around Start-menu presses.
    private static TaskbarLayoutSnapshot FallbackConservative()
    {
        var s = new TaskbarLayoutSnapshot
        {
            Bounds = Bar,
            TaskArea = new Rectangle(497, 1032, 532, 48),
            TrayArea = Rectangle.Empty,
            Source = "fallback-conservative(legacy=1)",
            UiaCount = 0,
            At = DateTime.UtcNow,
        };
        s.Items.Add(new Rectangle(0, 1032, 48, 48));
        return s;
    }

    [Fact]
    public void Good_uia_is_reliable_and_fallback_is_not()
    {
        Assert.True(TaskbarLayoutHold.IsReliable(GoodUia()));
        Assert.False(TaskbarLayoutHold.IsReliable(FallbackConservative()));
        Assert.False(TaskbarLayoutHold.IsReliable(null));
        Assert.False(TaskbarLayoutHold.IsReliable(new TaskbarLayoutSnapshot
        {
            Bounds = Bar,
            Source = "autohidden",
            UiaCount = 0,
        }));
    }

    [Fact]
    public void Start_menu_fallback_keeps_last_good_uia_snapshot()
    {
        Assert.True(TaskbarLayoutHold.ShouldRetain(GoodUia(), FallbackConservative()));
    }

    [Fact]
    public void First_probe_may_use_fallback_when_nothing_is_cached()
    {
        Assert.False(TaskbarLayoutHold.ShouldRetain(null, FallbackConservative()));
    }

    [Fact]
    public void Fresh_reliable_uia_replaces_previous()
    {
        var previous = GoodUia(27);
        var next = GoodUia(28);
        next.Source = "uia(28)+legacy(2)";
        next.Items.Add(new Rectangle(200, 1032, 48, 48));
        Assert.False(TaskbarLayoutHold.ShouldRetain(previous, next));
    }

    [Fact]
    public void Geometry_change_accepts_new_snapshot_even_if_fallback()
    {
        var moved = FallbackConservative();
        moved.Bounds = new Rectangle(0, 1032, 2560, 48);
        Assert.False(TaskbarLayoutHold.ShouldRetain(GoodUia(), moved));
    }

    [Fact]
    public void Partial_tree_missing_tray_is_treated_as_degraded()
    {
        var partial = GoodUia(8);
        partial.Source = "uia(8)+legacy(1)";
        partial.UiaCount = 8;
        partial.TrayArea = Rectangle.Empty;
        partial.Items.Clear();
        partial.Items.Add(new Rectangle(0, 1032, 48, 48));
        Assert.True(TaskbarLayoutHold.ShouldRetain(GoodUia(27), partial));
    }

    [Fact]
    public void Slight_uia_count_change_with_tray_intact_is_not_degraded()
    {
        var next = GoodUia(24);
        next.Source = "uia(24)+legacy(2)";
        Assert.False(TaskbarLayoutHold.ShouldRetain(GoodUia(27), next));
    }

    [Fact]
    public void Previous_fallback_is_not_held_over_another_fallback()
    {
        Assert.False(TaskbarLayoutHold.ShouldRetain(FallbackConservative(), FallbackConservative()));
    }

    [Fact]
    public void Autohide_is_a_real_state_change_and_is_not_held()
    {
        var hidden = new TaskbarLayoutSnapshot
        {
            Bounds = Bar,
            Source = "autohidden",
            UiaCount = 0,
            At = DateTime.UtcNow,
        };
        Assert.False(TaskbarLayoutHold.ShouldRetain(GoodUia(), hidden));
    }

    [Fact]
    public void Fallback_cutouts_would_rebuild_scenery_spans()
    {
        // Documents why holding the snapshot matters: the conservative fallback
        // plus OverlayWindow's "too little free space → use full bar" rule would
        // relocate landmarks across the whole taskbar.
        var good = new TaskbarRegions { Bounds = Bar };
        good.Cutouts.Add(Rectangle.FromLTRB(452, 1032, 1469, 1080));
        good.Cutouts.Add(Rectangle.FromLTRB(1706, 1032, 1920, 1080));
        var fb = new TaskbarRegions { Bounds = Bar };
        fb.Cutouts.Add(Rectangle.FromLTRB(144, 1032, 1920, 1080));

        var goodSpans = TaskbarService.FreeSpans(good, 1920, 10);
        var fbSpans = TaskbarService.FreeSpans(fb, 1920, 10);
        Assert.NotEqual(goodSpans, fbSpans);
        int fbFree = fbSpans.Sum(s => s.R - s.L);
        Assert.True(fbFree < (int)(1920 * 0.08f),
            "fallback free space is small enough to trigger full-bar landmark layout");

        var goodReg = new TaskbarRegions { Bounds = Bar, LayoutSource = "uia(28)+legacy(2)" };
        var fbReg = new TaskbarRegions { Bounds = Bar, LayoutSource = "fallback-conservative(legacy=1)" };
        Assert.True(TaskbarLayoutHold.ShouldRetainRegions(goodReg, fbReg));
        Assert.False(TaskbarLayoutHold.ShouldExpandCollapsedSpansToFullWidth(fbReg.LayoutSource, fbFree, 1920));
        Assert.True(TaskbarLayoutHold.ShouldExpandCollapsedSpansToFullWidth(goodReg.LayoutSource, fbFree, 1920));
    }

    [Fact]
    public void Overlay_keeps_existing_layout_on_transient_fallback()
    {
        Assert.True(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout("fallback-conservative(legacy=1)", true));
        Assert.False(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout("fallback-conservative(legacy=1)", false));
        Assert.False(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout("uia(28)+legacy(2)", true));
        Assert.False(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout("autohidden", true));
    }

    [Fact]
    public void Regions_hold_ignores_fallback_but_not_autohide_or_bounds_change()
    {
        var good = new TaskbarRegions
        {
            Bounds = Bar,
            LayoutSource = "uia(28)+legacy(2)",
            Signature = "good",
        };
        var fallback = new TaskbarRegions
        {
            Bounds = Bar,
            LayoutSource = "fallback-conservative(legacy=1)",
            Signature = "fb",
        };
        Assert.True(TaskbarLayoutHold.ShouldRetainRegions(good, fallback));

        var hidden = new TaskbarRegions { Bounds = Bar, LayoutSource = "autohidden", AutoHidden = true };
        Assert.False(TaskbarLayoutHold.ShouldRetainRegions(good, hidden));

        var moved = new TaskbarRegions
        {
            Bounds = new Rectangle(0, 1032, 2560, 48),
            LayoutSource = "fallback-conservative(legacy=1)",
        };
        Assert.False(TaskbarLayoutHold.ShouldRetainRegions(good, moved));
        Assert.False(TaskbarLayoutHold.ShouldRetainRegions(null, fallback));
        Assert.False(TaskbarLayoutHold.ShouldRetainRegions(fallback, fallback));
    }
}
