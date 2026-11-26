using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace AudioTaskbar;

/// <summary>WASAPI loopback of the default render device — captures all system audio.</summary>
public sealed class AudioCapture : IDisposable
{
    private readonly object _lock = new();
    private readonly NotificationClient _notifications = new();
    private MMDeviceEnumerator? _enumerator;
    private WasapiLoopbackCapture? _capture;
    private float[] _ring = new float[8192];
    private int _write;
    private int _count;
    private int _channels = 2;
    private float _sampleRate = 48000f;
    private bool _restartRequested;
    private bool _disposed;
    private string _deviceName = "(none)";

    public float SampleRate => _sampleRate;
    public string DeviceName => _deviceName;
    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }

    public event Action<string>? StatusChanged;

    public AudioCapture()
    {
        _notifications.DefaultChanged += () => _restartRequested = true;
    }

    public void Start()
    {
        if (_disposed) return;
        StopCapture();
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { /* first time */ }
            _enumerator.RegisterEndpointNotificationCallback(_notifications);

            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _deviceName = device.FriendlyName;
            _capture = new WasapiLoopbackCapture(device);
            var fmt = _capture.WaveFormat;
            _channels = Math.Max(1, fmt.Channels);
            _sampleRate = fmt.SampleRate;
            int ringLen = Math.Max(16384, NextPow2((int)_sampleRate) * 2);
            lock (_lock)
            {
                _ring = new float[ringLen];
                _write = 0;
                _count = 0;
            }
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
            _capture.StartRecording();
            IsRunning = true;
            LastError = null;
            StatusChanged?.Invoke("Capturing: " + _deviceName);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastError = ex.Message;
            StatusChanged?.Invoke("Capture error: " + ex.Message);
        }
    }

    public void TickWatchdog()
    {
        if (_disposed) return;
        if (_restartRequested || !IsRunning)
        {
            _restartRequested = false;
            Start();
        }
    }

    /// <summary>Copy the most recent <paramref name="count"/> mono samples into <paramref name="dest"/>.</summary>
    public bool TryGetMono(float[] dest, int count)
    {
        lock (_lock)
        {
            if (_count < count) return false;
            int len = _ring.Length;
            int start = (_write - count + len) % len;
            for (int i = 0; i < count; i++)
                dest[i] = _ring[(start + i) % len];
            return true;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var capture = _capture;
        if (capture is null) return;
        var format = capture.WaveFormat;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat || format.BitsPerSample == 32)
        {
            int floats = e.BytesRecorded / 4;
            var src = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, floats * 4));
            PushMixed(src, _channels);
        }
        else if (format.BitsPerSample == 16)
        {
            var src = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
            PushMixed16(src, _channels);
        }
    }

    private void PushMixed(ReadOnlySpan<float> src, int channels)
    {
        int frames = src.Length / Math.Max(1, channels);
        lock (_lock)
        {
            int len = _ring.Length;
            for (int i = 0; i < frames; i++)
            {
                float acc = 0f;
                int off = i * channels;
                for (int c = 0; c < channels; c++) acc += src[off + c];
                _ring[_write] = acc / channels;
                _write = (_write + 1) % len;
                if (_count < len) _count++;
            }
        }
    }

    private void PushMixed16(ReadOnlySpan<short> src, int channels)
    {
        int frames = src.Length / Math.Max(1, channels);
        const float scale = 1f / 32768f;
        lock (_lock)
        {
            int len = _ring.Length;
            for (int i = 0; i < frames; i++)
            {
                float acc = 0f;
                int off = i * channels;
                for (int c = 0; c < channels; c++) acc += src[off + c] * scale;
                _ring[_write] = acc / channels;
                _write = (_write + 1) % len;
                if (_count < len) _count++;
            }
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        IsRunning = false;
        if (e.Exception is not null)
        {
            LastError = e.Exception.Message;
            StatusChanged?.Invoke("Capture stopped: " + e.Exception.Message);
        }
        _restartRequested = true;
    }

    private void StopCapture()
    {
        if (_capture is null) return;
        try
        {
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnStopped;
            if (_capture.CaptureState != CaptureState.Stopped)
                _capture.StopRecording();
        }
        catch { /* ignore */ }
        try { _capture.Dispose(); } catch { /* ignore */ }
        _capture = null;
        IsRunning = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
        if (_enumerator is not null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { }
            _enumerator.Dispose();
            _enumerator = null;
        }
    }

    private static int NextPow2(int v)
    {
        int p = 1;
        while (p < v) p <<= 1;
        return p;
    }

    private sealed class NotificationClient : IMMNotificationClient
    {
        public event Action? DefaultChanged;
        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId)
        {
            if (dataFlow == DataFlow.Render) DefaultChanged?.Invoke();
        }
        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
}
