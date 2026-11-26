using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AudioTaskbar;

internal sealed class FrameContext
{
    public required AppConfig Cfg { get; init; }
    public required Analyzer Analyzer { get; init; }
    public float[]? Wave { get; init; }
    public WeatherSnapshot? Weather { get; init; }
    public required TaskbarRegions Regions { get; init; }
    public float Idle { get; init; }          // 0 = active, 1 = fully idle
    public float Time { get; init; }          // seconds
    public bool Fused { get; init; }          // TAP filled transparent; only full-bleed if BehindOk
    public bool BehindOk { get; init; }       // visualizer is actually below Shell_TrayWnd
    public bool ShowWeather { get; init; }
    public RegionProfile Region { get; init; } = RegionCatalog.Default;
}

internal sealed class OverlayWindow : Form
{
    private Bitmap? _buffer;
    private IntPtr _screenDc;
    private IntPtr _memDc;
    private readonly object _drawLock = new();
    private readonly Random _rng = new(7);
    private RainDrop[] _rain = Array.Empty<RainDrop>();
    private CloudBlob[] _clouds = Array.Empty<CloudBlob>();
    private readonly DioramaRenderer _diorama = new();

    public IntPtr TaskbarHwnd { get; set; }
    public bool IsPrimaryTaskbar { get; set; }
    /// <summary>True only after PlaceBehindTaskbar verified we sit under the taskbar.</summary>
    public bool BehindOk { get; private set; }

