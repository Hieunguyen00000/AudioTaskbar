using System.Drawing.Drawing2D;

namespace AudioTaskbar;

/// <summary>
/// v5 regional scenery: recognisable landmark silhouettes on the horizon, a
/// region-flavoured filler skyline, optional sea/river/lake band with boats,
/// characteristic local traffic on the road, plus night lights (lighthouse
/// beam, Eiffel sparkle, Dragon Bridge glow, Hội An lanterns...).
/// All coordinates are logical (bar height = 48); the caller scales.
/// </summary>
internal sealed class RegionScenery : IDisposable
{
    private RegionProfile _p = RegionCatalog.Default;
    private int _w, _h;
    private float _ground;       // y of the ground line
    private float _shore;        // base of the far skyline when there is water
    private float _roadNear, _roadFar;
    private string _key = "";
    private readonly Random _rng = new(5);

    private GraphicsPath _far = new();
    private GraphicsPath _mid = new();
    private GraphicsPath _sil = new();
    private readonly List<Accent> _accents = new();
    private readonly List<Stroke> _strokes = new();
    private readonly List<RectangleF> _windows = new();
    private readonly List<LightFx> _lights = new();
    private readonly List<(float x, float w)> _waterSegs = new();
    private readonly List<Vehicle> _vehicles = new();
    private readonly List<Petal> _petals = new();
    private float _spawnNear, _spawnFar, _spawnWater;
    private float _lastTime = -1;
    public List<(float L, float R)> PlacedLandmarks { get; } = new();
    public string ActiveId => _p.Id;

    private sealed class Accent
    {
        public GraphicsPath Path = new();
        public Color Day;
        public Color Night;
        public bool Glow;   // brighter at night instead of darker
    }

    private struct Stroke { public PointF A, B; public float Width; public int Layer; public Color? Color; }

    private enum LightKind { Beacon, EiffelSparkle, DragonGlow, Lantern, TowerGlow, RoofLed }

    private struct LightFx
    {
        public LightKind Kind;
        public float X, Y, W, H;
        public Color Color;
        public float Phase;
        public PointF[]? Path;
    }

    private sealed class Vehicle
    {
        public VehicleKind Kind;
        public float X, Speed, Len, Y, Scale;
        public int Dir; // +1 LTR, -1 RTL
        public int Lane; // 0 near road, 1 far road, 2 water
        public int Seed;
        public Color Paint;
    }

    private struct Petal { public float X, Y, Vx, Vy, Phase, Size; }

    // v5.2 snow caps: thin top-edge bands of each silhouette path, cached per geometry build.
    private readonly Dictionary<GraphicsPath, Region> _caps = new();

    private Region CapOf(GraphicsPath path, float depth)
    {
        if (_caps.TryGetValue(path, out var r)) return r;
        r = new Region(path);
        using (var shifted = (GraphicsPath)path.Clone())
        using (var m = new Matrix())
        {
            m.Translate(0, depth);
            shifted.Transform(m);
            r.Exclude(shifted);
        }
        _caps[path] = r;
        return r;
    }

    /// <summary>Snow colour for caps / ground: crisp white by day, moonlit blue at night, pink at dawn/dusk.</summary>
    public static Color SnowTint(SceneMood mood, int alpha)
    {
        var c = SceneClock.Mix(Color.FromArgb(255, 150, 164, 206), Color.FromArgb(255, 236, 242, 252), mood.DayK);
        c = SceneClock.Mix(c, Color.FromArgb(255, 246, 206, 220), 0.45f * mood.WarmK);
        return Color.FromArgb(Math.Clamp(alpha, 0, 255), c);
    }

    private void SnowCap(Graphics g, SceneMood mood, GraphicsPath path, float depth, int alpha)
    {
        if (mood.SnowCover <= 0.01f) return;
        using var br = new SolidBrush(SnowTint(mood, (int)(alpha * (0.55f + 0.45f * mood.SnowCover))));
        g.FillRegion(br, CapOf(path, depth));
    }

    public void Dispose()
    {
        foreach (var r in _caps.Values) r.Dispose();
        _caps.Clear();
        _far.Dispose(); _mid.Dispose(); _sil.Dispose();
        foreach (var a in _accents) a.Path.Dispose();
        _accents.Clear();
    }

    /// <summary>Rebuild static geometry when region, size or free spans change.</summary>
    public void Build(RegionProfile p, int w, int h, float ground, IReadOnlyList<(float L, float R)> spans)
    {
        string key = p.Id + "|" + w + "x" + h + "|" + string.Join(",", spans.Select(s => ((int)(s.L / 4)) + "-" + ((int)(s.R / 4))));
        if (key == _key) return;
        bool regionChanged = p.Id != _p.Id || w != _w;
        _key = key;
        _p = p; _w = w; _h = h; _ground = ground;
        bool water = p.Water2 != WaterKind.None;
        _shore = water ? ground - 3.6f : ground;
        _roadNear = h - 1.6f;
        _roadFar = h - 4.4f;

        Dispose();
        _far = new GraphicsPath(); _mid = new GraphicsPath(); _sil = new GraphicsPath();
        _strokes.Clear(); _windows.Clear(); _lights.Clear(); _waterSegs.Clear();
        PlacedLandmarks.Clear();

        var rnd = new Random(4242 + w);
        BuildFiller(rnd);
        if (water)
        {
            if (p.Water2 == WaterKind.Lake) { /* segments added by the lake landmark */ }
            else _waterSegs.Add((0, w));
        }
        PlaceLandmarks(spans);

        if (regionChanged || _vehicles.Count == 0)
        {
            _vehicles.Clear();
            SeedTraffic();
        }
        if (p.Petals && _petals.Count == 0)
        {
            for (int i = 0; i < 26; i++)
                _petals.Add(new Petal
                {
                    X = (float)_rng.NextDouble() * w, Y = (float)_rng.NextDouble() * h,
                    Vx = 5 + (float)_rng.NextDouble() * 7, Vy = 2.5f + (float)_rng.NextDouble() * 3.5f,
                    Phase = (float)_rng.NextDouble() * 6, Size = 1.1f + (float)_rng.NextDouble() * 0.9f
                });
        }
        else if (!p.Petals) _petals.Clear();
    }

    #region landmark placement

    private static float WidthOf(string id) => id switch
    {
        "landmark81" => 15, "bitexco" => 13, "notredame-sg" => 21, "baSonBridge" => 66,
        "christ-vt" => 48, "lighthouse-vt" => 36, "palms-baisau" => 40,
        "turtle-tower" => 30, "longbien" => 86, "one-pillar" => 17, "thehuc" => 30,
        "dragon-bridge" => 92, "golden-bridge" => 58, "hoian-lanterns" => 46,
        "fuji" => 112, "tokyo-tower" => 19, "torii" => 17, "sakura" => 34,
        "eiffel" => 25, "arc-triomphe" => 22, "dome" => 22,
        "hills-bridge" => 70, "cozy-skyline" => 52,
        "opera-house" => 40, "harbour-bridge" => 84, "sydney-tower" => 14,
        "statue-liberty" => 16, "empire-state" => 16, "brooklyn-bridge" => 80,
        "colosseum" => 40, "st-peters" => 34, "forum-pine" => 26,
        "pyramids" => 92, "sphinx" => 26, "minaret" => 15,
        "blue-dome" => 26, "windmill" => 14, "caldera-houses" => 58,
        _ => 20
    };

    private void PlaceLandmarks(IReadOnlyList<(float L, float R)> spansIn)
    {
        var spans = spansIn.Where(s => s.R - s.L >= 12).Select(s => new SpanFill(s.L, s.R)).ToList();
        if (spans.Count == 0) spans.Add(new SpanFill(0, _w));
        foreach (var id in _p.Landmarks)
        {
            float lw = WidthOf(id);
            // Spread landmarks across the free gaps (prefer roomy spans holding fewer landmarks).
            var best = spans.Where(s => s.Free >= lw + 4).OrderByDescending(s => s.Free / (s.Items.Count + 1)).FirstOrDefault();
            if (best is null) continue;
            best.Items.Add((id, lw));
            best.Used += lw + 4;
        }
        foreach (var s in spans)
        {
            if (s.Items.Count == 0) continue;
            float total = s.Items.Sum(i => i.w);
            float slack = Math.Max(0, (s.R - s.L) - total);
            float gap = slack / (s.Items.Count + 1);
            float x = s.L + gap;
            foreach (var (id, lw) in s.Items)
            {
                BuildLandmark(id, x, lw);
                PlacedLandmarks.Add((x, x + lw));
                x += lw + gap;
            }
        }
    }

    private sealed class SpanFill
    {
        public float L, R, Used;
        public List<(string id, float w)> Items = new();
        public SpanFill(float l, float r) { L = l; R = r; }
        public float Free => R - L - Used;
    }

    #endregion

    #region geometry helpers

    private void Rect(GraphicsPath p, float x, float y, float w, float h) => p.AddRectangle(new RectangleF(x, y, w, h));
    private void Poly(GraphicsPath p, params PointF[] pts) { p.StartFigure(); p.AddPolygon(pts); p.CloseFigure(); }
    private static PointF P(float x, float y) => new(x, y);
    private void Line(float x1, float y1, float x2, float y2, float w, int layer = 0, Color? c = null)
        => _strokes.Add(new Stroke { A = P(x1, y1), B = P(x2, y2), Width = w, Layer = layer, Color = c });

    private Accent NewAccent(Color day, Color night, bool glow = false)
    {
        var a = new Accent { Day = day, Night = night, Glow = glow };
        _accents.Add(a);
        return a;
    }

    private void Windows(float x, float y, float w, float h, float cell, Random rnd, float density = 0.55f)
    {
        for (float yy = y + 1.2f; yy < y + h - 1.2f; yy += cell)
            for (float xx = x + 1f; xx < x + w - 1.4f; xx += cell * 0.9f)
                if (rnd.NextDouble() < density) _windows.Add(new RectangleF(xx, yy, 0.9f, 1.0f));
    }

    private void Hill(GraphicsPath p, float x, float w, float h, float baseY)
    {
        p.StartFigure();
        p.AddLine(x, baseY + 1, x, baseY);
        p.AddBezier(P(x, baseY), P(x + w * 0.28f, baseY - h * 1.05f), P(x + w * 0.72f, baseY - h * 1.05f), P(x + w, baseY));
        p.AddLine(x + w, baseY, x + w, baseY + 1);
        p.CloseFigure();
    }

    private void Palm(float x, float baseY, float hgt, float lean, Random rnd)
    {
        float tx = x + lean, ty = baseY - hgt;
        Line(x, baseY, x + lean * 0.5f, baseY - hgt * 0.5f, 1.1f);
        Line(x + lean * 0.5f, baseY - hgt * 0.5f, tx, ty, 0.95f);
        for (int i = 0; i < 6; i++)
        {
            float a = -2.9f + i * 0.95f + (float)rnd.NextDouble() * 0.2f;
            float len = 4.6f + (float)rnd.NextDouble() * 1.6f;
            float ex = tx + MathF.Cos(a) * len, ey = ty + MathF.Sin(a) * len * 0.55f + 1.6f;
            Line(tx, ty, (tx + ex) / 2, ty + MathF.Sin(a) * len * 0.3f - 0.6f, 0.9f);
            Line((tx + ex) / 2, ty + MathF.Sin(a) * len * 0.3f - 0.6f, ex, ey, 0.75f);
        }
    }

    #endregion

    #region filler skylines

