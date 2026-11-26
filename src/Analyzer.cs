namespace AudioTaskbar;

/// <summary>
/// FFT spectrum, waveform peak, loudness and beat envelopes.
/// Allocation-light and UI-thread friendly. No Windows/NAudio dependency
/// so the same math can be reasoned about independently.
/// </summary>
public sealed class Analyzer
{
    private int _fftSize;
    private int _barCount;
    private float[] _window = Array.Empty<float>();
    private float[] _re = Array.Empty<float>();
    private float[] _im = Array.Empty<float>();
    private float[] _mags = Array.Empty<float>();
    private float[] _bars = Array.Empty<float>();
    private float[] _smoothed = Array.Empty<float>();
    private float[] _peaks = Array.Empty<float>();
    private int[] _binStart = Array.Empty<int>();
    private int[] _binEnd = Array.Empty<int>();
    private float _sampleRate = 48000f;
    private float _minFreq = -1f;
    private float _maxFreq = -1f;
    private float _gain = 4f;
    private float _energyEnv;
    private float _bassEnv;
    private float _bassSlow;
    private float _beatPulse;

    public float[] Bars => _smoothed;
    public float[] Peaks => _peaks;
    public float Energy { get; private set; }
    public float Bass { get; private set; }
    public float Beat { get; private set; }
    public float Gain => _gain;
    public float WavePeak { get; private set; }

