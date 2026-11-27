using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace AudioTaskbar;

/// <summary>Semantic version (major.minor.patch[-prerelease][+build]); "v" prefix allowed.</summary>
internal readonly record struct SemVer(int Major, int Minor, int Patch, string Pre) : IComparable<SemVer>
{
    public static bool TryParse(string? text, out SemVer v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string pre = "";
        int dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; if (pre.Length == 0) return false; }
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4) return false;
        var nums = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], out int n)) return false;
            if (i < 3) nums[i] = n; // a 4th (assembly revision) part is ignored
        }
        v = new SemVer(nums[0], nums[1], nums[2], pre);
        return true;
    }

    public int CompareTo(SemVer o)
    {
        int c = Major.CompareTo(o.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(o.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(o.Patch);
        if (c != 0) return c;
        if (Pre.Length == 0 && o.Pre.Length == 0) return 0;
        if (Pre.Length == 0) return 1;   // 1.0.0 > 1.0.0-beta
        if (o.Pre.Length == 0) return -1;
        var a = Pre.Split('.'); var b = o.Pre.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = int.TryParse(a[i], out int ai), bn = int.TryParse(b[i], out int bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");
}

internal sealed class UpdateInfo
{
    public SemVer Version { get; init; }
    public string SetupUrl { get; init; } = "";
    public string SetupName { get; init; } = "";
    public string? Sha256 { get; init; }
    public string Notes { get; init; } = "";
    public string PageUrl { get; init; } = "";
}

internal enum UpdateStatus { UpToDate, UpdateAvailable, NotFound, Forbidden, NoAsset, Offline, Error, Disabled }

internal sealed class UpdateCheckResult
{
    public UpdateStatus Status { get; init; }
    public UpdateInfo? Info { get; init; }
    public string Message { get; init; } = "";
    public DateTime CheckedAt { get; init; } = DateTime.Now;
}

internal sealed class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message) { }
}

/// <summary>
/// v5.3 auto-update: GitHub Releases (public repo) or a static latest.json feed →
/// download AudioTaskbar-Setup-x.y.z.exe → mandatory SHA-256 check → silent NSIS upgrade
/// (the installer stops the app cleanly via Local\AudioTaskbar.Cmd.Exit and relaunches it).
/// </summary>
internal sealed class AutoUpdater : IDisposable
{
    public const string ChecksumAssetName = "SHA256SUMS.txt";
    public const string ManifestAssetName = "latest.json";
    private readonly HttpClient _http;
    private readonly string _updatesDir;

    public SemVer Current { get; }
    public UpdateCheckResult? LastResult { get; private set; }

    public AutoUpdater(string currentVersion, HttpMessageHandler? handler = null, string? updatesDir = null)
    {
        Current = SemVer.TryParse(currentVersion, out var v) ? v : new SemVer(0, 0, 0, "");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AudioTaskbar-Updater/" + Current);
        _updatesDir = updatesDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioTaskbar", "updates");
    }

    public static string AppVersion()
    {
        var info = typeof(AutoUpdater).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        if (SemVer.TryParse(info, out var v)) return v.ToString();
        var asm = typeof(AutoUpdater).Assembly.GetName().Version;
        return asm is null ? "0.0.0" : $"{asm.Major}.{asm.Minor}.{asm.Build}";
    }

    public async Task<UpdateCheckResult> CheckAsync(AppConfig cfg, CancellationToken token = default)
    {
        UpdateCheckResult r;
        try
        {
            r = string.IsNullOrWhiteSpace(cfg.UpdateFeedUrl)
                ? await CheckGitHubAsync(cfg.UpdateRepo, token).ConfigureAwait(false)
                : await CheckFeedAsync(cfg.UpdateFeedUrl, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) { r = new UpdateCheckResult { Status = UpdateStatus.Offline, Message = "Không kết nối được máy chủ cập nhật: " + ex.Message }; }
        catch (TaskCanceledException) { r = new UpdateCheckResult { Status = UpdateStatus.Offline, Message = "Hết thời gian chờ máy chủ cập nhật." }; }
        catch (JsonException ex) { r = new UpdateCheckResult { Status = UpdateStatus.Error, Message = "Dữ liệu cập nhật không hợp lệ: " + ex.Message }; }
        catch (Exception ex) { r = new UpdateCheckResult { Status = UpdateStatus.Error, Message = "Lỗi kiểm tra cập nhật: " + ex.Message }; }
        LastResult = r;
        return r;
    }

    private UpdateCheckResult Compare(UpdateInfo info)
    {
        if (info.Version > Current)
            return new UpdateCheckResult { Status = UpdateStatus.UpdateAvailable, Info = info, Message = "Có bản mới v" + info.Version };
        return new UpdateCheckResult { Status = UpdateStatus.UpToDate, Info = info, Message = "Đã mới nhất (v" + Current + ")" };
    }

    private static UpdateCheckResult? HttpFailure(HttpStatusCode code, string what) => code switch
    {
        HttpStatusCode.NotFound => new UpdateCheckResult
        {
            Status = UpdateStatus.NotFound,
            Message = "Không tìm thấy bản phát hành (" + what + "). Repo có thể đang Private hoặc chưa có Release — người dùng không đăng nhập GitHub sẽ không tải được."
        },
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or (HttpStatusCode)429 => new UpdateCheckResult
        {
            Status = UpdateStatus.Forbidden,
            Message = "Máy chủ từ chối truy cập (" + (int)code + "): giới hạn lượt gọi GitHub hoặc repo Private. Thử lại sau."
        },
        _ when (int)code >= 400 => new UpdateCheckResult { Status = UpdateStatus.Error, Message = "Máy chủ cập nhật trả lỗi HTTP " + (int)code + "." },
        _ => null
    };

    internal async Task<UpdateCheckResult> CheckGitHubAsync(string repo, CancellationToken token)
    {
        var url = "https://api.github.com/repos/" + repo + "/releases/latest";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (HttpFailure(resp.StatusCode, repo) is { } fail) return fail;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (!SemVer.TryParse(tag, out var ver))
            return new UpdateCheckResult { Status = UpdateStatus.Error, Message = "Tag phát hành không đúng dạng vX.Y.Z: " + tag };
        var assets = new List<(string Name, string Url)>();
        if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
            {
                var n = a.TryGetProperty("name", out var an) ? an.GetString() : null;
                var u = a.TryGetProperty("browser_download_url", out var au) ? au.GetString() : null;
                if (!string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(u)) assets.Add((n!, u!));
            }
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

        // Preferred: latest.json manifest asset (version + downloadUrl + sha256).
        var manifest = assets.FirstOrDefault(a => a.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));
        string? sha = null, setupUrl = null, setupName = null;
        if (manifest.Url is not null)
        {
            try
            {
                var m = ParseManifest(await GetStringAsync(manifest.Url, token).ConfigureAwait(false));
                if (m.Version.CompareTo(ver) == 0) { sha = m.Sha256; setupUrl = m.SetupUrl; setupName = m.SetupName; }
            }
            catch (Exception) { /* fall back to assets + SHA256SUMS */ }
        }
        var setup = PickSetupAsset(assets, ver);
        if (setupUrl is null && setup is { } s0) { setupUrl = s0.Url; setupName = s0.Name; }
        if (setupUrl is null || setupName is null)
            return new UpdateCheckResult
            {
                Status = ver > Current ? UpdateStatus.NoAsset : UpdateStatus.UpToDate,
                Message = ver > Current ? "Release v" + ver + " chưa có file AudioTaskbar-Setup-*.exe." : "Đã mới nhất (v" + Current + ")"
            };
        if (sha is null)
        {
            var sums = assets.FirstOrDefault(a => a.Name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
            if (sums.Url is not null)
            {
                sha = ParseSha256Sums(await GetStringAsync(sums.Url, token).ConfigureAwait(false), setupName);
                if (sha is null)
                    return new UpdateCheckResult { Status = UpdateStatus.Error, Message = "SHA256SUMS.txt không có dòng cho " + setupName + " — từ chối cập nhật." };
            }
        }
        return Compare(new UpdateInfo { Version = ver, SetupUrl = setupUrl, SetupName = setupName, Sha256 = sha, Notes = notes, PageUrl = page });
    }

    internal async Task<UpdateCheckResult> CheckFeedAsync(string feedUrl, CancellationToken token)
    {
        using var resp = await _http.GetAsync(feedUrl, token).ConfigureAwait(false);
        if (HttpFailure(resp.StatusCode, feedUrl) is { } fail) return fail;
        var info = ParseManifest(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false), feedUrl);
        if (string.IsNullOrEmpty(info.SetupUrl))
            return new UpdateCheckResult { Status = UpdateStatus.NoAsset, Message = "latest.json thiếu downloadUrl." };
        return Compare(info);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken token)
    {
        using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }

    /// <summary>latest.json: { "version": "5.3.0", "downloadUrl": "...", "sha256": "...", "fileName": "...", "notes": "..." }</summary>
    internal static UpdateInfo ParseManifest(string json, string? baseUrl = null)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string? Str(params string[] names)
        {
            foreach (var n in names)
                if (r.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String) return e.GetString();
            return null;
        }
        if (!SemVer.TryParse(Str("version", "tag", "tag_name"), out var ver))
            throw new JsonException("latest.json: thiếu hoặc sai 'version'.");
        var url = Str("downloadUrl", "url", "setupUrl") ?? "";
        if (url.Length > 0 && baseUrl is not null && !Uri.IsWellFormedUriString(url, UriKind.Absolute)
            && Uri.TryCreate(new Uri(baseUrl), url, out var abs)) url = abs.ToString();
        var name = Str("fileName", "name")
                   ?? (Uri.TryCreate(url, UriKind.Absolute, out var u) ? Path.GetFileName(u.LocalPath) : Path.GetFileName(url));
        var sha = Str("sha256", "checksum");
        if (sha is not null && !IsSha256Hex(sha)) throw new JsonException("latest.json: sha256 không hợp lệ.");
        return new UpdateInfo { Version = ver, SetupUrl = url, SetupName = name, Sha256 = sha?.ToLowerInvariant(), Notes = Str("notes", "body") ?? "", PageUrl = Str("pageUrl", "html_url") ?? "" };
    }

    internal static (string Name, string Url)? PickSetupAsset(IReadOnlyList<(string Name, string Url)> assets, SemVer ver)
    {
        var exact = assets.FirstOrDefault(a => a.Name.Equals($"AudioTaskbar-Setup-{ver}.exe", StringComparison.OrdinalIgnoreCase));
        if (exact.Url is not null) return exact;
        var any = assets.FirstOrDefault(a => a.Name.StartsWith("AudioTaskbar-Setup-", StringComparison.OrdinalIgnoreCase)
                                             && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        return any.Url is not null ? any : null;
    }

    /// <summary>Parse "hash␠␠filename" / "hash *filename" lines (sha256sum format).</summary>
    internal static string? ParseSha256Sums(string text, string fileName)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66) continue;
            var hash = line[..64];
            if (!IsSha256Hex(hash)) continue;
            var name = line[64..].Trim().TrimStart('*').Trim();
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(name).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return hash.ToLowerInvariant();
        }
        return null;
    }

    internal static bool IsSha256Hex(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    public static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static bool VerifySha256(string path, string? expected)
        => expected is not null && IsSha256Hex(expected) && string.Equals(ComputeSha256(path), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Download the installer to %LOCALAPPDATA%\AudioTaskbar\updates and verify it. A checksum is
    /// mandatory when <paramref name="requireChecksum"/> (silent auto-install); a mismatch always
    /// deletes the file and throws <see cref="UpdateVerificationException"/>.
    /// </summary>
    public async Task<string> DownloadAndVerifyAsync(UpdateInfo info, bool requireChecksum, IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(info.SetupUrl)) throw new UpdateVerificationException("Thiếu đường dẫn tải bộ cài.");
        if (requireChecksum && info.Sha256 is null) throw new UpdateVerificationException("Release không có SHA-256 — từ chối tự cài đặt.");
        var name = string.IsNullOrWhiteSpace(info.SetupName) ? $"AudioTaskbar-Setup-{info.Version}.exe" : Path.GetFileName(info.SetupName);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new UpdateVerificationException("Tên bộ cài không hợp lệ: " + name);
        Directory.CreateDirectory(_updatesDir);
        var final = Path.Combine(_updatesDir, name);
        var part = final + ".part";
        try
        {
            using (var resp = await _http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if (HttpFailure(resp.StatusCode, info.SetupUrl) is { } fail) throw new HttpRequestException(fail.Message);
                long total = resp.Content.Headers.ContentLength ?? -1;
                await using var src = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var dst = File.Create(part);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, token).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), token).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report(Math.Clamp(done / (double)total, 0, 1));
                }
            }
            if (info.Sha256 is not null && !VerifySha256(part, info.Sha256))
                throw new UpdateVerificationException("SHA-256 không khớp — file tải về bị hỏng hoặc bị thay đổi. Đã hủy cập nhật.");
            File.Move(part, final, overwrite: true);
            progress?.Report(1);
            return final;
        }
        catch
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            throw;
        }
    }

    /// <summary>Install folder of the per-user NSIS install (HKCU Uninstall key), or null.</summary>
    public static string? InstalledLocation()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioTaskbar");
            return k?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// NSIS arguments for a silent upgrade. Portable copies (not in the registered install folder)
    /// are upgraded in place with /D= (must be last and unquoted).
    /// </summary>
    internal static string BuildInstallerArguments(string currentExeDir, string? installedLocation)
    {
        var args = "/S /UPDATED /NODESKTOP /NOSTARTMENU";
        string cur = Path.GetFullPath(currentExeDir).TrimEnd('\\', '/');
        string? inst = string.IsNullOrWhiteSpace(installedLocation) ? null : Path.GetFullPath(installedLocation).TrimEnd('\\', '/');
        if (inst is null || !string.Equals(cur, inst, StringComparison.OrdinalIgnoreCase))
            args += " /D=" + cur;
        return args;
    }

    /// <summary>True for a developer checkout (…\release next to .git): never self-install there.</summary>
    internal static bool IsDevCheckout(string exeDir)
    {
        try
        {
            var parent = Directory.GetParent(Path.GetFullPath(exeDir).TrimEnd('\\', '/'));
            return parent is not null && (Directory.Exists(Path.Combine(parent.FullName, ".git")) || File.Exists(Path.Combine(parent.FullName, ".git")));
        }
        catch { return false; }
    }

    public static Process? LaunchInstaller(string setupPath, string arguments)
        => Process.Start(new ProcessStartInfo { FileName = setupPath, Arguments = arguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(setupPath)! });

    public void Dispose() => _http.Dispose();
}

/// <summary>HKCU\...\Run autostart (with --minimized), like UniKey's "Khởi động cùng Windows".</summary>
internal static class AutoStart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "AudioTaskbar";

    public static string Command(string exePath) => "\"" + exePath + "\" --minimized";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    public static string? CurrentValue()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(ValueName) as string; }
        catch { return null; }
    }

    public static void Set(bool enabled, string exePath)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) k.SetValue(ValueName, Command(exePath), RegistryValueKind.String);
        else if (k.GetValue(ValueName) is not null) k.DeleteValue(ValueName, throwOnMissingValue: false);
        if (!enabled)
        {
            // legacy scripts\enable-autostart.ps1 shortcut
            try
            {
                var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Audio Taskbar.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
        }
    }
}