    private void BuildFiller(Random rnd)
    {
        float b = _shore;
        float x = -4;
        switch (_p.Filler)
        {
            case "metro":
                while (x < _w)
                {
                    float bw = 5 + (float)rnd.NextDouble() * 8;
                    float bh = 5 + (float)Math.Pow(rnd.NextDouble(), 1.6) * 13;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    if (rnd.NextDouble() < 0.25) Rect(_mid, x + bw * 0.4f, b - bh - 2, 0.7f, 2);
                    Windows(x, b - bh, bw, bh, 2.2f, rnd, 0.45f);
                    x += bw + 0.8f + (float)rnd.NextDouble() * 2.5f;
                }
                break;
            case "coast":
                while (x < _w)
                {
                    if (rnd.NextDouble() < 0.3)
                    {
                        Palm(x + 2, b + 0.5f, 7 + (float)rnd.NextDouble() * 4, -1.5f + (float)rnd.NextDouble() * 3, rnd);
                        x += 7;
                        continue;
                    }
                    float bw = 5 + (float)rnd.NextDouble() * 6;
                    float bh = 3 + (float)rnd.NextDouble() * 5;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    Poly(_mid, P(x - 0.6f, b - bh), P(x + bw / 2, b - bh - 2), P(x + bw + 0.6f, b - bh));
                    Windows(x, b - bh, bw, bh, 2.4f, rnd, 0.4f);
                    x += bw + 3 + (float)rnd.NextDouble() * 9;
                }
                break;
            case "oldquarter":
                while (x < _w)
                {
                    if (rnd.NextDouble() < 0.22)
                    {
                        float th = 6 + (float)rnd.NextDouble() * 4;
                        _mid.AddEllipse(x, b - th, th * 1.1f, th);
                        Rect(_mid, x + th * 0.5f, b - 2, 0.9f, 3);
                        x += th * 1.1f + 1;
                        continue;
                    }
                    float bw = 3 + (float)rnd.NextDouble() * 2.5f;
                    float bh = 5 + (float)rnd.NextDouble() * 5;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    Poly(_mid, P(x - 0.3f, b - bh), P(x + bw / 2, b - bh - 1.4f), P(x + bw + 0.3f, b - bh));
                    Windows(x, b - bh, bw, bh, 2.2f, rnd, 0.5f);
                    x += bw + 0.5f;
                }
                break;
            case "hoian":
                while (x < _w)
                {
                    float bw = 6 + (float)rnd.NextDouble() * 5;
                    float bh = 3.5f + (float)rnd.NextDouble() * 2.5f;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    Poly(_mid, P(x - 1.2f, b - bh), P(x + 1, b - bh - 2.4f), P(x + bw - 1, b - bh - 2.4f), P(x + bw + 1.2f, b - bh));
                    Windows(x, b - bh, bw, bh, 2.4f, rnd, 0.5f);
                    if (rnd.NextDouble() < 0.3)
                    {
                        float th = 6 + (float)rnd.NextDouble() * 3;
                        _mid.AddEllipse(x + bw + 1, b - th, th, th);
                        x += th + 1;
                    }
                    x += bw + 1.5f;
                }
                break;
            case "tokyo":
                while (x < _w)
                {
                    if (rnd.NextDouble() < 0.2)
                    {
                        float th = 5 + (float)rnd.NextDouble() * 3;
                        var pink = NewAccent(Color.FromArgb(255, 238, 168, 196), Color.FromArgb(255, 120, 78, 104));
                        pink.Path.AddEllipse(x, b - th - 1.5f, th * 1.3f, th);
                        Rect(_mid, x + th * 0.6f, b - 2.5f, 0.9f, 3.5f);
                        x += th * 1.3f + 1;
                        continue;
                    }
                    float bw = 4 + (float)rnd.NextDouble() * 7;
                    float bh = 4 + (float)Math.Pow(rnd.NextDouble(), 1.4) * 10;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    Windows(x, b - bh, bw, bh, 2.0f, rnd, 0.5f);
                    x += bw + 0.6f + (float)rnd.NextDouble() * 1.5f;
                }
                break;
            case "haussmann":
                while (x < _w)
                {
                    float bw = 8 + (float)rnd.NextDouble() * 6;
                    float bh = 6 + (float)rnd.NextDouble() * 3;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    Poly(_mid, P(x, b - bh), P(x + 1.2f, b - bh - 2.6f), P(x + bw - 1.2f, b - bh - 2.6f), P(x + bw, b - bh));
                    if (rnd.NextDouble() < 0.6) Rect(_mid, x + 2 + (float)rnd.NextDouble() * (bw - 4), b - bh - 4, 1, 1.6f);
                    Windows(x, b - bh, bw, bh, 2.0f, rnd, 0.6f);
                    x += bw + 0.4f;
                }
                break;
            case "harbour":
                while (x < _w)
                {
                    float bw = 4 + (float)rnd.NextDouble() * 6;
                    float bh = 4 + (float)Math.Pow(rnd.NextDouble(), 1.5) * 11;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    if (rnd.NextDouble() < 0.2) Poly(_mid, P(x, b - bh), P(x + bw * 0.5f, b - bh - 2.2f), P(x + bw, b - bh));
                    Windows(x, b - bh, bw, bh, 2.1f, rnd, 0.5f);
                    x += bw + 0.8f + (float)rnd.NextDouble() * 3f;
                }
                break;
            case "manhattan":
                while (x < _w)
                {
                    float bw = 4 + (float)rnd.NextDouble() * 4;
                    float bh = 7 + (float)Math.Pow(rnd.NextDouble(), 1.2) * 16;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    if (rnd.NextDouble() < 0.3)
                    {   // rooftop water tower on stilts
                        float tx = x + bw * 0.3f;
                        Rect(_mid, tx, b - bh - 2.6f, 1.9f, 2.0f);
                        Poly(_mid, P(tx - 0.2f, b - bh - 2.6f), P(tx + 0.95f, b - bh - 3.6f), P(tx + 2.1f, b - bh - 2.6f));
                        Rect(_mid, tx + 0.2f, b - bh - 0.7f, 0.3f, 0.8f); Rect(_mid, tx + 1.4f, b - bh - 0.7f, 0.3f, 0.8f);
                    }
                    Windows(x, b - bh, bw, bh, 1.9f, rnd, 0.55f);
                    x += bw + 0.3f + (float)rnd.NextDouble() * 0.7f;
                }
                break;
            case "roman":
                while (x < _w)
                {
                    double r = rnd.NextDouble();
                    if (r < 0.16)
                    {   // umbrella pine
                        float th = 8 + (float)rnd.NextDouble() * 4;
                        Rect(_mid, x + 2.4f, b - th, 0.7f, th + 0.5f);
                        _mid.AddEllipse(x - 0.6f, b - th - 2.4f, 7.2f, 3.4f);
                        x += 7;
                        continue;
                    }
                    if (r < 0.24)
                    {   // campanile
                        Rect(_mid, x, b - 13, 2.2f, 13.5f);
                        Poly(_mid, P(x - 0.2f, b - 13), P(x + 1.1f, b - 15.2f), P(x + 2.4f, b - 13));
                        _windows.Add(new RectangleF(x + 0.7f, b - 11, 0.8f, 1.6f));
                        x += 3.2f;
                        continue;
                    }
                    float bw = 5 + (float)rnd.NextDouble() * 5;
                    float bh = 4 + (float)rnd.NextDouble() * 3.4f;
                    Rect(_mid, x, b - bh, bw, bh + 1);
                    Poly(_mid, P(x - 0.5f, b - bh), P(x + 1.4f, b - bh - 1.7f), P(x + bw - 1.4f, b - bh - 1.7f), P(x + bw + 0.5f, b - bh));
                    Windows(x, b - bh, bw, bh, 2.2f, rnd, 0.5f);
                    x += bw + 0.5f + (float)rnd.NextDouble() * 1.2f;
                }
                break;
            case "desert":
                {
                    var crest = new List<PointF> { P(0, b + 1) };
                    float cx = 0;
                    while (cx < _w)
                    {
                        float span = 80 + (float)rnd.NextDouble() * 110;
                        crest.Add(P(Math.Min(_w, cx + span * 0.5f), b - (3 + (float)rnd.NextDouble() * 8)));
                        cx += span;
                    }
                    crest.Add(P(_w, b + 1));
                    _far.StartFigure();
                    _far.AddLine(0, _h, 0, b + 1);
                    _far.AddCurve(crest.ToArray(), 0.5f);
                    _far.AddLine(_w, b + 1, _w, _h);
                    _far.CloseFigure();
                    while (x < _w)
                    {
                        if (rnd.NextDouble() < 0.45)
                        {
                            Palm(x + 2, b + 0.5f, 7 + (float)rnd.NextDouble() * 4, -1.2f + (float)rnd.NextDouble() * 2.4f, rnd);
                            x += 6 + (float)rnd.NextDouble() * 14;
                        }
                        else
                        {
                            float bw = 5 + (float)rnd.NextDouble() * 5, bh = 3 + (float)rnd.NextDouble() * 4;
                            Rect(_mid, x, b - bh, bw, bh + 1);          // flat-roofed mud-brick house
                            if (rnd.NextDouble() < 0.4) Rect(_mid, x + 0.8f, b - bh - 1.6f, bw * 0.45f, 1.6f);
                            Windows(x, b - bh, bw, bh, 2.4f, rnd, 0.4f);
                            x += bw + 5 + (float)rnd.NextDouble() * 18;
                        }
                    }
                }
                break;
            case "cycladic":
                {
                    var wh = NewAccent(Color.FromArgb(255, 238, 240, 244), Color.FromArgb(255, 112, 120, 152));
                    while (x < _w)
                    {
                        float bw = 4 + (float)rnd.NextDouble() * 4;
                        float bh = 3 + (float)rnd.NextDouble() * 4;
                        Rect(wh.Path, x, b - bh, bw, bh + 1);                       // whitewashed cube
                        if (rnd.NextDouble() < 0.55) Rect(wh.Path, x + bw * 0.2f, b - bh - 2.4f, bw * 0.55f, 2.4f); // stacked terrace
                        if (rnd.NextDouble() < 0.18)
                        {
                            var blue = NewAccent(Color.FromArgb(255, 40, 104, 190), Color.FromArgb(255, 30, 58, 124));
                            blue.Path.AddPie(x + bw * 0.1f, b - bh - 3.6f, bw * 0.8f, 3.4f * 1.4f, 180, 180);
                        }
                        _windows.Add(new RectangleF(x + bw * 0.35f, b - bh + 1.2f, 0.9f, 1.1f));
                        x += bw + 0.6f + (float)rnd.NextDouble() * 1.4f;
                    }
                }
                break;
            default: // village
                {
                    var crest = new List<PointF> { P(0, b + 1) };
                    float cx = 0;
                    while (cx < _w)
                    {
                        float span = 70 + (float)rnd.NextDouble() * 120;
                        crest.Add(P(Math.Min(_w, cx + span * 0.5f), b - (4 + (float)rnd.NextDouble() * 8)));
                        cx += span;
                    }
                    crest.Add(P(_w, b + 1));
                    _far.StartFigure();
                    _far.AddLine(0, _h, 0, b + 1);
                    _far.AddCurve(crest.ToArray(), 0.45f);
                    _far.AddLine(_w, b + 1, _w, _h);
                    _far.CloseFigure();
                    while (x < _w)
                    {
                        if (rnd.NextDouble() < 0.55)
                        {
                            float th = 5 + (float)rnd.NextDouble() * 6;
                            _mid.AddEllipse(x, b - th, th * 0.9f, th);
                            Rect(_mid, x + th * 0.4f, b - 2, 0.9f, 3);
                            x += th + 2 + (float)rnd.NextDouble() * 10;
                        }
                        else
                        {
                            float bw = 5 + (float)rnd.NextDouble() * 4;
                            float bh = 3 + (float)rnd.NextDouble() * 3;
                            Rect(_mid, x, b - bh, bw, bh + 1);
                            Poly(_mid, P(x - 0.8f, b - bh), P(x + bw / 2, b - bh - 2.2f), P(x + bw + 0.8f, b - bh));
                            Windows(x, b - bh, bw, bh, 2.4f, rnd, 0.5f);
                            x += bw + 4 + (float)rnd.NextDouble() * 14;
                        }
                    }
                }
                break;
        }
    }

    #endregion

    #region landmarks

