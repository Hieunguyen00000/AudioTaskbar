namespace AudioTaskbar;

// v5.4 procedural ambience. Everything here is synthesized from noise and oscillators at run time: no audio
// files, no third-party samples, nothing to license, and nothing added to the exe size but this code.
// Every source is a *stream* (state carries across calls), so there is no loop point and no seam.

/// <summary>Small deterministic helpers shared by the generators.</summary>
internal static class Dsp
{
    public const int Rate = 44100;

    public static float OnePoleCoef(float cutoffHz) => 1f - MathF.Exp(-2f * MathF.PI * Math.Clamp(cutoffHz, 5f, Rate * 0.45f) / Rate);

    public static (float L, float R) Pan(float pan)
    {
        float a = (Math.Clamp(pan, -1f, 1f) + 1f) * 0.25f * MathF.PI; // equal power
        return (MathF.Cos(a), MathF.Sin(a));
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>xorshift32: fast, seedable, allocation-free.</summary>
internal sealed class FastRng
{
    private uint _s;
    public FastRng(int seed) { _s = (uint)(seed * 2654435761u + 0x9E3779B9u); if (_s == 0) _s = 1; }
    public uint NextU() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
    /// <summary>Uniform −1..1.</summary>
    public float Noise() => (NextU() >> 8) * (2f / 16777216f) - 1f;
    /// <summary>Uniform 0..1.</summary>
    public float Unit() => (NextU() >> 8) * (1f / 16777216f);
    public float Range(float a, float b) => a + (b - a) * Unit();
    public bool Chance(float p) => Unit() < p;
}

/// <summary>Chamberlin state-variable filter; <see cref="Band"/> is a resonant band-pass.</summary>
internal struct Svf
{
    private float _low, _band;
    public float Band(float x, float cutoffHz, float q)
    {
        float f = 2f * MathF.Sin(MathF.PI * Math.Clamp(cutoffHz, 20f, Dsp.Rate * 0.16f) / Dsp.Rate);
        float damp = 1f / Math.Max(0.5f, q);
        _low += f * _band;
        float high = x - _low - damp * _band;
        _band += f * high;
        return _band;
    }
}

internal interface ISoundSource
{
    /// <summary>Add <paramref name="frames"/> samples to <paramref name="left"/>/<paramref name="right"/> (the caller applies the gain).</summary>
    void Render(float[] left, float[] right, int frames, float level, float aux);
}

/// <summary>A short enveloped tone/noise event (bird note, horn, frog croak, rooster …) with a moving pitch.</summary>
internal sealed class ToneVoice
{
    public float Delay;                         // seconds of silence before it starts
    public (float t, float v)[] Freq = Array.Empty<(float, float)>();
    public (float t, float v)[] Amp = Array.Empty<(float, float)>();
    public float[] Harmonics = { 1f };
    public float AmRate, AmDepth, NoiseMix, VibRate, VibDepth;
    public float LpCoef = 1f;
    public float GainL = 0.7f, GainR = 0.7f;
    public float Duration;                      // set from the last amplitude key

    private double _t;
    private double _phase;
    private float _lp;
    public bool Done => _t >= Delay + Duration;

    private static float Interp((float t, float v)[] k, float t)
    {
        if (k.Length == 0) return 0f;
        if (t <= k[0].t) return k[0].v;
        for (int i = 1; i < k.Length; i++)
            if (t <= k[i].t)
            {
                float u = (t - k[i - 1].t) / Math.Max(1e-5f, k[i].t - k[i - 1].t);
                return k[i - 1].v + (k[i].v - k[i - 1].v) * u;
            }
        return k[^1].v;
    }

    public void Finish() => Duration = Amp.Length > 0 ? Amp[^1].t : 0f;

    public void Render(float[] l, float[] r, int frames, FastRng noise)
    {
        double dt = 1.0 / Dsp.Rate;
        float norm = 1f / Math.Max(1f, Harmonics.Sum());
        for (int i = 0; i < frames && !Done; i++)
        {
            double local = _t - Delay;
            _t += dt;
            if (local < 0) continue;
            float lt = (float)local;
            float f = Interp(Freq, lt);
            if (VibDepth > 0) f *= 1f + VibDepth * MathF.Sin(2f * MathF.PI * VibRate * lt);
            _phase += 2.0 * Math.PI * f * dt;
            if (_phase > 1e6) _phase %= 2.0 * Math.PI;
            float s = 0f;
            for (int h = 0; h < Harmonics.Length; h++) s += Harmonics[h] * MathF.Sin((float)(_phase * (h + 1)));
            s *= norm;
            if (AmDepth > 0) s *= 1f - AmDepth * (0.5f + 0.5f * MathF.Sin(2f * MathF.PI * AmRate * lt));
            if (NoiseMix > 0) s = s * (1f - NoiseMix) + NoiseMix * noise.Noise();
            _lp += LpCoef * (s - _lp);
            float o = _lp * Interp(Amp, lt);
            l[i] += o * GainL;
            r[i] += o * GainR;
        }
    }
}

/// <summary>Base for sources that fire randomly timed events made of <see cref="ToneVoice"/>s.</summary>
internal abstract class EventSource : ISoundSource
{
    protected readonly FastRng Rng;
    private readonly List<ToneVoice> _voices = new();
    private double _nextIn;
    private const int MaxVoices = 24;

    protected EventSource(int seed) { Rng = new FastRng(seed); _nextIn = 0.6 + Rng.Unit() * 2; }

    protected void Add(ToneVoice v) { v.Finish(); if (_voices.Count < MaxVoices) _voices.Add(v); }

    /// <summary>Seconds until the next event, given the scene level (0..1); null = nothing now.</summary>
    protected abstract double NextInterval(float level);
    protected abstract void Spawn(float level, float aux);

    public void Render(float[] left, float[] right, int frames, float level, float aux)
    {
        if (level > 0.01f)
        {
            _nextIn -= frames / (double)Dsp.Rate;
            if (_nextIn <= 0)
            {
                Spawn(level, aux);
                _nextIn = NextInterval(level);
            }
        }
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            _voices[i].Render(left, right, frames, Rng);
            if (_voices[i].Done) _voices.RemoveAt(i);
        }
    }

    protected static (float, float)[] Env(float dur, float attack, float release, float peak = 1f)
        => new[] { (0f, 0f), (attack, peak), (Math.Max(attack + 0.001f, dur - release), peak), (dur, 0f) };
}

internal sealed class RainSource : ISoundSource
{
    private readonly FastRng _rl = new(11), _rr = new(12), _rd = new(13);
    private float _lpHiL, _lpHiR, _lpLoL, _lpLoR;
    private readonly float _aHi = Dsp.OnePoleCoef(6200f), _aLo = Dsp.OnePoleCoef(900f);
    private struct Drop { public float T, Tau, Amp, GL, GR, Phase, Freq; }
    private readonly List<Drop> _drops = new();

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        float hiss = 0.05f + 0.20f * level;
        float dropsPerSec = 25f + 260f * level;
        float p = dropsPerSec / Dsp.Rate;
        for (int i = 0; i < frames; i++)
        {
            float nl = _rl.Noise(), nr = _rr.Noise();
            _lpHiL += _aHi * (nl - _lpHiL); _lpLoL += _aLo * (nl - _lpLoL);
            _lpHiR += _aHi * (nr - _lpHiR); _lpLoR += _aLo * (nr - _lpLoR);
            float sl = (_lpHiL - _lpLoL) * hiss * 2.2f, sr = (_lpHiR - _lpLoR) * hiss * 2.2f;
            if (_rd.Unit() < p && _drops.Count < 80)
            {
                var (gl, gr) = Dsp.Pan(_rd.Range(-1f, 1f));
                _drops.Add(new Drop { Tau = _rd.Range(0.004f, 0.016f), Amp = _rd.Range(0.05f, 0.30f) * (0.4f + 0.6f * level), GL = gl, GR = gr, Freq = _rd.Range(1800f, 5200f) });
            }
            for (int k = _drops.Count - 1; k >= 0; k--)
            {
                var d = _drops[k];
                float env = MathF.Exp(-d.T / d.Tau);
                float tick = MathF.Sin(d.Phase) * env * d.Amp;
                d.Phase += 2f * MathF.PI * d.Freq / Dsp.Rate;
                d.T += 1f / Dsp.Rate;
                sl += tick * d.GL; sr += tick * d.GR;
                if (env < 0.01f) _drops.RemoveAt(k); else _drops[k] = d;
            }
            l[i] += sl; r[i] += sr;
        }
    }
}

