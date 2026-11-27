namespace AudioTaskbar.Tests;

/// <summary>v5.3.2: footer row = Đóng | Kết thúc | Bật/Tắt hiệu ứng — even, aligned, never clipped.</summary>
public class PortalLayoutTests
{
    [Theory]
    [InlineData(true, "F8", "Tắt hiệu ứng (F8)")]
    [InlineData(false, "F8", "Bật hiệu ứng (F8)")]
    [InlineData(true, "Ctrl+Alt+F8", "Tắt hiệu ứng (Ctrl+Alt+F8)")]
    public void Toggle_label(bool on, string hk, string expected) => Assert.Equal(expected, PortalForm.ToggleText(on, hk));

    [Fact]
    public void Short_labels_get_three_equal_buttons_spanning_the_card_width()
    {
        var w = PortalForm.FooterColumns(440, 8, new[] { 140, 110, 125 });
        Assert.Equal(440, w.Sum());
        int b0 = w[0] - 4, b1 = w[1] - 8, b2 = w[2] - 4;   // button widths without cell margins
        Assert.InRange(Math.Max(b0, Math.Max(b1, b2)) - Math.Min(b0, Math.Min(b1, b2)), 0, 2);
    }

    [Theory]
    [InlineData(150, 110, 200)]  // long hotkey label
    [InlineData(160, 120, 140)]
    [InlineData(200, 200, 200)]  // does not fit: shrink proportionally, still fills the row exactly
    public void Every_button_gets_at_least_its_text_width_when_possible(int a, int b, int c)
    {
        var need = new[] { a, b, c };
        var w = PortalForm.FooterColumns(440, 8, need);
        Assert.Equal(440, w.Sum());
        var inner = new[] { w[0] - 4, w[1] - 8, w[2] - 4 };
        if (need.Sum() <= 440 - 16)
            for (int i = 0; i < 3; i++) Assert.True(inner[i] >= need[i], $"col {i}: {inner[i]} < {need[i]}");
        Assert.All(inner, x => Assert.True(x > 0));
    }
}