    private void BuildLandmark(string id, float x, float lw)
    {
        float b = _ground + 0.6f;
        var rnd = new Random(id.GetHashCode() & 0xFFFF);
        switch (id)
        {
            case "landmark81":
            {
                // Stacked square tubes stepping up to a crown and spire.
                float c = x + lw / 2;
                Poly(_sil, P(x + 1, b), P(x + 1, b - 17), P(x + 2.6f, b - 17), P(x + 2.6f, b - 22.5f), P(x + 4.4f, b - 22.5f),
                    P(x + 4.4f, b - 26.5f), P(c - 1.6f, b - 26.5f), P(c - 1.6f, b - 29.5f), P(c - 0.35f, b - 29.5f), P(c, b - 33.5f),
                    P(c + 0.35f, b - 29.5f), P(c + 1.6f, b - 29.5f), P(c + 1.6f, b - 25), P(x + lw - 4.4f, b - 25),
                    P(x + lw - 4.4f, b - 20.5f), P(x + lw - 2.6f, b - 20.5f), P(x + lw - 2.6f, b - 15), P(x + lw - 1, b - 15), P(x + lw - 1, b));
                Windows(x + 1, b - 26, lw - 2, 26, 2.1f, rnd, 0.5f);
                _lights.Add(new LightFx { Kind = LightKind.RoofLed, X = c - 1.6f, Y = b - 29.4f, W = 3.2f, H = 0.8f, Color = Color.FromArgb(255, 120, 200, 255) });
                break;
            }
            case "bitexco":
            {
                Poly(_sil, P(x + 3, b), P(x + 2.5f, b - 12), P(x + 3.2f, b - 20), P(x + 5, b - 25.5f), P(x + 9, b - 23), P(x + 9.6f, b - 12), P(x + 9.2f, b));
                _sil.AddEllipse(x + 6.8f, b - 19.2f, 6.2f, 1.7f);       // helipad
                Rect(_sil, x + 8.5f, b - 18.2f, 1.2f, 1.6f);
                Windows(x + 3, b - 22, 6, 22, 2.0f, rnd, 0.5f);
                _lights.Add(new LightFx { Kind = LightKind.RoofLed, X = x + 7, Y = b - 18.6f, W = 6, H = 0.6f, Color = Color.FromArgb(255, 255, 210, 120) });
                break;
            }
            case "notredame-sg":
            {
                var brick = NewAccent(Color.FromArgb(255, 168, 82, 62), Color.FromArgb(255, 70, 40, 40));
                Rect(brick.Path, x + 4, b - 9, 13, 9);
                Poly(brick.Path, P(x + 4, b - 9), P(x + 10.5f, b - 13), P(x + 17, b - 9));
                Rect(brick.Path, x + 2, b - 15, 4.2f, 15);
                Rect(brick.Path, x + 14.8f, b - 15, 4.2f, 15);
                Poly(_sil, P(x + 1.8f, b - 15), P(x + 4.1f, b - 22), P(x + 6.4f, b - 15));
                Poly(_sil, P(x + 14.6f, b - 15), P(x + 16.9f, b - 22), P(x + 19.2f, b - 15));
                Line(x + 4.1f, b - 22, x + 4.1f, b - 23.6f, 0.5f);
                Line(x + 16.9f, b - 22, x + 16.9f, b - 23.6f, 0.5f);
                _windows.Add(new RectangleF(x + 9.8f, b - 8, 1.4f, 2.2f));
                _windows.Add(new RectangleF(x + 3.4f, b - 12, 1.2f, 1.6f));
                _windows.Add(new RectangleF(x + 16.2f, b - 12, 1.2f, 1.6f));
                break;
            }
            case "baSonBridge":
            {
                float deckY = b - 3;
                Rect(_sil, x, deckY, lw, 1.3f);
                float px = x + lw * 0.36f;
                Poly(_sil, P(px - 1.6f, b), P(px + 3.6f, b - 23), P(px + 5, b - 22.6f), P(px + 0.8f, b));
                for (int i = 0; i < 9; i++)
                {
                    float ty = b - 21 + i * 1.6f;
                    float tx = px + 3.9f - i * 0.36f;
                    Line(tx, ty, px - 4 - i * 2.8f, deckY, 0.35f);
                    Line(tx, ty, px + 8 + i * 4.6f, deckY, 0.35f);
                }
                for (float pxs = x + 6; pxs < x + lw; pxs += 14) Rect(_sil, pxs, deckY, 1.1f, 3);
                for (float lx = x + 3; lx < x + lw; lx += 6)
                    _windows.Add(new RectangleF(lx, deckY - 0.9f, 0.8f, 0.8f));
                break;
            }
            case "christ-vt":
            {
                var forest = NewAccent(Color.FromArgb(255, 58, 92, 64), Color.FromArgb(255, 18, 26, 30));
                Hill(forest.Path, x, lw, 10, b);
                float c = x + lw / 2;
                float top = b - 10.4f;
                var stone = NewAccent(Color.FromArgb(255, 236, 232, 222), Color.FromArgb(255, 150, 150, 160), glow: true);
                Rect(stone.Path, c - 1.6f, top - 3, 3.2f, 3.2f);       // pedestal
                Rect(stone.Path, c - 0.75f, top - 10.5f, 1.5f, 7.6f);  // robe
                stone.Path.AddEllipse(c - 0.85f, top - 12.3f, 1.7f, 1.8f);
                Rect(stone.Path, c - 4.8f, top - 9.6f, 9.6f, 1.05f);   // outstretched arms
                break;
            }
            case "lighthouse-vt":
            {
                var forest = NewAccent(Color.FromArgb(255, 62, 96, 66), Color.FromArgb(255, 18, 26, 30));
                Hill(forest.Path, x, lw, 7.5f, b);
                float c = x + lw * 0.52f;
                float top = b - 7.8f;
                var white = NewAccent(Color.FromArgb(255, 240, 238, 232), Color.FromArgb(255, 120, 124, 136), glow: true);
                Poly(white.Path, P(c - 1.8f, top + 0.4f), P(c - 1.1f, top - 10), P(c + 1.1f, top - 10), P(c + 1.8f, top + 0.4f));
                Rect(_sil, c - 1.9f, top - 12.6f, 3.8f, 2.7f);          // lantern room
                Poly(_sil, P(c - 2.2f, top - 12.5f), P(c, top - 14.6f), P(c + 2.2f, top - 12.5f));
                Rect(_sil, c - 2.4f, top - 10.1f, 4.8f, 0.6f);          // gallery
                _lights.Add(new LightFx { Kind = LightKind.Beacon, X = c, Y = top - 11.3f, Color = Color.FromArgb(255, 255, 240, 190) });
                break;
            }
            case "palms-baisau":
            {
                for (int i = 0; i < 4; i++)
                    Palm(x + 4 + i * 10.5f, b, 11 + (i % 2) * 3 + (float)rnd.NextDouble() * 2, -2 + (float)rnd.NextDouble() * 4, rnd);
                break;
            }
            case "turtle-tower":
            {
                if (_p.Water2 == WaterKind.Lake) _waterSegs.Add((x - 22, lw + 44));
                var islet = NewAccent(Color.FromArgb(255, 70, 96, 60), Color.FromArgb(255, 20, 28, 30));
                islet.Path.AddEllipse(x + 3, b - 3.4f, lw - 6, 3.6f);
                float c = x + lw / 2;
                var t = NewAccent(Color.FromArgb(255, 196, 180, 150), Color.FromArgb(255, 70, 66, 70), glow: true);
                Rect(t.Path, c - 3.2f, b - 7, 6.4f, 4.2f);
                Rect(t.Path, c - 2.4f, b - 9.8f, 4.8f, 2.9f);
                Rect(t.Path, c - 1.6f, b - 12.2f, 3.2f, 2.5f);
                Poly(_sil, P(c - 2.2f, b - 12.1f), P(c, b - 14.2f), P(c + 2.2f, b - 12.1f));
                Rect(_sil, c - 3.8f, b - 7.3f, 7.6f, 0.6f);
                Rect(_sil, c - 3.0f, b - 10.1f, 6.0f, 0.5f);
                _windows.Add(new RectangleF(c - 0.5f, b - 6, 1, 1.6f));
                _windows.Add(new RectangleF(c - 0.4f, b - 9, 0.8f, 1.2f));
                break;
            }
            case "longbien":
            {
                float deckY = b - 4;
                Rect(_sil, x, deckY, lw, 1.4f);
                int spans = 4;
                float sw = lw / spans;
                for (int i = 0; i < spans; i++)
                {
                    float sx = x + i * sw;
                    var pts = new List<PointF>();
                    for (int k = 0; k <= 10; k++)
                    {
                        float t = k / 10f;
                        pts.Add(P(sx + t * sw, deckY - MathF.Sin(t * MathF.PI) * 7.5f));
                    }
                    for (int k = 0; k < pts.Count - 1; k++)
                    {
                        Line(pts[k].X, pts[k].Y, pts[k + 1].X, pts[k + 1].Y, 1.1f);
                        Line(pts[k].X, pts[k].Y, pts[k].X, deckY, 0.45f);
                        if (k % 2 == 0) Line(pts[k].X, pts[k].Y, pts[k + 1].X, deckY, 0.4f);
                    }
                    Rect(_sil, sx - 0.8f, deckY, 1.8f, b - deckY);
                }
                break;
            }
            case "one-pillar":
            {
                float c = x + lw / 2;
                Rect(_sil, c - 0.8f, b - 6.2f, 1.6f, 6.2f);
                Rect(_sil, c - 4.2f, b - 7.2f, 8.4f, 1.2f);
                var wood = NewAccent(Color.FromArgb(255, 150, 70, 52), Color.FromArgb(255, 60, 34, 34));
                Rect(wood.Path, c - 3.2f, b - 10.2f, 6.4f, 3.1f);
                Poly(_sil, P(c - 6.5f, b - 9.6f), P(c - 5.4f, b - 10.4f), P(c - 3.8f, b - 13.2f), P(c + 3.8f, b - 13.2f), P(c + 5.4f, b - 10.4f), P(c + 6.5f, b - 9.6f));
                Line(c, b - 13.2f, c, b - 14.6f, 0.6f);
                _windows.Add(new RectangleF(c - 0.6f, b - 9.4f, 1.2f, 1.6f));
                break;
            }
            case "thehuc":
            {
                var red = NewAccent(Color.FromArgb(255, 206, 46, 40), Color.FromArgb(255, 150, 36, 36), glow: true);
                var pts = new List<PointF>();
                for (int k = 0; k <= 16; k++)
                {
                    float t = k / 16f;
                    pts.Add(P(x + t * lw, b - 1 - MathF.Sin(t * MathF.PI) * 6.5f));
                }
                var top = pts.Select(p => P(p.X, p.Y - 1.3f)).ToArray();
                var poly = pts.Concat(top.Reverse()).ToArray();
                Poly(red.Path, poly);
                for (int k = 1; k < 16; k += 2)
                    red.Path.AddRectangle(new RectangleF(pts[k].X - 0.25f, pts[k].Y - 3.2f, 0.5f, 2));
                var rail = top.Select(p => P(p.X, p.Y - 1.9f)).ToArray();
                for (int k = 0; k < rail.Length - 1; k++)
                    Line(rail[k].X, rail[k].Y, rail[k + 1].X, rail[k + 1].Y, 0.5f, 0, Color.FromArgb(255, 206, 46, 40));
                break;
            }
            case "dragon-bridge":
            {
                float deckY = b - 3;
                Rect(_sil, x, deckY, lw, 1.3f);
                for (float px = x + 8; px < x + lw; px += 16) Rect(_sil, px, deckY, 1.3f, 3);
                var gold = NewAccent(Color.FromArgb(255, 226, 178, 60), Color.FromArgb(255, 255, 196, 80), glow: true);
                var body = new List<PointF>();
                float bx0 = x + 4, bx1 = x + lw - 9;
                for (int k = 0; k <= 48; k++)
                {
                    float t = k / 48f;
                    float yy = deckY - 0.6f - MathF.Abs(MathF.Sin(t * MathF.PI * 3.5f)) * 9.5f;
                    body.Add(P(bx0 + t * (bx1 - bx0), yy));
                }
                var upper = body.Select(p => P(p.X, p.Y - 1.6f)).ToArray();
                Poly(gold.Path, body.Concat(upper.Reverse()).ToArray());
                float hx = bx1, hy = body[^1].Y - 1;
                Poly(gold.Path, P(hx - 1, hy + 1), P(hx + 1, hy - 4.5f), P(hx + 5.5f, hy - 4.2f), P(hx + 8, hy - 2.4f), P(hx + 4.2f, hy - 1.4f), P(hx + 2.4f, hy + 1.5f));
                Poly(gold.Path, P(hx + 2, hy - 4.4f), P(hx + 1.5f, hy - 7), P(hx + 3.6f, hy - 4.6f));
                _lights.Add(new LightFx { Kind = LightKind.DragonGlow, X = hx + 8, Y = hy - 2.6f, Path = upper, Color = Color.FromArgb(255, 255, 170, 60) });
                break;
            }
            case "golden-bridge":
            {
                var stone = NewAccent(Color.FromArgb(255, 132, 130, 116), Color.FromArgb(255, 52, 54, 56));
                foreach (float hx in new[] { x + 13, x + lw - 13 })
                {
                    Rect(stone.Path, hx - 2.2f, b - 6, 4.4f, 6);
                    stone.Path.AddEllipse(hx - 4, b - 11, 8, 6.4f);
                    for (int f = 0; f < 4; f++)
                    {
                        float fx = hx - 3.6f + f * 2.1f;
                        stone.Path.AddEllipse(fx, b - 15.5f + (f == 0 || f == 3 ? 1.6f : 0), 1.8f, 6);
                    }
                    stone.Path.AddEllipse(hx + 2.6f, b - 10.5f, 3.4f, 1.8f);   // thumb
                }
                var gold = NewAccent(Color.FromArgb(255, 222, 176, 64), Color.FromArgb(255, 200, 150, 60), glow: true);
                var deck = new List<PointF>();
                for (int k = 0; k <= 20; k++)
                {
                    float t = k / 20f;
                    deck.Add(P(x + t * lw, b - 13.6f - MathF.Sin(t * MathF.PI) * 2.6f + (t < 0.08f || t > 0.92f ? 2f : 0)));
                }
                Poly(gold.Path, deck.Concat(deck.Select(p => P(p.X, p.Y + 1.4f)).Reverse()).ToArray());
                for (int k = 0; k <= 20; k += 2)
                    Line(deck[k].X, deck[k].Y, deck[k].X, deck[k].Y - 1.5f, 0.4f, 0, Color.FromArgb(255, 222, 176, 64));
                break;
            }
            case "hoian-lanterns":
            {
                var wall = NewAccent(Color.FromArgb(255, 222, 182, 76), Color.FromArgb(255, 96, 76, 48));
                var roof = NewAccent(Color.FromArgb(255, 112, 60, 44), Color.FromArgb(255, 40, 26, 24));
                float hx = x;
                for (int i = 0; i < 3; i++)
                {
                    float bw = 13 + i % 2 * 2;
                    Rect(wall.Path, hx + 1, b - 7, bw - 2, 7);
                    Poly(roof.Path, P(hx - 0.8f, b - 7), P(hx + 1.8f, b - 10), P(hx + bw - 1.8f, b - 10), P(hx + bw + 0.8f, b - 7));
                    _windows.Add(new RectangleF(hx + 4, b - 5, 1.6f, 2.2f));
                    _windows.Add(new RectangleF(hx + bw - 5.4f, b - 5, 1.6f, 2.2f));
                    hx += bw + 1;
                }
                Color[] cols = { Color.FromArgb(255, 236, 64, 48), Color.FromArgb(255, 255, 168, 40), Color.FromArgb(255, 255, 214, 64), Color.FromArgb(255, 220, 48, 96) };
                for (int i = 0; i < 9; i++)
                {
                    float t = i / 8f;
                    float lx = x + 1 + t * (lw - 2);
                    float ly = b - 13.5f + MathF.Sin(t * MathF.PI) * 2.2f;
                    _lights.Add(new LightFx { Kind = LightKind.Lantern, X = lx, Y = ly, Color = cols[i % cols.Length], Phase = i * 0.7f });
                }
                for (int i = 0; i < 8; i++)
                {
                    float t0 = i / 8f, t1 = (i + 1) / 8f;
                    Line(x + 1 + t0 * (lw - 2), b - 14.6f + MathF.Sin(t0 * MathF.PI) * 2.2f,
                         x + 1 + t1 * (lw - 2), b - 14.6f + MathF.Sin(t1 * MathF.PI) * 2.2f, 0.35f);
                }
                break;
            }
            case "fuji":
            {
                float top = b - 24;
                _far.StartFigure();
                _far.AddLine(x, b + 1, x, b);
                _far.AddBezier(P(x, b), P(x + lw * 0.22f, b - 5), P(x + lw * 0.36f, top + 4), P(x + lw * 0.44f, top));
                _far.AddLine(x + lw * 0.44f, top, x + lw * 0.56f, top);
                _far.AddBezier(P(x + lw * 0.56f, top), P(x + lw * 0.64f, top + 4), P(x + lw * 0.78f, b - 5), P(x + lw, b));
                _far.AddLine(x + lw, b, x + lw, b + 1);
                _far.CloseFigure();
                var snow = NewAccent(Color.FromArgb(255, 246, 248, 255), Color.FromArgb(255, 150, 160, 186), glow: true);
                Poly(snow.Path, P(x + lw * 0.44f, top), P(x + lw * 0.56f, top), P(x + lw * 0.62f, top + 6.5f), P(x + lw * 0.58f, top + 5.2f),
                    P(x + lw * 0.54f, top + 7.2f), P(x + lw * 0.5f, top + 5.4f), P(x + lw * 0.46f, top + 7.4f), P(x + lw * 0.42f, top + 5.4f), P(x + lw * 0.38f, top + 6.5f));
                break;
            }
            case "tokyo-tower":
            {
                var orange = NewAccent(Color.FromArgb(255, 232, 92, 44), Color.FromArgb(255, 255, 140, 60), glow: true);
                orange.Path.FillMode = FillMode.Alternate;
                float c = x + lw / 2;
                Poly(orange.Path, P(x + 1, b), P(c - 1.6f, b - 14), P(c - 0.7f, b - 24), P(c - 0.25f, b - 30.5f), P(c + 0.25f, b - 30.5f),
                    P(c + 0.7f, b - 24), P(c + 1.6f, b - 14), P(x + lw - 1, b), P(c + 4.2f, b), P(c, b - 6), P(c - 4.2f, b));
                Rect(_sil, c - 3.2f, b - 14.6f, 6.4f, 1.6f);
                Rect(_sil, c - 1.6f, b - 22.6f, 3.2f, 1.2f);
                for (float yy = b - 3; yy > b - 24; yy -= 3)
                {
                    float hw = (b - yy) < 14 ? 7.5f - (b - yy) * 0.42f : 1.6f - (b - yy - 14) * 0.08f;
                    Line(c - hw, yy, c + hw, yy - 1.5f, 0.3f, 0, Color.FromArgb(255, 255, 255, 255));
                }
                _lights.Add(new LightFx { Kind = LightKind.TowerGlow, X = c, Y = b - 15, W = lw, H = 30, Color = Color.FromArgb(255, 255, 150, 60) });
                break;
            }
            case "torii":
            {
                var red = NewAccent(Color.FromArgb(255, 214, 52, 40), Color.FromArgb(255, 120, 32, 30));
                Rect(red.Path, x + 3, b - 11.5f, 1.7f, 11.5f);
                Rect(red.Path, x + lw - 4.7f, b - 11.5f, 1.7f, 11.5f);
                Rect(red.Path, x + 1.6f, b - 9.6f, lw - 3.2f, 1.1f);
                Poly(_sil, P(x - 0.4f, b - 12.6f), P(x + lw + 0.4f, b - 12.6f), P(x + lw - 0.6f, b - 14.2f), P(x + lw / 2, b - 13.6f), P(x + 0.6f, b - 14.2f));
                Rect(red.Path, x + 0.8f, b - 12.6f, lw - 1.6f, 1.2f);
                break;
            }
            case "sakura":
            {
                var pink = NewAccent(Color.FromArgb(255, 246, 172, 200), Color.FromArgb(255, 132, 84, 112));
                for (int i = 0; i < 3; i++)
                {
                    float tx = x + 5 + i * 11, th = 9 + (i % 2) * 3;
                    Line(tx, b, tx + 0.6f, b - th * 0.6f, 1.1f);
                    Line(tx + 0.6f, b - th * 0.6f, tx - 2, b - th * 0.85f, 0.6f);
                    pink.Path.AddEllipse(tx - 5, b - th - 2, 7, 5.5f);
                    pink.Path.AddEllipse(tx - 1.5f, b - th - 3, 7, 6);
                    pink.Path.AddEllipse(tx - 3.5f, b - th + 1, 8, 4.5f);
                }
                break;
            }
            case "eiffel":
            {
                var iron = NewAccent(Color.FromArgb(255, 92, 76, 64), Color.FromArgb(255, 150, 112, 60), glow: true);
                iron.Path.FillMode = FillMode.Alternate;
                float c = x + lw / 2;
                Poly(iron.Path, P(x, b), P(c - 6, b - 7.5f), P(c - 2.4f, b - 16), P(c - 1, b - 25), P(c - 0.3f, b - 31.5f), P(c + 0.3f, b - 31.5f),
                    P(c + 1, b - 25), P(c + 2.4f, b - 16), P(c + 6, b - 7.5f), P(x + lw, b), P(c + 5.4f, b), P(c + 3.4f, b - 4.8f), P(c, b - 6),
                    P(c - 3.4f, b - 4.8f), P(c - 5.4f, b));
                Rect(_sil, c - 7.6f, b - 8.4f, 15.2f, 1.2f);
                Rect(_sil, c - 3.4f, b - 16.6f, 6.8f, 1.0f);
                Rect(_sil, c - 1.4f, b - 25.6f, 2.8f, 0.9f);
                _lights.Add(new LightFx { Kind = LightKind.EiffelSparkle, X = c, Y = b, W = lw, H = 31, Color = Color.FromArgb(255, 255, 236, 180) });
                break;
            }
            case "arc-triomphe":
            {
                var stone = NewAccent(Color.FromArgb(255, 196, 184, 160), Color.FromArgb(255, 84, 80, 84), glow: true);
                stone.Path.FillMode = FillMode.Alternate;
                stone.Path.AddRectangle(new RectangleF(x + 2, b - 14, lw - 4, 14));
                var arch = new GraphicsPath();
                stone.Path.StartFigure();
                float aw = 6, ax = x + lw / 2 - aw / 2;
                stone.Path.AddLine(ax, b, ax, b - 6.5f);
                stone.Path.AddArc(ax, b - 9.5f, aw, 6, 180, 180);
                stone.Path.AddLine(ax + aw, b - 6.5f, ax + aw, b);
                stone.Path.CloseFigure();
                arch.Dispose();
                Rect(_sil, x + 1.4f, b - 15, lw - 2.8f, 1.6f);
                break;
            }
            case "dome":
            {
                float c = x + lw / 2;
                Rect(_sil, x + 2, b - 5, lw - 4, 5);
                for (float cx = x + 3; cx < x + lw - 3; cx += 2.2f) Rect(_sil, cx, b - 7, 0.8f, 2);
                Rect(_sil, c - 6, b - 11, 12, 6);
                _sil.AddPie(c - 6, b - 17, 12, 12, 180, 180);
                Rect(_sil, c - 0.7f, b - 19.5f, 1.4f, 3);
                Line(c, b - 19.5f, c, b - 21, 0.5f);
                for (float wx = c - 4.5f; wx < c + 4.5f; wx += 2.2f) _windows.Add(new RectangleF(wx, b - 9.6f, 0.9f, 2.4f));
                break;
            }
            case "opera-house":
            {
                var sails = NewAccent(Color.FromArgb(255, 244, 242, 238), Color.FromArgb(255, 188, 196, 224), glow: true);
                void Sail(float x0, float w, float h, float apex)
                {
                    float ax = x0 + w * apex;
                    sails.Path.StartFigure();
                    sails.Path.AddBezier(P(x0, b - 2.6f), P(x0 + w * 0.04f, b - 2.6f - h * 0.7f), P(ax - w * 0.2f, b - 2.6f - h * 0.97f), P(ax, b - 2.6f - h));
                    sails.Path.AddBezier(P(ax, b - 2.6f - h), P(ax + w * 0.28f, b - 2.6f - h * 0.55f), P(x0 + w * 0.86f, b - 2.6f - h * 0.2f), P(x0 + w, b - 2.6f));
                    sails.Path.CloseFigure();
                }
                Sail(x + 2.0f, 12.5f, 15.5f, 0.30f); Sail(x + 6.8f, 11f, 12.5f, 0.34f); Sail(x + 11.2f, 9.5f, 9.5f, 0.40f);
                Sail(x + 21.5f, 12f, 12f, 0.34f); Sail(x + 26.4f, 10.5f, 9.4f, 0.40f); Sail(x + 30.4f, 8.6f, 6.6f, 0.45f);
                Rect(_sil, x, b - 2.8f, lw, 3.4f);                                // granite podium
                _lights.Add(new LightFx { Kind = LightKind.TowerGlow, X = x + lw / 2, Y = b - 8, W = lw - 4, H = 14, Color = Color.FromArgb(255, 255, 214, 150) });
                break;
            }
            case "harbour-bridge":
            {
                float deck = b - 7f, apexY = b - 27f, left = x + 6, right = x + lw - 6;
                float Arch(float t) => deck - (deck - apexY) * (1f - (2f * t - 1f) * (2f * t - 1f));
                const int N = 24;
                for (int i = 0; i < N; i++)
                {
                    float t0 = i / (float)N, t1 = (i + 1) / (float)N;
                    float xa = left + t0 * (right - left), xb = left + t1 * (right - left);
                    Line(xa, Arch(t0), xb, Arch(t1), 1.0f);                                          // upper chord
                    float lowA = Arch(t0) + 3.4f * MathF.Sin(t0 * MathF.PI), lowB = Arch(t1) + 3.4f * MathF.Sin(t1 * MathF.PI);
                    Line(xa, lowA, xb, lowB, 0.8f);                                                   // lower chord
                    if (i % 2 == 0) Line(xa, lowA, xa, deck, 0.45f);                                  // hangers
                    if (i < N - 1 && i % 2 == 0) Line(xa, Arch(t0), xb + (xb - xa), lowB + 0.4f, 0.3f);
                }
                Line(x, deck, x + lw, deck, 1.5f);                                                    // deck
                Rect(_sil, x + 1.4f, b - 15.5f, 4.6f, 15.5f);                                         // stone pylons
                Rect(_sil, x + lw - 6f, b - 15.5f, 4.6f, 15.5f);
                for (float wx = x + 8; wx < x + lw - 8; wx += 3.6f) _windows.Add(new RectangleF(wx, deck - 1.3f, 0.8f, 0.7f));
                break;
            }
            case "sydney-tower":
            {
                float c = x + lw / 2;
                Rect(_sil, c - 0.75f, b - 28, 1.5f, 28);
                var gold = NewAccent(Color.FromArgb(255, 218, 170, 74), Color.FromArgb(255, 255, 214, 120), glow: true);
                Poly(gold.Path, P(c - 4.2f, b - 24.5f), P(c + 4.2f, b - 24.5f), P(c + 3f, b - 28.8f), P(c - 3f, b - 28.8f));
                Poly(gold.Path, P(c - 3f, b - 28.8f), P(c + 3f, b - 28.8f), P(c + 0.5f, b - 31.4f), P(c - 0.5f, b - 31.4f));
                Line(c, b - 31.4f, c, b - 36.5f, 0.45f);
                Rect(_sil, c - 1.9f, b - 3f, 3.8f, 3f);
                _lights.Add(new LightFx { Kind = LightKind.RoofLed, X = c - 0.8f, Y = b - 36.6f, W = 1.6f, H = 0.8f, Color = Color.FromArgb(255, 255, 90, 80) });
                break;
            }
            case "statue-liberty":
            {
                float c = x + lw / 2;
                Rect(_sil, x + 1.5f, b - 2.2f, lw - 3, 2.2f);                                        // star fort base
                Poly(_sil, P(c - 4.2f, b - 2.2f), P(c - 3f, b - 9), P(c + 3f, b - 9), P(c + 4.2f, b - 2.2f)); // pedestal
                var green = NewAccent(Color.FromArgb(255, 108, 168, 148), Color.FromArgb(255, 78, 122, 122));
                Poly(green.Path, P(c - 3f, b - 9), P(c - 2f, b - 17), P(c - 0.5f, b - 21.5f), P(c + 0.7f, b - 21.5f), P(c + 1.8f, b - 17), P(c + 3f, b - 9));
                green.Path.AddEllipse(c - 1.5f, b - 24.6f, 3f, 3.2f);                                // head
                for (int k = -2; k <= 2; k++) Line(c + k * 0.9f, b - 24.4f, c + k * 1.5f, b - 26.4f - Math.Abs(k) * -0.2f, 0.4f, 0, Color.FromArgb(255, 108, 168, 148)); // crown rays
                Line(c + 1.2f, b - 20.5f, c + 4.2f, b - 26.5f, 1.0f, 0, Color.FromArgb(255, 108, 168, 148));    // raised arm
                Line(c + 4.2f, b - 26.5f, c + 4.2f, b - 28.4f, 0.9f, 0, Color.FromArgb(255, 108, 168, 148));    // torch
                Rect(green.Path, c - 3.2f, b - 18.6f, 1.8f, 2.6f);                                   // tablet
                var flame = NewAccent(Color.FromArgb(255, 255, 196, 60), Color.FromArgb(255, 255, 214, 90), glow: true);
                flame.Path.AddEllipse(c + 3.4f, b - 31.6f, 1.7f, 3.4f);
                break;
            }
            case "empire-state":
            {
                float c = x + lw / 2;
                Poly(_sil, P(c - 6.4f, b), P(c - 6.4f, b - 16), P(c - 4.6f, b - 16), P(c - 4.6f, b - 22), P(c - 3.2f, b - 22),
                    P(c - 3.2f, b - 26.5f), P(c + 3.2f, b - 26.5f), P(c + 3.2f, b - 22), P(c + 4.6f, b - 22), P(c + 4.6f, b - 16), P(c + 6.4f, b - 16), P(c + 6.4f, b));
                Windows(c - 6.4f, b - 16, 12.8f, 16, 1.9f, rnd, 0.5f);
                var top = NewAccent(Color.FromArgb(255, 186, 190, 198), Color.FromArgb(255, 255, 212, 130), glow: true);
                Poly(top.Path, P(c - 3.2f, b - 26.5f), P(c - 2.2f, b - 29.4f), P(c + 2.2f, b - 29.4f), P(c + 3.2f, b - 26.5f));
                Poly(top.Path, P(c - 1.5f, b - 29.4f), P(c - 0.9f, b - 31.6f), P(c + 0.9f, b - 31.6f), P(c + 1.5f, b - 29.4f));
                Line(c, b - 31.6f, c, b - 37f, 0.5f);
                _lights.Add(new LightFx { Kind = LightKind.TowerGlow, X = c, Y = b - 24, W = 11, H = 18, Color = Color.FromArgb(255, 255, 190, 120) });
                break;
            }
            case "brooklyn-bridge":
            {
                float deck = b - 7f;
                float t1 = x + lw * 0.27f, t2 = x + lw * 0.73f, topY = b - 21f;
                foreach (float tx in new[] { t1, t2 })
                {
                    Rect(_sil, tx - 3.2f, topY, 2.2f, 21f); Rect(_sil, tx + 1f, topY, 2.2f, 21f);        // twin stone piers
                    Poly(_sil, P(tx - 3.2f, topY), P(tx - 2.1f, topY - 2.2f), P(tx - 1f, topY));          // pointed arches
                    Poly(_sil, P(tx + 1f, topY), P(tx + 2.1f, topY - 2.2f), P(tx + 3.2f, topY));
                    Rect(_sil, tx - 3.2f, topY + 5, 6.4f, 1.0f);
                }
                Line(x, deck, x + lw, deck, 1.4f);
                void Cable(float xa, float ya, float xb, float yb, float sag)
                {
                    const int M = 14;
                    for (int i = 0; i < M; i++)
                    {
                        float u0 = i / (float)M, u1 = (i + 1) / (float)M;
                        float xa0 = xa + (xb - xa) * u0, xa1 = xa + (xb - xa) * u1;
                        float ya0 = ya + (yb - ya) * u0 + sag * 4 * u0 * (1 - u0), ya1 = ya + (yb - ya) * u1 + sag * 4 * u1 * (1 - u1);
                        Line(xa0, ya0, xa1, ya1, 0.5f);
                    }
                }
                Cable(t1, topY, t2, topY, (deck - 2f) - topY);                // main span sags to the deck
                Cable(x, deck - 0.6f, t1, topY, 2.2f); Cable(t2, topY, x + lw, deck - 0.6f, 2.2f);
                for (int k = 1; k <= 5; k++)                                   // diagonal stays (the bridge's web)
                {
                    float dx = k * 5.5f;
                    Line(t1, topY + 0.4f, t1 - dx, deck, 0.25f); Line(t1, topY + 0.4f, t1 + dx, deck, 0.25f);
                    Line(t2, topY + 0.4f, t2 - dx, deck, 0.25f); Line(t2, topY + 0.4f, t2 + dx, deck, 0.25f);
                }
                for (float wx = x + 3; wx < x + lw - 3; wx += 3.2f) _windows.Add(new RectangleF(wx, deck - 1.3f, 0.8f, 0.7f));
                break;
            }
            case "colosseum":
            {
                var stone = NewAccent(Color.FromArgb(255, 208, 178, 134), Color.FromArgb(255, 118, 98, 84), glow: true);
                // tall outer wall on the left, the ruined side lower on the right
                Poly(stone.Path, P(x + 0.5f, b), P(x + 0.5f, b - 12.5f), P(x + 3f, b - 13.4f), P(x + 22f, b - 13.4f), P(x + 25f, b - 11f),
                    P(x + 27.5f, b - 8.6f), P(x + 31f, b - 7.8f), P(x + 34f, b - 6.4f), P(x + lw - 0.5f, b - 5.2f), P(x + lw - 0.5f, b));
                var arches = NewAccent(Color.FromArgb(255, 112, 88, 66), Color.FromArgb(255, 255, 196, 112), glow: true);
                for (float ax = x + 2f; ax < x + 22f; ax += 2.7f)
                {
                    arches.Path.AddPie(ax, b - 11.6f, 1.5f, 2.6f, 180, 180); Rect(arches.Path, ax, b - 10.3f, 1.5f, 1.3f);
                    arches.Path.AddPie(ax, b - 7.6f, 1.5f, 2.6f, 180, 180); Rect(arches.Path, ax, b - 6.3f, 1.5f, 1.3f);
                }
                for (float ax = x + 22.5f; ax < x + lw - 3; ax += 2.7f)
                {
                    float top = ax < x + 28 ? 8.4f : 5.6f;
                    arches.Path.AddPie(ax, b - top, 1.5f, 2.4f, 180, 180); Rect(arches.Path, ax, b - top + 1.2f, 1.5f, 1.3f);
                }
                Rect(arches.Path, x + 2f, b - 3.4f, lw - 5, 0.4f);
                break;
            }
            case "st-peters":
            {
                float c = x + lw / 2;
                var stone = NewAccent(Color.FromArgb(255, 218, 204, 178), Color.FromArgb(255, 104, 96, 92), glow: true);
                Rect(stone.Path, c - 9, b - 8.5f, 18, 8.5f);                                           // facade
                Poly(stone.Path, P(c - 9.6f, b - 8.5f), P(c, b - 11.4f), P(c + 9.6f, b - 8.5f));        // pediment
                Rect(stone.Path, c - 5.6f, b - 15.6f, 11.2f, 4.4f);                                    // attic / drum base
                Rect(stone.Path, c - 4.4f, b - 19.4f, 8.8f, 3.8f);                                     // drum
                stone.Path.AddPie(c - 5.2f, b - 26.2f, 10.4f, 12.4f, 180, 180);                        // dome
                Rect(stone.Path, c - 0.8f, b - 29.4f, 1.6f, 3.4f);                                     // lantern
                Poly(stone.Path, P(c - 0.9f, b - 29.4f), P(c, b - 31f), P(c + 0.9f, b - 29.4f));
                Line(c, b - 31f, c, b - 33.4f, 0.4f); Line(c - 0.8f, b - 32.6f, c + 0.8f, b - 32.6f, 0.4f); // cross
                for (float cx = x + 1.2f; cx < c - 10; cx += 1.7f) { Rect(_sil, cx, b - 4.6f, 0.6f, 4.6f); }  // colonnades
                for (float cx = c + 10.4f; cx < x + lw - 1; cx += 1.7f) { Rect(_sil, cx, b - 4.6f, 0.6f, 4.6f); }
                Rect(_sil, x + 0.6f, b - 5.2f, c - 9.2f - x, 0.8f); Rect(_sil, c + 9f, b - 5.2f, x + lw - c - 9.6f, 0.8f);
                for (float wx = c - 7; wx < c + 7; wx += 2.8f) _windows.Add(new RectangleF(wx, b - 6.4f, 0.9f, 2.4f));
                break;
            }
            case "forum-pine":
            {
                var marble = NewAccent(Color.FromArgb(255, 226, 216, 198), Color.FromArgb(255, 112, 108, 112), glow: true);
                Rect(marble.Path, x + 1, b - 1.2f, 15, 1.2f);
                for (int i = 0; i < 3; i++)
                {
                    float cx = x + 2.4f + i * 4.6f;
                    Rect(marble.Path, cx, b - 11 - (i == 2 ? 2f : 0), 1.5f, 10f + (i == 2 ? 2f : 0));  // fluted column
                    Rect(marble.Path, cx - 0.5f, b - 11.8f - (i == 2 ? 2f : 0), 2.5f, 0.9f);               // capital
                }
                Rect(marble.Path, x + 1.6f, b - 13f, 10.2f, 1.2f);                                     // surviving architrave
                // umbrella pine
                float px = x + lw - 6;
                Line(px, b, px + 0.5f, b - 11, 1.1f);
                var pineCol = NewAccent(Color.FromArgb(255, 48, 98, 62), Color.FromArgb(255, 26, 46, 42));
                pineCol.Path.AddEllipse(px - 4.8f, b - 15.5f, 10.4f, 5f);
                pineCol.Path.AddEllipse(px - 3f, b - 17.6f, 7f, 3.6f);
                break;
            }
            case "pyramids":
            {
                var sunSide = NewAccent(Color.FromArgb(255, 228, 188, 118), Color.FromArgb(255, 102, 88, 92));
                var shade = NewAccent(Color.FromArgb(255, 188, 146, 88), Color.FromArgb(255, 70, 60, 70));
                void Pyr(float bx, float w, float h)
                {
                    Poly(sunSide.Path, P(bx, b), P(bx + w / 2, b - h), P(bx + w * 0.55f, b));
                    Poly(shade.Path, P(bx + w / 2, b - h), P(bx + w, b), P(bx + w * 0.55f, b));
                }
                Pyr(x + 28, 32, 18.5f);                // Menkaure (rear, small right)
                Pyr(x + 62, 28, 11.2f);
                Pyr(x + 2, 46, 26.5f);                 // Khufu (largest)
                break;
            }
            case "sphinx":
            {
                var sand = NewAccent(Color.FromArgb(255, 206, 164, 100), Color.FromArgb(255, 88, 74, 78));
                Poly(sand.Path, P(x + 9, b), P(x + 8.6f, b - 4.2f), P(x + 17, b - 5.2f), P(x + 23, b - 4f), P(x + 24.6f, b - 1.6f), P(x + 25.6f, b));
                Rect(sand.Path, x, b - 1.8f, 10, 1.8f);                                                  // paws
                Poly(sand.Path, P(x + 4.4f, b - 1.8f), P(x + 3.6f, b - 7), P(x + 5.1f, b - 8.6f), P(x + 9.4f, b - 8.2f), P(x + 10f, b - 4f)); // chest + neck
                sand.Path.AddEllipse(x + 3.4f, b - 12.2f, 4f, 4.6f);                                      // face
                Poly(sand.Path, P(x + 2.4f, b - 6.8f), P(x + 2.8f, b - 12f), P(x + 5.4f, b - 13.8f), P(x + 8.2f, b - 12f), P(x + 8.6f, b - 6.8f), P(x + 7.2f, b - 7.4f), P(x + 3.6f, b - 7.4f)); // nemes
                break;
            }
            case "minaret":
            {
                Rect(_sil, x + 4.6f, b - 7.6f, 6.2f, 7.6f);
                _sil.AddPie(x + 4.8f, b - 12.6f, 5.8f, 7.6f, 180, 180);
                Line(x + 7.7f, b - 12.6f, x + 7.7f, b - 14.2f, 0.4f);
                foreach (float mx in new[] { x + 1.4f, x + 13.6f })
                {
                    Rect(_sil, mx - 0.7f, b - 20f, 1.4f, 20f);
                    Rect(_sil, mx - 1.5f, b - 15.6f, 3f, 0.8f);
                    Poly(_sil, P(mx - 0.9f, b - 20f), P(mx, b - 22.8f), P(mx + 0.9f, b - 20f));
                    Line(mx, b - 22.8f, mx, b - 24f, 0.35f);
                    _windows.Add(new RectangleF(mx - 0.3f, b - 18.2f, 0.6f, 1.2f));
                }
                for (float wx = x + 5.4f; wx < x + 10.4f; wx += 1.7f) _windows.Add(new RectangleF(wx, b - 5.6f, 0.8f, 1.8f));
                break;
            }
            case "blue-dome":
            {
                var white = NewAccent(Color.FromArgb(255, 244, 245, 248), Color.FromArgb(255, 176, 182, 210), glow: true);
                var blue = NewAccent(Color.FromArgb(255, 36, 100, 188), Color.FromArgb(255, 34, 62, 128));
                Rect(white.Path, x + 1, b - 4.6f, 5, 4.6f);                                               // neighbouring cube
                Rect(white.Path, x + 6.4f, b - 8.4f, 11, 8.4f);                                           // church body
                blue.Path.AddPie(x + 7.2f, b - 14.2f, 9.4f, 11.4f, 180, 180);                             // blue dome
                Line(x + 11.9f, b - 14.2f, x + 11.9f, b - 16f, 0.4f, 0, Color.FromArgb(255, 244, 245, 248));
                Line(x + 11.2f, b - 15.4f, x + 12.6f, b - 15.4f, 0.4f, 0, Color.FromArgb(255, 244, 245, 248));
                Rect(white.Path, x + 18.4f, b - 14f, 4.4f, 14f);                                          // bell tower
                Rect(blue.Path, x + 18.9f, b - 13f, 3.4f, 1.0f);
                blue.Path.AddPie(x + 19f, b - 16.4f, 3.2f, 4.4f, 180, 180);
                _windows.Add(new RectangleF(x + 19.9f, b - 11.2f, 1.2f, 2.4f));
                _windows.Add(new RectangleF(x + 10.8f, b - 6.2f, 1.4f, 2.6f));
                break;
            }
            case "windmill":
            {
                float c = x + lw / 2 - 0.5f;
                var white = NewAccent(Color.FromArgb(255, 242, 243, 246), Color.FromArgb(255, 170, 176, 204), glow: true);
                Poly(white.Path, P(c - 3f, b), P(c - 2.1f, b - 11), P(c + 2.1f, b - 11), P(c + 3f, b));
                var cap = NewAccent(Color.FromArgb(255, 150, 96, 64), Color.FromArgb(255, 80, 58, 56));
                Poly(cap.Path, P(c - 2.8f, b - 11), P(c, b - 14.6f), P(c + 2.8f, b - 11));
                float hx = c, hy = b - 11.6f;
                for (int k = 0; k < 4; k++)
                {
                    float a = MathF.PI / 4 + k * MathF.PI / 2;
                    Line(hx, hy, hx + MathF.Cos(a) * 7.2f, hy - MathF.Sin(a) * 7.2f, 0.6f, 0, Color.FromArgb(255, 226, 214, 196));
                }
                _windows.Add(new RectangleF(c - 0.5f, b - 6, 1.0f, 1.6f));
                break;
            }
            case "caldera-houses":
            {
                // volcanic cliff rising to the right, topped by cascading whitewashed houses and a blue dome
                Poly(_sil, P(x, b), P(x + 6, b - 5), P(x + 16, b - 9), P(x + 30, b - 13), P(x + 44, b - 16), P(x + lw, b - 17), P(x + lw, b));
                var white = NewAccent(Color.FromArgb(255, 240, 242, 246), Color.FromArgb(255, 148, 156, 190), glow: true);
                var blue = NewAccent(Color.FromArgb(255, 36, 100, 188), Color.FromArgb(255, 34, 62, 128));
                float CliffY(float px) => px < 6 ? b - px * 0.83f : px < 16 ? b - 5 - (px - 6) * 0.4f : px < 30 ? b - 9 - (px - 16) * 0.285f : px < 44 ? b - 13 - (px - 30) * 0.21f : b - 16 - (px - 44) * 0.07f;
                for (float px = 4; px < lw - 4; px += 4.6f)
                {
                    float cy = CliffY(px + 2f);
                    float bw = 3.6f + (float)rnd.NextDouble() * 1.6f, bh = 2.8f + (float)rnd.NextDouble() * 2.2f;
                    Rect(white.Path, x + px, cy - bh, bw, bh + 0.8f);
                    if (rnd.NextDouble() < 0.5) Rect(white.Path, x + px + 0.5f, cy - bh - 1.8f, bw * 0.6f, 1.8f);
                    _windows.Add(new RectangleF(x + px + bw * 0.4f, cy - bh + 1f, 0.8f, 1f));
                }
                float dx = x + lw * 0.62f, dy = CliffY(dx - x + 1.5f);
                blue.Path.AddPie(dx, dy - 5.8f, 4.6f, 5.4f, 180, 180);
                Line(dx + 2.3f, dy - 5.8f, dx + 2.3f, dy - 7f, 0.35f, 0, Color.FromArgb(255, 240, 242, 246));
                break;
            }
            case "hills-bridge":
            {
                Hill(_sil, x, lw * 0.55f, 9, b);
                Hill(_sil, x + lw * 0.4f, lw * 0.6f, 6, b);
                var pts = new List<PointF>();
                for (int k = 0; k <= 12; k++) { float t = k / 12f; pts.Add(P(x + lw * 0.2f + t * lw * 0.6f, b - 1 - MathF.Sin(t * MathF.PI) * 4)); }
                for (int k = 0; k < pts.Count - 1; k++) Line(pts[k].X, pts[k].Y, pts[k + 1].X, pts[k + 1].Y, 1.1f);
                break;
            }
            case "cozy-skyline":
            {
                float xx = x;
                while (xx < x + lw - 4)
                {
                    float bw = 5 + (float)rnd.NextDouble() * 5, bh = 7 + (float)rnd.NextDouble() * 12;
                    Rect(_sil, xx, b - bh, bw, bh);
                    Windows(xx, b - bh, bw, bh, 2.1f, rnd, 0.6f);
                    xx += bw + 1;
                }
                break;
            }
        }
    }

