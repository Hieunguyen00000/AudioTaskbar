using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AudioTaskbar.Tests;

/// <summary>v5.3 single-exe release: portable asset preferred, in-place swap script, friendly messages.</summary>
public class PortableUpdateTests
{
    private static readonly byte[] Exe = Encoding.UTF8.GetBytes("MZ portable AudioTaskbar " + new string('p', 4000));
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static AppConfig Cfg(string feed = "") { var c = new AppConfig { UpdateFeedUrl = feed }; c.Normalize(); return c; }

    private static string Release(string tag, params string[] names)
        => "{\"tag_name\":\"" + tag + "\",\"assets\":[" + string.Join(",", names.Select(n => "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://dl.example/" + n + "\"}")) + "]}";

    private static MockHandler GitHub(string release, string? sums) => new(req =>
    {
        var url = req.RequestUri!.ToString();
        if (url.Contains("api.github.com")) return MockHandler.Json(release);
        if (url.EndsWith("SHA256SUMS.txt")) return sums is null ? MockHandler.Status(HttpStatusCode.NotFound) : MockHandler.Json(sums);
        if (url.EndsWith(".exe")) return MockHandler.Bytes(Exe);
        return MockHandler.Status(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task Portable_exe_is_preferred_over_setup()
    {
        var sums = Sha(Exe) + "  AudioTaskbar.exe\n" + new string('1', 64) + "  AudioTaskbar-Setup-5.4.0.exe\n";
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "AudioTaskbar-Setup-5.4.0.exe", "AudioTaskbar.exe", "SHA256SUMS.txt"), sums));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Equal("AudioTaskbar.exe", r.Info!.SetupName);
        Assert.True(r.Info.IsPortable);
        Assert.Equal(Sha(Exe), r.Info.Sha256);
    }

