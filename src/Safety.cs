namespace AudioTaskbar;

/// <summary>
/// v5.3: small pure-logic pieces pulled out of the UI / IO classes so they can be unit-tested
/// (tests/AudioTaskbar.Tests). Behaviour is unchanged from v5.2.
/// </summary>
internal sealed class LocationStabilizer
{
    public const double JumpKm = 80;
    private (double Lat, double Lon, string Name, string Country)? _stable;
    private (double Lat, double Lon, string Name, string Country)? _pending;
    private int _pendingCount;

    public (double Lat, double Lon, string Name, string Country)? Stable => _stable;

    /// <summary>
    /// IP geolocation of Vietnamese ISPs hops between provinces. Keep the last stable place unless a
    /// new place more than <see cref="JumpKm"/> away is confirmed by two consecutive lookups.
    /// Returns the place to use and whether the stable place was kept instead of the new one.
    /// </summary>
    public ((double Lat, double Lon, string Name, string Country) Place, bool KeptStable) Apply(
        double lat, double lon, string name, string country,
        (double Lat, double Lon, string Name, string Country)? seed = null)
    {
        var stable = _stable ?? seed;
        if (stable is { } st && DistanceKm(st.Lat, st.Lon, lat, lon) > JumpKm)
        {
            if (_pending is { } pd && DistanceKm(pd.Lat, pd.Lon, lat, lon) <= JumpKm) _pendingCount++;
            else { _pending = (lat, lon, name, country); _pendingCount = 1; }
            if (_pendingCount < 2) return (st, true);
            _stable = (lat, lon, name, country);
            _pending = null; _pendingCount = 0;
            return (_stable.Value, false);
        }
        _stable = (lat, lon, name, country);
        _pending = null; _pendingCount = 0;
        return (_stable.Value, false);
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        if (!double.IsFinite(lat1) || !double.IsFinite(lon1) || !double.IsFinite(lat2) || !double.IsFinite(lon2)) return double.MaxValue;
        double r = 6371, dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

internal enum TapAttachDecision { Reuse, Inject, RefuseLegacy }

internal static class TapSafety
{
    /// <summary>
    /// Double-TAP guard. A DLL cannot be unloaded from Explorer and a second copy of an old
    /// (v1/v2) TAP crashes it. Reuse a live v3 service; inject only when no TaskbarTap module
    /// is loaded or the loaded copy is a teardown-safe v3 whose site was released.
    /// </summary>
    public static TapAttachDecision Decide(bool v3ServiceAlive, bool v3Loaded, bool explorerHasTapModule)
    {
        if (v3ServiceAlive) return TapAttachDecision.Reuse;
        if (explorerHasTapModule && !v3Loaded) return TapAttachDecision.RefuseLegacy;
        return TapAttachDecision.Inject;
    }
}

/// <summary>
/// Smart App Control decision for TaskbarTap.dll. An unsigned DLL must never be injected
/// into explorer.exe when SAC is on. A signed (or unknown-signature) DLL may be tried;
/// <see cref="TaskbarTapHost"/> falls back to UIA if Code Integrity then blocks the load.
/// </summary>
internal enum SacTapDecision { Proceed, SkipUnsigned, TrySigned }

internal static class SacTapPolicy
{
    public const int ErrorInvalidImageHash = 577;
    public const int ErrorAccessDenied = 5;
    public const int HresultInvalidImageHash = unchecked((int)0x80070241);
    public const int HresultAccessDenied = unchecked((int)0x80070005);

    /// <summary>
    /// <paramref name="hasAuthenticodeSignature"/> is <c>true</c> / <c>false</c> when the
    /// check succeeded, or <c>null</c> when the file could not be inspected (try-load).
    /// </summary>
    public static SacTapDecision Decide(bool sacEnabled, bool? hasAuthenticodeSignature)
    {
        if (!sacEnabled) return SacTapDecision.Proceed;
        if (hasAuthenticodeSignature == false) return SacTapDecision.SkipUnsigned;
        return SacTapDecision.TrySigned;
    }

    public static bool ShouldSkipUnsignedTap(bool sacEnabled, bool? hasAuthenticodeSignature)
        => Decide(sacEnabled, hasAuthenticodeSignature) == SacTapDecision.SkipUnsigned;

    /// <summary>
    /// HRESULT or Win32 codes that mean the image failed signature / Code Integrity checks
    /// (ERROR_INVALID_IMAGE_HASH 577 / 0x80070241, ERROR_ACCESS_DENIED 5 / 0x80070005, common trust HRESULTs).
    /// </summary>
    public static bool IsIntegrityOrTrustFailure(int hrOrWin32)
    {
        if (hrOrWin32 is HresultInvalidImageHash or HresultAccessDenied or ErrorInvalidImageHash or ErrorAccessDenied)
            return true;
        uint u = unchecked((uint)hrOrWin32);
        if ((u & 0xFFFF0000) == 0x80070000)
        {
            uint win32 = u & 0xFFFF;
            if (win32 is ErrorInvalidImageHash or ErrorAccessDenied) return true;
        }
        // TRUST_E_NOSIGNATURE, CERT_E_UNTRUSTEDROOT, CERT_E_CHAINING, CERT_E_WRONG_USAGE, TRUST_E_FAIL, TRUST_E_SUBJECT_NOT_TRUSTED
        if (u is 0x800B0100 or 0x800B0109 or 0x800B010A or 0x800B0111 or 0x800B010B or 0x800B0104) return true;
        return false;
    }
}

/// <summary>Lightweight Authenticode presence check (does not require a full WinVerifyTrust chain).</summary>
internal static class TapSignature
{
    public static bool? HasAuthenticodeSignature(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
            return !string.IsNullOrEmpty(cert.Subject);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Crash-loop circuit breaker: if Explorer exits within <see cref="Window"/> of a TAP attach,
/// assume the TAP was involved and never attach again for this session.
/// </summary>
internal sealed class CrashLoopBreaker
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(90);
    private DateTime _attachedAt = DateTime.MinValue;
    public bool Tripped { get; private set; }
    public string? Reason { get; private set; }

    public void RecordAttach(DateTime now) => _attachedAt = now;

    /// <summary>Call when the attached Explorer went away. Returns true when the breaker trips.</summary>
    public bool OnExplorerExited(DateTime now)
    {
        if (_attachedAt == DateTime.MinValue) return false;
        var since = now - _attachedAt;
        if (since >= TimeSpan.Zero && since < Window)
        {
            Tripped = true;
            Reason = "explorer-exited-soon-after-attach";
            return true;
        }
        return false;
    }

    public bool AllowsAttach => !Tripped;
}

internal static class SceneMath
{
    /// <summary>Train progress 0..1 (exclusive) for a crossing period; never NaN / Infinity.</summary>
    public static double TrainProgress(double phase0, double timeSeconds, double periodSeconds)
    {
        if (!double.IsFinite(periodSeconds) || periodSeconds < 1) periodSeconds = 1;
        if (!double.IsFinite(phase0)) phase0 = 0;
        if (!double.IsFinite(timeSeconds)) timeSeconds = 0;
        double p = phase0 + timeSeconds / periodSeconds;
        p -= Math.Floor(p);
        if (!double.IsFinite(p) || p < 0 || p >= 1) p = 0;
        return p;
    }
}

internal static class LayoutMath
{
    /// <summary>Per-column visibility of the overlay: 0 inside each cutout (+pad), smooth feather outside.</summary>
    public static float[] ColumnVisibility(IEnumerable<Rectangle> cutouts, Rectangle bounds, int width, int pad, int feather)
    {
        width = Math.Max(0, width);
        var vis = new float[width];
        Array.Fill(vis, 1f);
        foreach (var c in cutouts)
        {
            int l = c.Left - bounds.X - pad;
            int r = c.Right - bounds.X + pad;
            int a = Math.Max(0, l - feather), b = Math.Min(width, r + feather);
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
                if (v < vis[x]) vis[x] = v;
            }
        }
        return vis;
    }

    /// <summary>
    /// Weather HUD placement inside free spans (already guarded away from icons).
    /// Returns an empty rect when nothing fits, so the HUD is hidden instead of covering icons.
    /// </summary>
    public static RectangleF PlaceBadge(IReadOnlyList<(int L, int R)> spans, SizeF size, float margin, int barHeight, bool right)
    {
        if (size.Width <= 0 || size.Height <= 0) return RectangleF.Empty;
        var fits = spans.Where(sp => sp.R - sp.L >= size.Width + 2 * margin).ToList();
        if (fits.Count == 0) return RectangleF.Empty;
        var span = right ? fits[^1] : fits[0];
        float bx = right ? span.R - margin - size.Width : span.L + margin;
        return new RectangleF(bx, (barHeight - size.Height) / 2f, size.Width, size.Height);
    }
}
