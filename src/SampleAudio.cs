using System.Collections.Concurrent;
using System.Reflection;
using NAudio.Vorbis;

namespace AudioTaskbar;

// v5.4.1: real CC0 recordings (embedded OGG Vorbis, decoded with NAudio.Vorbis / NVorbis, MIT) for rain, thunder, traffic,
// car horns, the train horn, birds, insects, animals and waves. Wind, the airplane and the train rumble stay synthesized.
// Credits and processing notes: NOTICE.md. A clip is decoded lazily, only once its slider is raised (see IWarmable), and a
// clip that cannot be decoded just stays silent - it can never crash the app.

/// <summary>A decoded clip: 44.1 kHz float, mono clips share one array for both channels.</summary>
internal sealed class SampleClip
{
    public required float[] L, R;
    public int Frames => L.Length;
}

/// <summary>Sources that load their recordings in the background as soon as they become audible.</summary>
internal interface IWarmable { void Warm(); }

internal static class SampleBank
{
    public const string ResourcePrefix = "AudioTaskbar.Sounds.";

    // One-line swap for the rain recording (any embedded name without ".ogg"): Mưa A = Raphael.Malfliet 689960.
    public const string RainLoop = "rain";
    public const string Birds = "birds", Waves = "waves", Traffic = "traffic", Crickets = "crickets", Cicadas = "cicadas";
    public const string Horn = "horn", TrainHorn = "trainhorn", Rooster = "rooster", Dog = "dog";
    public static readonly string[] Thunder = { "thunder1", "thunder2", "thunder3", "thunder4", "thunder5" };
    public static readonly string[] Frogs = { "frogs1", "frogs2" };

    // Gentle per-recording level trims (the loops are normalised to about -18 LUFS, the clips to about -20 LUFS).
    public const float TrainHornTrim = 0.9f;

    private static readonly ConcurrentDictionary<string, Lazy<SampleClip?>> Cache = new();

    /// <summary>The names of every embedded recording (without prefix and extension).</summary>
    public static IReadOnlyList<string> EmbeddedNames() =>
        typeof(SampleBank).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".ogg", StringComparison.Ordinal))
            .Select(n => n.Substring(ResourcePrefix.Length, n.Length - ResourcePrefix.Length - 4)).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The decoded clip (decoding on first use; blocks only if another thread is decoding the same clip). Null if unavailable.</summary>
    public static SampleClip? Get(string name) => Cache.GetOrAdd(name, n => new Lazy<SampleClip?>(() => Load(n))).Value;

    /// <summary>Starts decoding in the background so the audio thread does not have to wait.</summary>
    public static void Warm(params string[] names)
    {
        foreach (var n in names)
        {
            if (Cache.TryGetValue(n, out var lz) && lz.IsValueCreated) continue;
            var name = n;
            Task.Run(() => Get(name));
        }
    }

    public static bool IsLoaded(string name) => Cache.TryGetValue(name, out var lz) && lz.IsValueCreated && lz.Value is not null;

    private static SampleClip? Load(string name)
    {
        try
        {
            using var stream = typeof(SampleBank).Assembly.GetManifestResourceStream(ResourcePrefix + name + ".ogg");
            return stream is null ? null : Decode(stream);
        }
        catch { return null; }
    }

    internal static SampleClip Decode(Stream stream)
    {
        using var reader = new VorbisWaveReader(stream);
        int ch = Math.Max(1, reader.WaveFormat.Channels), rate = reader.WaveFormat.SampleRate;
        var all = new List<float[]>();
        int total = 0;
        var buf = new float[16384 * ch];
        int n;
        while ((n = reader.Read(buf, 0, buf.Length)) > 0)
        {
            var c = new float[n]; Array.Copy(buf, c, n); all.Add(c); total += n;
        }
        int frames = total / ch;
        var l = new float[frames];
        var r = ch >= 2 ? new float[frames] : l;
        int f = 0;
        foreach (var part in all)
            for (int i = 0; i + ch <= part.Length; i += ch, f++)
            {
                l[f] = part[i];
                if (ch >= 2) r[f] = part[i + 1];
            }
        if (rate != Dsp.Rate && frames > 1)
        {
            l = Resample(l, rate);
            r = ch >= 2 ? Resample(r, rate) : l;
        }
        return new SampleClip { L = l, R = r };
    }

    private static float[] Resample(float[] a, int rate)
    {
        int n = (int)((long)a.Length * Dsp.Rate / rate);
        var o = new float[n];
        double step = rate / (double)Dsp.Rate;
        for (int i = 0; i < n; i++)
        {
            double p = i * step; int j = (int)p; float fr = (float)(p - j);
            o[i] = a[j] + ((j + 1 < a.Length ? a[j + 1] : a[j]) - a[j]) * fr;
        }
        return o;
    }

    /// <summary>Linear interpolation inside a looping clip.</summary>
    internal static float At(float[] a, double pos)
    {
        int n = a.Length, i = (int)pos;
        float f = (float)(pos - i);
        i %= n; int j = i + 1 == n ? 0 : i + 1;
        return a[i] + (a[j] - a[i]) * f;
    }
}