internal sealed class WindSource : ISoundSource
{
    private readonly FastRng _rng = new(21);
    private Svf _bandL, _bandR, _whistle;
    private float _gust = 0.3f, _gustTarget = 0.4f, _timer;

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        for (int i = 0; i < frames; i++)
        {
            if (_timer <= 0) { _gustTarget = _rng.Range(0.25f, 1f); _timer = _rng.Range(1.5f, 5f) * Dsp.Rate; }
            _timer--;
            _gust += (_gustTarget - _gust) * 0.00004f;
            float fc = 220f + 700f * _gust;
            float amp = level * (0.3f + 0.7f * _gust);
            float nl = _rng.Noise(), nr = _rng.Noise();
            float bl = _bandL.Band(nl, fc, 0.9f) * amp * 0.9f;
            float br = _bandR.Band(nr, fc * 1.07f, 0.9f) * amp * 0.9f;
            float wh = _whistle.Band((nl + nr) * 0.5f, 800f + 500f * _gust, 14f) * amp * _gust * 0.5f;
            l[i] += bl + wh; r[i] += br + wh;
        }
    }
}

internal sealed class WavesSource : ISoundSource
{
    private readonly FastRng _rl = new(31), _rr = new(32);
    private float _lpL, _lpR, _hpL, _hpR;
    private double _phase;

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        for (int i = 0; i < frames; i++)
        {
            _phase += 1.0 / (8.2 * Dsp.Rate);
            if (_phase > 1000) _phase -= 1000;
            float sw = (float)Math.Sin(2 * Math.PI * _phase);
            float swR = (float)Math.Sin(2 * Math.PI * _phase + 0.9);
            float eL = MathF.Pow(0.5f + 0.5f * sw, 2.2f), eR = MathF.Pow(0.5f + 0.5f * swR, 2.2f);
            float nl = _rl.Noise(), nr = _rr.Noise();
            _lpL += Dsp.OnePoleCoef(350f + 2300f * eL) * (nl - _lpL);
            _lpR += Dsp.OnePoleCoef(350f + 2300f * eR) * (nr - _lpR);
            _hpL += 0.25f * (nl - _hpL); _hpR += 0.25f * (nr - _hpR);
            float foamL = (nl - _hpL) * MathF.Pow(eL, 3f) * 0.12f, foamR = (nr - _hpR) * MathF.Pow(eR, 3f) * 0.12f;
            l[i] += (_lpL * (0.3f + 0.7f * eL) * 1.4f + foamL) * level * 0.7f;
            r[i] += (_lpR * (0.3f + 0.7f * eR) * 1.4f + foamR) * level * 0.7f;
        }
    }
}

