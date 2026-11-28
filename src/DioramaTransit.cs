using System.Drawing;
using System.Drawing.Drawing2D;

namespace AudioTaskbar;

// v5.4: the station platform with its passengers, and the airplane (v5.4.2: airborne only, no take-off strip).
// All motion comes from the pure state machines in Transit.cs; this file only paints a given state.
internal sealed partial class DioramaRenderer
{
    private static readonly Color[] PaxColors =
    {
        Color.FromArgb(255, 214, 90, 80), Color.FromArgb(255, 70, 130, 200), Color.FromArgb(255, 232, 180, 60),
        Color.FromArgb(255, 96, 170, 120), Color.FromArgb(255, 170, 100, 190), Color.FromArgb(255, 236, 236, 232)
    };

    private double _planePhase0;
    private bool _planeSeeded;

    // ───────────────────────────── people ─────────────────────────────

    /// <summary>A tiny pixel person standing with the feet at (<paramref name="x"/>, <paramref name="feetY"/>).</summary>
    private void DrawPerson(Graphics g, SceneMood mood, float x, float feetY, int colorIdx, float alpha, float walk, float scale = 1f)
    {
        if (alpha <= 0.02f) return;
        int a = (int)(255 * Math.Clamp(alpha, 0f, 1f));
        var shirt = SceneClock.Mix(Color.FromArgb(255, 30, 34, 48), PaxColors[colorIdx % PaxColors.Length], 0.45f + 0.55f * mood.DayK);
        float step = MathF.Sin(walk * 6.28f) * 0.55f * scale;
        float bob = MathF.Abs(MathF.Cos(walk * 6.28f)) * 0.25f * scale;
        float fy = feetY - bob;
        using var leg = new Pen(Color.FromArgb(a, 36, 38, 52), 0.8f * scale);
        g.DrawLine(leg, x, fy - 1.9f * scale, x - step, feetY);
        g.DrawLine(leg, x, fy - 1.9f * scale, x + step, feetY);
        using var body = new SolidBrush(Color.FromArgb(a, shirt));
        g.FillRectangle(body, x - 0.95f * scale, fy - 4.8f * scale, 1.9f * scale, 3.1f * scale);
        using var head = new SolidBrush(Color.FromArgb(a, SceneClock.Mix(Color.FromArgb(255, 120, 100, 92), Color.FromArgb(255, 238, 200, 164), mood.DayK)));
        g.FillEllipse(head, x - 0.95f * scale, fy - 6.5f * scale, 1.9f * scale, 1.9f * scale);
    }

    private static float Fade(float w) => 1f - TransitMath.Clamp01((w - 0.78f) / 0.22f);

    // ───────────────────────────── train platform ─────────────────────────────

