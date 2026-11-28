namespace AudioTaskbar.Tests;

/// <summary>
/// Opening Start on a secondary monitor can drop Shell_SecondaryTrayWnd from
/// EnumWindows while the hwnd stays alive. GetTaskbars used to return only
/// the primary bar, and SyncOverlays then Close+Dispose'd the secondary
/// overlay — the diorama vanished. These tests lock the merge/retention
/// policy for multi-monitor Start open/close on each bar.
/// </summary>
public class SecondaryStartLayoutTests
{
    private static readonly IntPtr Primary = new(0x100);
    private static readonly IntPtr Secondary = new(0x200);
    private static readonly IntPtr OtherSecondary = new(0x300);
    private static readonly Rectangle PrimaryBounds = new(0, 1032, 1920, 48);
    private static readonly Rectangle SecondaryBounds = new(-1920, 1032, 1920, 48);

    private static TaskbarInfo PrimaryBar()
        => new(Primary, PrimaryBounds, true);

    private static TaskbarInfo SecondaryBar(IntPtr? hwnd = null, Rectangle? bounds = null)
        => new(hwnd ?? Secondary, bounds ?? SecondaryBounds, false);

    private static TaskbarInfo? TryReadKnown(IntPtr h)
    {
        if (h == Secondary) return SecondaryBar();
        if (h == OtherSecondary) return new TaskbarInfo(OtherSecondary, new Rectangle(1920, 1032, 1920, 48), false);
        return null;
    }