internal sealed class ThunderSource : ISoundSource
{
    private readonly FastRng _rng = new(41);
    private double _next = 6;
    private struct Roll { public float T, Delay, Len, Pan, Dist, Phase; public float Lp1, Lp2; }
    private readonly List<Roll> _rolls = new();
    private float _crackHp;

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        if (level > 0.05f)
        {
            _next -= frames / (double)Dsp.Rate;
            if (_next <= 0)
            {
                float dist = _rng.Range(0.25f, 1f);
                _rolls.Add(new Roll { Delay = dist * 1.6f, Len = _rng.Range(3f, 6.5f), Pan = _rng.Range(-0.8f, 0.8f), Dist = dist, Phase = _rng.Range(0f, 6f) });
                _next = Dsp.Lerp(26f, 7f, level) * _rng.Range(0.6f, 1.4f);
            }
        }
        for (int k = _rolls.Count - 1; k >= 0; k--)
        {
            var ro = _rolls[k];
            var (gl, gr) = Dsp.Pan(ro.Pan);
            float aLp = Dsp.OnePoleCoef(Dsp.Lerp(240f, 80f, ro.Dist));
            for (int i = 0; i < frames; i++)
            {
                float t = ro.T - ro.Delay;
                ro.T += 1f / Dsp.Rate;
                if (t < 0) continue;
                if (t > ro.Len) break;
                float n = _rng.Noise();
                ro.Lp1 += aLp * (n - ro.Lp1);
                ro.Lp2 += aLp * (ro.Lp1 - ro.Lp2);
                float env = MathF.Min(1f, t / 0.06f) * MathF.Exp(-t / (ro.Len * 0.33f))
                            * (0.65f + 0.35f * MathF.Sin(2f * MathF.PI * 0.8f * t + ro.Phase));
                float s = ro.Lp2 * 7.5f * env;
                // the sharp crack of a close strike
                if (t < 0.18f && ro.Dist < 0.7f)
                {
                    _crackHp += 0.3f * (n - _crackHp);
                    s += (n - _crackHp) * MathF.Exp(-t / 0.035f) * (1f - ro.Dist) * 0.7f;
                }
                s *= 0.55f;
                l[i] += s * gl * 1.3f; r[i] += s * gr * 1.3f;
            }
            if (ro.T - ro.Delay > ro.Len) _rolls.RemoveAt(k); else _rolls[k] = ro;
        }
    }
}

