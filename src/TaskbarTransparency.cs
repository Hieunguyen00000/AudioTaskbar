namespace AudioTaskbar;

/// <summary>
/// Attempts to make the real Win11 taskbar background clear so our visualizer
/// can sit behind the icons. On 22H2+ the XAML fill ignores
/// SetWindowCompositionAttribute; we still apply it (harmless, reversible)
/// and report whether fusion is actually visible via a pixel probe.
/// </summary>
public sealed class TaskbarTransparency : IDisposable
{
    private readonly List<IntPtr> _applied = new();
    private bool _appliedFlag;
    public bool Desired { get; private set; }
    public bool Effective { get; set; }
    public string Status { get; private set; } = "idle";

    public void Apply(IEnumerable<TaskbarInfo> taskbars, bool desired)
    {
        Desired = desired;
        if (!desired)
        {
            Restore();
            Status = "disabled";
            Effective = false;
            return;
        }

        Restore();
        foreach (var tb in taskbars)
        {
            try
            {
                if (Native.ApplyAccent(tb.Hwnd, Native.ACCENT_ENABLE_TRANSPARENTGRADIENT, 0x00000000))
                    _applied.Add(tb.Hwnd);
                int none = 1;
                Native.DwmSetWindowAttribute(tb.Hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            }
            catch { /* never break the app or explorer */ }
        }
        _appliedFlag = _applied.Count > 0;
        Status = _appliedFlag ? "accent-applied (may be ignored on XAML taskbar)" : "accent-failed";
    }

    public void Reapply(IEnumerable<TaskbarInfo> taskbars)
    {
        if (!Desired) return;
        Apply(taskbars, true);
    }

    /// <summary>
    /// After the overlay has painted a unique probe color at a known empty
    /// point (behind the taskbar), sample that screen pixel. If it matches,
    /// the taskbar background is actually transparent and fusion works.
    /// </summary>
    public bool ProbeEffective(Point screenPoint, Color expected, int tolerance = 30)
    {
        try
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(screenPoint, Point.Empty, new Size(1, 1));
            var c = bmp.GetPixel(0, 0);
            bool ok = Math.Abs(c.R - expected.R) <= tolerance &&
                      Math.Abs(c.G - expected.G) <= tolerance &&
                      Math.Abs(c.B - expected.B) <= tolerance;
            Effective = ok;
            Status = ok
                ? "fused: taskbar background is transparent"
                : "not fused: XAML taskbar fill is still opaque (build 26300 ignores accent API)";
            return ok;
        }
        catch
        {
            Effective = false;
            return false;
        }
    }

    public void Restore()
    {
        foreach (var hwnd in _applied)
        {
            try
            {
                Native.ApplyAccent(hwnd, Native.ACCENT_DISABLED, 0);
                Native.SendMessage(hwnd, Native.WM_DWMCOMPOSITIONCHANGED, IntPtr.Zero, IntPtr.Zero);
                int auto = 0;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref auto, sizeof(int));
            }
            catch { }
        }
        _applied.Clear();
        _appliedFlag = false;
        Effective = false;
    }

    public void Dispose() => Restore();
}
