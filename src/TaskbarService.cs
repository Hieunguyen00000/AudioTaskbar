namespace AudioTaskbar;

public readonly record struct TaskbarInfo(IntPtr Hwnd, Rectangle Bounds, bool IsPrimary);

public sealed class TaskbarRegions
{
    public Rectangle Bounds;
    public Rectangle IconCluster;   // screen coords; empty if unknown
    public Rectangle Tray;          // screen coords
    public Rectangle WeatherSlot;   // client coords relative to Bounds
    public bool AutoHidden;
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

    public static TaskbarRegions GetRegions(TaskbarInfo info, AppConfig cfg)
    {
        var regions = new TaskbarRegions
        {
            Bounds = info.Bounds,
            AutoHidden = IsAutoHidden(info.Bounds)
        };

        var children = new List<(string cls, Rectangle r, bool vis)>();
        Native.EnumChildWindows(info.Hwnd, (h, _) =>
        {
            if (!Native.GetWindowRect(h, out var r) || r.Width <= 0 || r.Height <= 0) return true;
            var cls = Native.GetClass(h);
            children.Add((cls, r.ToRectangle(), Native.IsWindowVisible(h)));
            return true;
        }, IntPtr.Zero);

        var iconParts = new List<Rectangle>();
        foreach (var (cls, r, vis) in children)
        {
            if (cls is "TrayNotifyWnd" or "Button" || cls.Contains("Notify", StringComparison.OrdinalIgnoreCase))
            {
                if (vis && r.Width > 20 && r.Width < info.Bounds.Width / 2)
                    regions.Tray = regions.Tray.IsEmpty ? r : Rectangle.Union(regions.Tray, r);
            }
            if (cls is "MSTaskListWClass" or "MSTaskSwWClass" or "ReBarWindow32" or "Start" or "TrayDummySearchControl")
            {
                if (r.Width > 8 && r.Width < info.Bounds.Width * 4 / 5)
                    iconParts.Add(r);
            }
        }
        if (iconParts.Count > 0)
        {
            var u = iconParts[0];
            for (int i = 1; i < iconParts.Count; i++) u = Rectangle.Union(u, iconParts[i]);
            regions.IconCluster = Rectangle.Intersect(u, info.Bounds);
        }

        // Fallback: assume a centered cluster (~40% width) if we couldn't see legacy children.
        if (regions.IconCluster.IsEmpty)
        {
            int w = Math.Max(200, info.Bounds.Width * 2 / 5);
            int x = info.Bounds.X + (info.Bounds.Width - w) / 2;
            regions.IconCluster = new Rectangle(x, info.Bounds.Y, w, info.Bounds.Height);
        }
        if (regions.Tray.IsEmpty)
        {
            int tw = Math.Min(260, info.Bounds.Width / 5);
            regions.Tray = new Rectangle(info.Bounds.Right - tw, info.Bounds.Y, tw, info.Bounds.Height);
        }

        regions.WeatherSlot = ComputeWeatherSlot(info.Bounds, regions.IconCluster, regions.Tray, cfg);
        return regions;
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