internal sealed class TrafficSource : ISoundSource
{
    private readonly FastRng _rng = new(51);
    private float _lpL, _lpR, _hiL, _hiR;
    private readonly float _aLo = Dsp.OnePoleCoef(170f);
    private double _next = 1;
    private struct Pass { public float T, Len, Dir, Amp, Phase, Lp; public bool Truck; }
    private readonly List<Pass> _passes = new();

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        _next -= frames / (double)Dsp.Rate;
        if (_next <= 0 && level > 0.03f)
        {
            _passes.Add(new Pass { Len = _rng.Range(1.6f, 4.2f), Dir = _rng.Chance(0.5f) ? 1 : -1, Amp = _rng.Range(0.5f, 1f), Truck = _rng.Chance(0.2f) });
            _next = _rng.Range(0.5f, 3.5f) / (0.25f + level * 1.5f);
        }
        float wet = aux; // wet roads add a hiss
        for (int i = 0; i < frames; i++)
        {
            float nl = _rng.Noise(), nr = _rng.Noise();
            _lpL += _aLo * (nl - _lpL); _lpR += _aLo * (nr - _lpR);
            _hiL += 0.3f * (nl - _hiL); _hiR += 0.3f * (nr - _hiR);
            float sl = _lpL * 0.9f * level + (nl - _hiL) * 0.05f * wet * level;
            float sr = _lpR * 0.9f * level + (nr - _hiR) * 0.05f * wet * level;
            for (int k = 0; k < _passes.Count; k++)
            {
                var p = _passes[k];
                float u = p.T / p.Len;
                if (u >= 1f) continue;
                float env = MathF.Sin(MathF.PI * u); env *= env;
                float n = _rng.Noise();
                p.Lp += Dsp.OnePoleCoef(250f + 1500f * env) * (n - p.Lp);
                float f = (p.Truck ? 62f : 95f) * (1.07f - 0.14f * u);        // doppler-like drop as it passes
                p.Phase += 2f * MathF.PI * f / Dsp.Rate;
                float s = (p.Lp * 1.6f + MathF.Sin(p.Phase) * 0.14f) * env * p.Amp * level * (p.Truck ? 1.2f : 0.9f);
                var (gl, gr) = Dsp.Pan(p.Dir * (2f * u - 1f) * 0.9f);
                sl += s * gl; sr += s * gr;
                p.T += 1f / Dsp.Rate;
                _passes[k] = p;
            }
            l[i] += sl * 0.55f; r[i] += sr * 0.55f;
        }
        _passes.RemoveAll(p => p.T >= p.Len);
    }
}

internal sealed class HornSource : EventSource
{
    public HornSource() : base(61) { }
    protected override double NextInterval(float level) => Rng.Range(2.5f, 11f) / (0.2f + level * 1.6f);

