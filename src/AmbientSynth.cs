namespace AudioTaskbar;

// Procedural part of the ambience: wind, the train rumble / rail clack and the jet are still synthesized from noise and
// oscillators at run time (no loop point, no seam). Everything else is a real CC0 recording (see SampleAudio.cs, NOTICE.md).

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

/// <summary>
/// Train that rumbles, clacks and blows its horn as it crosses. <see cref="Proximity"/> (0 = far / off screen, 1 = right
/// in front) and <see cref="Pan"/> follow the train drawn on the taskbar, so it fades and pans like a real pass.
/// </summary>
internal sealed class TrainSource : ISoundSource, IWarmable
{
    private readonly FastRng _rng = new(101);
    private float _lp, _lpRumble;
    private double _clackPhase;
    private float _clickT = 9;
    private bool _horned;
    private readonly SampleVoice _horn = new();
    private float _prox;

    public volatile float Proximity;
    public volatile float Pan;

    public void Warm() => SampleBank.Warm(SampleBank.TrainHorn);

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        float target = Math.Clamp(Proximity, 0f, 1f);
        if (target < 0.04f) _horned = false;
        if (!_horned && target > 0.3f && level > 0.05f)
        {
            _horned = true;
            _horn.Start(SampleBank.Get(SampleBank.TrainHorn), 1f, 0f);
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
        // horn (a real recording, heard with the same distance / pan)
        if (_horn.Active)
        {
            float g = Math.Clamp(_prox, 0f, 1f) * level * SampleBank.TrainHornTrim * 1.41f;
            _horn.Mix(l, r, frames, g * gl, g * gr);
        }
    }
}

/// <summary>
/// Jet take-off and climb: a turbine rush that swells with engine effort (<see cref="Thrust"/>) and with how close the plane is
/// (<see cref="Proximity"/>), plus a thin spooling whine. Silent while the plane is away. Pans with the plane on the taskbar.
/// </summary>
internal sealed class AirplaneSource : ISoundSource
{
    private readonly FastRng _rng = new(313);
    private float _lp1, _lp2, _prox, _thr;
    private double _ph;

    public volatile float Proximity;
    public volatile float Thrust;
    public volatile float Pan;

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        float targetProx = Math.Clamp(Proximity, 0f, 1f);
        float targetThr = Math.Clamp(Thrust, 0f, 1f);
        var (gl, gr) = Dsp.Pan(Pan);
        for (int i = 0; i < frames; i++)
        {
            _prox += (targetProx - _prox) * 0.0005f;
            _thr += (targetThr - _thr) * 0.0004f;
            float amp = _prox * _prox * level * (0.12f + 0.88f * _thr);
            if (amp < 1e-4f) continue;
            float n = _rng.Noise();
            // turbine rush: low-passed noise that opens up with thrust
            float cut = Dsp.OnePoleCoef(220f + 2800f * _thr);
            _lp1 += cut * (n - _lp1);
            _lp2 += cut * (_lp1 - _lp2);
            float rush = _lp2 * (2.5f + 0.5f * _thr);
            // thin spooling whine (rises with thrust)
            _ph += (520.0 + 780.0 * _thr) / Dsp.Rate;
            if (_ph > 1) _ph -= 1;
            float whine = MathF.Sin(2f * MathF.PI * (float)_ph) * 0.06f * _thr;
            float o = (rush + whine) * amp;
            l[i] += o * gl; r[i] += o * gr;
        }
    }
}
