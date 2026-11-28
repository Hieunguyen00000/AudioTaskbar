using NAudio.Wave;

namespace AudioTaskbar;

/// <summary>
/// Mixes the synthesized sources. Final gain of an effect = master × per-effect volume (both 0 on first run) × scene
/// level × fade. Level and gain are smoothed (soft fades, no clicks), the mix is soft-clipped, and work is done in fixed
/// 256-frame blocks so the output does not depend on how the audio device chunks its reads.
/// A source whose gain is 0 is not rendered at all.
/// </summary>
internal sealed class AmbientMixer
{
    public const int Block = 256;

    private sealed class Slot
    {
        public required ISoundSource Src;
        public float[] L = new float[Block], R = new float[Block];
        public float Level, Aux, TargetLevel, TargetAux, Gain, TargetGain;
        public long RenderCalls;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Slot> _slots = new();
    private readonly float[] _outL = new float[Block], _outR = new float[Block];
    private int _carry;                      // frames of the last block not yet handed out
    private int _master;
    private bool _paused;
    private readonly float _gainK = 1f - MathF.Exp(-1f / (0.18f * Dsp.Rate));     // ~180 ms fade
    private readonly float _levelK = 1f - MathF.Exp(-Block / (2.5f * Dsp.Rate));  // ~2.5 s scene fade

    public TrainSource Train { get; } = new();
    public ThunderSampleSource Thunder { get; } = new();
    public AirplaneSource Airplane { get; } = new();

    public AmbientMixer()
    {
        void Add(string id, ISoundSource s) => _slots[id] = new Slot { Src = s };
        Add(SoundEffects.Rain, new SampleLoopSource(SampleBank.RainLoop));
        Add(SoundEffects.Thunder, Thunder);
        Add(SoundEffects.Wind, new WindSource());
        Add(SoundEffects.Traffic, new SampleLoopSource(SampleBank.Traffic, 0.9f));
        Add(SoundEffects.Horns, new HornSampleSource());
        Add(SoundEffects.Train, Train);
        Add(SoundEffects.Airplane, Airplane);
        Add(SoundEffects.Birds, new SampleLoopSource(SampleBank.Birds));
        Add(SoundEffects.Insects, new InsectSampleSource());
        Add(SoundEffects.Animals, new AnimalSampleSource());
        Add(SoundEffects.Waves, new SampleLoopSource(SampleBank.Waves, 0.9f));
    }

    public void SetVolumes(int master, IReadOnlyDictionary<string, int>? perEffect)
    {
        lock (_lock)
        {
            _master = Math.Clamp(master, 0, 100);
            foreach (var (id, slot) in _slots)
            {
                int v = perEffect is not null && perEffect.TryGetValue(id, out var x) ? x : 0;
                slot.TargetGain = SoundScape.Gain(_master, v);
                if (slot.TargetGain > 0f && slot.Src is IWarmable w) w.Warm();   // decode its recordings in the background
            }
        }
    }

    /// <summary>A lightning strike was flashed <paramref name="ageSeconds"/> ago: the thunder follows it. Ignored while the thunder volume is 0, the effects are paused or there is no storm.</summary>
    public void Strike(LightningStrike strike, double ageSeconds = 0)
    {
        lock (_lock)
        {
            var slot = _slots[SoundEffects.Thunder];
            if (!_paused && slot.TargetGain > 0f && slot.TargetLevel > 0.05f) Thunder.Strike(strike, ageSeconds);   // level > 0 = a storm
        }
    }

    public void SetScene(IReadOnlyDictionary<string, SoundTarget> scene)
    {
        lock (_lock)
            foreach (var (id, slot) in _slots)
            {
                if (scene.TryGetValue(id, out var t)) { slot.TargetLevel = Math.Clamp(t.Level, 0f, 1f); slot.TargetAux = Math.Clamp(t.Aux, 0f, 1f); }
                else slot.TargetLevel = 0f;
            }
    }