    protected override void Spawn(float level, float aux)
    {
        float dist = Rng.Range(0.2f, 1f);
        float f1 = Rng.Range(380f, 520f), f2 = f1 * 1.26f;
        int pattern = Rng.Chance(0.55f) ? 1 : Rng.Chance(0.6f) ? 2 : 3;
        var (gl, gr) = Dsp.Pan(Rng.Range(-0.9f, 0.9f));
        float amp = (1f - dist * 0.75f) * 0.5f;
        float lp = Dsp.OnePoleCoef(Dsp.Lerp(4200f, 1100f, dist));
        float t = 0;
        for (int i = 0; i < pattern; i++)
        {
            float dur = pattern == 1 ? Rng.Range(0.22f, 0.5f) : Rng.Range(0.1f, 0.22f);
            foreach (var f in new[] { f1, f2 })
                Add(new ToneVoice
                {
                    Delay = t, Freq = new[] { (0f, f), (dur, f * 0.995f) }, Amp = Env(dur, 0.012f, 0.03f, amp * 0.5f),
                    Harmonics = new[] { 1f, 0.55f, 0.32f, 0.15f }, LpCoef = lp, GainL = gl, GainR = gr
                });
            t += dur + Rng.Range(0.06f, 0.14f);
        }
    }
}

internal sealed class BirdSource : EventSource
{
    public BirdSource() : base(71) { }
    protected override double NextInterval(float level) => Rng.Range(0.3f, 2.2f) / (0.12f + level * 2.2f);

    protected override void Spawn(float level, float aux)
    {
        float dist = Rng.Range(0.1f, 1f);
        float amp = (1f - dist * 0.8f) * 0.28f;
        var (gl, gr) = Dsp.Pan(Rng.Range(-1f, 1f));
        float lp = Dsp.OnePoleCoef(Dsp.Lerp(9000f, 3500f, dist));
        int species = Rng.NextU() % 4 == 0 ? 2 : (int)(Rng.NextU() % 3);
        float f0 = Rng.Range(2600f, 4600f);
        float t = 0;
        int notes = species switch { 0 => 2 + (int)(Rng.NextU() % 3), 1 => 5 + (int)(Rng.NextU() % 5), _ => 1 + (int)(Rng.NextU() % 2) };
        for (int i = 0; i < notes; i++)
        {
            float dur = species switch { 0 => Rng.Range(0.06f, 0.11f), 1 => 0.045f, _ => Rng.Range(0.16f, 0.3f) };
            float a = f0 * (species == 1 ? (i % 2 == 0 ? 1f : 1.12f) : Rng.Range(0.85f, 1.2f));
            float b = species switch { 0 => a * Rng.Range(1.2f, 1.5f), 1 => a * 0.97f, _ => a * 0.75f };
            if (species == 2) { a *= 0.62f; b *= 0.62f; }
            Add(new ToneVoice
            {
                Delay = t, Freq = new[] { (0f, a), (dur, b) }, Amp = Env(dur, dur * 0.18f, dur * 0.4f, amp),
                Harmonics = species == 2 ? new[] { 1f, 0.12f } : new[] { 1f, 0.25f }, VibRate = 28f, VibDepth = species == 2 ? 0.02f : 0f,
                LpCoef = lp, GainL = gl, GainR = gr
            });
            t += dur + (species == 1 ? 0.03f : Rng.Range(0.05f, 0.11f));
        }
    }
}

internal sealed class AnimalSource : EventSource
{
    public AnimalSource() : base(81) { }
    protected override double NextInterval(float level) => Rng.Range(4f, 15f) / (0.3f + level * 1.5f);

    protected override void Spawn(float level, float aux)
    {
        var (gl, gr) = Dsp.Pan(Rng.Range(-1f, 1f));
        float dist = Rng.Range(0.3f, 1f);
        float amp = (1f - dist * 0.7f) * 0.45f;
        float lp = Dsp.OnePoleCoef(Dsp.Lerp(2600f, 1100f, dist));
        float roll = Rng.Unit();
        if (aux > 0.55f && roll < 0.5f) Rooster(amp, lp, gl, gr);
        else if (roll < 0.62f && aux <= 0.55f) Frogs(amp, lp, gl, gr);
        else if (roll < 0.8f) Dog(amp, lp, gl, gr);
        else if (aux <= 0.55f) Frogs(amp, lp, gl, gr);
        else Rooster(amp, lp, gl, gr);
    }