    #endregion

    #region drawing

    private static bool Lit(SceneMood m) => m.LightsK > 0.3f;

    public void DrawFar(Graphics g, SceneMood mood, float time)
    {
        if (mood.LightsK > 0.02f)
        {
            // City glow on the horizon so dark silhouettes still read at night.
            float k = mood.LightsK;
            var glowCol = _p.Id switch
            {
                "tokyo" => Color.FromArgb(255, 255, 150, 190),
                "paris" => Color.FromArgb(255, 255, 196, 120),
                "baria-vungtau" or "danang-hoian" => Color.FromArgb(255, 120, 190, 230),
                _ => Color.FromArgb(255, 255, 176, 110)
            };
            var rect = new RectangleF(0, _ground - 22, _w, 23);
            using var gb = new LinearGradientBrush(rect, Color.FromArgb(0, glowCol), Color.FromArgb((int)(70 * k), glowCol), LinearGradientMode.Vertical);
            g.FillRectangle(gb, rect);
        }
        var farCol = SceneClock.Mix(mood.Silhouette, mood.SkyBot, 0.45f);
        using (var br = new SolidBrush(farCol)) g.FillPath(br, _far);
        SnowCap(g, mood, _far, 1.0f, 70);
        var midCol = SceneClock.Mix(mood.Silhouette, mood.SkyBot, 0.2f);
        using (var br = new SolidBrush(midCol)) g.FillPath(br, _mid);
        SnowCap(g, mood, _mid, 1.2f, 115);
    }