    [Fact]
    public async Task Setup_is_used_only_when_no_portable_exe()
    {
        var sums = Sha(Exe) + "  AudioTaskbar-Setup-5.4.0.exe\n";
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "AudioTaskbar-Setup-5.4.0.exe", "SHA256SUMS.txt"), sums));
        var r = await u.CheckAsync(Cfg());
        Assert.False(r.Info!.IsPortable);
    }

    [Fact]
    public async Task Portable_download_gets_a_versioned_name_and_is_verified()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Exe)), d.Path);
        var info = new UpdateInfo { Version = new SemVer(5, 4, 0, ""), SetupUrl = "https://dl.example/AudioTaskbar.exe", SetupName = "AudioTaskbar.exe", Sha256 = Sha(Exe) };
        var path = await u.DownloadAndVerifyAsync(info, true);
        Assert.Equal("AudioTaskbar-5.4.0.exe", Path.GetFileName(path));
        Assert.Equal(Sha(Exe), AutoUpdater.ComputeSha256(path));
    }

    [Fact]
    public async Task Tampered_portable_exe_is_rejected()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Encoding.UTF8.GetBytes("evil"))), d.Path);
        var info = new UpdateInfo { Version = new SemVer(5, 4, 0, ""), SetupUrl = "https://dl.example/AudioTaskbar.exe", SetupName = "AudioTaskbar.exe", Sha256 = Sha(Exe) };
        await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(info, true));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Fact]
    public async Task Feed_manifest_for_portable_exe()
    {
        var manifest = "{\"version\":\"5.4.0\",\"fileName\":\"AudioTaskbar.exe\",\"downloadUrl\":\"AudioTaskbar.exe\",\"sha256\":\"" + Sha(Exe) + "\"}";
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Json(manifest)));
        var r = await u.CheckAsync(Cfg("https://updates.example/at/latest.json"));
        Assert.True(r.Info!.IsPortable);
        Assert.Equal("https://updates.example/at/AudioTaskbar.exe", r.Info.SetupUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task User_messages_never_show_repo_or_url(HttpStatusCode code)
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Status(code)));
        var r = await u.CheckAsync(Cfg());
        Assert.DoesNotContain("Hieunguyen00000", r.Message);
        Assert.DoesNotContain("github", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(r.Message is AutoUpdater.MsgNoRelease or AutoUpdater.MsgCannotCheck);
        Assert.False(string.IsNullOrEmpty(r.Detail));
    }

    [Fact]
    public async Task Offline_and_bad_json_messages_are_friendly()
    {
        using var off = new AutoUpdater("5.3.0", new MockHandler(_ => throw new HttpRequestException("api.github.com unreachable")));
        Assert.Equal(AutoUpdater.MsgCannotCheck, (await off.CheckAsync(Cfg())).Message);
        using var bad = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Json("{oops")));
        Assert.Equal(AutoUpdater.MsgCannotCheck, (await bad.CheckAsync(Cfg())).Message);
        using var sums = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "AudioTaskbar.exe", "SHA256SUMS.txt"), "nothing useful"));
        var r = await sums.CheckAsync(Cfg());
        Assert.Equal(AutoUpdater.MsgInvalid, r.Message);
        Assert.DoesNotContain("github", r.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Up_to_date_message_is_friendly()
    {
        using var u = new AutoUpdater("5.4.0", GitHub(Release("v5.4.0", "AudioTaskbar.exe"), null));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(UpdateStatus.UpToDate, r.Status);
        Assert.Equal("Bạn đang dùng bản mới nhất (v5.4.0).", r.Message);
    }

    // ---- apply-update.cmd ----
    [Fact]
    public void Apply_script_waits_backs_up_swaps_and_relaunches_minimized()
    {
        var s = AutoUpdater.BuildApplyScript(4321, @"C:\U\updates\AudioTaskbar-5.4.0.exe", @"D:\Tools\Audio Taskbar\AudioTaskbar.exe", @"C:\U\app.log");
        Assert.Contains("chcp 65001", s);
        Assert.Contains("PID eq 4321", s);
        Assert.Contains("set \"TARGET=D:\\Tools\\Audio Taskbar\\AudioTaskbar.exe\"", s);
        Assert.Contains("move /y \"%TARGET%\" \"%TARGET%.old\"", s);
        Assert.Contains("copy /y /b \"%NEW%\" \"%TARGET%\"", s);
        Assert.Contains(":rollback", s);
        Assert.Contains("move /y \"%TARGET%.old\" \"%TARGET%\"", s);
        Assert.Contains("start \"\" \"%TARGET%\" --minimized --updated", s);
        Assert.Contains("del /f /q \"%~f0\"", s);
        Assert.True(s.IndexOf(":gone", StringComparison.Ordinal) < s.IndexOf("move /y \"%TARGET%\" \"%TARGET%.old\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_script_escapes_percent_in_paths()
    {
        var s = AutoUpdater.BuildApplyScript(1, @"C:\a%b\n.exe", @"C:\100%\AudioTaskbar.exe", @"C:\l.log");
        Assert.Contains(@"C:\100%%\AudioTaskbar.exe", s);
        Assert.Contains(@"C:\a%%b\n.exe", s);
    }

    [Fact]
    public void Apply_script_really_swaps_the_exe_after_the_process_exits()
    {
        using var d = new TempDir();
        var target = Path.Combine(d.Path, "Thư mục có dấu", "AudioTaskbar.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old version");
        var fresh = d.File("AudioTaskbar-9.9.9.exe");
        File.WriteAllText(fresh, "new version");
        var log = d.File("apply.log");
        // A short-lived process stands in for the running app.
        using var holder = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        var script = d.File("apply-update.cmd");
        File.WriteAllText(script, AutoUpdater.BuildApplyScript(holder.Id, fresh, target, log, relaunch: false, waitSeconds: 30), new UTF8Encoding(false));
        using var run = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"\"" + script + "\"\"") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(run.WaitForExit(60000));
        Assert.True(holder.HasExited);
        Assert.Equal("new version", File.ReadAllText(target));
        Assert.Equal("old version", File.ReadAllText(target + ".old"));
        Assert.False(File.Exists(fresh));
        Assert.False(File.Exists(script));           // self-deleting
        Assert.Contains("replaced", File.ReadAllText(log));
        AutoUpdater.CleanupOldBackup(target);
        Assert.False(File.Exists(target + ".old"));
    }

    [Fact]
    public void Apply_script_rolls_back_when_the_new_exe_is_missing()
    {
        using var d = new TempDir();
        var target = d.File("AudioTaskbar.exe");
        File.WriteAllText(target, "old version");
        var log = d.File("apply.log");
        var script = d.File("apply-update.cmd");
        File.WriteAllText(script, AutoUpdater.BuildApplyScript(999999, d.File("missing.exe"), target, log, relaunch: false, waitSeconds: 5), new UTF8Encoding(false));
        using var run = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"\"" + script + "\"\"") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(run.WaitForExit(60000));
        Assert.Equal("old version", File.ReadAllText(target));
        Assert.Contains("FAILED", File.ReadAllText(log));
    }

    [Fact]
    public void Write_probe_detects_writable_folder()
    {
        using var d = new TempDir();
        Assert.True(AutoUpdater.CanWriteNextTo(d.File("AudioTaskbar.exe")));
        Assert.False(AutoUpdater.CanWriteNextTo(Path.Combine(d.Path, "does-not-exist", "AudioTaskbar.exe")));
        Assert.Empty(Directory.GetFiles(d.Path));
    }
}