    private void Rooster(float amp, float lp, float gl, float gr)
    {
        float f = Rng.Range(430f, 520f);
        Add(new ToneVoice
        {
            Freq = new[] { (0f, f), (0.22f, f * 1.7f), (0.4f, f * 1.65f), (0.55f, f * 1.45f), (0.85f, f * 1.9f), (1.05f, f * 1.8f), (1.55f, f * 0.95f) },
            Amp = new[] { (0f, 0f), (0.04f, amp), (0.5f, amp * 0.9f), (0.55f, amp * 0.3f), (0.62f, amp), (1.2f, amp * 0.9f), (1.6f, 0f) },
            Harmonics = new[] { 1f, 0.8f, 0.6f, 0.4f, 0.25f }, NoiseMix = 0.04f, VibRate = 11f, VibDepth = 0.02f,
            LpCoef = lp, GainL = gl, GainR = gr
        });
    }

    private void Frogs(float amp, float lp, float gl, float gr)
    {
        for (int frog = 0; frog < 2; frog++)
        {
            float f = Rng.Range(330f, 560f);
            float t = frog * Rng.Range(0.1f, 0.35f);
            int croaks = 2 + (int)(Rng.NextU() % 3);
            for (int c = 0; c < croaks; c++)
            {
                float dur = Rng.Range(0.14f, 0.28f);
                Add(new ToneVoice
                {
                    Delay = t, Freq = new[] { (0f, f * 0.9f), (dur, f * 1.1f) }, Amp = Env(dur, 0.02f, 0.05f, amp * 0.8f),
                    Harmonics = new[] { 1f, 0.7f, 0.5f, 0.3f }, AmRate = Rng.Range(38f, 56f), AmDepth = 0.85f, NoiseMix = 0.08f,
                    LpCoef = lp, GainL = gl, GainR = gr
                });
                t += dur + Rng.Range(0.07f, 0.16f);
            }
        }
    }

    private void Dog(float amp, float lp, float gl, float gr)
    {
        float f = Rng.Range(260f, 380f);
        int barks = 2 + (int)(Rng.NextU() % 3);
        float t = 0;
        for (int b = 0; b < barks; b++)
        {
            float dur = Rng.Range(0.13f, 0.2f);
            Add(new ToneVoice
            {
                Delay = t, Freq = new[] { (0f, f * 1.25f), (dur, f * 0.8f) }, Amp = Env(dur, 0.01f, 0.06f, amp * 0.9f),
                Harmonics = new[] { 1f, 0.9f, 0.7f, 0.5f, 0.35f }, NoiseMix = 0.22f, LpCoef = lp, GainL = gl, GainR = gr
            });
            t += dur + Rng.Range(0.14f, 0.3f);
        }
    }
}

internal sealed class InsectSource : ISoundSource
{
    private readonly FastRng _rng = new(91);
    private sealed class Cricket
    {
        public float F, Rate, Cadence, Offset, GL, GR, Phase;
        public double T;
    }
    private readonly Cricket[] _crickets;
    private Svf _cl, _cr;
    private double _swell;
    private float _buzzPhase;

    public InsectSource()
    {
        _crickets = new Cricket[5];
        for (int i = 0; i < _crickets.Length; i++)
        {
            var (gl, gr) = Dsp.Pan(_rng.Range(-1f, 1f));
            _crickets[i] = new Cricket { F = _rng.Range(4100f, 5200f), Rate = _rng.Range(26f, 34f), Cadence = _rng.Range(0.42f, 0.65f), Offset = _rng.Range(0f, 1f), GL = gl, GR = gr };
        }
    }

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        float night = 1f - aux, day = aux;
        for (int i = 0; i < frames; i++)
        {
            float sl = 0, sr = 0;
            if (night > 0.01f)
                foreach (var c in _crickets)
                {
                    double cyc = (c.T / c.Cadence + c.Offset) % 1.0;           // one chirp group per cadence
                    float gate = cyc < 0.22 ? 0.5f - 0.5f * MathF.Cos((float)(cyc / 0.22 * 2 * Math.PI * 3)) : 0f; // 3 pulses
                    c.Phase += 2f * MathF.PI * c.F / Dsp.Rate;
                    if (c.Phase > 1e5f) c.Phase %= 2f * MathF.PI;
                    float s = MathF.Sin(c.Phase) * gate * 0.05f * night;
                    c.T += 1.0 / Dsp.Rate;
                    sl += s * c.GL; sr += s * c.GR;
                }
            if (day > 0.01f)
            {
                _swell += 1.0 / (11.0 * Dsp.Rate);
                float sw = 0.55f + 0.45f * (float)Math.Sin(2 * Math.PI * _swell);
                _buzzPhase += 2f * MathF.PI * 105f / Dsp.Rate;
                float am = 0.5f + 0.5f * MathF.Sin(_buzzPhase);
                float n1 = _rng.Noise(), n2 = _rng.Noise();
                sl += _cl.Band(n1, 6200f, 8f) * am * sw * 0.16f * day;
                sr += _cr.Band(n2, 6700f, 8f) * am * sw * 0.16f * day;
            }
            l[i] += sl * level; r[i] += sr * level;
        }
    }
}

