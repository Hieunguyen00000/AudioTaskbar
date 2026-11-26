namespace AudioTaskbar;

internal static class Hotkey
{
    public static bool TryParse(string? text, out uint modifiers, out uint vk, out string display)
    {
        modifiers = Native.MOD_NOREPEAT;
        vk = 0x77; // F8
        display = "F8";
        if (string.IsNullOrWhiteSpace(text)) return true;

        uint mods = Native.MOD_NOREPEAT;
        uint key = 0;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keyName = "";
        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            var u = p.ToUpperInvariant();
            if (u is "CTRL" or "CONTROL") { mods |= Native.MOD_CONTROL; continue; }
            if (u is "ALT") { mods |= Native.MOD_ALT; continue; }
            if (u is "SHIFT") { mods |= Native.MOD_SHIFT; continue; }
            if (u is "WIN" or "WINDOWS" or "META") { mods |= Native.MOD_WIN; continue; }
            if (u.Length >= 2 && u[0] == 'F' && int.TryParse(u.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
            {
                key = (uint)(0x70 + fn - 1);
                keyName = "F" + fn;
                continue;
            }
            if (u.Length == 1 && u[0] is >= 'A' and <= 'Z')
            {
                key = u[0];
                keyName = u;
                continue;
            }
            if (u.Length == 1 && u[0] is >= '0' and <= '9')
            {
                key = u[0];
                keyName = u;
                continue;
            }
            if (u is "SPACE") { key = 0x20; keyName = "Space"; continue; }
            return false;
        }
        if (key == 0) return false;
        modifiers = mods;
        vk = key;
        var labels = new List<string>();
        if ((mods & Native.MOD_CONTROL) != 0) labels.Add("Ctrl");
        if ((mods & Native.MOD_ALT) != 0) labels.Add("Alt");
        if ((mods & Native.MOD_SHIFT) != 0) labels.Add("Shift");
        if ((mods & Native.MOD_WIN) != 0) labels.Add("Win");
        labels.Add(keyName);
        display = string.Join("+", labels);
        return true;
    }
}