    public void DrawWater(Graphics g, SceneMood mood, float time, float dt)
    {
        if (_waterSegs.Count == 0) return;
        bool lit = Lit(mood);
        var top = SceneClock.Mix(mood.SkyBot, Color.FromArgb(255, 40, 90, 130), 0.45f);
        var bot = SceneClock.Mix(mood.Ground, Color.FromArgb(255, 20, 50, 80), 0.5f);
        float y0 = _shore - 0.4f, y1 = _ground + 0.8f;
        foreach (var (x, w) in _waterSegs)
        {
            using var br = new LinearGradientBrush(new RectangleF(x, y0 - 0.5f, w, y1 - y0 + 1), top, bot, LinearGradientMode.Vertical);
            g.FillRectangle(br, x, y0, w, y1 - y0);
            using var hi = new Pen(Color.FromArgb((int)(90 - 20 * mood.LightsK), 255, 255, 255), 0.5f);
            for (int i = 0; i < (int)(w / 9); i++)
            {
                float wx = x + ((i * 37.3f + time * (2 + i % 3)) % w);
                float wy = y0 + 1 + (i % 3) * 1.1f;
                g.DrawLine(hi, wx, wy, wx + 2.5f, wy);
            }
        }
        if (mood.LightsK > 0.02f) // reflections of lit windows on the water
        {
            using var refl = new SolidBrush(Color.FromArgb((int)(60 * mood.LightsK), 255, 210, 130));
            foreach (var r in _windows)
                if (r.Bottom < _shore && r.Bottom > _shore - 8 && InWater(r.X))
                    g.FillRectangle(refl, r.X, _shore + (_shore - r.Bottom) * 0.25f + 0.3f, r.Width, 0.5f);
        }
        DrawVehicles(g, mood, time, lane: 2);
    }