    [Fact]
    public void Secondary_Start_recovers_bar_missed_by_EnumWindows()
    {
        // Primary is still found via FindWindow("Shell_TrayWnd"); the secondary
        // hwnd is gone from EnumWindows but still alive (FindWindowEx / cache).
        var found = new[] { PrimaryBar() };
        var merged = TaskbarService.MergeSecondaryTaskbars(found, new[] { Secondary }, Primary, TryReadKnown);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, b => b.IsPrimary && b.Hwnd == Primary && b.Bounds == PrimaryBounds);
        Assert.Contains(merged, b => !b.IsPrimary && b.Hwnd == Secondary && b.Bounds == SecondaryBounds);
    }

    [Fact]
    public void Primary_Start_keeps_both_bars_when_both_are_enumerated()
    {
        var found = new[] { PrimaryBar(), SecondaryBar() };
        var merged = TaskbarService.MergeSecondaryTaskbars(found, new[] { Secondary }, Primary, TryReadKnown);
        Assert.Equal(2, merged.Count);
        Assert.Single(merged, b => b.Hwnd == Secondary);
        Assert.Single(merged, b => b.Hwnd == Primary);
    }

    [Fact]
    public void Start_close_keeps_the_same_secondary_hwnd()
    {
        var whileOpen = TaskbarService.MergeSecondaryTaskbars(
            new[] { PrimaryBar() }, new[] { Secondary }, Primary, TryReadKnown);
        var afterClose = TaskbarService.MergeSecondaryTaskbars(
            new[] { PrimaryBar(), SecondaryBar() }, new[] { Secondary }, Primary, TryReadKnown);
        Assert.Equal(Secondary, Assert.Single(whileOpen, b => !b.IsPrimary).Hwnd);
        Assert.Equal(Secondary, Assert.Single(afterClose, b => !b.IsPrimary).Hwnd);
        Assert.Equal(2, afterClose.Count);
    }

    [Fact]
    public void Merge_does_not_duplicate_an_already_enumerated_secondary()
    {
        var found = new[] { PrimaryBar(), SecondaryBar() };
        var merged = TaskbarService.MergeSecondaryTaskbars(found, new[] { Secondary, Secondary }, Primary, TryReadKnown);
        Assert.Equal(2, merged.Count);
        Assert.Single(merged, b => b.Hwnd == Secondary);
    }

    [Fact]
    public void Merge_drops_a_dead_or_unusable_cached_secondary()
    {
        var found = new[] { PrimaryBar() };
        var merged = TaskbarService.MergeSecondaryTaskbars(found, new[] { Secondary }, Primary, _ => null);
        Assert.Single(merged);
        Assert.Equal(Primary, merged[0].Hwnd);
        Assert.True(merged[0].IsPrimary);
    }

    [Fact]
    public void Merge_does_not_readd_the_primary_hwnd_as_a_secondary()
    {
        var found = new[] { PrimaryBar() };
        var merged = TaskbarService.MergeSecondaryTaskbars(found, new[] { Primary }, Primary,
            h => new TaskbarInfo(h, PrimaryBounds, false));
        Assert.Single(merged);
        Assert.True(merged[0].IsPrimary);
        Assert.Equal(Primary, merged[0].Hwnd);
    }

    [Fact]
    public void Merge_recovers_two_secondaries_when_one_is_missed()
    {
        var found = new[] { PrimaryBar(), SecondaryBar() };
        var merged = TaskbarService.MergeSecondaryTaskbars(
            found, new[] { Secondary, OtherSecondary }, Primary, TryReadKnown);
        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, b => b.Hwnd == OtherSecondary);
    }

    [Fact]
    public void Overlay_is_parked_not_disposed_when_secondary_is_transiently_missing()
    {
        Assert.Equal(TaskbarOverlayRetention.Keep, TaskbarService.DecideOverlayRetention(true, true));
        Assert.Equal(TaskbarOverlayRetention.Keep, TaskbarService.DecideOverlayRetention(true, false));
        Assert.Equal(TaskbarOverlayRetention.Park, TaskbarService.DecideOverlayRetention(false, true));
        Assert.Equal(TaskbarOverlayRetention.Dispose, TaskbarService.DecideOverlayRetention(false, false));
    }

    [Fact]
    public void Secondary_fallback_is_held_even_when_that_bar_has_no_tray_cluster()
    {
        var secondaryGood = new TaskbarLayoutSnapshot
        {
            Bounds = SecondaryBounds,
            TaskArea = new Rectangle(-1468, 1032, 1017, 48),
            TrayArea = Rectangle.Empty,
            Source = "uia(12)+legacy(1)",
            UiaCount = 12,
            At = DateTime.UtcNow,
        };
        secondaryGood.Items.Add(new Rectangle(-1920, 1032, 48, 48));
        secondaryGood.Items.Add(secondaryGood.TaskArea);

        var secondaryFallback = new TaskbarLayoutSnapshot
        {
            Bounds = SecondaryBounds,
            TaskArea = new Rectangle(-1423, 1032, 532, 48),
            TrayArea = Rectangle.Empty,
            Source = "fallback-conservative(legacy=1)",
            UiaCount = 0,
            At = DateTime.UtcNow,
        };
        secondaryFallback.Items.Add(new Rectangle(-1920, 1032, 48, 48));

        Assert.True(TaskbarLayoutHold.ShouldRetain(secondaryGood, secondaryFallback));

        var primaryGood = new TaskbarLayoutSnapshot
        {
            Bounds = PrimaryBounds,
            TaskArea = new Rectangle(452, 1032, 1017, 48),
            TrayArea = new Rectangle(1706, 1032, 214, 48),
            Source = "uia(27)+legacy(2)",
            UiaCount = 27,
            At = DateTime.UtcNow,
        };
        // A fallback on the secondary snapshot does not change the primary
        // pair: each hwnd's hold is a function of that bar's own previous/next.
        Assert.False(TaskbarLayoutHold.ShouldRetain(primaryGood, primaryGood));
        Assert.True(TaskbarLayoutHold.ShouldRetain(secondaryGood, secondaryFallback));
        Assert.False(TaskbarLayoutHold.ShouldRetain(primaryGood, secondaryFallback)); // bounds differ
    }

    [Fact]
    public void Regions_hold_is_independent_on_primary_and_secondary_bars()
    {
        var primaryGood = new TaskbarRegions
        {
            Bounds = PrimaryBounds,
            LayoutSource = "uia(28)+legacy(2)",
            Signature = "primary-good",
        };
        var primaryFallback = new TaskbarRegions
        {
            Bounds = PrimaryBounds,
            LayoutSource = "fallback-conservative(legacy=1)",
            Signature = "primary-fb",
        };
        var secondaryGood = new TaskbarRegions
        {
            Bounds = SecondaryBounds,
            LayoutSource = "uia(12)+legacy(1)",
            Signature = "secondary-good",
        };
        var secondaryFallback = new TaskbarRegions
        {
            Bounds = SecondaryBounds,
            LayoutSource = "fallback-conservative(legacy=1)",
            Signature = "secondary-fb",
        };

        Assert.True(TaskbarLayoutHold.ShouldRetainRegions(primaryGood, primaryFallback));
        Assert.True(TaskbarLayoutHold.ShouldRetainRegions(secondaryGood, secondaryFallback));
        Assert.False(TaskbarLayoutHold.ShouldRetainRegions(primaryGood, secondaryFallback));
        Assert.False(TaskbarLayoutHold.ShouldRetainRegions(secondaryGood, primaryFallback));

        Assert.True(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout(secondaryFallback.LayoutSource, true));
        Assert.True(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout(primaryFallback.LayoutSource, true));
        Assert.False(TaskbarLayoutHold.ShouldKeepExistingOverlayLayout("uia(12)+legacy(1)", true));
    }
}
