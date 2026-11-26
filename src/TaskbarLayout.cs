using System.Windows.Automation;

namespace AudioTaskbar;

/// <summary>Exact, DPI-correct (physical pixel) bounds of every interactive item on one taskbar.</summary>
public sealed class TaskbarLayoutSnapshot
{
    public IntPtr Hwnd;
    public Rectangle Bounds;
    /// <summary>Screen-space rects of Start/Search/Task View/Widgets/app buttons/tray/clock.</summary>
    public List<Rectangle> Items = new();
    public Rectangle TaskArea;   // union of Start..last app button
    public Rectangle TrayArea;   // union of tray/clock items
    public string Source = "";
    public int UiaCount;
    public DateTime At;

    public string Signature()
    {
        unchecked
        {
            int h = Bounds.GetHashCode();
            foreach (var r in Items) h = h * 31 + r.GetHashCode();
            return h.ToString("X8") + ":" + Items.Count;
        }
    }
}

/// <summary>
/// Background prober: queries Windows UI Automation (plus legacy child HWNDs)
/// of Shell_TrayWnd / Shell_SecondaryTrayWnd every ~400 ms so the overlay can
/// cut crisp holes around every icon, at any DPI, alignment or icon count.
/// UIA can block when Explorer is busy, so this never runs on the UI thread.
/// </summary>
internal sealed class TaskbarLayoutProbe : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, TaskbarLayoutSnapshot> _snaps = new();
    private readonly AutoResetEvent _kick = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile IntPtr[] _targets = Array.Empty<IntPtr>();
    private readonly Action<string> _log;
    private string _lastLogged = "";

    public int IntervalMs { get; set; } = 400;
    /// <summary>Raised on the probe thread when any taskbar's item layout changes.</summary>
    public event Action? Changed;

    public TaskbarLayoutProbe(Action<string> log)
    {
        _log = log;
        _thread = new Thread(Loop) { IsBackground = true, Name = "TaskbarLayoutProbe" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void SetTargets(IEnumerable<IntPtr> hwnds)
    {
        _targets = hwnds.Where(h => h != IntPtr.Zero).Distinct().ToArray();
        _kick.Set();
    }

    public void Kick() => _kick.Set();

    public bool TryGet(IntPtr hwnd, out TaskbarLayoutSnapshot snap)
    {
        lock (_lock)
        {
            if (_snaps.TryGetValue(hwnd, out var s)) { snap = s; return true; }
        }
        snap = null!;
        return false;
    }

    /// <summary>Synchronous probe (used once at startup so the very first frame is already icon-safe).</summary>
    public TaskbarLayoutSnapshot ProbeNow(IntPtr hwnd)
    {
        var s = Probe(hwnd);
        lock (_lock) _snaps[hwnd] = s;
        return s;
    }

    private void Loop()
    {
        while (!_stop)
        {
            bool changed = false;
            foreach (var h in _targets)
            {
                if (_stop) break;
                TaskbarLayoutSnapshot s;
                try { s = Probe(h); }
                catch (Exception ex) { _log("Layout probe error: " + ex.Message); continue; }
                lock (_lock)
                {
                    if (!_snaps.TryGetValue(h, out var old) || old.Signature() != s.Signature())
                        changed = true;
                    _snaps[h] = s;
                }
                var sig = $"{s.Source} items={s.Items.Count} task={s.TaskArea} tray={s.TrayArea}";
                if (sig != _lastLogged && h == _targets.FirstOrDefault())
                {
                    _lastLogged = sig;
                    _log("Layout(" + (h == _targets.FirstOrDefault() ? "primary" : "secondary") + "): " + sig);
                }
            }
            if (changed)
            {
                try { Changed?.Invoke(); } catch { }
            }
            _kick.WaitOne(IntervalMs);
        }
    }

    private static readonly HashSet<string> InteractiveIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartButton", "SearchButton", "TaskViewButton", "WidgetsButton", "CopilotButton",
        "SystemTrayIcon", "NotificationCenterButton", "ControlCenterButton", "ShowDesktopButton"
    };

    internal static TaskbarLayoutSnapshot Probe(IntPtr hwnd)
    {
        var snap = new TaskbarLayoutSnapshot { Hwnd = hwnd, At = DateTime.Now };
        if (!Native.GetWindowRect(hwnd, out var wr)) return snap;
        var bounds = wr.ToRectangle();
        snap.Bounds = bounds;
        if (bounds.Width <= 4 || bounds.Height <= 4) { snap.Source = "autohidden"; return snap; }

        var items = new List<Rectangle>();
        var task = new List<Rectangle>();
        var tray = new List<Rectangle>();
        int uia = 0;

        // 1) UI Automation — the only source that sees the XAML taskbar buttons.
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var cr = new CacheRequest();
            cr.Add(AutomationElement.BoundingRectangleProperty);
            cr.Add(AutomationElement.ControlTypeProperty);
            cr.Add(AutomationElement.ClassNameProperty);
            cr.Add(AutomationElement.AutomationIdProperty);
            cr.Add(AutomationElement.IsOffscreenProperty);
            cr.AutomationElementMode = AutomationElementMode.None;
            AutomationElementCollection all;
            using (cr.Activate())
                all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

            foreach (AutomationElement e in all)
            {
                System.Windows.Rect br;
                ControlType? ct; string cls, aid; bool off;
                try
                {
                    br = (System.Windows.Rect)e.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty);
                    ct = e.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                    cls = e.GetCachedPropertyValue(AutomationElement.ClassNameProperty) as string ?? "";
                    aid = e.GetCachedPropertyValue(AutomationElement.AutomationIdProperty) as string ?? "";
                    off = e.GetCachedPropertyValue(AutomationElement.IsOffscreenProperty) is true;
                }
                catch { continue; }
                if (br.IsEmpty || br.Width < 2 || br.Height < 2 || double.IsInfinity(br.Width)) continue;
                var r = Rectangle.FromLTRB((int)Math.Floor(br.Left), (int)Math.Floor(br.Top),
                                           (int)Math.Ceiling(br.Right), (int)Math.Ceiling(br.Bottom));
                r = Rectangle.Intersect(r, bounds);
                if (r.Width < 2 || r.Height < 2) continue;
                if (off && r.Width < 4) continue;

                bool isButtonish = ct == ControlType.Button || ct == ControlType.SplitButton ||
                                   ct == ControlType.MenuItem || ct == ControlType.ListItem ||
                                   ct == ControlType.CheckBox || ct == ControlType.RadioButton ||
                                   ct == ControlType.Hyperlink || ct == ControlType.ToolBar && r.Width < bounds.Width / 3;
                bool trayish = cls.StartsWith("SystemTray", StringComparison.OrdinalIgnoreCase) ||
                               cls is "TrayNotifyWnd" or "TrayClockWClass" or "NotifyIconOverflowWindow" ||
                               aid is "SystemTrayIcon" or "NotificationCenterButton" or "ControlCenterButton";
                bool taskish = cls.Contains("TaskListButton", StringComparison.OrdinalIgnoreCase) ||
                               aid is "StartButton" or "SearchButton" or "TaskViewButton" or "CopilotButton" ||
                               cls is "MSTaskSwWClass" or "MSTaskListWClass";
                bool known = InteractiveIds.Contains(aid) || trayish || taskish ||
                             aid.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase);
                if (!(isButtonish || known)) continue;
                // Skip the frame / full-width containers.
                if (r.Width >= bounds.Width * 0.6 && bounds.Width > bounds.Height) continue;
                if (r.Height >= bounds.Height * 0.6 && bounds.Height > bounds.Width) continue;

                uia++;
                items.Add(r);
                if (trayish) tray.Add(r);
                else if (taskish) task.Add(r);
            }
        }
        catch
        {
            // UIA unavailable — legacy + conservative fallback below.
        }

        // 2) Legacy child HWNDs (a strict superset safeguard: only adds cut-outs).
        int legacy = 0;
        Native.EnumChildWindows(hwnd, (h, _) =>
        {
            if (!Native.IsWindowVisible(h)) return true;
            if (!Native.GetWindowRect(h, out var cr2)) return true;
            var r = Rectangle.Intersect(cr2.ToRectangle(), bounds);
            if (r.Width < 4 || r.Height < 4) return true;
            var cls = Native.GetClass(h);
            if (cls is "TrayNotifyWnd" or "Start" or "TrayButton" or "TrayClockWClass" or "MSTaskSwWClass" or "MSTaskListWClass")
            {
                if (r.Width < bounds.Width * 0.6)
                {
                    items.Add(r);
                    legacy++;
                    if (cls is "TrayNotifyWnd" or "TrayClockWClass") tray.Add(r);
                    else task.Add(r);
                }
            }
            return true;
        }, IntPtr.Zero);

        if (uia == 0)
        {
            // 3) Conservative fallback: without UIA we cannot see XAML buttons, so
            // protect everything except a narrow strip at the far left. Never guess widths.
            int keep = Math.Min(bounds.Width / 8, bounds.Height * 3);
            var guard = Rectangle.FromLTRB(bounds.Left + keep, bounds.Top, bounds.Right, bounds.Bottom);
            items.Add(guard);
            snap.Source = "fallback-conservative(legacy=" + legacy + ")";
        }
        else snap.Source = "uia(" + uia + ")+legacy(" + legacy + ")";

        snap.UiaCount = uia;
        snap.Items = MergeNearby(items, 1);
        snap.TaskArea = task.Count > 0 ? task.Aggregate(Rectangle.Union) : Rectangle.Empty;
        snap.TrayArea = tray.Count > 0 ? tray.Aggregate(Rectangle.Union) : Rectangle.Empty;
        return snap;
    }

    /// <summary>Merge overlapping / touching rects (in X) into column bands for a simpler mask.</summary>
    private static List<Rectangle> MergeNearby(List<Rectangle> rects, int gap)
    {
        var sorted = rects.OrderBy(r => r.Left).ToList();
        var outList = new List<Rectangle>();
        foreach (var r in sorted)
        {
            if (outList.Count > 0)
            {
                var last = outList[^1];
                if (r.Left <= last.Right + gap)
                {
                    outList[^1] = Rectangle.Union(last, r);
                    continue;
                }
            }
            outList.Add(r);
        }
        return outList;
    }

    public void Dispose()
    {
        _stop = true;
        _kick.Set();
        try { _thread.Join(800); } catch { }
    }
}
