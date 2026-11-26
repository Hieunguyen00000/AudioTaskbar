namespace AudioTaskbar;

public readonly record struct TaskbarInfo(IntPtr Hwnd, Rectangle Bounds, bool IsPrimary);

public sealed class TaskbarRegions
{
    public Rectangle Bounds;
    public Rectangle IconCluster;   // screen coords; empty if unknown
    public Rectangle Tray;          // screen coords
    public Rectangle WeatherSlot;   // client coords relative to Bounds (legacy)
    public bool AutoHidden;
    /// <summary>v5: exact screen rects of every taskbar item (UIA-measured).</summary>
    public List<Rectangle> Cutouts = new();
    public string LayoutSource = "";
    public string Signature = "";
}

public static class TaskbarService
{
    public static List<TaskbarInfo> GetTaskbars()
    {
        var list = new List<TaskbarInfo>();
        var primary = Native.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && Native.GetWindowRect(primary, out var pr) && pr.Width > 0 && pr.Height > 0)
            list.Add(new TaskbarInfo(primary, pr.ToRectangle(), true));

        Native.EnumWindows((h, _) =>
        {
            if (h == primary) return true;
            if (!Native.IsWindowVisible(h)) return true;
            if (Native.GetClass(h) != "Shell_SecondaryTrayWnd") return true;
            if (!Native.GetWindowRect(h, out var r) || r.Width <= 0 || r.Height <= 0) return true;
            list.Add(new TaskbarInfo(h, r.ToRectangle(), false));
            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static bool IsAutoHidden(Rectangle bounds)
        => bounds.Width <= 4 || bounds.Height <= 4;

    public static TaskbarRegions GetRegions(TaskbarInfo info, AppConfig cfg, TaskbarLayoutSnapshot? layout)
    {
        var regions = new TaskbarRegions
        {
            Bounds = info.Bounds,
            AutoHidden = IsAutoHidden(info.Bounds)
        };
        layout ??= TaskbarLayoutProbe.Probe(info.Hwnd);
        regions.Cutouts = layout.Items
            .Select(r => Rectangle.Intersect(r, info.Bounds))
            .Where(r => r.Width > 0 && r.Height > 0)
            .ToList();
        regions.LayoutSource = layout.Source;
        regions.Signature = layout.Signature();
        regions.IconCluster = layout.TaskArea;
        regions.Tray = layout.TrayArea;
        if (regions.IconCluster.IsEmpty && regions.Cutouts.Count > 0)
            regions.IconCluster = regions.Cutouts.Aggregate(Rectangle.Union);
        regions.WeatherSlot = ComputeWeatherSlot(info.Bounds, regions.IconCluster, regions.Tray, cfg);
        return regions;
    }

    /// <summary>
    /// Free horizontal spans (client coords) that hold no taskbar item, after
    /// inflating every cut-out by <paramref name="guard"/> pixels.
    /// </summary>
    public static List<(int L, int R)> FreeSpans(TaskbarRegions regions, int width, int guard, int minWidth = 8)
    {
        var spans = new List<(int L, int R)> { (0, width) };
        foreach (var c in regions.Cutouts)
        {
            int l = c.Left - regions.Bounds.X - guard;
            int r = c.Right - regions.Bounds.X + guard;
            var next = new List<(int L, int R)>();
            foreach (var s in spans)
            {
                if (r <= s.L || l >= s.R) { next.Add(s); continue; }
                if (l > s.L) next.Add((s.L, l));
                if (r < s.R) next.Add((r, s.R));
            }
            spans = next;
        }
        return spans.Where(s => s.R - s.L >= minWidth).OrderBy(s => s.L).ToList();
    }

    private static Rectangle ComputeWeatherSlot(Rectangle bounds, Rectangle icons, Rectangle tray, AppConfig cfg)
    {
        int pad = 10;
        int width = Math.Clamp(bounds.Width / 8, 120, 200);
        int height = Math.Max(20, bounds.Height - 6);
        int y = 3;

        var reserved = new List<Rectangle>();
        if (!icons.IsEmpty) reserved.Add(icons);
        if (!tray.IsEmpty) reserved.Add(tray);

        if (cfg.WeatherPosition == "right")
        {
            int right = tray.IsEmpty ? bounds.Right - pad : tray.Left - pad;
            int x = right - width;
            var slot = new Rectangle(x, bounds.Y + y, width, height);
            if (reserved.Any(r => r.IntersectsWith(Rectangle.Inflate(slot, 4, 0))))
            {
                // Try just left of icons
                x = icons.IsEmpty ? bounds.X + pad : icons.Left - pad - width;
                slot = new Rectangle(x, bounds.Y + y, width, height);
            }
            slot = Rectangle.Intersect(slot, bounds);
            return ToClient(slot, bounds);
        }
        else
        {
            int x = bounds.X + pad;
            var slot = new Rectangle(x, bounds.Y + y, width, height);
            if (reserved.Any(r => r.IntersectsWith(Rectangle.Inflate(slot, 4, 0))))
            {
                // shift right up to the icon cluster
                int maxR = icons.IsEmpty ? bounds.Right : icons.Left;
                x = Math.Max(x, Math.Min(maxR - width - pad, bounds.X + pad));
                slot = new Rectangle(x, bounds.Y + y, width, height);
            }
            slot = Rectangle.Intersect(slot, bounds);
            return ToClient(slot, bounds);
        }
    }

    private static Rectangle ToClient(Rectangle screen, Rectangle bounds)
        => new(screen.X - bounds.X, screen.Y - bounds.Y, Math.Max(0, screen.Width), Math.Max(0, screen.Height));

    public static bool IsFullscreenOnBounds(Rectangle taskbarBounds)
    {
        var screen = Screen.FromRectangle(taskbarBounds);
        var bounds = screen.Bounds;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var wr = r.ToRectangle();
        if (Math.Abs(wr.Left - bounds.Left) > 8 || Math.Abs(wr.Top - bounds.Top) > 8) return false;
        if (Math.Abs(wr.Width - bounds.Width) > 16 || Math.Abs(wr.Height - bounds.Height) > 16) return false;

        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0) return false;
        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            if (pid == (uint)proc.Id) return false;
            var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            if (name is "explorer" or "SearchHost" or "ShellExperienceHost" or "StartMenuExperienceHost")
                return false;
        }
        catch { /* process exited */ }
        return true;
    }
}