    /// <summary>Paused (effects off): everything fades to silence.</summary>
    public bool Paused { get => _paused; set { lock (_lock) _paused = value; } }

    /// <summary>True when anything could be heard now or is still fading out.</summary>
    public bool HasActivity
    {
        get
        {
            lock (_lock)
                return _slots.Values.Any(s => s.Gain > 1e-4f || (!_paused && s.TargetGain > 0f && s.TargetLevel > 0.001f));
        }
    }

    /// <summary>True when the user's volumes allow any sound at all (master and at least one effect above 0).</summary>
    public bool WantsAudio { get { lock (_lock) return !_paused && _slots.Values.Any(s => s.TargetGain > 0f); } }

    public long RenderCallsFor(string id) { lock (_lock) return _slots[id].RenderCalls; }

    /// <summary>Fill <paramref name="buffer"/> with interleaved stereo floats (frames = count / 2).</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        int done = 0;
        lock (_lock)
        {
            while (done < frames)
            {
                if (_carry == 0) { RenderBlock(); _carry = Block; }
                int take = Math.Min(frames - done, _carry);
                int from = Block - _carry;
                for (int i = 0; i < take; i++)
                {
                    buffer[offset + (done + i) * 2] = _outL[from + i];
                    buffer[offset + (done + i) * 2 + 1] = _outR[from + i];
                }
                _carry -= take; done += take;
            }
        }
        return frames * 2;
    }

    private void RenderBlock()
    {
        Array.Clear(_outL); Array.Clear(_outR);
        foreach (var slot in _slots.Values)
        {
            float target = _paused ? 0f : slot.TargetGain;
            slot.Level += (slot.TargetLevel - slot.Level) * _levelK;
            slot.Aux += (slot.TargetAux - slot.Aux) * _levelK;
            if (target == 0f && slot.Gain < 1e-5f) { slot.Gain = 0f; continue; }
            if (slot.Level < 1e-3f && slot.TargetLevel < 1e-3f) { slot.Gain += (target - slot.Gain) * Math.Min(1f, _gainK * Block); continue; }
            Array.Clear(slot.L); Array.Clear(slot.R);
            slot.Src.Render(slot.L, slot.R, Block, slot.Level, slot.Aux);
            slot.RenderCalls++;
            float g = slot.Gain;
            for (int i = 0; i < Block; i++)
            {
                g += (target - g) * _gainK;
                _outL[i] += slot.L[i] * g;
                _outR[i] += slot.R[i] * g;
            }
            slot.Gain = g;
        }
        for (int i = 0; i < Block; i++)
        {
            _outL[i] = MathF.Tanh(_outL[i]);
            _outR[i] = MathF.Tanh(_outR[i]);
        }
    }
}

/// <summary>NAudio source over <see cref="AmbientMixer"/> (44.1 kHz stereo float).</summary>
internal sealed class AmbientSampleProvider : ISampleProvider
{
    private readonly AmbientMixer _mixer;
    public AmbientSampleProvider(AmbientMixer mixer) => _mixer = mixer;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Dsp.Rate, 2);
    public int Read(float[] buffer, int offset, int count) => _mixer.Read(buffer, offset, count);
}

/// <summary>
/// Owns the output device. The device is opened only when the user has turned some volume up, closed again after a few
/// quiet seconds, paused-by-fade when the effects are off, and stopped cleanly on exit.
/// </summary>
internal sealed class AmbientEngine : IDisposable
{
    private readonly AmbientMixer _mixer = new();
    private readonly Action<string>? _log;
    private WaveOutEvent? _out;
    private DateTime _quietSince = DateTime.MinValue;
    private bool _failed;
    private bool _disposed;

    private readonly LightningSchedule _lightning;

    /// <param name="lightning">The lightning every diorama flashes; each flashed strike becomes thunder here.</param>
    public AmbientEngine(Action<string>? log = null, LightningSchedule? lightning = null)
    {
        _log = log;
        _lightning = lightning ?? LightningSchedule.Shared;
        _lightning.Shown += OnStrikeShown;
    }