/// <summary>
/// Train that rumbles, clacks and blows its horn as it crosses. <see cref="Proximity"/> (0 = far / off screen, 1 = right
/// in front) and <see cref="Pan"/> follow the train drawn on the taskbar, so it fades and pans like a real pass.
/// </summary>
internal sealed class TrainSource : ISoundSource
{
    private readonly FastRng _rng = new(101);
    private float _lp, _lpRumble;
    private double _clackPhase;
    private float _clickT = 9;
    private bool _horned;
    private readonly List<ToneVoice> _horn = new();
    private float _prox;

    public volatile float Proximity;
    public volatile float Pan;

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        float target = Math.Clamp(Proximity, 0f, 1f);
        if (target < 0.04f) _horned = false;
        if (!_horned && target > 0.3f && level > 0.05f)
        {
            _horned = true;
            for (int k = 0; k < 2; k++)
            {
                float dur = k == 0 ? 0.65f : 1.1f, delay = k == 0 ? 0f : 0.85f;
                foreach (var f in new[] { 311f, 392f, 466f })
                    _horn.Add(MakeHorn(f, delay, dur));
            }
        }
        var (gl, gr) = Dsp.Pan(Pan);
        for (int i = 0; i < frames; i++)
        {
            _prox += (target - _prox) * 0.0006f;
            float p = _prox * _prox * level;
            if (p < 1e-4f) continue;
            float n = _rng.Noise();
            float cut = Dsp.OnePoleCoef(300f + 5200f * _prox);
            // rolling rumble
            _lpRumble += Dsp.OnePoleCoef(150f) * (n - _lpRumble);
            float s = _lpRumble * 1.4f;
            // clickety-clack: two pulses per rail joint
            _clackPhase += 4.3 / Dsp.Rate;
            double ph = _clackPhase % 1.0;
            if (ph < 4.3 / Dsp.Rate) { _clickT = 0; }
            if (Math.Abs(ph - 0.18) < 4.3 / Dsp.Rate) { _clickT = 0; }
            _clickT += 1f / Dsp.Rate;
            float click = n * MathF.Exp(-_clickT / 0.02f) * 0.6f + MathF.Sin(2f * MathF.PI * 72f * _clickT) * MathF.Exp(-_clickT / 0.07f) * 0.5f;
            s += click;
            _lp += cut * (s - _lp);
            float o = _lp * p;
            l[i] += o * gl; r[i] += o * gr;
        }
        // horn (heard with the same distance / pan)
        for (int v = _horn.Count - 1; v >= 0; v--)
        {
            var tmp = new float[frames];
            var tmp2 = new float[frames];
            _horn[v].Render(tmp, tmp2, frames, _rng);
            float g = Math.Clamp(_prox, 0f, 1f) * level * 0.45f;
            for (int i = 0; i < frames; i++) { l[i] += tmp[i] * g * gl; r[i] += tmp[i] * g * gr; }
            if (_horn[v].Done) _horn.RemoveAt(v);
        }
    }

    private static ToneVoice MakeHorn(float f, float delay, float dur) => new()
    {
        Delay = delay, Freq = new[] { (0f, f * 0.97f), (0.08f, f), (dur, f * 0.99f) },
        Amp = new[] { (0f, 0f), (0.06f, 0.5f), (dur - 0.08f, 0.5f), (dur, 0f) },
        Harmonics = new[] { 1f, 0.6f, 0.4f, 0.25f, 0.12f }, LpCoef = Dsp.OnePoleCoef(2400f), GainL = 1f, GainR = 1f,
        Duration = dur
    };
}
