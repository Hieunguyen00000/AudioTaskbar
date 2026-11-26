using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AudioTaskbar;

/// <summary>
/// Safe-mode wallpaper blocker. When the TAP cannot make the XAML taskbar
/// transparent (e.g. Smart App Control blocks the unsigned DLL), the Win11
/// taskbar acrylic still samples whatever sits *behind* it. This click-through
/// window lives directly under Shell_TrayWnd and paints a solid scene-tinted
/// gradient, so the area under the icons shows our colours instead of the
/// wallpaper — without ever being drawn on top of an icon.
/// </summary>
internal sealed class BackdropWindow : Form
{
    private IntPtr _screenDc, _memDc;
    private Color _top = Color.Black, _bottom = Color.Black;
    private bool _placed;
    public IntPtr TaskbarHwnd { get; set; }
    public bool Placed => _placed;

    public BackdropWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                          Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(_screenDc);
    }

    /// <summary>Show strictly underneath the taskbar (never via Form.Show, which could raise it).</summary>
    public bool ShowBehind(Rectangle bounds, Color top, Color bottom)
    {
        _ = Handle; // ensure created, still hidden
        if (TaskbarHwnd == IntPtr.Zero) return false;
        bool repaint = top != _top || bottom != _bottom || Bounds != bounds || !_placed;
        _top = top; _bottom = bottom;
        if (repaint)
        {
            Native.SetWindowPos(Handle, TaskbarHwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            PaintGradient(bounds);
        }
        Native.SetWindowPos(Handle, TaskbarHwnd, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_SHOWWINDOW);
        _placed = IsBelowTaskbar();
        if (!_placed) HideNow();
        return _placed;
    }

    public void HideNow()
    {
        if (!IsHandleCreated) return;
        Native.ShowWindow(Handle, 0);
        _placed = false;
    }

    public bool IsBelowTaskbar()
    {
        if (!IsHandleCreated || TaskbarHwnd == IntPtr.Zero) return false;
        var w = TaskbarHwnd;
        for (int i = 0; i < 512; i++)
        {
            w = Native.GetWindow(w, Native.GW_HWNDNEXT);
            if (w == IntPtr.Zero) return false;
            if (w == Handle) return true;
        }
        return false;
    }

    private void PaintGradient(Rectangle bounds)
    {
        int w = Math.Max(1, bounds.Width), h = Math.Max(1, bounds.Height);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var br = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), _top, _bottom, LinearGradientMode.Vertical))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillRectangle(br, 0, 0, w, h);
        }
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(_memDc, hBmp);
        try
        {
            var loc = new Native.POINT { X = bounds.X, Y = bounds.Y };
            var size = new Native.SIZE { Cx = w, Cy = h };
            var src = new Native.POINT { X = 0, Y = 0 };
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, _screenDc, ref loc, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(_memDc, old);
            Native.DeleteObject(hBmp);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideNow(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (_memDc != IntPtr.Zero) { Native.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
        if (_screenDc != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
        base.Dispose(disposing);
    }
}