/// <summary>Plays one clip once (optionally delayed and resampled) into the output.</summary>
internal sealed class SampleVoice
{
    private SampleClip? _clip;
    private double _pos, _rate = 1, _delay;
    public float GL = 1f, GR = 1f;
    public bool Active => _clip is not null;

    public void Start(SampleClip? clip, float rate, float delaySeconds)
    {
        _clip = clip; _pos = 0; _rate = rate <= 0 ? 1 : rate; _delay = delaySeconds * Dsp.Rate;
        if (clip is null || clip.Frames < 2) _clip = null;
    }

    public void Mix(float[] l, float[] r, int frames, float gl, float gr)
    {
        var c = _clip;
        if (c is null) return;
        int last = c.Frames - 1;
        for (int i = 0; i < frames; i++)
        {
            if (_delay > 0) { _delay--; continue; }
            int j = (int)_pos;
            if (j >= last) { _clip = null; return; }
            float f = (float)(_pos - j);
            float sl = c.L[j] + (c.L[j + 1] - c.L[j]) * f;
            float sr = ReferenceEquals(c.L, c.R) ? sl : c.R[j] + (c.R[j + 1] - c.R[j]) * f;
            l[i] += sl * gl; r[i] += sr * gr;
            _pos += _rate;
        }
    }
}

/// <summary>A seamless looping recording (rain, birds, waves, traffic). The loop was cross-faded when the file was built.</summary>
internal sealed class SampleLoopSource : ISoundSource, IWarmable
{
    private readonly string _name;
    private readonly float _trim;
    private double _pos;

    public SampleLoopSource(string name, float trim = 1f, double startSeconds = 0) { _name = name; _trim = trim; _pos = startSeconds * Dsp.Rate; }
    public void Warm() => SampleBank.Warm(_name);

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        if (level < 0.001f) return;
        var c = SampleBank.Get(_name);
        if (c is null || c.Frames < 2) return;
        float g = level * _trim;
        int n = c.Frames, p = (int)_pos;
        for (int i = 0; i < frames; i++)
        {
            l[i] += c.L[p] * g; r[i] += c.R[p] * g;
            if (++p == n) p = 0;
        }
        _pos = p;
    }
}

/// <summary>Night crickets (two overlapping layers, the recording is sparse) cross-faded with daytime cicadas by <c>aux</c> (0 night, 1 day).</summary>
internal sealed class InsectSampleSource : ISoundSource, IWarmable
{
    private double _pa, _pb, _pc;
    private const double RateB = 0.975;

    public InsectSampleSource() { _pb = 11.0 * Dsp.Rate; _pc = 0; }
    public void Warm() => SampleBank.Warm(SampleBank.Crickets, SampleBank.Cicadas);

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        if (level < 0.001f) return;
        float a = Math.Clamp(aux, 0f, 1f) * (MathF.PI * 0.5f);
        float night = MathF.Cos(a), day = MathF.Sin(a);
        var cr = night > 0.002f ? SampleBank.Get(SampleBank.Crickets) : null;
        var ci = day > 0.002f ? SampleBank.Get(SampleBank.Cicadas) : null;
        float gc = level * night * 1.2f, gi = level * day * 0.8f;
        for (int i = 0; i < frames; i++)
        {
            if (cr is not null)
            {
                float x = SampleBank.At(cr.L, _pa) * gc, y = SampleBank.At(cr.L, _pb) * gc;
                l[i] += x * 0.85f + y * 0.45f; r[i] += x * 0.45f + y * 0.85f;
                _pa += 1.0; _pb += RateB;
                if (_pa >= cr.Frames) _pa -= cr.Frames;
                if (_pb >= cr.Frames) _pb -= cr.Frames;
            }
            if (ci is not null)
            {
                l[i] += SampleBank.At(ci.L, _pc) * gi; r[i] += SampleBank.At(ci.R, _pc) * gi;
                _pc += 1.0; if (_pc >= ci.Frames) _pc -= ci.Frames;
            }
        }
    }
}

/// <summary>Base for sources that fire randomly timed one-shot recordings (thunder, horns, animals).</summary>
internal abstract class SampleEventSource : ISoundSource, IWarmable
{
    protected readonly FastRng Rng;
    private readonly SampleVoice[] _voices = new SampleVoice[12];
    private double _nextIn;
    protected virtual float MinLevel => 0.01f;

    protected SampleEventSource(int seed, double firstMin, double firstMax)
    {
        Rng = new FastRng(seed);
        for (int i = 0; i < _voices.Length; i++) _voices[i] = new SampleVoice();
        _nextIn = firstMin + Rng.Unit() * (firstMax - firstMin);
    }

    protected abstract string[] Names { get; }
    protected abstract double NextInterval(float level, float aux);
    protected abstract void Spawn(float level, float aux);

    public void Warm() => SampleBank.Warm(Names);