    private void OnStrikeShown(LightningStrike s)
    {
        if (!_disposed) _mixer.Strike(s, Math.Max(0, _lightning.Now - s.Time));
    }

    public AmbientMixer Mixer => _mixer;
    public bool DeviceOpen => _out is not null;

    /// <param name="trainProgress">0..1 position of the train across the taskbar, or &lt;0 when there is no train.</param>
    /// <param name="trainSpeed">0 (standing at the platform) .. 1, scales how loud the rolling train is.</param>
    /// <param name="planeX01">0..1 position of the plane across the bar, or &lt;0 when it is not on screen.</param>
    /// <param name="planeThrust">0..1 engine effort of the plane.</param>
    public void Update(AppConfig cfg, RegionProfile region, SceneMood mood, SeasonId season, bool effectsOn, float trainProgress,
        float trainSpeed = 1f, float planeX01 = -1f, float planeThrust = 0f)
    {
        if (_disposed) return;
        _mixer.SetVolumes(cfg.SoundMasterVolume, SoundMix.Effective(cfg, SoundMix.Preset(region, mood, season)));
        _mixer.SetScene(SoundScape.Targets(region, mood, season));
        _mixer.Paused = !effectsOn;
        if (trainProgress >= 0f && cfg.ShowTrain)
        {
            _mixer.Train.Proximity = TrainProximity(trainProgress) * (0.25f + 0.75f * Math.Clamp(trainSpeed, 0f, 1f));
            _mixer.Train.Pan = Math.Clamp((trainProgress * 2f - 1f) * 0.85f, -1f, 1f);
        }
        else _mixer.Train.Proximity = 0f;
        if (planeX01 >= 0f && cfg.ShowAirplane)
        {
            _mixer.Airplane.Proximity = TrainProximity(planeX01);
            _mixer.Airplane.Thrust = Math.Clamp(planeThrust, 0f, 1f);
            _mixer.Airplane.Pan = Math.Clamp((planeX01 * 2f - 1f) * 0.85f, -1f, 1f);
        }
        else _mixer.Airplane.Proximity = 0f;

        if (_mixer.WantsAudio)
        {
            _quietSince = DateTime.MinValue;
            EnsureDevice();
        }
        else if (_out is not null)
        {
            if (!_mixer.HasActivity)
            {
                if (_quietSince == DateTime.MinValue) _quietSince = DateTime.UtcNow;
                else if ((DateTime.UtcNow - _quietSince).TotalSeconds > 2) CloseDevice();
            }
            else _quietSince = DateTime.MinValue;
        }
    }

    /// <summary>Bell-shaped loudness over the crossing: quiet far away, loudest when the train is in front.</summary>
    public static float TrainProximity(float progress)
    {
        if (progress < 0f || progress > 1f) return 0f;
        float s = MathF.Sin(MathF.PI * progress);
        return s * s * s;
    }

    private void EnsureDevice()
    {
        if (_out is not null || _failed) return;
        try
        {
            _out = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 3 };
            _out.Init(new AmbientSampleProvider(_mixer));
            _out.Play();
            _log?.Invoke("Ambient sound: output opened.");
        }
        catch (Exception ex)
        {
            _failed = true; // no audio device: never retry, never crash
            _log?.Invoke("Ambient sound unavailable: " + ex.Message);
            CloseDevice();
        }
    }

    private void CloseDevice()
    {
        var o = _out;
        _out = null;
        if (o is null) return;
        try { o.Stop(); } catch { }
        try { o.Dispose(); } catch { }
        _log?.Invoke("Ambient sound: output closed.");
    }

    /// <summary>Stop immediately (exit): no fade, device released.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lightning.Shown -= OnStrikeShown;
        _mixer.Paused = true;
        CloseDevice();
    }
}
