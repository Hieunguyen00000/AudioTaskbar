namespace AudioTaskbar.Tests;

/// <summary>5. Hotkey parsing: valid, invalid, empty — never throws.</summary>
public class HotkeyTests
{
    [Theory]
    [InlineData("F8", 0x77u, 0u, "F8")]
    [InlineData("F9", 0x78u, 0u, "F9")]
    [InlineData("Ctrl+Alt+F8", 0x77u, Native.MOD_CONTROL | Native.MOD_ALT, "Ctrl+Alt+F8")]
    [InlineData("Ctrl+Shift+F8", 0x77u, Native.MOD_CONTROL | Native.MOD_SHIFT, "Ctrl+Shift+F8")]
    [InlineData("ctrl + alt + f9", 0x78u, Native.MOD_CONTROL | Native.MOD_ALT, "Ctrl+Alt+F9")]
    [InlineData("Alt+Ctrl+F8", 0x77u, Native.MOD_CONTROL | Native.MOD_ALT, "Ctrl+Alt+F8")]
    [InlineData("Control+Shift+A", 0x41u, Native.MOD_CONTROL | Native.MOD_SHIFT, "Ctrl+Shift+A")]
    [InlineData("Win+Space", 0x20u, Native.MOD_WIN, "Win+Space")]
    [InlineData("F24", 0x87u, 0u, "F24")]
    [InlineData("Ctrl+1", 0x31u, Native.MOD_CONTROL, "Ctrl+1")]
    public void Valid_hotkeys(string text, uint vk, uint mods, string display)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k, out var d));
        Assert.Equal(vk, k);
        Assert.Equal(mods | Native.MOD_NOREPEAT, m);
        Assert.Equal(display, d);
    }

    [Theory]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Shift")]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F-1")]
    [InlineData("++")]
    [InlineData("Ctrl++")]
    [InlineData("Hyper+F8")]
    [InlineData("F8F9")]
    [InlineData("Ctrl+F99999999999999999999")]
    [InlineData("é")]
    public void Invalid_hotkeys_are_rejected(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_hotkey_means_default_F8(string? text)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k, out var d));
        Assert.Equal(0x77u, k);
        Assert.Equal(Native.MOD_NOREPEAT, m);
        Assert.Equal("F8", d);
    }

    [Fact]
    public void Random_garbage_never_throws()
    {
        var rng = new Random(42);
        const string alphabet = "+ FfCtrlAltShiftWin0123456789!@#$%^&*()é\t\n";
        for (int i = 0; i < 3000; i++)
        {
            var chars = Enumerable.Range(0, rng.Next(0, 24)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray();
            var ex = Record.Exception(() => Hotkey.TryParse(new string(chars), out _, out _, out _));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void Portal_presets_are_all_valid()
    {
        foreach (var p in new[] { "F8", "F9", "Ctrl+Alt+F8", "Ctrl+Shift+F8" })
            Assert.True(Hotkey.TryParse(p, out _, out _, out var d) && d == p);
    }
}
