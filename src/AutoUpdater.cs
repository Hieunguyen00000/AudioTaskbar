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
    /// <summary>Download URL of the package (portable AudioTaskbar.exe, or a Setup exe).</summary>
    public string SetupUrl { get; init; } = "";
    /// <summary>Package file name, e.g. AudioTaskbar.exe or AudioTaskbar-Setup-5.4.0.exe.</summary>
    public string SetupName { get; init; } = "";
    public string? Sha256 { get; init; }
    public string Notes { get; init; } = "";
    public string PageUrl { get; init; } = "";
    /// <summary>True for the single self-contained AudioTaskbar.exe (in-place self-update, no installer).</summary>
    public bool IsPortable => !SetupName.StartsWith("AudioTaskbar-Setup-", StringComparison.OrdinalIgnoreCase);
}

internal enum UpdateStatus { UpToDate, UpdateAvailable, NotFound, Forbidden, NoAsset, Offline, Error, Disabled }

internal sealed class UpdateCheckResult
{
    public UpdateStatus Status { get; init; }
    public UpdateInfo? Info { get; init; }
    /// <summary>Short, user-facing Vietnamese text (never contains repo names or URLs).</summary>
    public string Message { get; init; } = "";
    /// <summary>Technical detail for app.log only.</summary>
    public string Detail { get; init; } = "";
    public DateTime CheckedAt { get; init; } = DateTime.Now;
}

internal sealed class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message) { }
}

/// <summary>
/// v5.3 auto-update: GitHub Releases (public repo) or a static latest.json feed →
/// download the single portable AudioTaskbar.exe (or, as a fallback, AudioTaskbar-Setup-x.y.z.exe) →
/// mandatory SHA-256 check → in-place atomic swap by apply-update.cmd after a clean exit → relaunch --minimized.
/// </summary>
internal sealed class AutoUpdater : IDisposable
{
    public const string ChecksumAssetName = "SHA256SUMS.txt";
    public const string ManifestAssetName = "latest.json";
    public const string PortableAssetName = "AudioTaskbar.exe";

    public const string MsgNoRelease = "Hiện chưa có bản phát hành mới.";
    public const string MsgCannotCheck = "Không thể kiểm tra cập nhật lúc này.";
    public const string MsgInvalid = "Bản cập nhật không hợp lệ nên đã được bỏ qua.";

    private readonly HttpClient _http;
    private readonly string _updatesDir;

