#!/usr/bin/env python3
"""Port of Analyzer FFT + color mapping to verify math before the Windows build."""
from __future__ import annotations
import math
import sys

def fft(re, im):
    n = len(re)
    j = 0
    for i in range(1, n):
        bit = n >> 1
        while j & bit:
            j ^= bit
            bit >>= 1
        j ^= bit
        if i < j:
            re[i], re[j] = re[j], re[i]
            im[i], im[j] = im[j], im[i]
    length = 2
    while length <= n:
        ang = -2.0 * math.pi / length
        wlen_re = math.cos(ang)
        wlen_im = math.sin(ang)
        i = 0
        while i < n:
            w_re, w_im = 1.0, 0.0
            half = length >> 1
            for k in range(half):
                u = i + k
                v = u + half
                t_re = w_re * re[v] - w_im * im[v]
                t_im = w_re * im[v] + w_im * re[v]
                re[v] = re[u] - t_re
                im[v] = im[u] - t_im
                re[u] += t_re
                im[u] += t_im
                nw_re = w_re * wlen_re - w_im * wlen_im
                w_im = w_re * wlen_im + w_im * wlen_re
                w_re = nw_re
            i += length
        length <<= 1


def parse_color(text, fallback):
    s = text.strip().lstrip("#")
    if len(s) == 6:
        v = int(s, 16)
        return (255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)
    if len(s) == 8:
        v = int(s, 16)
        return ((v >> 24) & 0xFF, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)
    return fallback


def lerp_color(a, c, t):
    t = min(1.0, max(0.0, t))
    return (
        255,
        int(a[1] + (c[1] - a[1]) * t),
        int(a[2] + (c[2] - a[2]) * t),
        int(a[3] + (c[3] - a[3]) * t),
    )


def map_background(energy, bass, beat, opacity, color_low, color_mid, color_high):
    t = min(1.0, max(0.0, energy * 0.55 + bass * 0.45))
    t = min(1.0, max(0.0, t + beat * 0.25))
    if t < 0.5:
        c = lerp_color(color_low, color_mid, t * 2)
    else:
        c = lerp_color(color_mid, color_high, (t - 0.5) * 2)
    boost = 1.0 + beat * 0.35
    r = min(255, max(0, int(c[1] * boost)))
    g = min(255, max(0, int(c[2] * boost)))
    b = min(255, max(0, int(c[3] * boost)))
    op = opacity * (0.35 + 0.65 * t)
    a = min(255, max(0, int(op * 255)))
    return r, g, b, a, t