    private bool InWater(float x) => _waterSegs.Any(s => x >= s.x && x <= s.x + s.w);

    public void DrawLandmarks(Graphics g, SceneMood mood, float time)
    {
        bool lit = Lit(mood);
        float nightK = mood.LightsK;
        foreach (var a in _accents)
        {
            var c = SceneClock.Mix(a.Day, a.Night, nightK);
            if (!a.Glow) c = SceneClock.Mix(c, mood.Silhouette, 0.25f * (1 - nightK));
            using var br = new SolidBrush(c);
            g.FillPath(br, a.Path);
            if (!a.Glow) SnowCap(g, mood, a.Path, 1.1f, 150);
        }
        var silCol = SceneClock.Mix(mood.Silhouette, Color.FromArgb(255, 58, 66, 92), 0.35f * nightK);
        using (var br = new SolidBrush(silCol)) g.FillPath(br, _sil);
        SnowCap(g, mood, _sil, 1.4f, 190);
        foreach (var s in _strokes)
        {
            var col = s.Color is { } sc ? SceneClock.Mix(sc, mood.Silhouette, nightK * 0.4f) : mood.Silhouette;
            using var pen = new Pen(col, s.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, s.A, s.B);
        }
        if (nightK > 0.02f)
        {
            int a = (int)(210 * nightK);
            using var win = new SolidBrush(Color.FromArgb(a, 255, 208, 126));
            int i = 0;
            foreach (var r in _windows)
            {
                i++;
                if (i % 9 == 0 && MathF.Sin(time * 0.7f + i) < -0.6f) continue; // a few switch off/on
                g.FillRectangle(win, r);
            }
        }
        DrawLights(g, mood, time, lit);
    }