    public SemVer Current { get; }
    public UpdateCheckResult? LastResult { get; private set; }
    public string UpdatesDir => _updatesDir;

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
        catch (HttpRequestException ex) { r = Fail(UpdateStatus.Offline, "network: " + ex.Message); }
        catch (TaskCanceledException) { r = Fail(UpdateStatus.Offline, "timeout"); }
        catch (JsonException ex) { r = Fail(UpdateStatus.Error, "invalid release data: " + ex.Message); }
        catch (Exception ex) { r = Fail(UpdateStatus.Error, ex.GetType().Name + ": " + ex.Message); }
        LastResult = r;
        return r;
    }

    private static UpdateCheckResult Fail(UpdateStatus st, string detail) => new()
    {
        Status = st,
        Message = st is UpdateStatus.NotFound or UpdateStatus.NoAsset ? MsgNoRelease : MsgCannotCheck,
        Detail = detail
    };

    private UpdateCheckResult Compare(UpdateInfo info, string source)
    {
        if (info.Version > Current)
            return new UpdateCheckResult { Status = UpdateStatus.UpdateAvailable, Info = info, Message = "Có bản mới v" + info.Version + ".", Detail = source + " -> " + info.SetupName + " v" + info.Version };
        return new UpdateCheckResult { Status = UpdateStatus.UpToDate, Info = info, Message = "Bạn đang dùng bản mới nhất (v" + Current + ").", Detail = source + " latest=v" + info.Version };
    }

    private static UpdateCheckResult? HttpFailure(HttpStatusCode code, string what) => code switch
    {
        HttpStatusCode.NotFound => Fail(UpdateStatus.NotFound, "404 " + what + " (no release yet, or the repo is Private: anonymous users cannot read it)"),
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or (HttpStatusCode)429 => Fail(UpdateStatus.Forbidden, (int)code + " " + what + " (GitHub rate limit or Private repo)"),
        _ when (int)code >= 400 => Fail(UpdateStatus.Error, "HTTP " + (int)code + " " + what),
        _ => null
    };

    internal async Task<UpdateCheckResult> CheckGitHubAsync(string repo, CancellationToken token)
    {
        var url = "https://api.github.com/repos/" + repo + "/releases/latest";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (HttpFailure(resp.StatusCode, url) is { } fail) return fail;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (!SemVer.TryParse(tag, out var ver))
            return Fail(UpdateStatus.Error, "release tag is not vX.Y.Z: " + tag);
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
        string? sha = null, pkgUrl = null, pkgName = null;
        if (manifest.Url is not null)
        {
            try
            {
                var m = ParseManifest(await GetStringAsync(manifest.Url, token).ConfigureAwait(false));
                if (m.Version.CompareTo(ver) == 0 && m.SetupUrl.Length > 0) { sha = m.Sha256; pkgUrl = m.SetupUrl; pkgName = m.SetupName; }
            }
            catch (Exception) { /* fall back to assets + SHA256SUMS */ }
        }
        var pkg = PickAsset(assets, ver);
        if (pkgUrl is null && pkg is { } p0) { pkgUrl = p0.Url; pkgName = p0.Name; }
        if (pkgUrl is null || pkgName is null)
            return ver > Current
                ? Fail(UpdateStatus.NoAsset, "release v" + ver + " has no AudioTaskbar.exe / AudioTaskbar-Setup-*.exe asset")
                : new UpdateCheckResult { Status = UpdateStatus.UpToDate, Message = "Bạn đang dùng bản mới nhất (v" + Current + ").", Detail = "latest=v" + ver };
        if (sha is null)
        {
            var sums = assets.FirstOrDefault(a => a.Name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
            if (sums.Url is not null)
            {
                sha = ParseSha256Sums(await GetStringAsync(sums.Url, token).ConfigureAwait(false), pkgName);
                if (sha is null)
                    return new UpdateCheckResult { Status = UpdateStatus.Error, Message = MsgInvalid, Detail = "SHA256SUMS.txt has no line for " + pkgName + " — refusing update" };
            }
        }
        return Compare(new UpdateInfo { Version = ver, SetupUrl = pkgUrl, SetupName = pkgName, Sha256 = sha, Notes = notes, PageUrl = page }, "github");
    }

    internal async Task<UpdateCheckResult> CheckFeedAsync(string feedUrl, CancellationToken token)
    {
        using var resp = await _http.GetAsync(feedUrl, token).ConfigureAwait(false);
        if (HttpFailure(resp.StatusCode, feedUrl) is { } fail) return fail;
        var info = ParseManifest(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false), feedUrl);
        if (string.IsNullOrEmpty(info.SetupUrl))
            return Fail(UpdateStatus.NoAsset, "latest.json has no downloadUrl");
        return Compare(info, "feed");
    }

    private async Task<string> GetStringAsync(string url, CancellationToken token)
    {
        using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }

    /// <summary>latest.json: { "version": "5.3.0", "fileName": "AudioTaskbar.exe", "downloadUrl": "...", "sha256": "...", "notes": "..." }</summary>
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
            throw new JsonException("latest.json: missing or invalid 'version'.");
        var url = Str("downloadUrl", "url", "setupUrl") ?? "";
        if (url.Length > 0 && baseUrl is not null && !Uri.IsWellFormedUriString(url, UriKind.Absolute)
            && Uri.TryCreate(new Uri(baseUrl), url, out var abs)) url = abs.ToString();
        var name = Str("fileName", "name")
                   ?? (Uri.TryCreate(url, UriKind.Absolute, out var u) ? Path.GetFileName(u.LocalPath) : Path.GetFileName(url));
        var sha = Str("sha256", "checksum");
        if (sha is not null && !IsSha256Hex(sha)) throw new JsonException("latest.json: invalid sha256.");
        return new UpdateInfo { Version = ver, SetupUrl = url, SetupName = name, Sha256 = sha?.ToLowerInvariant(), Notes = Str("notes", "body") ?? "", PageUrl = Str("pageUrl", "html_url") ?? "" };
    }

    /// <summary>Portable AudioTaskbar.exe first (v5.3 primary asset), then a versioned portable exe, then Setup.</summary>
    internal static (string Name, string Url)? PickAsset(IReadOnlyList<(string Name, string Url)> assets, SemVer ver)
    {
        foreach (var want in new[] { PortableAssetName, $"AudioTaskbar-{ver}.exe", $"AudioTaskbar-Setup-{ver}.exe" })
        {
            var hit = assets.FirstOrDefault(a => a.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
            if (hit.Url is not null) return hit;
        }
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
    /// Download the package to %LOCALAPPDATA%\AudioTaskbar\updates and verify it. A checksum is
    /// mandatory when <paramref name="requireChecksum"/> (automatic install); a mismatch always
    /// deletes the file and throws <see cref="UpdateVerificationException"/>.
    /// </summary>
    public async Task<string> DownloadAndVerifyAsync(UpdateInfo info, bool requireChecksum, IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(info.SetupUrl)) throw new UpdateVerificationException(MsgInvalid);
        if (requireChecksum && info.Sha256 is null) throw new UpdateVerificationException("Bản cập nhật chưa có mã kiểm tra SHA-256 nên không được tự cài.");
        var name = string.IsNullOrWhiteSpace(info.SetupName) ? PortableAssetName : Path.GetFileName(info.SetupName.Replace('\\', '/'));
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new UpdateVerificationException(MsgInvalid);
        // Never download over a file called AudioTaskbar.exe: keep versioned names in the updates folder.
        if (name.Equals(PortableAssetName, StringComparison.OrdinalIgnoreCase)) name = $"AudioTaskbar-{info.Version}.exe";
        Directory.CreateDirectory(_updatesDir);
        var final = Path.Combine(_updatesDir, name);
        var part = final + ".part";
        try
        {
            using (var resp = await _http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if (HttpFailure(resp.StatusCode, info.SetupUrl) is { } fail) throw new HttpRequestException(fail.Detail);
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
                throw new UpdateVerificationException("Tệp cập nhật bị hỏng hoặc bị thay đổi (SHA-256 không khớp) — đã hủy cập nhật.");
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

    /// <summary>After a portable self-update of an NSIS-installed copy, keep "Apps & features" in sync.</summary>
    public static void StampInstalledVersion(string exeDir, string version)
    {
        try
        {
            var inst = InstalledLocation();
            if (inst is null || !string.Equals(Path.GetFullPath(inst).TrimEnd('\\', '/'), Path.GetFullPath(exeDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioTaskbar", writable: true);
            k?.SetValue("DisplayVersion", version, RegistryValueKind.String);
        }
        catch { }
    }

    /// <summary>
    /// NSIS arguments for a silent upgrade (Setup fallback). Portable copies (not in the registered
    /// install folder) are upgraded in place with /D= (must be last and unquoted).
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

    /// <summary>True for a developer checkout (…\release next to .git): never self-install there automatically.</summary>
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

    // ---------------- portable in-place self-update ----------------

    /// <summary>True when the folder of <paramref name="exePath"/> accepts new files (no admin needed).</summary>
    public static bool CanWriteNextTo(string exePath)
    {
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(exePath)!, ".at-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static string CmdQuote(string path) => path.Replace("%", "%%");

    /// <summary>
    /// apply-update.cmd: wait (≤ 60 s) until process <paramref name="pid"/> has exited cleanly, move the
    /// running exe to &lt;exe&gt;.old, copy the verified new exe in place (roll back on failure), relaunch
    /// it with <c>--minimized --updated</c>, then delete itself. UTF-8 (chcp 65001) so Vietnamese paths work.
    /// </summary>
    internal static string BuildApplyScript(int pid, string newExe, string targetExe, string logPath, bool relaunch = true, int waitSeconds = 60)
    {
        var nl = "\r\n";
        var sb = new System.Text.StringBuilder();
        sb.Append("@echo off").Append(nl);
        sb.Append("chcp 65001 >nul").Append(nl);
        sb.Append("setlocal").Append(nl);
        sb.Append("set \"NEW=").Append(CmdQuote(newExe)).Append('"').Append(nl);
        sb.Append("set \"TARGET=").Append(CmdQuote(targetExe)).Append('"').Append(nl);
        sb.Append("set \"LOG=").Append(CmdQuote(logPath)).Append('"').Append(nl);
        sb.Append("set /a N=0").Append(nl);
        sb.Append(":wait").Append(nl);
        sb.Append($"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \" {pid} \" >nul").Append(nl);
        sb.Append("if errorlevel 1 goto gone").Append(nl);
        sb.Append("set /a N+=1").Append(nl);
        sb.Append($"if %N% GEQ {Math.Max(1, waitSeconds)} goto timeout").Append(nl);
        sb.Append("ping -n 2 127.0.0.1 >nul").Append(nl);
        sb.Append("goto wait").Append(nl);
        sb.Append(":gone").Append(nl);
        sb.Append("if exist \"%TARGET%.old\" del /f /q \"%TARGET%.old\" >nul 2>&1").Append(nl);
        sb.Append("move /y \"%TARGET%\" \"%TARGET%.old\" >nul 2>&1").Append(nl);
        sb.Append("if errorlevel 1 goto fail").Append(nl);
        sb.Append("copy /y /b \"%NEW%\" \"%TARGET%\" >nul 2>&1").Append(nl);
        sb.Append("if errorlevel 1 goto rollback").Append(nl);
        sb.Append("echo %date% %time% apply-update: replaced \"%TARGET%\" >> \"%LOG%\"").Append(nl);
        sb.Append("del /f /q \"%NEW%\" >nul 2>&1").Append(nl);
        if (relaunch) sb.Append("start \"\" \"%TARGET%\" --minimized --updated").Append(nl);
        sb.Append("goto end").Append(nl);
        sb.Append(":rollback").Append(nl);
        sb.Append("move /y \"%TARGET%.old\" \"%TARGET%\" >nul 2>&1").Append(nl);
        sb.Append(":fail").Append(nl);
        sb.Append("echo %date% %time% apply-update: FAILED, kept the current version >> \"%LOG%\"").Append(nl);
        if (relaunch) sb.Append("start \"\" \"%TARGET%\" --minimized").Append(nl);
        sb.Append("goto end").Append(nl);
        sb.Append(":timeout").Append(nl);
        sb.Append("echo %date% %time% apply-update: app did not exit, update skipped >> \"%LOG%\"").Append(nl);
        sb.Append(":end").Append(nl);
        sb.Append("endlocal").Append(nl);
        sb.Append("(goto) 2>nul & del /f /q \"%~f0\"").Append(nl);
        return sb.ToString();
    }

    /// <summary>Write apply-update.cmd next to the download and start it hidden. The caller must then exit cleanly.</summary>
    public string StartPortableApply(string newExe, string targetExe, string logPath)
    {
        Directory.CreateDirectory(_updatesDir);
        var script = Path.Combine(_updatesDir, "apply-update.cmd");
        File.WriteAllText(script, BuildApplyScript(Environment.ProcessId, newExe, targetExe, logPath), new System.Text.UTF8Encoding(false));
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/d /c \"\"" + script + "\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _updatesDir
        });
        return script;
    }

    /// <summary>Delete the &lt;exe&gt;.old backup left by a previous successful self-update.</summary>
    public static void CleanupOldBackup(string? exePath)
    {
        try { if (exePath is not null && File.Exists(exePath + ".old")) File.Delete(exePath + ".old"); } catch { }
    }

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
