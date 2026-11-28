namespace AudioTaskbar.Tests;

/// <summary>
/// Settings > Personalization > Taskbar: orientation, auto-hide sliver,
/// unusable 0x0 bounds during Explorer restart, and conservative fallback
/// on a vertical bar.
/// </summary>
public class TaskbarPersonalizeTests
{
    private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Bottom_and_top_bars_are_horizontal_left_and_right_are_vertical()
    {
        Assert.False(TaskbarGeometry.IsVertical(new Rectangle(0, 1032, 1920, 48)));
        Assert.False(TaskbarGeometry.IsVertical(new Rectangle(0, 0, 1920, 48)));
        Assert.True(TaskbarGeometry.IsVertical(new Rectangle(0, 0, 48, 1080)));
        Assert.True(TaskbarGeometry.IsVertical(new Rectangle(1872, 0, 48, 1080)));
        Assert.Equal(48, TaskbarGeometry.Thickness(new Rectangle(0, 1032, 1920, 48)));
        Assert.Equal(48, TaskbarGeometry.Thickness(new Rectangle(0, 0, 48, 1080)));
        Assert.Equal(1080, TaskbarGeometry.Length(new Rectangle(0, 0, 48, 1080)));
    }

    [Fact]
    public void Zero_or_tiny_bounds_are_not_usable()
    {
        Assert.False(TaskbarGeometry.IsUsableBounds(Rectangle.Empty));
        Assert.False(TaskbarGeometry.IsUsableBounds(new Rectangle(0, 0, 0, 0)));
        Assert.False(TaskbarGeometry.IsUsableBounds(new Rectangle(2, 1032, 148, 4)));
        Assert.True(TaskbarGeometry.IsUsableBounds(new Rectangle(0, 1032, 1920, 32)));
        Assert.True(TaskbarGeometry.IsUsableBounds(new Rectangle(0, 0, 48, 1080)));
    }

    [Fact]
    public void Auto_hide_detects_a_2px_strip_or_an_offscreen_full_height_hwnd()
    {
        // Classic shrink.
        Assert.True(TaskbarGeometry.IsAutoHidden(new Rectangle(0, 1078, 1920, 2), Monitor));
        // Win11 slide: hwnd stays 1920x48 but sits at Y=1078 (2 px on screen).
        Assert.True(TaskbarGeometry.IsAutoHidden(new Rectangle(0, 1078, 1920, 48), Monitor));
        // Fully visible bottom / top / left / right are not hidden.
        Assert.False(TaskbarGeometry.IsAutoHidden(new Rectangle(0, 1032, 1920, 48), Monitor));
        Assert.False(TaskbarGeometry.IsAutoHidden(new Rectangle(0, 0, 1920, 48), Monitor));
        Assert.False(TaskbarGeometry.IsAutoHidden(new Rectangle(0, 0, 48, 1080), Monitor));
        Assert.False(TaskbarGeometry.IsAutoHidden(new Rectangle(1872, 0, 48, 1080), Monitor));
        // Left auto-hide sliver.
        Assert.True(TaskbarGeometry.IsAutoHidden(new Rectangle(-46, 0, 48, 1080), Monitor));
    }

    [Fact]
    public void AlongSpan_uses_Y_on_a_vertical_bar()
    {
        var bar = new Rectangle(0, 0, 48, 1080);
        var item = new Rectangle(0, 200, 48, 40);
        Assert.Equal((200, 240), TaskbarGeometry.AlongSpan(item, bar));
        var hbar = new Rectangle(0, 1032, 1920, 48);
        var icon = new Rectangle(430, 1032, 48, 48);
        Assert.Equal((430, 478), TaskbarGeometry.AlongSpan(icon, hbar));
    }

    [Fact]
    public void Left_vs_right_side()
    {
        Assert.True(TaskbarGeometry.IsLeftSide(new Rectangle(0, 0, 48, 1080), Monitor));
        Assert.False(TaskbarGeometry.IsLeftSide(new Rectangle(1872, 0, 48, 1080), Monitor));
        var secondary = new Rectangle(-1920, 0, 1920, 1080);
        Assert.True(TaskbarGeometry.IsLeftSide(new Rectangle(-1920, 0, 48, 1080), secondary));
        Assert.False(TaskbarGeometry.IsLeftSide(new Rectangle(-48, 0, 48, 1080), secondary));
    }

    [Fact]
    public void FreeSpans_on_a_vertical_bar_avoid_icons_along_Y()
    {
        var r = new TaskbarRegions { Bounds = new Rectangle(0, 0, 48, 1080) };
        r.Cutouts.Add(new Rectangle(0, 0, 48, 80));
        r.Cutouts.Add(new Rectangle(0, 200, 48, 80));
        var spans = TaskbarService.FreeSpans(r, 1080, 4);
        Assert.DoesNotContain(spans, s => s.L < 80 && s.R > 0 && s.L < 84);
        Assert.Contains(spans, s => s.L >= 80 && s.R <= 200);
        Assert.Equal((84, 196), spans.First(s => s.L >= 80 && s.R <= 200));
    }

    [Fact]
    public void Fallback_cutout_keeps_a_start_strip_on_both_orientations()
    {
        var h = new Rectangle(0, 1032, 1920, 48);
        var hf = TaskbarLayoutProbe.FallbackConservativeCutout(h);
        Assert.Equal(144, hf.Left - h.Left);
        Assert.Equal(h.Right, hf.Right);

        var v = new Rectangle(0, 0, 48, 1080);
        var vf = TaskbarLayoutProbe.FallbackConservativeCutout(v);
        Assert.Equal(v.Left, vf.Left);
        Assert.Equal(v.Right, vf.Right);
        Assert.True(vf.Top - v.Top >= 8);
        Assert.Equal(v.Bottom, vf.Bottom);
    }

    [Fact]
    public void Unusable_bounds_are_skipped_so_overlays_do_not_move_to_0x0()
    {
        Assert.False(TaskbarGeometry.IsUsableBounds(new Rectangle(0, 0, 0, 0)));
        // A 1920x48 bar must remain usable after a size-32 or top-edge move.
        Assert.True(TaskbarGeometry.IsUsableBounds(new Rectangle(0, 0, 1920, 48)));
        Assert.True(TaskbarGeometry.IsUsableBounds(new Rectangle(0, 1048, 1920, 32)));
    }
}