    private void DrawLights(Graphics g, SceneMood mood, float time, bool lit)
    {
        foreach (var l in _lights)
        {
            switch (l.Kind)
            {
                case LightKind.Lantern:
                {
                    float pulse = 0.85f + 0.15f * MathF.Sin(time * 2.2f + l.Phase);
                    if (lit)
                    {
                        using var halo = new SolidBrush(Color.FromArgb((int)(60 * pulse), l.Color));
                        g.FillEllipse(halo, l.X - 2.6f, l.Y - 2.2f, 5.2f, 5.2f);
                    }
                    using var body = new SolidBrush(Color.FromArgb(lit ? 250 : 220, l.Color));
                    g.FillEllipse(body, l.X - 1.1f, l.Y - 0.6f, 2.2f, 2.6f);
                    break;
                }
                case LightKind.RoofLed when lit:
                {
                    using var br = new SolidBrush(Color.FromArgb(200, l.Color));
                    g.FillRectangle(br, l.X, l.Y, l.W, l.H);
                    break;
                }
                case LightKind.Beacon when lit:
                {
                    // Rotating lighthouse beam sweeping left and right.
                    float ang = time * 0.85f + l.Phase;
                    float cx = MathF.Cos(ang);
                    float reach = 90f * MathF.Abs(cx) + 8f;
                    float dir = cx >= 0 ? 1 : -1;
                    int alpha = (int)(110 * MathF.Pow(MathF.Abs(cx), 0.6f));
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { P(l.X, l.Y), P(l.X + dir * reach, l.Y - 3.2f), P(l.X + dir * reach, l.Y + 2.4f) });
                        using var pgb = new PathGradientBrush(path)
                        {
                            CenterPoint = P(l.X, l.Y),
                            CenterColor = Color.FromArgb(alpha, l.Color),
                            SurroundColors = new[] { Color.FromArgb(0, l.Color) }
                        };
                        g.FillPath(pgb, path);
                    }
                    float flash = 0.6f + 0.4f * MathF.Abs(cx);
                    using var glow = new SolidBrush(Color.FromArgb((int)(120 * flash), l.Color));
                    g.FillEllipse(glow, l.X - 2.4f, l.Y - 2.4f, 4.8f, 4.8f);
                    using var core = new SolidBrush(Color.FromArgb(255, 255, 250, 220));
                    g.FillEllipse(core, l.X - 0.9f, l.Y - 0.9f, 1.8f, 1.8f);
                    break;
                }
                case LightKind.EiffelSparkle when lit:
                {
                    // Gentle golden glow + sparkle (stronger during the first 5 minutes of each hour, like Paris).
                    bool show = mood.Hour - MathF.Floor(mood.Hour) < 5f / 60f; // first 5 (scene) minutes of each hour
                    int n = show ? 26 : 10;
                    var rnd = new Random((int)(time * (show ? 9 : 4)));
                    using var glow = new SolidBrush(Color.FromArgb(28, 255, 196, 90));
                    g.FillEllipse(glow, l.X - 9, l.Y - l.H, 18, l.H);
                    for (int i = 0; i < n; i++)
                    {
                        float t = (float)rnd.NextDouble();
                        float hw = (1 - MathF.Pow(t, 0.55f)) * (l.W / 2 - 1) + 0.4f;
                        float sx = l.X + ((float)rnd.NextDouble() * 2 - 1) * hw;
                        float sy = l.Y - t * l.H;
                        using var sp = new SolidBrush(Color.FromArgb(230, 255, 255, 236));
                        g.FillEllipse(sp, sx - 0.45f, sy - 0.45f, 0.9f, 0.9f);
                    }
                    using var beacon = new SolidBrush(Color.FromArgb(200, 255, 230, 160));
                    g.FillEllipse(beacon, l.X - 0.8f, l.Y - l.H - 1.4f, 1.6f, 1.6f);
                    break;
                }
                case LightKind.TowerGlow when lit:
                {
                    using var glow = new SolidBrush(Color.FromArgb(34, l.Color));
                    g.FillEllipse(glow, l.X - l.W / 2 - 2, l.Y - l.H / 2, l.W + 4, l.H + 6);
                    break;
                }
                case LightKind.DragonGlow:
                {
                    if (lit && l.Path is { Length: > 2 })
                    {
                        float hue = time * 0.15f;
                        var c = HueColor(hue);
                        using var pen = new Pen(Color.FromArgb(120, c), 1.1f);
                        g.DrawLines(pen, l.Path);
                    }
                    // Fire breath every ~45s (Đà Nẵng fires on weekend nights; we keep it subtle).
                    float cyc = time % 45f;
                    if (lit && cyc < 3.2f)
                    {
                        float k = cyc / 3.2f;
                        for (int i = 0; i < 7; i++)
                        {
                            float fx = l.X + k * (6 + i * 2.2f);
                            float fy = l.Y - MathF.Sin(i * 1.7f + time * 8) * 1.2f;
                            int a = (int)(200 * (1 - k) * (1 - i / 8f));
                            using var fb = new SolidBrush(Color.FromArgb(Math.Clamp(a, 0, 255), 255, 120 + i * 15, 40));
                            g.FillEllipse(fb, fx - 1.4f, fy - 1.2f, 2.8f + i * 0.3f, 2.4f);
                        }
                    }
                    break;
                }
            }
        }
    }

    private static Color HueColor(float h)
    {
        h = h - MathF.Floor(h);
        float r = MathF.Abs(h * 6 - 3) - 1, gg = 2 - MathF.Abs(h * 6 - 2), b = 2 - MathF.Abs(h * 6 - 4);
        return Color.FromArgb(255, (int)(Math.Clamp(r, 0, 1) * 255), (int)(Math.Clamp(gg, 0, 1) * 255), (int)(Math.Clamp(b, 0, 1) * 255));
    }

    public void DrawRoadTraffic(Graphics g, SceneMood mood, float time)
    {
        float dt = _lastTime < 0 ? 0 : Math.Clamp(time - _lastTime, 0, 0.25f);
        _lastTime = time;
        StepTraffic(dt);
        DrawVehicles(g, mood, time, lane: 1);
        DrawVehicles(g, mood, time, lane: 0);
    }

    public void DrawPetals(Graphics g, SceneMood mood, float time)
    {
        if (_petals.Count == 0) return;
        float dt = 1f / 30f;
        using var br = new SolidBrush(Color.FromArgb((int)(150 + 60 * mood.DayK), 255, 190, 214));
        for (int i = 0; i < _petals.Count; i++)
        {
            var p = _petals[i];
            p.X += (p.Vx + MathF.Sin(time * 0.8f + p.Phase) * 3) * dt;
            p.Y += (p.Vy + MathF.Cos(time * 1.3f + p.Phase) * 1.5f) * dt;
            if (p.X > _w + 4) p.X = -4;
            if (p.Y > _h + 2) { p.Y = -2; p.X = (float)_rng.NextDouble() * _w; }
            _petals[i] = p;
            float s = p.Size * (0.8f + 0.2f * MathF.Sin(time * 3 + p.Phase));
            g.FillEllipse(br, p.X, p.Y, s * 1.4f, s);
        }
    }

    #endregion

    #region traffic

    private static (float min, float max) SpeedOf(VehicleKind k) => k switch
    {
        VehicleKind.Motorbike => (20, 30),
        VehicleKind.CityBus => (12, 16),
        VehicleKind.Cyclo => (5, 7),
        VehicleKind.Taxi => (20, 27),
        VehicleKind.Bicycle => (8, 11),
        VehicleKind.FlowerBike => (6, 8),
        VehicleKind.Mamachari => (8, 10),
        VehicleKind.ClassicTaxi => (18, 24),
        VehicleKind.KeiTruck => (16, 21),
        VehicleKind.VintageCar => (14, 18),
        VehicleKind.TourBus => (10, 13),
        VehicleKind.Car => (20, 28),
        VehicleKind.Scooter => (14, 22),
        VehicleKind.YellowCab => (18, 25),
        VehicleKind.Camel => (4.5f, 6.5f),
        VehicleKind.Donkey => (4f, 5.5f),
        VehicleKind.Ferry => (2.6f, 4f),
        VehicleKind.FishingBoat => (2.2f, 3.6f),
        VehicleKind.Sailboat => (3, 5),
        VehicleKind.LanternBoat => (1.6f, 2.6f),
        _ => (10, 14)
    };

    private static float LenOf(VehicleKind k) => k switch
    {
        VehicleKind.CityBus => 26, VehicleKind.TourBus => 24, VehicleKind.Taxi => 15, VehicleKind.Car => 14,
        VehicleKind.ClassicTaxi => 15, VehicleKind.VintageCar => 13, VehicleKind.KeiTruck => 12,
        VehicleKind.Cyclo => 13, VehicleKind.Scooter => 10, VehicleKind.YellowCab => 15, VehicleKind.Camel => 14, VehicleKind.Donkey => 11,
        VehicleKind.Ferry => 15, VehicleKind.FishingBoat => 12, VehicleKind.Sailboat => 9, VehicleKind.LanternBoat => 8,
        _ => 10
    };

    private VehicleKind Pick((VehicleKind kind, float weight)[] table)
    {
        float total = table.Sum(t => t.weight);
        float r = (float)_rng.NextDouble() * total;
        foreach (var (k, w) in table) { r -= w; if (r <= 0) return k; }
        return table[^1].kind;
    }

    private static readonly Color[] Paints =
    {
        Color.FromArgb(255, 200, 48, 44), Color.FromArgb(255, 40, 92, 170), Color.FromArgb(255, 236, 236, 236),
        Color.FromArgb(255, 30, 30, 34), Color.FromArgb(255, 226, 180, 40), Color.FromArgb(255, 60, 140, 90),
        Color.FromArgb(255, 150, 150, 156)
    };

    private Vehicle? Spawn(int lane, float? atX = null)
    {
        var table = lane == 2 ? _p.Water : _p.Road;
        if (table.Length == 0) return null;
        if (lane == 2 && _waterSegs.Count == 0) return null;
        var k = Pick(table);
        var (mn, mx) = SpeedOf(k);
        int dir = lane == 0 ? 1 : lane == 1 ? -1 : (_rng.NextDouble() < 0.5 ? 1 : -1);
        float len = LenOf(k);
        float scale = lane == 1 ? 0.86f : lane == 2 ? 0.7f : 1f;
        float x;
        if (atX is { } ax) x = ax;
        else if (lane == 2)
        {
            var seg = _waterSegs[_rng.Next(_waterSegs.Count)];
            x = dir > 0 ? seg.x - len : seg.x + seg.w + len;
        }
        else x = dir > 0 ? -len - 2 : _w + len + 2;
        var v = new Vehicle
        {
            Kind = k, Dir = dir, Lane = lane, Len = len * scale, Scale = scale,
            Speed = mn + (float)_rng.NextDouble() * (mx - mn),
            X = x,
            Y = lane == 0 ? _roadNear : lane == 1 ? _roadFar : _shore + 1.6f,
            Seed = _rng.Next(1000),
            Paint = k switch
            {
                VehicleKind.CityBus => Color.FromArgb(255, 40, 150, 80),
                VehicleKind.Taxi => _rng.NextDouble() < 0.5 ? Color.FromArgb(255, 46, 160, 90) : Color.FromArgb(255, 236, 236, 236),
                VehicleKind.ClassicTaxi => Color.FromArgb(255, 40, 40, 44),
                VehicleKind.YellowCab => Color.FromArgb(255, 244, 196, 36),
                VehicleKind.Camel => Color.FromArgb(255, 176, 132, 80),
                VehicleKind.Donkey => Color.FromArgb(255, 132, 126, 120),
                VehicleKind.Ferry => Color.FromArgb(255, 236, 238, 240),
                VehicleKind.TourBus => Color.FromArgb(255, 200, 40, 44),
                VehicleKind.KeiTruck => Color.FromArgb(255, 236, 236, 236),
                VehicleKind.VintageCar => _rng.NextDouble() < 0.5 ? Color.FromArgb(255, 120, 170, 200) : Color.FromArgb(255, 200, 70, 60),
                _ => Paints[_rng.Next(Paints.Length)]
            }
        };
        _vehicles.Add(v);
        return v;
    }

    private void SeedTraffic()
    {
        _spawnNear = 1.5f; _spawnFar = 3f; _spawnWater = 4f;
        int n = Math.Max(3, _w / 260);
        for (int i = 0; i < n; i++)
        {
            Spawn(0, (float)_rng.NextDouble() * _w);
            Spawn(1, (float)_rng.NextDouble() * _w);
        }
        foreach (var seg in _waterSegs)
            for (int i = 0; i < Math.Max(1, (int)(seg.w / 300)); i++)
                Spawn(2, seg.x + (float)_rng.NextDouble() * seg.w);
    }

    /// <summary>
    /// v5.4: x range (logical px) with an airport. Road vehicles never enter it: one that reaches its edge turns off the road
    /// (is removed), and none is seeded or kept inside. null = no airport, traffic as before.
    /// </summary>
    public (float L, float R)? NoRoadZone { get; set; }

    /// <summary>Current x extents of the vehicles on the two road lanes (test hook).</summary>
    internal IReadOnlyList<(float L, float R, int Lane)> RoadExtents()
        => _vehicles.Where(v => v.Lane != 2).Select(v => v.Dir > 0 ? (v.X, v.X + v.Len, v.Lane) : (v.X - v.Len, v.X, v.Lane)).ToList();

    private void RemoveRoadVehiclesInZone()
    {
        if (NoRoadZone is not { } z) return;
        for (int i = _vehicles.Count - 1; i >= 0; i--)
        {
            var v = _vehicles[i];
            if (v.Lane == 2) continue;
            float l = v.Dir > 0 ? v.X : v.X - v.Len, r = v.Dir > 0 ? v.X + v.Len : v.X;
            if (r > z.L && l < z.R) _vehicles.RemoveAt(i);
        }
    }

    private void StepTraffic(float dt)
    {
        RemoveRoadVehiclesInZone();
        if (dt <= 0) return;
        for (int i = _vehicles.Count - 1; i >= 0; i--)
        {
            var v = _vehicles[i];
            v.X += v.Dir * v.Speed * dt;
            bool gone = v.Lane == 2
                ? !_waterSegs.Any(s => v.X > s.x - v.Len * 2 && v.X < s.x + s.w + v.Len * 2)
                : (v.Dir > 0 ? v.X > _w + v.Len + 4 : v.X < -v.Len - 4);
            if (gone) _vehicles.RemoveAt(i);
        }
        RemoveRoadVehiclesInZone();
        int maxRoad = Math.Max(3, _w / 220);
        _spawnNear -= dt; _spawnFar -= dt; _spawnWater -= dt;
        if (_spawnNear <= 0)
        {
            if (_vehicles.Count(v => v.Lane == 0) < maxRoad) Spawn(0);
            _spawnNear = 2.5f + (float)_rng.NextDouble() * 6f;
        }
        if (_spawnFar <= 0)
        {
            if (_vehicles.Count(v => v.Lane == 1) < maxRoad) Spawn(1);
            _spawnFar = 3f + (float)_rng.NextDouble() * 7f;
        }
        if (_spawnWater <= 0)
        {
            if (_vehicles.Count(v => v.Lane == 2) < Math.Max(2, _w / 400)) Spawn(2);
            _spawnWater = 12f + (float)_rng.NextDouble() * 18f;
        }
    }

    private void DrawVehicles(Graphics g, SceneMood mood, float time, int lane)
    {
        bool lit = Lit(mood);
        foreach (var v in _vehicles)
        {
            if (v.Lane != lane) continue;
            var st = g.Save();
            g.TranslateTransform(v.X, v.Y);
            g.ScaleTransform(v.Dir * v.Scale, v.Scale);
            try { DrawVehicle(g, v, mood, lit, time); }
            finally { g.Restore(st); }
        }
    }

    // Each vehicle is drawn facing +X with its rear at x=0, wheels touching y=0.
    private void DrawVehicle(Graphics g, Vehicle v, SceneMood mood, bool lit, float time)
    {
        var dark = Color.FromArgb(240, 22, 22, 26);
        var paint = lit ? SceneClock.Mix(v.Paint, Color.FromArgb(255, 20, 22, 30), 0.55f) : SceneClock.Mix(v.Paint, mood.Silhouette, 0.2f);
        using var body = new SolidBrush(paint);
        using var dk = new SolidBrush(dark);
        using var pen = new Pen(dark, 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var glass = new SolidBrush(lit ? Color.FromArgb(220, 255, 214, 140) : Color.FromArgb(200, 150, 190, 220));
        float spin = time * 6f;

        void Wheel(float cx, float r)
        {
            g.FillEllipse(dk, cx - r, -r * 2, r * 2, r * 2);
            using var hub = new Pen(Color.FromArgb(150, 120, 120, 124), 0.4f);
            g.DrawLine(hub, cx, -r, cx + MathF.Cos(spin) * r * 0.8f, -r + MathF.Sin(spin) * r * 0.8f);
        }
        void Rider(float x, float seatY, Color shirt, bool conicalHat = false)
        {
            using var sb = new SolidBrush(SceneClock.Mix(shirt, mood.Silhouette, lit ? 0.6f : 0.25f));
            g.FillRectangle(sb, x - 0.9f, seatY - 4.2f, 1.9f, 4.2f);
            using var head = new SolidBrush(Color.FromArgb(240, 40, 36, 38));
            if (conicalHat)
            {
                using var hat = new SolidBrush(Color.FromArgb(240, 214, 196, 150));
                g.FillPolygon(hat, new[] { P(x - 2.2f, seatY - 5.1f), P(x + 0.1f, seatY - 7.4f), P(x + 2.4f, seatY - 5.1f) });
            }
            else
                g.FillEllipse(head, x - 1, seatY - 6.4f, 2.1f, 2.2f);
        }
        void Headlight(float x, float y, float reach)
        {
            if (!lit) return;
            using var path = new GraphicsPath();
            path.AddPolygon(new[] { P(x, y), P(x + reach, y - 2.4f), P(x + reach, y + 1.8f) });
            using var pgb = new PathGradientBrush(path)
            {
                CenterPoint = P(x, y),
                CenterColor = Color.FromArgb(70, 255, 236, 180),
                SurroundColors = new[] { Color.FromArgb(0, 255, 236, 180) }
            };
            g.FillPath(pgb, path);
            using var hl = new SolidBrush(Color.FromArgb(255, 255, 240, 190));
            g.FillEllipse(hl, x - 0.7f, y - 0.7f, 1.4f, 1.4f);
        }
        void Tail(float x, float y)
        {
            using var tl = new SolidBrush(Color.FromArgb(lit ? 255 : 170, 255, 50, 40));
            g.FillRectangle(tl, x, y - 0.5f, 0.8f, 1.0f);
        }

        switch (v.Kind)
        {
            case VehicleKind.Motorbike:
            {
                Wheel(1.8f, 1.6f); Wheel(8.2f, 1.6f);
                g.FillPolygon(body, new[] { P(1, -2.4f), P(3, -4.4f), P(7.6f, -4.2f), P(9, -2.6f), P(6, -1.8f) });
                g.DrawLine(pen, 8.2f, -1.6f, 7.6f, -5.6f);
                Rider(4.3f, -4.2f, Paints[v.Seed % Paints.Length], conicalHat: v.Seed % 5 == 0);
                if (v.Seed % 3 == 0) Rider(2.4f, -3.9f, Paints[(v.Seed / 3) % Paints.Length]);
                Headlight(9.2f, -3.6f, 14); Tail(0.4f, -3);
                break;
            }
            case VehicleKind.CityBus:
            case VehicleKind.TourBus:
            {
                bool tour = v.Kind == VehicleKind.TourBus;
                float hgt = tour ? 10.5f : 8.4f;
                g.FillRectangle(body, 0, -hgt - 1.2f, 25, hgt);
                for (float wx = 2; wx < 22; wx += 3.4f) g.FillRectangle(glass, wx, -hgt + 0.4f, 2.4f, 2.2f);
                if (tour)
                {
                    for (float wx = 2; wx < 22; wx += 3.4f) g.FillRectangle(glass, wx, -hgt + 4.6f, 2.4f, 2.0f);
                    using var rail = new Pen(paint, 0.5f);
                    g.DrawLine(rail, 0, -hgt - 1.6f, 25, -hgt - 1.6f);
                    using var heads = new SolidBrush(Color.FromArgb(220, 40, 36, 38));
                    for (float hx = 3; hx < 22; hx += 4.2f) g.FillEllipse(heads, hx, -hgt - 3.4f, 1.6f, 1.7f);
                }
                else
                {
                    using var stripe = new SolidBrush(Color.FromArgb(220, 255, 220, 80));
                    g.FillRectangle(stripe, 0, -4.6f, 25, 0.8f);
                }
                g.FillRectangle(glass, 23, -hgt + 0.2f, 1.8f, 4.4f);
                Wheel(5, 1.7f); Wheel(20, 1.7f);
                Headlight(25.2f, -3.2f, 18); Tail(0, -3);
                break;
            }
            case VehicleKind.Cyclo:
            {
                // Passenger seat in front, driver pedalling behind (Vietnamese xích lô).
                Wheel(3, 1.7f); Wheel(10.6f, 1.5f); Wheel(12.4f, 1.5f);
                g.FillPolygon(body, new[] { P(8, -2), P(8.4f, -6.2f), P(13.4f, -6.2f), P(13.4f, -2.6f) });
                using var hood = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 30, 60, 110), mood.Silhouette, 0.3f));
                g.FillPie(hood, 8.4f, -9.6f, 6.2f, 6.4f, 180, 110);
                g.DrawLine(pen, 3, -1.7f, 6.2f, -5.8f);
                g.DrawLine(pen, 6.2f, -5.8f, 9, -2.4f);
                Rider(5.8f, -5.6f, Color.FromArgb(255, 230, 230, 230), conicalHat: true);
                Rider(11.2f, -3.8f, Color.FromArgb(255, 200, 80, 60));
                break;
            }
            case VehicleKind.Taxi:
            case VehicleKind.YellowCab:
            case VehicleKind.Car:
            case VehicleKind.ClassicTaxi:
            case VehicleKind.VintageCar:
            {
                if (v.Kind == VehicleKind.VintageCar)
                {
                    // Citroën 2CV-ish rounded silhouette.
                    g.FillPie(body, 0.5f, -9.2f, 12.4f, 14.4f, 180, 180);
                    g.FillRectangle(body, 0, -3.4f, 13, 2.2f);
                    g.FillRectangle(glass, 3.4f, -6.6f, 2.8f, 2.2f);
                    g.FillRectangle(glass, 7, -6.6f, 2.8f, 2.2f);
                    Wheel(2.8f, 1.6f); Wheel(10.2f, 1.6f);
                    Headlight(13, -3.2f, 14); Tail(0, -3);
                    break;
                }
                bool classic = v.Kind == VehicleKind.ClassicTaxi;
                float len = classic ? 15 : v.Kind is VehicleKind.Taxi or VehicleKind.YellowCab ? 15 : 14;
                g.FillRectangle(body, 0, -4.8f, len, 3.4f);
                if (classic)
                    g.FillPolygon(body, new[] { P(4, -4.8f), P(4.6f, -7.4f), P(10.2f, -7.4f), P(11, -4.8f) });
                else
                    g.FillPolygon(body, new[] { P(3, -4.8f), P(4.8f, -7.6f), P(10, -7.6f), P(12, -4.8f) });
                g.FillRectangle(glass, 5.2f, -7f, 2.2f, 1.9f);
                g.FillRectangle(glass, 8, -7f, 2.0f, 1.9f);
                if (v.Kind is VehicleKind.Taxi or VehicleKind.ClassicTaxi or VehicleKind.YellowCab)
                {
                    using var sign = new SolidBrush(Color.FromArgb(255, 255, 214, 90));
                    g.FillRectangle(sign, 6.4f, -8.8f, 2.4f, 1.2f);
                }
                Wheel(3, 1.6f); Wheel(len - 3, 1.6f);
                Headlight(len, -3.6f, 18); Tail(0, -3.6f);
                break;
            }
            case VehicleKind.Scooter:
            {
                // Vespa-style scooter with a rounded body and a windscreen.
                Wheel(1.9f, 1.5f); Wheel(8.2f, 1.5f);
                g.FillPolygon(body, new[] { P(0.6f, -2.2f), P(1.2f, -5.2f), P(5.4f, -5.4f), P(6.6f, -3.8f), P(8.8f, -3.6f), P(9f, -2.2f), P(6f, -1.4f) });
                g.DrawLine(pen, 8.2f, -1.6f, 7.8f, -6.4f);
                g.DrawLine(pen, 7.2f, -6.4f, 8.8f, -6.4f);
                g.FillRectangle(glass, 7.9f, -8.4f, 0.8f, 2.0f);
                Rider(3.6f, -5.2f, Paints[v.Seed % Paints.Length]);
                if (v.Seed % 3 == 0) Rider(1.9f, -4.9f, Paints[(v.Seed / 3) % Paints.Length]);
                Headlight(9.2f, -3.8f, 14); Tail(0.4f, -3.2f);
                break;
            }
            case VehicleKind.Camel:
            case VehicleKind.Donkey:
            {
                bool camel = v.Kind == VehicleKind.Camel;
                float sc = camel ? 1f : 0.78f;
                var hide = SceneClock.Mix(v.Paint, mood.Silhouette, lit ? 0.55f : 0.22f);
                using var hb = new SolidBrush(hide);
                using var lp = new Pen(hide, 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float stride = MathF.Sin(time * (camel ? 2.4f : 3.2f) + v.Seed) * 0.5f;
                float legTop = -5.4f * sc;
                foreach (var (lx, ph) in new[] { (2.2f, 0f), (3.6f, 3.1f), (8.0f, 3.1f), (9.4f, 0f) })
                {
                    float sw = MathF.Sin(time * (camel ? 2.4f : 3.2f) + v.Seed + ph) * 1.3f;
                    g.DrawLine(lp, lx * sc + 1f, legTop, lx * sc + 1f + sw, 0f);
                }
                g.FillEllipse(hb, 1f * sc, -9.6f * sc, 10f * sc, 4.6f * sc);                         // body
                if (camel) g.FillEllipse(hb, 3.6f * sc, -12.4f * sc, 3.2f, 3.4f);                    // hump
                g.FillPolygon(hb, new[] { P(8.8f * sc, -8.6f * sc), P(11f * sc, -13f * sc), P(12.2f * sc, -12.6f * sc), P(10.2f * sc, -7.6f * sc) }); // neck
                g.FillEllipse(hb, 11f * sc, -14.2f * sc, 3.6f * sc, 1.8f * sc);                      // head
                if (!camel) { g.FillPolygon(hb, new[] { P(11.4f * sc, -14f * sc), P(11.8f * sc, -16.2f * sc), P(12.4f * sc, -14f * sc) }); }
                g.DrawLine(lp, 1.2f * sc, -8.6f * sc, -0.4f * sc, -5.6f * sc + stride * 0.4f);        // tail
                if (camel && v.Seed % 2 == 0) Rider(5.2f * sc, -11.6f * sc, Color.FromArgb(255, 226, 226, 214), conicalHat: false);
                break;
            }
            case VehicleKind.Ferry:
            {
                float bob = MathF.Sin(time * 1.1f + v.Seed) * 0.35f;
                g.TranslateTransform(0, bob);
                var hull = SceneClock.Mix(Color.FromArgb(255, 34, 70, 120), mood.Silhouette, lit ? 0.55f : 0.2f);
                using var hb = new SolidBrush(hull);
                g.FillPolygon(hb, new[] { P(0, -2.6f), P(14.4f, -2.6f), P(13f, 0.4f), P(1.2f, 0.4f) });
                g.FillRectangle(body, 1.6f, -5.4f, 10.6f, 2.8f);                                       // passenger deck
                g.FillRectangle(body, 3.4f, -7.8f, 6f, 2.4f);                                          // bridge
                for (float wx = 2.2f; wx < 11.4f; wx += 1.9f) g.FillRectangle(glass, wx, -4.8f, 1.2f, 1.1f);
                g.FillRectangle(glass, 4f, -7.2f, 4.8f, 1.1f);
                using var stack = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 200, 50, 40), mood.Silhouette, 0.3f));
                g.FillRectangle(stack, 6f, -10f, 1.6f, 2.4f);
                break;
            }
            case VehicleKind.KeiTruck:
            {
                g.FillRectangle(body, 7.4f, -7.6f, 4.6f, 6.2f);
                g.FillRectangle(glass, 9.6f, -7f, 2, 2.2f);
                using var bed = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 160, 160, 164), mood.Silhouette, 0.3f));
                g.FillRectangle(bed, 0, -4.4f, 7.4f, 3);
                using var cargo = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 120, 160, 80), mood.Silhouette, 0.4f));
                g.FillRectangle(cargo, 0.6f, -6.4f, 5.4f, 2);
                Wheel(2.4f, 1.4f); Wheel(9.6f, 1.4f);
                Headlight(12, -3.2f, 14); Tail(0, -3);
                break;
            }
            case VehicleKind.Bicycle:
            case VehicleKind.FlowerBike:
            case VehicleKind.Mamachari:
            {
                float r = 1.9f;
                using var tire = new Pen(dark, 0.7f);
                g.DrawEllipse(tire, 0.4f, -r * 2, r * 2, r * 2);
                g.DrawEllipse(tire, 6.8f, -r * 2, r * 2, r * 2);
                g.DrawLine(pen, 0.4f + r, -r, 4.4f, -4.8f);
                g.DrawLine(pen, 4.4f, -4.8f, 6.8f + r, -r);
                g.DrawLine(pen, 0.4f + r, -r, 6.8f + r, -r);
                g.DrawLine(pen, 7.8f, -r, 7.6f, -5.6f);
                Rider(4.2f, -4.8f, Paints[v.Seed % Paints.Length], conicalHat: v.Kind == VehicleKind.FlowerBike);
                if (v.Kind == VehicleKind.FlowerBike)
                {
                    // Hà Nội flower bike: overflowing baskets of blooms behind the rider.
                    Color[] fl = { Color.FromArgb(255, 236, 80, 120), Color.FromArgb(255, 255, 210, 70), Color.FromArgb(255, 250, 250, 250), Color.FromArgb(255, 200, 60, 200) };
                    for (int i = 0; i < 9; i++)
                    {
                        using var fb = new SolidBrush(SceneClock.Mix(fl[(i + v.Seed) % fl.Length], mood.Silhouette, lit ? 0.45f : 0.1f));
                        g.FillEllipse(fb, -2.6f + (i % 3) * 1.8f, -8.2f + (i / 3) * 1.6f, 1.9f, 1.7f);
                    }
                }
                if (v.Kind == VehicleKind.Mamachari)
                {
                    using var basket = new SolidBrush(Color.FromArgb(230, 70, 60, 50));
                    g.FillRectangle(basket, 7.6f, -6.4f, 2.6f, 2.0f);
                }
                Headlight(9.4f, -4.4f, 10);
                break;
            }
            case VehicleKind.FishingBoat:
            {
                var hull = SceneClock.Mix(Color.FromArgb(255, 40, 80, 150), mood.Silhouette, lit ? 0.6f : 0.2f);
                using var hb = new SolidBrush(hull);
                float bob = MathF.Sin(time * 1.4f + v.Seed) * 0.4f;
                g.TranslateTransform(0, bob);
                g.FillPolygon(hb, new[] { P(0, -2.4f), P(12, -3.2f), P(10.6f, 0.4f), P(1.2f, 0.4f) });
                using var red = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 200, 50, 40), mood.Silhouette, 0.3f));
                g.FillRectangle(red, 0.6f, -2.8f, 11, 0.6f);
                g.FillRectangle(body, 3.4f, -5.4f, 4, 2.6f);
                g.DrawLine(pen, 6, -5.4f, 6, -10);
                if (lit)
                {
                    using var lamp = new SolidBrush(Color.FromArgb(255, 255, 236, 170));
                    g.FillEllipse(lamp, 5.2f, -11, 1.6f, 1.6f);
                    using var halo = new SolidBrush(Color.FromArgb(50, 255, 236, 170));
                    g.FillEllipse(halo, 3.4f, -12.8f, 5.2f, 5.2f);
                }
                break;
            }
            case VehicleKind.Sailboat:
            {
                float bob = MathF.Sin(time * 1.2f + v.Seed) * 0.4f;
                g.TranslateTransform(0, bob);
                g.FillPolygon(dk, new[] { P(0, -1.6f), P(9, -1.6f), P(7.6f, 0.4f), P(1.2f, 0.4f) });
                g.DrawLine(pen, 4.4f, -1.6f, 4.4f, -11);
                using var sail = new SolidBrush(lit ? Color.FromArgb(200, 120, 120, 136) : Color.FromArgb(235, 246, 242, 230));
                g.FillPolygon(sail, new[] { P(4.8f, -10.6f), P(8.8f, -2.4f), P(4.8f, -2.4f) });
                using var sail2 = new SolidBrush(lit ? Color.FromArgb(200, 140, 70, 60) : Color.FromArgb(235, 220, 90, 70));
                g.FillPolygon(sail2, new[] { P(4, -9.4f), P(4, -2.4f), P(1, -2.4f) });
                break;
            }
            case VehicleKind.LanternBoat:
            {
                float bob = MathF.Sin(time * 1.1f + v.Seed) * 0.35f;
                g.TranslateTransform(0, bob);
                using var hb = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 90, 54, 36), mood.Silhouette, 0.4f));
                g.FillPolygon(hb, new[] { P(0, -2.2f), P(8, -2.2f), P(7, 0.3f), P(1, 0.3f) });
                Rider(3.6f, -2.2f, Color.FromArgb(255, 220, 220, 210), conicalHat: true);
                Color[] lc = { Color.FromArgb(255, 255, 90, 60), Color.FromArgb(255, 255, 200, 60), Color.FromArgb(255, 255, 120, 180) };
                for (int i = 0; i < 2; i++)
                {
                    var c = lc[(v.Seed + i) % lc.Length];
                    using var lb = new SolidBrush(Color.FromArgb(240, c));
                    g.FillEllipse(lb, 1 + i * 4.6f, -4.6f, 1.6f, 1.9f);
                    if (lit)
                    {
                        using var halo = new SolidBrush(Color.FromArgb(60, c));
                        g.FillEllipse(halo, -0.4f + i * 4.6f, -6, 4.4f, 4.4f);
                    }
                }
                // floating hoa đăng (paper lanterns) trailing behind
                using var fl = new SolidBrush(Color.FromArgb(lit ? 240 : 170, 255, 200, 90));
                for (int i = 1; i <= 3; i++)
                    g.FillRectangle(fl, -4.5f * i, -1.2f + MathF.Sin(time + i) * 0.3f, 1.2f, 1.0f);
                break;
            }
        }
    }

    #endregion
}