    /// <summary>Start a clip with a random pan; <paramref name="gain"/> 1 = the clip's own level.</summary>
    protected bool Play(string name, float gain, float pan, float rate = 1f, float delay = 0f)
    {
        var clip = SampleBank.Get(name);
        if (clip is null) return false;
        foreach (var v in _voices)
            if (!v.Active)
            {
                var (gl, gr) = Dsp.Pan(pan);
                v.GL = gl * 1.41f * gain; v.GR = gr * 1.41f * gain;
                v.Start(clip, rate, delay);
                return true;
            }
        return false;
    }

    public void Render(float[] l, float[] r, int frames, float level, float aux)
    {
        if (level > MinLevel)
        {
            _nextIn -= frames / (double)Dsp.Rate;
            if (_nextIn <= 0)
            {
                Spawn(level, aux);
                _nextIn = NextInterval(level, aux);
            }
        }
        foreach (var v in _voices)
            if (v.Active) v.Mix(l, r, frames, v.GL, v.GR);
    }
}

/// <summary>
/// Thunder: one-shots cut from a recorded storm, at random intervals that shorten with rain intensity
/// (level = storm strength, aux = rain intensity 0..1; about every 70 s in light rain, every 18 s in a downpour).
/// </summary>
internal sealed class ThunderSampleSource : SampleEventSource
{
    private int _last = -1;
    public ThunderSampleSource() : base(41, 4, 8) { }
    protected override float MinLevel => 0.05f;
    protected override string[] Names => SampleBank.Thunder;
    /// <summary>Mean seconds between strikes for a rain intensity 0..1.</summary>
    public static float MeanInterval(float rainIntensity) => Dsp.Lerp(70f, 18f, Math.Clamp(rainIntensity, 0f, 1f));
    protected override double NextInterval(float level, float aux) => MeanInterval(aux) * Rng.Range(0.6f, 1.4f);

    protected override void Spawn(float level, float aux)
    {
        int k = (int)(Rng.Unit() * SampleBank.Thunder.Length) % SampleBank.Thunder.Length;
        if (k == _last) k = (k + 1) % SampleBank.Thunder.Length;
        _last = k;
        float dist = Rng.Range(0f, 1f);
        Play(SampleBank.Thunder[k], (1f - dist * 0.55f) * 1.2f, Rng.Range(-0.7f, 0.7f), Rng.Range(0.92f, 1.08f));
    }
}

/// <summary>Car horns: one to three honks cut from a city recording, each a little different in pitch and distance.</summary>
internal sealed class HornSampleSource : SampleEventSource
{
    public HornSampleSource() : base(61, 0.6, 2.6) { }
    protected override string[] Names => new[] { SampleBank.Horn };
    protected override double NextInterval(float level, float aux) => Rng.Range(2.5f, 11f) / (0.2f + level * 1.6f);

    protected override void Spawn(float level, float aux)
    {
        float dist = Rng.Range(0.1f, 1f);
        float pan = Rng.Range(-0.9f, 0.9f), amp = 1f - dist * 0.6f, rate = Rng.Range(0.85f, 1.2f);
        int honks = Rng.Chance(0.55f) ? 1 : Rng.Chance(0.6f) ? 2 : 3;
        float t = 0;
        for (int i = 0; i < honks; i++)
        {
            Play(SampleBank.Horn, amp, pan, rate * Rng.Range(0.96f, 1.04f), t);
            t += Rng.Range(0.45f, 0.9f);
        }
    }
}

/// <summary>Rooster at daybreak (aux 1), frogs and a distant dog at night (aux 0).</summary>
internal sealed class AnimalSampleSource : SampleEventSource
{
    public AnimalSampleSource() : base(81, 0.6, 2.6) { }
    protected override string[] Names => new[] { SampleBank.Rooster, SampleBank.Dog, SampleBank.Frogs[0], SampleBank.Frogs[1] };
    protected override double NextInterval(float level, float aux) => Rng.Range(4f, 15f) / (0.3f + level * 1.5f);

    protected override void Spawn(float level, float aux)
    {
        float dist = Rng.Range(0.2f, 1f), amp = 1f - dist * 0.55f, pan = Rng.Range(-1f, 1f);
        float roll = Rng.Unit();
        bool day = aux > 0.55f;
        if (day && roll < 0.5f) Rooster(amp, pan);
        else if (!day && roll < 0.62f) Frogs(amp, pan);
        else if (roll < 0.8f) Dog(amp, pan);
        else if (!day) Frogs(amp, pan);
        else Rooster(amp, pan);
    }

    private void Rooster(float amp, float pan) => Play(SampleBank.Rooster, amp, pan, Rng.Range(0.94f, 1.08f));
    private void Frogs(float amp, float pan) => Play(SampleBank.Frogs[Rng.Chance(0.5f) ? 0 : 1], amp * 0.9f, pan, Rng.Range(0.97f, 1.03f));
    private void Dog(float amp, float pan)
    {
        int barks = 1 + (int)(Rng.Unit() * 3) % 3;
        float t = 0;
        for (int i = 0; i < barks; i++) { Play(SampleBank.Dog, amp, pan, Rng.Range(0.9f, 1.1f), t); t += Rng.Range(0.55f, 1.1f); }
    }
}
