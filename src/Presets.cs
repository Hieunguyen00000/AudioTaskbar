using Microsoft.Win32;

namespace AudioTaskbar;

public readonly record struct ColorPreset(string Id, string Name, string Low, string Mid, string High, string Bar);

public static class Presets
{
    public static readonly ColorPreset[] All =
    {
        new("aurora", "Aurora", "#071526", "#2EE6A6", "#7AE7FF", "#E8FBFF"),
        new("night-ocean", "Night Ocean", "#0B1C4A", "#00B8D4", "#FF3D71", "#E8F4FF"),
        new("sunset", "Sunset", "#2A0E16", "#FF7A45", "#FFD166", "#FFE8D6"),
        new("minimal", "Minimal", "#121212", "#9AA3AD", "#E6E8EB", "#F4F6F8"),
        new("windows-accent", "Windows Accent", "#07101A", "#0078D4", "#4CC2FF", "#E8F4FF")
    };

    public static ColorPreset? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var p in All)
            if (p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public static void Apply(AppConfig cfg, string id)
    {
        ColorPreset p;
        if (id.Equals("windows-accent", StringComparison.OrdinalIgnoreCase))
        {
            p = FromWindowsAccent() ?? All.First(x => x.Id == "windows-accent");
        }
        else if (Find(id) is { } found)
        {
            p = found;
        }
        else return;

        cfg.Preset = p.Id;
        cfg.ColorLow = p.Low;
        cfg.ColorMid = p.Mid;
        cfg.ColorHigh = p.High;
        cfg.BarColor = p.Bar;
        cfg.Normalize();
    }

    public static ColorPreset? FromWindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key is null) return null;
            // ColorizationColor is 0xAARRGGBB. AccentColor is often 0xAABBGGRR.
            Color mid;
            if (key.GetValue("ColorizationColor") is int cz)
            {
                uint v = unchecked((uint)cz);
                mid = Color.FromArgb(255, (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
            }
            else if (key.GetValue("AccentColor") is int ac)
            {
                uint v = unchecked((uint)ac);
                mid = Color.FromArgb(255, (int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF));
            }
            else return null;

            var low = Color.FromArgb(255,
                Math.Max(0, mid.R * 12 / 100),
                Math.Max(0, mid.G * 14 / 100),
                Math.Max(0, mid.B * 18 / 100));
            var high = Color.FromArgb(255,
                Math.Min(255, mid.R + (255 - mid.R) * 2 / 3),
                Math.Min(255, mid.G + (255 - mid.G) * 2 / 3),
                Math.Min(255, mid.B + (255 - mid.B) * 1 / 2));
            var bar = Color.FromArgb(255,
                Math.Min(255, mid.R + 180),
                Math.Min(255, mid.G + 190),
                Math.Min(255, mid.B + 200));
            return new ColorPreset("windows-accent", "Windows Accent",
                AppConfig.ToHex(low), AppConfig.ToHex(mid), AppConfig.ToHex(high), AppConfig.ToHex(bar));
        }
        catch { return null; }
    }
}