def test_fft_sine_bin():
    n = 2048
    sr = 48000.0
    freq = 1000.0
    re = [math.sin(2 * math.pi * freq * i / sr) * (0.5 * (1 - math.cos(2 * math.pi * i / (n - 1)))) for i in range(n)]
    im = [0.0] * n
    fft(re, im)
    mags = [math.sqrt(re[i] ** 2 + im[i] ** 2) for i in range(n // 2)]
    peak = max(range(1, n // 2), key=lambda i: mags[i])
    hz = peak * sr / n
    assert abs(hz - freq) <= (sr / n) * 1.5, f"peak at {hz:.1f} Hz, expected ~{freq}"
    # DC should be tiny vs peak
    assert mags[0] < mags[peak] * 0.05, f"DC {mags[0]} vs peak {mags[peak]}"
    print(f"OK  FFT sine peak {hz:.2f} Hz (bin {peak}), mag={mags[peak]:.1f}")


def test_fft_silence():
    n = 1024
    re = [0.0] * n
    im = [0.0] * n
    fft(re, im)
    total = sum(math.sqrt(re[i] ** 2 + im[i] ** 2) for i in range(n))
    assert total < 1e-6, total
    print("OK  FFT silence is zero")


def test_fft_matches_numpy():
    try:
        import numpy as np
    except ImportError:
        print("SKIP numpy comparison (numpy not installed)")
        return
    rng = np.random.default_rng(0)
    n = 512
    sig = rng.standard_normal(n).astype(np.float64)
    re = sig.tolist()
    im = [0.0] * n
    fft(re, im)
    ref = np.fft.fft(sig)
    err = max(abs(complex(re[i], im[i]) - ref[i]) for i in range(n))
    # Our implementation is float32-ish in C#; here we use Python float (64).
    # Should still be very close for n=512.
    assert err < 1e-8, f"max err {err}"
    print(f"OK  FFT matches numpy, max err={err:.2e}")


def test_parse_and_lerp():
    c = parse_color("#0B1C4A", (255, 0, 0, 0))
    assert c == (255, 11, 28, 74), c
    c8 = parse_color("#80FF3D71", (255, 0, 0, 0))
    assert c8 == (128, 255, 61, 113), c8
    mid = lerp_color((255, 0, 0, 0), (255, 100, 0, 0), 0.5)
    assert mid[1] == 50, mid
    print("OK  color parse + lerp")


def test_color_mapping_endpoints():
    low = parse_color("#0B1C4A", (255, 0, 0, 0))
    mid = parse_color("#00B8D4", (255, 0, 0, 0))
    high = parse_color("#FF3D71", (255, 0, 0, 0))
    r, g, b, a, t = map_background(0, 0, 0, 0.45, low, mid, high)
    assert t == 0.0
    assert (r, g, b) == (low[1], low[2], low[3])
    assert a == int(0.45 * 0.35 * 255)
    r2, g2, b2, a2, t2 = map_background(1, 1, 0, 0.45, low, mid, high)
    assert t2 == 1.0
    assert (r2, g2, b2) == (high[1], high[2], high[3])
    # beat brightens and raises t
    r3, g3, b3, a3, t3 = map_background(0.5, 0.5, 1.0, 0.45, low, mid, high)
    assert t3 >= 0.5
    assert a3 >= a  # at least not dimmer than silence
    print(f"OK  color map silence=({r},{g},{b},{a}) loud=({r2},{g2},{b2},{a2}) beat t={t3:.2f}")


def test_log_bins_cover_range():
    fft_size = 2048
    sr = 48000
    bar_count = 64
    min_f, max_f = 30.0, 16000.0
    hz = sr / fft_size
    nyquist = fft_size // 2
    starts, ends = [], []
    for b in range(bar_count):
        t0 = b / bar_count
        t1 = (b + 1) / bar_count
        f0 = min_f * (max_f / min_f) ** t0
        f1 = min_f * (max_f / min_f) ** t1
        i0 = max(1, min(nyquist - 1, math.floor(f0 / hz)))
        i1 = max(i0 + 1, min(nyquist, math.ceil(f1 / hz)))
        starts.append(i0)
        ends.append(i1)
    assert starts[0] < ends[0] <= starts[-1] < ends[-1]
    assert ends[-1] <= nyquist
    # coverage should be contiguous enough (small overlaps/gaps ok at int rounding)
    print(f"OK  log bins {starts[0]}-{ends[0]} ... {starts[-1]}-{ends[-1]} (nyquist {nyquist})")


def test_weather_kind_map():
    def kind(code, is_day):
        if code in (95, 96, 99):
            return "storm"
        if code in (71, 73, 75, 77, 85, 86):
            return "snow"
        if code in (51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 80, 81, 82):
            return "rain"
        if code in (45, 48):
            return "fog"
        if code in (1, 2, 3):
            return "cloudy"
        if not is_day:
            return "night"
        return "clear"
    assert kind(0, True) == "clear"
    assert kind(0, False) == "night"
    assert kind(61, True) == "rain"
    assert kind(95, True) == "storm"
    assert kind(3, True) == "cloudy"
    print("OK  weather kind mapping")


def test_temp_units():
    def fmt(c, fahrenheit):
        return f"{round(c * 9 / 5 + 32)}°F" if fahrenheit else f"{round(c)}°C"
    assert fmt(30, False) == "30°C"
    assert fmt(30, True) == "86°F"
    print("OK  temperature formatting")


def test_icon_safe_visibility():
    # DistanceFade replica
    def fade(client_x, left, right, pad, min_v=0.08, edge=36):
        left -= pad
        right += pad
        if left <= client_x <= right:
            return min_v
        if left - edge < client_x < left:
            return min_v + (1 - min_v) * ((left - client_x) / edge)
        if right < client_x < right + edge:
            return min_v + (1 - min_v) * ((client_x - right) / edge)
        return 1.0
    # icons 500-1000, pad 28
    assert fade(0, 500, 1000, 28) == 1.0
    assert fade(700, 500, 1000, 28) == 0.08
    mid = fade(500 - 28 - 18, 500, 1000, 28)
    assert 0.08 < mid < 1.0
    print(f"OK  icon-safe fade (edge sample {mid:.2f})")

def main():
    tests = [
        test_fft_silence,
        test_fft_sine_bin,
        test_fft_matches_numpy,
        test_parse_and_lerp,
        test_color_mapping_endpoints,
        test_log_bins_cover_range,
        test_weather_kind_map,
        test_temp_units,
        test_icon_safe_visibility,
    ]
    failed = 0
    for fn in tests:
        try:
            fn()
        except Exception as e:
            failed += 1
            print(f"FAIL {fn.__name__}: {e}")
    if failed:
        print(f"\n{failed} test(s) failed")
        sys.exit(1)
    print("\nAll analyzer tests passed.")


if __name__ == "__main__":
    main()