    public void Configure(AppConfig cfg, float sampleRate)
    {
        bool binsChanged = Math.Abs(_minFreq - cfg.MinFreq) > 0.5f || Math.Abs(_maxFreq - cfg.MaxFreq) > 0.5f;
        bool resize = _fftSize != cfg.FftSize || _barCount != cfg.BarCount ||
                      Math.Abs(_sampleRate - sampleRate) > 0.5f || _window.Length != cfg.FftSize;
        _fftSize = cfg.FftSize;
        _barCount = cfg.BarCount;
        _sampleRate = sampleRate > 0 ? sampleRate : 48000f;
        if (resize)
        {
            _window = new float[_fftSize];
            _re = new float[_fftSize];
            _im = new float[_fftSize];
            _mags = new float[_fftSize / 2];
            _bars = new float[_barCount];
            _smoothed = new float[_barCount];
            _peaks = new float[_barCount];
            _binStart = new int[_barCount];
            _binEnd = new int[_barCount];
            for (int i = 0; i < _fftSize; i++)
                _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (_fftSize - 1)));
            RebuildBins(cfg.MinFreq, cfg.MaxFreq);
        }
        else if (binsChanged)
        {
            RebuildBins(cfg.MinFreq, cfg.MaxFreq);
        }
    }

    private void RebuildBins(float minFreq, float maxFreq)
    {
        _minFreq = minFreq;
        _maxFreq = maxFreq;
        int nyquistBins = _fftSize / 2;
        float hzPerBin = _sampleRate / _fftSize;
        minFreq = Math.Max(minFreq, hzPerBin);
        maxFreq = Math.Min(maxFreq, _sampleRate / 2f - hzPerBin);
        if (maxFreq <= minFreq) maxFreq = minFreq + hzPerBin * 2;

        for (int b = 0; b < _barCount; b++)
        {
            float t0 = (float)b / _barCount;
            float t1 = (float)(b + 1) / _barCount;
            float f0 = minFreq * MathF.Pow(maxFreq / minFreq, t0);
            float f1 = minFreq * MathF.Pow(maxFreq / minFreq, t1);
            int i0 = (int)MathF.Floor(f0 / hzPerBin);
            int i1 = (int)MathF.Ceiling(f1 / hzPerBin);
            i0 = Math.Clamp(i0, 1, nyquistBins - 1);
            i1 = Math.Clamp(i1, i0 + 1, nyquistBins);
            _binStart[b] = i0;
            _binEnd[b] = i1;
        }
    }

    public void Process(ReadOnlySpan<float> mono, AppConfig cfg)
    {
        int n = _fftSize;
        if (mono.Length < n || n == 0) return;

        float wavePeak = 0f;
        float rmsAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            float s = mono[i];
            float a = MathF.Abs(s);
            if (a > wavePeak) wavePeak = a;
            rmsAcc += s * s;
            _re[i] = s * _window[i];
            _im[i] = 0f;
        }
        WavePeak = wavePeak;
        float rms = MathF.Sqrt(rmsAcc / n);

        Fft(_re, _im);

        int half = n / 2;
        for (int i = 0; i < half; i++)
        {
            float mag = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) / (n * 0.5f);
            _mags[i] = mag;
        }

        // Bass 20–150 Hz
        float hzPerBin = _sampleRate / n;
        int b0 = Math.Clamp((int)(20f / hzPerBin), 1, half - 1);
        int b1 = Math.Clamp((int)(150f / hzPerBin), b0 + 1, half);
        float bass = 0f;
        for (int i = b0; i < b1; i++) bass += _mags[i];
        bass /= (b1 - b0);

        if (cfg.AutoGain)
        {
            float peakBar = 0f;
            for (int b = 0; b < _barCount; b++)
            {
                float v = AverageBin(b);
                if (v > peakBar) peakBar = v;
            }
            float target = peakBar * _gain;
            // Aim for ~0.75 peak occupancy; ignore near-silence.
            if (peakBar > 1e-5f)
            {
                float desired = 0.75f / Math.Max(peakBar, 1e-6f);
                desired = Math.Clamp(desired, 0.4f, 40f);
                _gain = _gain * 0.98f + desired * 0.02f;
            }
            else
            {
                _gain = _gain * 0.995f + 4f * 0.005f;
            }
            _ = target;
        }
        else
        {
            _gain = 8f;
        }

        float gain = _gain * cfg.Sensitivity;
        for (int b = 0; b < _barCount; b++)
        {
            // Gentle high-frequency boost so cymbals/rain are visible
            float tilt = 0.7f + 0.6f * b / Math.Max(_barCount - 1, 1);
            float v = AverageBin(b) * gain * tilt;
            v = MathF.Min(1f, Compress(v));
            _bars[b] = v;
            float sm = cfg.Smoothing;
            _smoothed[b] = _smoothed[b] * sm + v * (1f - sm);
            if (cfg.PeakHold)
            {
                if (_smoothed[b] > _peaks[b]) _peaks[b] = _smoothed[b];
                else _peaks[b] = MathF.Max(0f, _peaks[b] - cfg.PeakDecay);
            }
            else _peaks[b] = _smoothed[b];
        }

        float energy = MathF.Min(1f, Compress(rms * gain * 2.2f));
        Energy = ApplyEnvelope(ref _energyEnv, energy, cfg.ColorAttack, cfg.ColorRelease);
        Bass = ApplyEnvelope(ref _bassEnv, MathF.Min(1f, Compress(bass * gain * 3f)), cfg.ColorAttack, cfg.ColorRelease);

        _bassSlow = _bassSlow * 0.97f + Bass * 0.03f;
        float onset = 0f;
        if (Bass > 0.08f && Bass > _bassSlow * 1.35f && Bass - _bassSlow > 0.05f)
            onset = Math.Clamp((Bass - _bassSlow) * 3f, 0f, 1f);
        _beatPulse = MathF.Max(_beatPulse * 0.82f, onset);
        Beat = _beatPulse;
    }

    private float AverageBin(int b)
    {
        int i0 = _binStart[b];
        int i1 = _binEnd[b];
        float sum = 0f;
        for (int i = i0; i < i1; i++) sum += _mags[i];
        return sum / Math.Max(1, i1 - i0);
    }

    private static float Compress(float x)
    {
        // Soft knee so quiet rain still moves, loud kicks don't clip hard
        return MathF.Log(1f + 6f * MathF.Max(0f, x)) / MathF.Log(7f);
    }

    private static float ApplyEnvelope(ref float env, float value, float attack, float release)
    {
        float coeff = value > env ? attack : release;
        env = env + (value - env) * coeff;
        return env;
    }

    /// <summary>In-place radix-2 Cooley–Tukey FFT.</summary>
    public static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * MathF.PI / len;
            float wlenRe = MathF.Cos(ang);
            float wlenIm = MathF.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float wRe = 1f, wIm = 0f;
                int half = len >> 1;
                for (int k = 0; k < half; k++)
                {
                    int u = i + k;
                    int v = u + half;
                    float tRe = wRe * re[v] - wIm * im[v];
                    float tIm = wRe * im[v] + wIm * re[v];
                    re[v] = re[u] - tRe;
                    im[v] = im[u] - tIm;
                    re[u] += tRe;
                    im[u] += tIm;
                    float nwRe = wRe * wlenRe - wIm * wlenIm;
                    wIm = wRe * wlenIm + wIm * wlenRe;
                    wRe = nwRe;
                }
            }
        }
    }

    public static void MapBackground(AppConfig cfg, float energy, float bass, float beat,
        out int r, out int g, out int b, out int a)
    {
        float t = Math.Clamp(energy * 0.55f + bass * 0.45f, 0f, 1f);
        t = Math.Clamp(t + beat * 0.25f, 0f, 1f);
        Color c;
        if (t < 0.5f) c = LerpColor(cfg.RgbaLow, cfg.RgbaMid, t * 2f);
        else c = LerpColor(cfg.RgbaMid, cfg.RgbaHigh, (t - 0.5f) * 2f);

        // Brighten slightly on beats
        float boost = 1f + beat * 0.35f;
        r = Math.Clamp((int)(c.R * boost), 0, 255);
        g = Math.Clamp((int)(c.G * boost), 0, 255);
        b = Math.Clamp((int)(c.B * boost), 0, 255);
        // Alpha is decided by the overlay (opaque base + wash). Here we only return colour richness 0..1 as 0..255.
        a = Math.Clamp((int)((0.35f + 0.65f * t) * 255f), 0, 255);
    }

    public static Color LerpColor(Color a, Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)(a.R + (c.R - a.R) * t),
            (int)(a.G + (c.G - a.G) * t),
            (int)(a.B + (c.B - a.B) * t));
    }

    public static Color BarColorAt(AppConfig cfg, int index, int count, float height)
    {
        if (!cfg.BarColorByFrequency)
        {
            var b = cfg.RgbaBar;
            return Color.FromArgb(255, b.R, b.G, b.B);
        }
        float t = count <= 1 ? 0f : (float)index / (count - 1);
        // Blend frequency position with height so loud bars go warmer
        t = Math.Clamp(t * 0.75f + height * 0.25f, 0f, 1f);
        if (t < 0.5f) return LerpColor(cfg.RgbaLow, cfg.RgbaMid, t * 2f);
        return LerpColor(cfg.RgbaMid, cfg.RgbaHigh, (t - 0.5f) * 2f);
    }
}