    private struct RainDrop { public float X, Y, Speed, Len, Alpha; }
    private struct CloudBlob { public float X, Y, W, H, Speed, Alpha; }

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                          Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(_screenDc);
    }

    public void MoveTo(Rectangle bounds)
    {
        if (Bounds.X == bounds.X && Bounds.Y == bounds.Y && Bounds.Width == bounds.Width && Bounds.Height == bounds.Height)
            return;
        Bounds = bounds;
        EnsureBuffer(bounds.Width, bounds.Height);
        _rain = Array.Empty<RainDrop>();
        _clouds = Array.Empty<CloudBlob>();
        _diorama.Reset();
    }

    // v5 layout cache (physical pixels, client coords)
    private string _layoutKey = "";
    private float[] _colVis = Array.Empty<float>();
    private RectangleF _badgeRect = RectangleF.Empty;
    private int _badgeLevel = -1;
    private List<(float L, float R)> _logicalSpans = new();
    public string LastLayoutInfo { get; private set; } = "";

    public void DrawFrame(FrameContext ctx)
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        lock (_drawLock)
        {
            EnsureBuffer(Width, Height);
            if (_buffer is null) return;
            RenderCore(_buffer, ctx);
            PushLayered(_buffer);
        }
    }

    /// <summary>Offscreen render (used by --render to preview exactly what a machine would see).</summary>
    public Bitmap RenderOffscreen(int w, int h, FrameContext ctx)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        RenderCore(bmp, ctx);
        return bmp;
    }

    private int _rw, _rh;

    private void RenderCore(Bitmap buffer, FrameContext ctx)
    {
        _rw = buffer.Width; _rh = buffer.Height;
        int W = _rw, H = _rh;
        var mood = SceneClock.Compose(ctx.Weather, ctx.Cfg, DateTime.Now);
        bool behind = ctx.Fused && ctx.BehindOk;
        // Per-monitor DPI: the scene is authored for a 48 px bar and scaled
        // to the real physical height (60 px at 125 %, 72 px at 150 %...).
        float s = Math.Max(0.5f, H / 48f);
        int logicalW = (int)Math.Ceiling(W / s);
        using (var g = Graphics.FromImage(buffer))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            UpdateLayout(g, ctx, mood, s);

            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            _diorama.Ensure(logicalW, 48);
            _diorama.SetLayout(ctx.Region, _logicalSpans);
            var st = g.Save();
            g.ScaleTransform(s, s);
            _diorama.Draw(g, ctx, mood, ctx.Time);
            g.Restore(st);

            if (behind)
                DimUnderIcons(g, ctx);

            if (ctx.ShowWeather && _badgeLevel >= 0 && !_badgeRect.IsEmpty)
                DioramaRenderer.DrawBadge(g, mood, _badgeRect, H, _badgeLevel);
        }
        // Safe mode (not truly behind the taskbar): punch crisp, DPI-exact
        // holes around every UIA-measured taskbar item. Never skipped.
        if (!behind)
            ApplyCutoutMask(buffer);
    }

    private void UpdateLayout(Graphics g, FrameContext ctx, SceneMood mood, float s)
    {
        string text = ctx.ShowWeather ? mood.TemperatureText + "|" + mood.ConditionVi + "|" + mood.Place + "|" + ctx.Cfg.WeatherPosition : "-";
        string key = ctx.Regions.Signature + "|" + _rw + "x" + _rh + "|" + text + "|" + ctx.Region.Id;
        if (key == _layoutKey) return;
        _layoutKey = key;

        int pad = Math.Max(2, (int)Math.Round(2 * s));
        int feather = Math.Max(4, (int)Math.Round(6 * s));
        var spans = TaskbarService.FreeSpans(ctx.Regions, _rw, pad + feather, 6);

        // Column visibility: 0 inside (cutout + pad), smooth 6px (scaled) feather outside.
        if (_colVis.Length != _rw) _colVis = new float[_rw];
        for (int x = 0; x < _rw; x++) _colVis[x] = 1f;
        foreach (var c in ctx.Regions.Cutouts)
        {
            int l = c.Left - ctx.Regions.Bounds.X - pad;
            int r = c.Right - ctx.Regions.Bounds.X + pad;
            int a = Math.Max(0, l - feather), b = Math.Min(_rw, r + feather);
            for (int x = a; x < b; x++)
            {
                float v;
                if (x >= l && x < r) v = 0f;
                else
                {
                    float d = x < l ? (l - x) : (x - r + 1);
                    float t = Math.Clamp(d / (feather + 1f), 0f, 1f);
                    v = t * t * (3 - 2 * t);
                }
                if (v < _colVis[x]) _colVis[x] = v;
            }
        }

        // Weather HUD: largest-fitting free span (left or right preference).
        _badgeRect = RectangleF.Empty;
        _badgeLevel = -1;
        if (ctx.ShowWeather)
        {
            float margin = Math.Max(4, 4 * s);
            for (int level = 0; level <= 2 && _badgeLevel < 0; level++)
            {
                var size = DioramaRenderer.MeasureBadge(g, mood, _rh, level);
                var fits = spans.Where(sp => sp.R - sp.L >= size.Width + 2 * margin).ToList();
                if (fits.Count == 0) continue;
                var span = ctx.Cfg.WeatherPosition == "right" ? fits[^1] : fits[0];
                float bx = ctx.Cfg.WeatherPosition == "right" ? span.R - margin - size.Width : span.L + margin;
                _badgeRect = new RectangleF(bx, (_rh - size.Height) / 2f, size.Width, size.Height);
                _badgeLevel = level;
            }
        }

        // Spans left for landmarks (logical units), minus the HUD.
        var free = new List<(float L, float R)>();
        foreach (var sp in spans)
        {
            if (!_badgeRect.IsEmpty && _badgeRect.Right > sp.L && _badgeRect.Left < sp.R)
            {
                float gap = 6 * s;
                if (_badgeRect.Left - gap - sp.L > 10) free.Add((sp.L, _badgeRect.Left - gap));
                if (sp.R - (_badgeRect.Right + gap) > 10) free.Add((_badgeRect.Right + gap, sp.R));
            }
            else free.Add((sp.L, sp.R));
        }
        float totalFree = free.Sum(f => f.R - f.L);
        if (totalFree < _rw * 0.08f) free = new List<(float L, float R)> { (0, _rw) };
        _logicalSpans = free.Select(f => (f.L / s, f.R / s)).ToList();
        LastLayoutInfo = $"cutouts={ctx.Regions.Cutouts.Count} spans=[{string.Join(", ", spans.Select(x => x.L + "-" + x.R))}] badge={(_badgeLevel >= 0 ? $"L{_badgeLevel}@{_badgeRect.X:0}+{_badgeRect.Width:0}" : "none")} scale={s:0.00}";
    }

    private void ApplyCutoutMask(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        if (_colVis.Length != w) return;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            var buf = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            for (int x = 0; x < w; x++)
            {
                float vis = _colVis[x];
                if (vis >= 0.999f) continue;
                int mul = Math.Clamp((int)(vis * 256f), 0, 256);
                for (int y = 0; y < h; y++)
                {
                    int i = y * stride + x * 4;
                    if (mul == 0) { buf[i] = buf[i + 1] = buf[i + 2] = buf[i + 3] = 0; continue; }
                    buf[i] = (byte)((buf[i] * mul) >> 8);
                    buf[i + 1] = (byte)((buf[i + 1] * mul) >> 8);
                    buf[i + 2] = (byte)((buf[i + 2] * mul) >> 8);
                    buf[i + 3] = (byte)((buf[i + 3] * mul) >> 8);
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        }
        finally { bmp.UnlockBits(data); }
    }

    private void DimUnderIcons(Graphics g, FrameContext ctx)
    {
        // Truly behind the taskbar: icons sit on top; a faint calm band keeps them readable.
        using var br = new SolidBrush(Color.FromArgb(46, 6, 8, 14));
        foreach (var c in ctx.Regions.Cutouts)
            g.FillRectangle(br, c.Left - ctx.Regions.Bounds.X, 0, c.Width, _rh);
    }

    private void DrawBackground(Graphics g, FrameContext ctx, float live)
    {
        var cfg = ctx.Cfg;
        Analyzer.MapBackground(cfg, ctx.Analyzer.Energy, ctx.Analyzer.Bass, ctx.Analyzer.Beat,
            out int br, out int bg, out int bb, out int richness);

        // 1) Fully opaque (or BackdropOpacity) dark themed base — wallpaper cannot
        //    bleed through this layer. Idle fade does NOT apply to the base.
        int baseA = Math.Clamp((int)(cfg.BackdropOpacity * 255f), 128, 255);
        var baseDark = Darken(cfg.RgbaLow, 0.55f);
        // Slight weather/bass tint of the solid base so it still feels alive,
        // but never drops below a deep, solid colour.
        var baseTint = Color.FromArgb(255,
            ClampByte(baseDark.R * 0.82 + br * 0.18),
            ClampByte(baseDark.G * 0.82 + bg * 0.18),
            ClampByte(baseDark.B * 0.82 + bb * 0.18));
        var baseRight = Color.FromArgb(255,
            ClampByte(baseTint.R * 0.88 + cfg.RgbaHigh.R * 0.08),
            ClampByte(baseTint.G * 0.88 + cfg.RgbaHigh.G * 0.08),
            ClampByte(baseTint.B * 0.88 + cfg.RgbaHigh.B * 0.10));
        if (baseA >= 255)
        {
            // SourceCopy a fully opaque gradient so no leftover alpha remains.
            var prev = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var brush = new LinearGradientBrush(
                       new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                       Color.FromArgb(255, baseTint), Color.FromArgb(255, baseRight),
                       LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, 0, 0, Width, Height);
            }
            g.CompositingMode = prev;
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(baseA, baseTint));
            g.FillRectangle(brush, 0, 0, Width, Height);
        }

        // 2) Reactive wash on top (colour only; base already blocks wallpaper).
        int washA = Math.Clamp((int)(richness / 255f * live * 140f), 0, 160);
        if (washA > 4)
        {
            var left = Color.FromArgb(washA, br, bg, bb);
            var mid = Color.FromArgb(Math.Min(180, washA + 24),
                Math.Min(255, br + 16), Math.Min(255, bg + 22), Math.Min(255, bb + 26));
            var right = Color.FromArgb(washA, Analyzer.LerpColor(cfg.RgbaLow, cfg.RgbaHigh, 0.4f));
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                left, right, LinearGradientMode.Horizontal);
            var blend = new ColorBlend
            {
                Colors = new[] { left, mid, right },
                Positions = new[] { 0f, 0.45f, 1f }
            };
            brush.InterpolationColors = blend;
            g.FillRectangle(brush, 0, 0, Width, Height);
        }

        // Soft bass glow — a wide ellipse sitting on the bar.
        float glow = (ctx.Analyzer.Bass * 0.7f + ctx.Analyzer.Beat * 0.55f) * live * cfg.Intensity;
        if (glow > 0.04f)
        {
            int ga = Math.Clamp((int)(glow * 140f), 0, 170);
            using var path = new GraphicsPath();
            path.AddEllipse(-Width * 0.15f, Height * 0.10f, Width * 1.3f, Height * 2.2f);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = Color.FromArgb(ga, cfg.RgbaMid);
            pgb.SurroundColors = new[] { Color.FromArgb(0, cfg.RgbaMid) };
            pgb.CenterPoint = new PointF(Width * 0.5f, Height * 0.85f);
            g.FillPath(pgb, path);
        }

        // Hairline top edge so the bar reads as a solid panel, not a stain on the desktop.
        using var edge = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);
        g.DrawLine(edge, 0, 0, Width, 0);
    }

    private static Color Darken(Color c, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(255,
            (int)(c.R * amount), (int)(c.G * amount), (int)(c.B * amount));
    }

    private static int ClampByte(double v) => Math.Clamp((int)Math.Round(v), 0, 255);

    private void ApplyIconSafeMask(Bitmap bmp, FrameContext ctx)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            var buf = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            for (int x = 0; x < w; x++)
            {
                float vis = LocalVisibility(ctx, x + 0.5f, 0f);
                if (vis >= 0.995f) continue;
                byte mul = (byte)Math.Clamp((int)(vis * 255f), 0, 255);
                for (int y = 0; y < h; y++)
                {
                    int i = y * stride + x * 4;
                    // premultiplied BGRA: scale all channels
                    buf[i] = (byte)((buf[i] * mul) / 255);
                    buf[i + 1] = (byte)((buf[i + 1] * mul) / 255);
                    buf[i + 2] = (byte)((buf[i + 2] * mul) / 255);
                    buf[i + 3] = (byte)((buf[i + 3] * mul) / 255);
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        }
        finally { bmp.UnlockBits(data); }
    }

    private void DrawSpectrum(Graphics g, FrameContext ctx, float live)
    {
        var cfg = ctx.Cfg;
        var bars = ctx.Analyzer.Bars;
        var peaks = ctx.Analyzer.Peaks;
        int n = bars.Length;
        if (n == 0) return;
        float gapFrac = cfg.BarGapPercent / 100f;
        float slot = Width / (float)n;
        float gap = slot * gapFrac;
        float barW = Math.Max(1.2f, slot - gap);
        float maxH = Height * (cfg.BarHeightPercent / 100f) * (0.62f + 0.38f * live);
        float barOp = Math.Max(cfg.EffectiveBarOpacity, 0.88f);
        int baseAlpha = Math.Clamp((int)(barOp * live * 255f), 0, 255);

        for (int i = 0; i < n; i++)
        {
            float x = i * slot + gap * 0.5f;
            float cx = x + barW * 0.5f;
            float local = LocalVisibility(ctx, cx, ctx.Fused ? 0.62f : 0.10f);
            if (local < 0.04f) continue;
            int alpha = Math.Clamp((int)(baseAlpha * local), 0, 255);
            float h = Math.Clamp(bars[i], 0f, 1f) * maxH;
            var col = Analyzer.BarColorAt(cfg, i, n, bars[i]);
            float radius = Math.Min(barW * 0.5f, 3.2f);

            if (h > 0.6f)
            {
                using var brush = new LinearGradientBrush(
                    new RectangleF(x, Height - h - 1, barW, h + 2),
                    Color.FromArgb(Math.Min(255, alpha + 30), col),
                    Color.FromArgb(Math.Max(20, alpha / 3), col),
                    LinearGradientMode.Vertical);
                FillRoundedTop(g, brush, x, Height - h, barW, h, radius);
                if (cfg.MirroredBars && h > 4f)
                {
                    float mh = h * 0.28f;
                    using var mb = new SolidBrush(Color.FromArgb(Math.Max(12, alpha / 5), col));
                    g.FillRectangle(mb, x, 0, barW, mh);
                }
            }

            if (cfg.PeakHold)
            {
                float ph = Math.Clamp(peaks[i], 0f, 1f) * maxH;
                if (ph > 1.5f)
                {
                    float py = Height - ph;
                    using var peakPen = new Pen(Color.FromArgb(Math.Min(255, alpha + 30), col), 1.15f);
                    peakPen.StartCap = LineCap.Round;
                    peakPen.EndCap = LineCap.Round;
                    g.DrawLine(peakPen, x, py, x + barW, py);
                }
            }
        }
    }

    private float LocalVisibility(FrameContext ctx, float clientX, float minBehindIcons)
    {
        if (!ctx.Cfg.IconSafeFade) return 1f;
        float vis = 1f;
        vis = Math.Min(vis, DistanceFade(clientX, ctx.Regions.IconCluster, ctx.Cfg.IconSafePadding, minBehindIcons));
        vis = Math.Min(vis, DistanceFade(clientX, ctx.Regions.Tray, Math.Max(8, ctx.Cfg.IconSafePadding / 2), minBehindIcons));
        return vis;
    }

    private float DistanceFade(float clientX, Rectangle screenRect, int pad, float minV)
    {
        if (screenRect.IsEmpty) return 1f;
        float left = screenRect.Left - Left - pad;
        float right = screenRect.Right - Left + pad;
        if (clientX >= left && clientX <= right) return minV;
        float edge = 36f;
        if (clientX < left && clientX > left - edge)
            return minV + (1f - minV) * ((left - clientX) / edge);
        if (clientX > right && clientX < right + edge)
            return minV + (1f - minV) * ((clientX - right) / edge);
        return 1f;
    }

    private void DrawWaveform(Graphics g, FrameContext ctx, float live)
    {
        var samples = ctx.Wave!;
        int len = samples.Length;
        if (len < 4) return;
        int points = Math.Min(Width, 480);
        var path = new PointF[points];
        float mid = Height * 0.62f;
        float amp = Height * 0.28f * live * Math.Clamp(ctx.Cfg.Sensitivity, 0.2f, 3f);
        int stepSrc = Math.Max(1, len / points);
        for (int i = 0; i < points; i++)
        {
            int si = Math.Min(len - 1, i * stepSrc);
            float s = Math.Clamp(samples[si], -1f, 1f);
            path[i] = new PointF(i * (Width - 1f) / (points - 1), mid - s * amp);
        }
        var col = Analyzer.LerpColor(ctx.Cfg.RgbaMid, ctx.Cfg.RgbaHigh, Math.Clamp(ctx.Analyzer.Energy, 0f, 1f));
        int alpha = Math.Clamp((int)(ctx.Cfg.EffectiveBarOpacity * live * 180f), 0, 255);
        using var pen = new Pen(Color.FromArgb(alpha, col), 1.4f);
        pen.LineJoin = LineJoin.Round;
        g.DrawLines(pen, path);
    }

    private void DrawAmbientWeather(Graphics g, FrameContext ctx, float live)
    {
        var w = ctx.Weather!;
        var kind = w.Kind;
        float ambi = 0.55f + 0.45f * live;

        if (kind is WeatherKind.Night or WeatherKind.Clear && !w.IsDay)
        {
            int a = Math.Clamp((int)(50 * ambi), 0, 80);
            using var night = new SolidBrush(Color.FromArgb(a, 8, 14, 36));
            g.FillRectangle(night, 0, 0, Width, Height);
        }
        if (kind == WeatherKind.Clear && w.IsDay)
        {
            int a = Math.Clamp((int)(40 * ambi * ctx.Cfg.Intensity), 0, 70);
            using var path = new GraphicsPath();
            path.AddEllipse(Width * 0.55f, -Height * 1.2f, Width * 0.7f, Height * 3.2f);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor = Color.FromArgb(a, 255, 210, 120);
            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 210, 120) };
            g.FillPath(pgb, path);
        }
        if (kind is WeatherKind.Cloudy or WeatherKind.Fog or WeatherKind.Storm)
        {
            EnsureClouds();
            float speedMul = kind == WeatherKind.Storm ? 1.6f : 0.7f;
            for (int i = 0; i < _clouds.Length; i++)
            {
                ref var c = ref _clouds[i];
                c.X += c.Speed * speedMul;
                if (c.X > Width + c.W) c.X = -c.W;
                int a = Math.Clamp((int)(c.Alpha * ambi * 255f * (kind == WeatherKind.Fog ? 0.7f : 0.45f)), 0, 80);
                using var br = new SolidBrush(Color.FromArgb(a, 230, 236, 244));
                g.FillEllipse(br, c.X, c.Y, c.W, c.H);
            }
        }
        if (kind is WeatherKind.Rain or WeatherKind.Storm)
        {
            EnsureRain();
            using var pen = new Pen(Color.FromArgb(0, 180, 210, 230), 1f);
            for (int i = 0; i < _rain.Length; i++)
            {
                ref var d = ref _rain[i];
                d.Y += d.Speed;
                if (d.Y > Height + d.Len)
                {
                    d.Y = -d.Len;
                    d.X = (float)_rng.NextDouble() * Width;
                }
                int a = Math.Clamp((int)(d.Alpha * ambi * 220f), 0, 140);
                pen.Color = Color.FromArgb(a, 190, 220, 240);
                g.DrawLine(pen, d.X, d.Y, d.X + 1.2f, d.Y + d.Len);
            }
        }
        if (kind == WeatherKind.Snow)
        {
            EnsureRain(); // reuse as flakes (slower, shorter)
            using var br = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
            for (int i = 0; i < _rain.Length; i++)
            {
                ref var d = ref _rain[i];
                d.Y += Math.Max(0.3f, d.Speed * 0.25f);
                d.X += MathF.Sin((ctx.Time + i) * 0.8f) * 0.25f;
                if (d.Y > Height + 2) { d.Y = -2; d.X = (float)_rng.NextDouble() * Width; }
                int a = Math.Clamp((int)(d.Alpha * ambi * 200f), 0, 160);
                br.Color = Color.FromArgb(a, 245, 248, 255);
                g.FillEllipse(br, d.X, d.Y, 2.2f, 2.2f);
            }
        }
        if (kind == WeatherKind.Storm && (int)(ctx.Time * 10) % 47 == 0)
        {
            using var flash = new SolidBrush(Color.FromArgb(28, 200, 220, 255));
            g.FillRectangle(flash, 0, 0, Width, Height);
        }
    }

    private void EnsureRain()
    {
        if (_rain.Length == 40) return;
        _rain = new RainDrop[40];
        for (int i = 0; i < _rain.Length; i++)
        {
            _rain[i] = new RainDrop
            {
                X = (float)_rng.NextDouble() * Math.Max(1, Width),
                Y = (float)_rng.NextDouble() * Math.Max(1, Height),
                Speed = 2.4f + (float)_rng.NextDouble() * 3.2f,
                Len = 5f + (float)_rng.NextDouble() * 8f,
                Alpha = 0.25f + (float)_rng.NextDouble() * 0.45f
            };
        }
    }

    private void EnsureClouds()
    {
        if (_clouds.Length == 4) return;
        _clouds = new CloudBlob[4];
        for (int i = 0; i < _clouds.Length; i++)
        {
            _clouds[i] = new CloudBlob
            {
                X = (float)_rng.NextDouble() * Math.Max(1, Width),
                Y = -Height * 0.4f,
                W = Width * (0.28f + (float)_rng.NextDouble() * 0.25f),
                H = Height * (0.9f + (float)_rng.NextDouble() * 0.5f),
                Speed = 0.08f + (float)_rng.NextDouble() * 0.12f,
                Alpha = 0.18f + (float)_rng.NextDouble() * 0.12f
            };
        }
    }

    private void DrawWeatherWidget(Graphics g, FrameContext ctx)
    {
        var slot = ctx.Regions.WeatherSlot;
        if (slot.Width < 40 || slot.Height < 16) return;
        var w = ctx.Weather!;
        string temp = w.Ok ? w.TemperatureText(ctx.Cfg.WeatherUnits == "fahrenheit") : "—";
        string cond = w.Ok ? w.ConditionVi : (string.IsNullOrEmpty(w.Error) ? "…" : "Offline");
        string line = temp + "  " + cond;

        using var font = new Font("Segoe UI Semibold", Math.Clamp(Height * 0.36f, 8.5f, 11.5f), FontStyle.Regular, GraphicsUnit.Point);
        var size = g.MeasureString(line, font);
        float glyph = Math.Min(slot.Height - 4, 16);
        float padX = 7f, padY = 2f;
        float boxW = glyph + 4 + size.Width + padX * 2;
        float boxH = Math.Max(size.Height, glyph) + padY * 2;
        float x = slot.X + 4;
        if (ctx.Cfg.WeatherPosition == "right")
            x = slot.Right - boxW - 4;
        float y = slot.Y + Math.Max(0, (slot.Height - boxH) / 2f);

        using (var path = RoundedRect(x, y, boxW, boxH, Math.Min(8f, boxH / 2f)))
        using (var plate = new SolidBrush(Color.FromArgb(210, 6, 12, 22)))
        using (var rim = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
        {
            g.FillPath(plate, path);
            g.DrawPath(rim, path);
        }

        var glyphRect = new RectangleF(x + padX, y + (boxH - glyph) / 2f, glyph, glyph);
        DrawWeatherGlyph(g, w.Ok ? w.Kind : WeatherKind.Cloudy, w.IsDay, glyphRect, ctx.Cfg);
        var textPos = new PointF(x + padX + glyph + 4, y + (boxH - size.Height) / 2f);
        using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            g.DrawString(line, font, shadow, textPos.X, textPos.Y + 1);
        using var fg = new SolidBrush(Color.FromArgb(245, 248, 252, 255));
        g.DrawString(line, font, fg, textPos);
    }

    private static void DrawWeatherGlyph(Graphics g, WeatherKind kind, bool isDay, RectangleF r, AppConfig cfg)
    {
        var mid = cfg.RgbaMid;
        using var fill = new SolidBrush(Color.FromArgb(230, mid));
        using var pen = new Pen(Color.FromArgb(230, 248, 251, 255), 1.2f);
        switch (kind)
        {
            case WeatherKind.Clear:
                g.FillEllipse(isDay ? Brushes.Gold : Brushes.LightGray,
                    r.X + r.Width * 0.18f, r.Y + r.Height * 0.18f, r.Width * 0.64f, r.Height * 0.64f);
                break;
            case WeatherKind.Night:
                g.FillEllipse(Brushes.LightGray, r.X + r.Width * 0.22f, r.Y + r.Height * 0.18f, r.Width * 0.58f, r.Height * 0.58f);
                using (var punch = new SolidBrush(Color.FromArgb(0, 0, 0, 0))) { /* crescent approximated */ }
                using (var hole = new SolidBrush(Color.FromArgb(180, 10, 16, 32)))
                    g.FillEllipse(hole, r.X + r.Width * 0.42f, r.Y + r.Height * 0.14f, r.Width * 0.5f, r.Height * 0.5f);
                break;
            case WeatherKind.Rain:
            case WeatherKind.Storm:
                g.FillEllipse(fill, r.X + 1, r.Y + 1, r.Width * 0.7f, r.Height * 0.55f);
                using (var drop = new Pen(Color.FromArgb(230, 170, 210, 255), 1.3f))
                {
                    g.DrawLine(drop, r.X + r.Width * 0.3f, r.Y + r.Height * 0.6f, r.X + r.Width * 0.22f, r.Y + r.Height * 0.95f);
                    g.DrawLine(drop, r.X + r.Width * 0.55f, r.Y + r.Height * 0.6f, r.X + r.Width * 0.47f, r.Y + r.Height * 0.95f);
                }
                break;
            case WeatherKind.Snow:
                g.FillEllipse(Brushes.White, r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
                break;
            default:
                g.FillEllipse(fill, r.X, r.Y + r.Height * 0.2f, r.Width * 0.7f, r.Height * 0.5f);
                g.FillEllipse(fill, r.X + r.Width * 0.3f, r.Y + r.Height * 0.1f, r.Width * 0.6f, r.Height * 0.55f);
                break;
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Max(0.5f, Math.Min(r, Math.Min(w, h) / 2f));
        var path = new GraphicsPath();
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillRoundedTop(Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        using var path = new GraphicsPath();
        if (h <= r * 2 || w <= r * 2)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
        }
        else
        {
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddLine(x + w, y + r, x + w, y + h);
            path.AddLine(x + w, y + h, x, y + h);
            path.AddLine(x, y + h, x, y + r);
            path.CloseFigure();
        }
        g.FillPath(brush, path);
    }

    private void EnsureBuffer(int w, int h)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        if (_buffer is not null && _buffer.Width == w && _buffer.Height == h) return;
        _buffer?.Dispose();
        _buffer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
    }

    private void PushLayered(Bitmap bmp)
    {
        if (!IsHandleCreated || _memDc == IntPtr.Zero) return;
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(_memDc, hBmp);
        try
        {
            var loc = new Native.POINT { X = Left, Y = Top };
            var size = new Native.SIZE { Cx = bmp.Width, Cy = bmp.Height };
            var src = new Native.POINT { X = 0, Y = 0 };
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA
            };
            Native.UpdateLayeredWindow(Handle, _screenDc, ref loc, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(_memDc, old);
            Native.DeleteObject(hBmp);
        }
    }

    public bool PlaceBehindTaskbar()
    {
        if (!IsHandleCreated || TaskbarHwnd == IntPtr.Zero)
        {
            BehindOk = false;
            return false;
        }

        // Stay in the topmost band (with the taskbar) so maximized windows
        // cannot cover the strip — but NEVER pass HWND_TOPMOST as insert-after,
        // which would put us at the top of that band and cover every icon.
        // SetWindowPos(viz, taskbarHwnd) inserts viz immediately AFTER the
        // taskbar in z-order, i.e. directly underneath it.
        int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
        if ((ex & Native.WS_EX_TOPMOST) == 0)
            Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOPMOST));

        const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Native.SetWindowPos(Handle, TaskbarHwnd, 0, 0, 0, 0, flags);
            if (IsBelowTaskbar() && !IsAboveTaskbar())
            {
                BehindOk = true;
                return true;
            }
        }
        BehindOk = false;
        return false;
    }

    public void PlaceAboveTaskbarSafe()
    {
        BehindOk = false;
        if (!IsHandleCreated) return;
        // Immediately above the taskbar, below Start / flyouts. Icon-safe mask
        // must be applied in this mode so icons are never covered.
        IntPtr insertAfter = Native.HwndTopmost;
        if (TaskbarHwnd != IntPtr.Zero)
        {
            var above = Native.GetWindow(TaskbarHwnd, Native.GW_HWNDPREV);
            if (above != IntPtr.Zero && above != Handle)
                insertAfter = above;
        }
        Native.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    public void ReassertZ(bool fused)
    {
        if (fused) PlaceBehindTaskbar();
        else PlaceAboveTaskbarSafe();
    }

    public bool IsAboveTaskbar()
    {
        if (!IsHandleCreated || TaskbarHwnd == IntPtr.Zero) return false;
        var w = Handle;
        for (int i = 0; i < 512; i++)
        {
            w = Native.GetWindow(w, Native.GW_HWNDNEXT);
            if (w == IntPtr.Zero) return false;
            if (w == TaskbarHwnd) return true;
        }
        return false;
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

    public void ClearAndHide()
    {
        if (!IsHandleCreated) return;
        lock (_drawLock)
        {
            EnsureBuffer(Math.Max(1, Width), Math.Max(1, Height));
            if (_buffer is null) return;
            using (var g = Graphics.FromImage(_buffer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                    g.FillRectangle(clear, 0, 0, _buffer.Width, _buffer.Height);
            }
            PushLayered(_buffer);
        }
        if (Visible) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
            _diorama.Reset();
        }
        if (_memDc != IntPtr.Zero) { Native.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
        if (_screenDc != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
        base.Dispose(disposing);
    }
}
