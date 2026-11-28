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
    private static readonly object SecondaryCacheLock = new();
    private static IntPtr[] _cachedSecondaries = Array.Empty<IntPtr>();

    public static List<TaskbarInfo> GetTaskbars()
    {
        var list = new List<TaskbarInfo>();
        var seen = new HashSet<IntPtr>();
        var primary = Native.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && Native.GetWindowRect(primary, out var pr) && TaskbarGeometry.IsUsableBounds(pr.ToRectangle()))
        {
            seen.Add(primary);
            list.Add(new TaskbarInfo(primary, pr.ToRectangle(), true));
        }

        void ConsiderSecondary(IntPtr h)
        {
            if (h == IntPtr.Zero || h == primary || !seen.Add(h)) return;
            if (!TryReadSecondary(h, out var info)) return;
            list.Add(info);
        }

        Native.EnumWindows((h, _) =>
        {
            if (Native.GetClass(h) == "Shell_SecondaryTrayWnd")
                ConsiderSecondary(h);
            return true;
        }, IntPtr.Zero);

        // Opening Start on a secondary monitor can drop Shell_SecondaryTrayWnd
        // from EnumWindows while the hwnd stays alive, visible, and the same
        // size. FindWindowEx and last-known hwnds still see it.
        IntPtr prev = IntPtr.Zero;
        for (int i = 0; i < 16; i++)
        {
            var h = Native.FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null);
            if (h == IntPtr.Zero) break;
            ConsiderSecondary(h);
            prev = h;
        }

        IntPtr[] cached;
        lock (SecondaryCacheLock) cached = _cachedSecondaries;
        foreach (var recovered in MergeSecondaryTaskbars(Array.Empty<TaskbarInfo>(), cached, primary,
                     h => TryReadSecondary(h, out var info) ? info : null))
            ConsiderSecondary(recovered.Hwnd);

        var secondaries = list.Where(b => !b.IsPrimary).Select(b => b.Hwnd).ToArray();
        lock (SecondaryCacheLock) _cachedSecondaries = secondaries;
        return list;
    }

    /// <summary>
    /// A secondary tray is still usable if its hwnd is alive and has a real
    /// bar rect — even when EnumWindows missed it (Start on that monitor).
    /// </summary>
    internal static bool TryReadSecondary(IntPtr hwnd, out TaskbarInfo info)
    {
        info = default;
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return false;
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var bounds = r.ToRectangle();
        if (!TaskbarGeometry.IsUsableBounds(bounds)) return false;
        info = new TaskbarInfo(hwnd, bounds, false);
        return true;
    }

    /// <summary>
    /// Combine the secondary trays found in this pass with last-known hwnds
    /// that are still alive, without duplicating the primary bar.
    /// </summary>
    internal static List<TaskbarInfo> MergeSecondaryTaskbars(
        IReadOnlyList<TaskbarInfo> found,
        IEnumerable<IntPtr> knownSecondaries,
        IntPtr primary,
        Func<IntPtr, TaskbarInfo?> tryRead)
    {
        var list = found.ToList();
        var seen = new HashSet<IntPtr>(list.Select(b => b.Hwnd));
        if (primary != IntPtr.Zero) seen.Add(primary);
        foreach (var h in knownSecondaries)
        {
            if (h == IntPtr.Zero || !seen.Add(h)) continue;
            var info = tryRead(h);
            if (info is { } t) list.Add(t);
        }
        return list;
    }

    /// <summary>
    /// Keep an existing overlay when its bar is listed this pass; park it
    /// (preserve the diorama) when a still-alive hwnd was missed this pass;
    /// dispose only when the hwnd is truly gone.
    /// </summary>
    internal static TaskbarOverlayRetention DecideOverlayRetention(bool listedInThisPass, bool hwndStillAlive)
        => listedInThisPass
            ? TaskbarOverlayRetention.Keep
            : hwndStillAlive ? TaskbarOverlayRetention.Park : TaskbarOverlayRetention.Dispose;

    public static bool IsAutoHidden(Rectangle bounds)
        => TaskbarGeometry.IsAutoHidden(bounds);

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
        bool vertical = TaskbarGeometry.IsVertical(regions.Bounds);
        foreach (var c in regions.Cutouts)
        {
            var (a, b) = TaskbarGeometry.AlongSpan(c, regions.Bounds);
            int l = a - guard;
            int r = b + guard;
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

internal enum TaskbarOverlayRetention { Keep, Park, Dispose }