    /// <summary>Platform slab, lamp posts, name board and the passengers getting off / on.</summary>
    private void DrawStation(Graphics g, SceneMood mood, float time, TrainState ts)
    {
        float top = _railY - 0.8f, hgt = 3.7f, l = _platL, r = _platR;
        var concrete = SceneClock.Mix(Color.FromArgb(255, 54, 58, 74), Color.FromArgb(255, 150, 150, 148), mood.DayK);
        using (var slab = new SolidBrush(concrete)) g.FillRectangle(slab, l, top, r - l, hgt);
        using (var edge = new SolidBrush(Color.FromArgb(210, 232, 196, 70))) g.FillRectangle(edge, l, top, r - l, 0.9f);
        using (var shade = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(shade, l, top + hgt - 1.1f, r - l, 1.1f);
        if (mood.SnowCover > 0.01f)
            using (var sn = new SolidBrush(RegionScenery.SnowTint(mood, (int)(200 * mood.SnowCover))))
                g.FillRectangle(sn, l, top - 0.7f, r - l, 0.9f);

        // lamp posts at both ends, a name board on the left one
        float lk = mood.LightsK;
        foreach (float px in new[] { l + 5f, r - 5f })
        {
            using var pole = new Pen(Color.FromArgb(230, 30, 32, 40), 0.9f);
            g.DrawLine(pole, px, top, px, top - 12.5f);
            if (lk > 0.05f)
                using (var glow = new SolidBrush(Color.FromArgb((int)(70 * lk), 255, 226, 150))) g.FillEllipse(glow, px - 4f, top - 17f, 8f, 8f);
            using var head = new SolidBrush(Color.FromArgb(255, SceneClock.Mix(Color.FromArgb(255, 60, 62, 70), Color.FromArgb(255, 255, 230, 160), lk)));
            g.FillRectangle(head, px - 1.6f, top - 13.6f, 3.2f, 1.3f);
        }
        using (var board = new SolidBrush(Color.FromArgb(255, SceneClock.Mix(Color.FromArgb(255, 22, 50, 90), Color.FromArgb(255, 36, 104, 170), mood.DayK))))
            g.FillRectangle(board, l + 8.5f, top - 10.5f, 10f, 3.6f);
        using (var line = new Pen(Color.FromArgb(220, 240, 244, 250), 0.7f))
            g.DrawLine(line, l + 10f, top - 8.7f, l + 17f, top - 8.7f);

        // passengers
        const int n = 6;
        for (int k = 0; k < n; k++)
        {
            int door = k / 2;
            int dir = k % 2 == 0 ? -1 : 1;
            float doorX = ts.X + (door * 40 + 19) * TrainScale;
            if (doorX < l + 6 || doorX > r - 6) { /* the door is outside the platform: nothing to walk on */ }
            float room = dir < 0 ? doorX - l - 6f : r - doorX - 6f;
            float dist = Math.Clamp(room, 5f, 24f + 7f * (k / 2));
            float feet = top - 0.2f;
            if (ts.Alighting > 0f && ts.Alighting < 1f)
            {
                float w = TrainCycle.Walk(ts.Alighting, k, n);
                if (w > 0f && w < 1f)
                    DrawPerson(g, mood, doorX + dir * dist * w, feet, k, TransitMath.Clamp01(w * 6f) * Fade(w), time * 2.2f + k);
            }
            if (ts.Boarding > 0f && ts.Boarding < 1f)
            {
                float w = TrainCycle.Walk(ts.Boarding, k, n);
                if (w > 0f && w < 1f)
                    DrawPerson(g, mood, doorX + dir * dist * (1f - w), feet, k + 3, TransitMath.Clamp01(w * 5f) * (1f - TransitMath.Clamp01((w - 0.9f) / 0.1f)), time * 2.2f + k);
            }
        }
    }

    // ───────────────────────────── airplane ─────────────────────────────

    /// <summary>v5.4.2: the plane is only ever in the air (no runway): climb in from the left, cruise, descend out at the right.</summary>
    private void DrawFlight(Graphics g, SceneMood mood, float time, AppConfig cfg)
    {
        double period = Math.Max(1.0, cfg.AirplaneCycleMinutes * 60.0);
        if (!_planeSeeded)
        {
            // Start a little into the climb so a plane is in view right after launch.
            double seed = PlaneCycle.StartOf(PlanePhase.Climb) + 0.15 * 0.28;
            _planePhase0 = seed - time / period;
            _planeSeeded = true;
        }
        double p = DebugPlaneCycle ?? SceneMath.TrainProgress(_planePhase0, time, period);
        var ps = PlaneCycle.Evaluate(p, _w);
        if (!ps.Visible) { PlaneX01 = -1f; PlaneThrust = 0f; return; }
        PlaneX01 = Math.Clamp(ps.X / Math.Max(1f, _w), 0f, 1f);
        PlaneThrust = ps.Thrust;

        float cy = PlaneCentreY(ps);
        DrawPlane(g, mood, time, ps, cy);
    }

    /// <summary>Fuselage centre line for a state: <see cref="PlaneState.Alt"/> px above the bottom edge of the bar.</summary>
    internal float PlaneCentreY(PlaneState ps) => _h - 2.7f - ps.Alt - 5.2f;

    /// <summary>Side-on airliner, nose to the right, about 34 px long. (<paramref name="cy"/> = fuselage centre line.)</summary>
    private void DrawPlane(Graphics g, SceneMood mood, float time, PlaneState ps, float cy)
    {
        float day = mood.DayK, lk = mood.LightsK;
        var fus = SceneClock.Mix(Color.FromArgb(255, 112, 122, 148), Color.FromArgb(255, 240, 242, 248), day * (mood.IsRainy ? 0.88f : 1f));
        var belly = SceneClock.Mix(fus, Color.FromArgb(255, 56, 66, 94), 0.30f);
        var livery = SceneClock.Mix(Color.FromArgb(255, 20, 54, 100), Color.FromArgb(255, 36, 104, 176), day);
        var wingCol = SceneClock.Mix(fus, Color.FromArgb(255, 60, 70, 96), 0.22f);

        var st = g.Save();
        g.TranslateTransform(ps.X, cy);
        if (MathF.Abs(ps.PitchDeg) > 0.05f) g.RotateTransform(-ps.PitchDeg);

        // landing light (dusk / night, while low)
        if (lk > 0.05f && ps.Alt < 12f && ps.Speed > 0.25f)
        {
            using var cone = new GraphicsPath();
            cone.AddPolygon(new[] { new PointF(16f, 0.4f), new PointF(46f, -3f), new PointF(46f, 6f) });
            using var pgb = new PathGradientBrush(cone) { CenterColor = Color.FromArgb((int)(60 * lk), 255, 244, 210), SurroundColors = new[] { Color.FromArgb(0, 255, 244, 210) }, CenterPoint = new PointF(17f, 1.2f) };
            g.FillPath(pgb, cone);
        }

        // far wing + horizontal stabiliser (behind the fuselage)
        using (var wb = new SolidBrush(SceneClock.Mix(wingCol, Color.Black, 0.18f)))
        {
            g.FillPolygon(wb, new[] { new PointF(1f, -0.8f), new PointF(5f, -0.8f), new PointF(-6f, -4.6f), new PointF(-9.5f, -4.6f) });
            g.FillPolygon(wb, new[] { new PointF(-14f, -1.2f), new PointF(-11f, -1.2f), new PointF(-14.5f, -4f), new PointF(-17.5f, -4f) });
        }
        // fuselage: rounded body + tapered tail cone
        using (var fb = new SolidBrush(fus))
        {
            g.FillEllipse(fb, -14f, -3.7f, 31f, 7.4f);
            g.FillPolygon(fb, new[] { new PointF(-10f, -3.2f), new PointF(-19.2f, -2.3f), new PointF(-19.6f, -1.2f), new PointF(-11f, 3f) });
        }
        using (var bb = new SolidBrush(belly))
            g.FillPolygon(bb, new[] { new PointF(-17f, 1.5f), new PointF(10f, 1.7f), new PointF(14f, 0.9f), new PointF(14f, 2f), new PointF(8f, 3.3f), new PointF(-9f, 3.1f) });
        // livery stripe and tail fin
        using (var ls = new Pen(Color.FromArgb(235, livery), 0.9f)) g.DrawLine(ls, -15f, 0.9f, 13f, 0.9f);
        using (var fin = new SolidBrush(livery))
            g.FillPolygon(fin, new[] { new PointF(-9.5f, -3.1f), new PointF(-12.6f, -10.8f), new PointF(-16.6f, -10.8f), new PointF(-18.4f, -2.4f) });
        // windows: cockpit + cabin row
        using (var cock = new SolidBrush(Color.FromArgb(255, 36, 48, 70)))
            g.FillPolygon(cock, new[] { new PointF(12.2f, -2.5f), new PointF(15.4f, -1.5f), new PointF(16.4f, -0.2f), new PointF(12.2f, -0.2f) });
        var cabinWin = SceneClock.Mix(Color.FromArgb(255, 70, 88, 122), Color.FromArgb(255, 255, 214, 130), lk);
        using (var cw = new SolidBrush(cabinWin))
            for (float x = -12f; x <= 9.5f; x += 2.5f) g.FillRectangle(cw, x, -1.5f, 1.1f, 1.2f);
        // door outline at the front
        using (var door = new Pen(Color.FromArgb(120, 40, 50, 74), 0.5f)) g.DrawRectangle(door, 8.4f, -2.6f, 1.8f, 4.4f);
        // near wing + engine
        using (var wb = new SolidBrush(wingCol))
            g.FillPolygon(wb, new[] { new PointF(4f, 1.2f), new PointF(-2.5f, 1.2f), new PointF(-12.5f, 6.4f), new PointF(-8f, 6.4f) });
        using (var eng = new SolidBrush(SceneClock.Mix(Color.FromArgb(255, 70, 78, 100), Color.FromArgb(255, 150, 158, 176), day)))
            g.FillEllipse(eng, -1f, 3.1f, 7f, 2.9f);
        // lights: red beacon, strobe, nav light
        float blink = MathF.Sin(time * 5.5f) > 0.82f ? 1f : 0f;
        float beacon = 0.5f + 0.5f * MathF.Sin(time * 3.2f);
        if (lk > 0.05f || ps.Alt > 1f)
        {
            float vis = 0.35f + 0.65f * lk;
            using var red = new SolidBrush(Color.FromArgb((int)(240 * beacon * vis), 255, 60, 52));
            g.FillEllipse(red, -0.9f, -4.7f, 1.8f, 1.8f);
            using var strobe = new SolidBrush(Color.FromArgb((int)(255 * blink * vis), 255, 255, 255));
            g.FillEllipse(strobe, -19.6f, -3.2f, 1.7f, 1.7f);
            using var nav = new SolidBrush(Color.FromArgb((int)(220 * vis), 70, 230, 120));
            g.FillEllipse(nav, -12.4f, 5.6f, 1.5f, 1.5f);
        }
        g.Restore(st);
    }
}
