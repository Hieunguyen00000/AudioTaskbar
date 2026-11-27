using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AudioTaskbar.Tests;

/// <summary>6. Auto-update: SemVer, 404/403, SHA-256 match / mismatch, missing assets, real local HTTP feed.</summary>
public class UpdaterTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("MZ fake AudioTaskbar setup payload " + new string('x', 5000));
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static AppConfig Cfg(string feed = "") { var c = new AppConfig { UpdateFeedUrl = feed }; c.Normalize(); return c; }

    private static string Release(string tag, params string[] assetNames)
        => "{\"tag_name\":\"" + tag + "\",\"html_url\":\"https://github.com/o/r/releases/tag/" + tag + "\",\"body\":\"notes\",\"assets\":[" +
           string.Join(",", assetNames.Select(n => "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://dl.example/" + n + "\"}")) + "]}";

    // ---- SemVer ----
    [Theory]
    [InlineData("5.3.0", "5.3.1", -1)]
    [InlineData("5.3.1", "5.3.0", 1)]
    [InlineData("5.10.0", "5.2.0", 1)]
    [InlineData("5.2.0", "5.10.0", -1)]
    [InlineData("v5.3.0", "5.3.0", 0)]
    [InlineData("V5.3.0", "v5.3.0", 0)]
    [InlineData("5.3.0.0", "5.3.0", 0)]
    [InlineData("5.3", "5.3.0", 0)]
    [InlineData("5.3.0+build.7", "5.3.0", 0)]
    [InlineData("5.3.0-beta", "5.3.0", -1)]
    [InlineData("5.3.0", "5.3.0-rc.1", 1)]
    [InlineData("5.3.0-alpha", "5.3.0-beta", -1)]
    [InlineData("5.3.0-beta.2", "5.3.0-beta.10", -1)]
    [InlineData("5.3.0-beta", "5.3.0-beta.1", -1)]
    [InlineData("6.0.0-alpha", "5.99.99", 1)]
    [InlineData("10.0.0", "9.9.9", 1)]
    public void SemVer_ordering(string a, string b, int sign)
    {
        Assert.True(SemVer.TryParse(a, out var va));
        Assert.True(SemVer.TryParse(b, out var vb));
        Assert.Equal(sign, Math.Sign(va.CompareTo(vb)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5")]
    [InlineData("5.x.1")]
    [InlineData("5.3.0-")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.2.3")]
    [InlineData("5..3")]
    [InlineData("5.3.99999999999")]
    [InlineData("vv5.3.0")]
    [InlineData("٥.٣.٠")]
    public void SemVer_invalid(string? text) => Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void App_version_is_semver_5_3_or_newer()
    {
        Assert.True(SemVer.TryParse(AutoUpdater.AppVersion(), out var v));
        Assert.True(v.CompareTo(new SemVer(5, 3, 0, "")) >= 0);
    }

    // ---- GitHub Releases API ----
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.Unauthorized, "Forbidden")]
    [InlineData((HttpStatusCode)429, "Forbidden")]
    [InlineData(HttpStatusCode.InternalServerError, "Error")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Error")]
    public async Task GitHub_http_failures_are_reported_not_thrown(HttpStatusCode code, string expected)
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Status(code)));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(expected, r.Status.ToString());
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.Same(r, u.LastResult);
    }

    [Fact]
    public async Task Private_repo_404_explains_why()
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Status(HttpStatusCode.NotFound)));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(AutoUpdater.MsgNoRelease, r.Message);   // friendly text for users
        Assert.Contains("Private", r.Detail);                // technical reason goes to app.log
    }

    [Fact]
    public async Task Offline_is_reported()
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => throw new HttpRequestException("No such host is known.")));
        Assert.Equal(UpdateStatus.Offline, (await u.CheckAsync(Cfg())).Status);
    }

    [Fact]
    public async Task Timeout_is_reported_as_offline()
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler((_, _) => throw new TaskCanceledException("timeout")));
        Assert.Equal(UpdateStatus.Offline, (await u.CheckAsync(Cfg())).Status);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"tag_name\":\"latest\"}")]
    [InlineData("{}")]
    public async Task Garbage_release_json_is_an_error(string body)
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Json(body)));
        Assert.Equal(UpdateStatus.Error, (await u.CheckAsync(Cfg())).Status);
    }

    private static MockHandler GitHub(string releaseJson, string? sums = null, string? manifest = null)
        => new(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("api.github.com")) return MockHandler.Json(releaseJson);
            if (url.EndsWith("SHA256SUMS.txt")) return sums is null ? MockHandler.Status(HttpStatusCode.NotFound) : MockHandler.Json(sums);
            if (url.EndsWith("latest.json")) return manifest is null ? MockHandler.Status(HttpStatusCode.NotFound) : MockHandler.Json(manifest);
            if (url.EndsWith(".exe")) return MockHandler.Bytes(Payload);
            return MockHandler.Status(HttpStatusCode.NotFound);
        });

    [Fact]
    public async Task Newer_release_with_checksums_is_offered()
    {
        var sums = Sha(Payload) + "  AudioTaskbar-Setup-5.3.1.exe\n" + new string('0', 64) + "  AudioTaskbar.exe\n";
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.3.1", "AudioTaskbar-Setup-5.3.1.exe", "SHA256SUMS.txt"), sums));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Equal("5.3.1", r.Info!.Version.ToString());
        Assert.Equal(Sha(Payload), r.Info.Sha256);
        Assert.Equal("AudioTaskbar-Setup-5.3.1.exe", r.Info.SetupName);
    }

    [Theory]
    [InlineData("v5.3.0")]
    [InlineData("v5.2.0")]
    [InlineData("v5.3.0-rc.1")]
    public async Task Same_or_older_release_is_up_to_date(string tag)
    {
        using var u = new AutoUpdater("5.3.0", GitHub(Release(tag, $"AudioTaskbar-Setup-{tag[1..]}.exe")));
        Assert.Equal(UpdateStatus.UpToDate, (await u.CheckAsync(Cfg())).Status);
    }

    [Fact]
    public async Task Newer_release_without_setup_asset_is_NoAsset()
    {
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "source.zip", "SHA256SUMS.txt"), ""));
        Assert.Equal(UpdateStatus.NoAsset, (await u.CheckAsync(Cfg())).Status);
    }

    [Fact]
    public async Task Checksum_file_without_our_line_refuses_update()
    {
        var sums = Sha(Payload) + "  SomethingElse.exe\n";
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "AudioTaskbar-Setup-5.4.0.exe", "SHA256SUMS.txt"), sums));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(UpdateStatus.Error, r.Status);
        Assert.Null(r.Info);
    }

    [Fact]
    public async Task Release_without_any_checksum_cannot_be_silently_installed()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "AudioTaskbar-Setup-5.4.0.exe")), d.Path);
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Null(r.Info!.Sha256);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(r.Info, requireChecksum: true));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Fact]
    public async Task Latest_json_asset_is_preferred()
    {
        var manifest = "{\"version\":\"5.4.0\",\"fileName\":\"AudioTaskbar-Setup-5.4.0.exe\",\"downloadUrl\":\"https://cdn.example/AudioTaskbar-Setup-5.4.0.exe\",\"sha256\":\"" + Sha(Payload).ToUpperInvariant() + "\"}";
        using var u = new AutoUpdater("5.3.0", GitHub(Release("v5.4.0", "AudioTaskbar-Setup-5.4.0.exe", "latest.json"), null, manifest));
        var r = await u.CheckAsync(Cfg());
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Equal("https://cdn.example/AudioTaskbar-Setup-5.4.0.exe", r.Info!.SetupUrl);
        Assert.Equal(Sha(Payload), r.Info.Sha256);
    }

    // ---- static feed (updateFeedUrl) ----
    [Fact]
    public async Task Feed_relative_download_url_is_resolved()
    {
        var manifest = "{\"version\":\"5.3.2\",\"downloadUrl\":\"files/AudioTaskbar-Setup-5.3.2.exe\",\"sha256\":\"" + Sha(Payload) + "\"}";
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Json(manifest)));
        var r = await u.CheckAsync(Cfg("https://updates.example/at/latest.json"));
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Equal("https://updates.example/at/files/AudioTaskbar-Setup-5.3.2.exe", r.Info!.SetupUrl);
        Assert.Equal("AudioTaskbar-Setup-5.3.2.exe", r.Info.SetupName);
    }

    [Theory]
    [InlineData("{\"version\":\"5.4.0\",\"downloadUrl\":\"https://x/a.exe\",\"sha256\":\"1234\"}", "Error")]
    [InlineData("{\"version\":\"five\",\"downloadUrl\":\"https://x/a.exe\"}", "Error")]
    [InlineData("{\"version\":\"5.4.0\"}", "NoAsset")]
    [InlineData("<html/>", "Error")]
    public async Task Bad_feeds_are_rejected(string manifest, string expected)
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Json(manifest)));
        Assert.Equal(expected, (await u.CheckAsync(Cfg("https://updates.example/latest.json"))).Status.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    public async Task Feed_http_failures(HttpStatusCode code, string expected)
    {
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Status(code)));
        Assert.Equal(expected, (await u.CheckAsync(Cfg("https://updates.example/latest.json"))).Status.ToString());
    }

    // ---- download + SHA-256 ----
    private static UpdateInfo Info(string? sha, string name = "AudioTaskbar-Setup-5.4.0.exe")
        => new() { Version = new SemVer(5, 4, 0, ""), SetupUrl = "https://dl.example/" + name, SetupName = name, Sha256 = sha };

    [Fact]
    public async Task Download_with_matching_sha_is_kept()
    {
        using var d = new TempDir();
        var progress = new List<double>();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Payload)), d.Path);
        var path = await u.DownloadAndVerifyAsync(Info(Sha(Payload)), true, new Progress<double>(p => { lock (progress) progress.Add(p); }));
        Assert.True(File.Exists(path));
        Assert.Equal(Sha(Payload), AutoUpdater.ComputeSha256(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Download_with_wrong_sha_is_deleted_and_rejected()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Payload)), d.Path);
        var wrong = Sha(Encoding.UTF8.GetBytes("tampered"));
        await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(Info(wrong), true));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Fact]
    public async Task Truncated_download_is_rejected()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Payload[..100])), d.Path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(Info(Sha(Payload)), true));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Fact]
    public async Task Missing_installer_404_leaves_nothing_behind()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Status(HttpStatusCode.NotFound)), d.Path);
        await Assert.ThrowsAsync<HttpRequestException>(() => u.DownloadAndVerifyAsync(Info(Sha(Payload)), true));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Theory]
    [InlineData("evil.msi")]
    [InlineData("setup.bat")]
    [InlineData("")]
    public async Task Non_exe_names_are_refused(string name)
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Payload)), d.Path);
        if (name.Length == 0)
        {
            var info = new UpdateInfo { Version = new SemVer(5, 4, 0, ""), SetupUrl = "", Sha256 = Sha(Payload) };
            await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(info, true));
        }
        else await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(Info(Sha(Payload), name), true));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Fact]
    public async Task Path_traversal_in_name_stays_inside_updates_folder()
    {
        using var d = new TempDir();
        using var u = new AutoUpdater("5.3.0", new MockHandler(_ => MockHandler.Bytes(Payload)), d.Path);
        var path = await u.DownloadAndVerifyAsync(Info(Sha(Payload), "..\\..\\Windows\\AudioTaskbar-Setup-5.4.0.exe"), true);
        Assert.Equal(Path.GetFullPath(d.Path), Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    [Fact]
    public void Sha256_helpers()
    {
        using var d = new TempDir();
        var f = d.File("a.bin");
        File.WriteAllBytes(f, Payload);
        Assert.True(AutoUpdater.VerifySha256(f, Sha(Payload)));
        Assert.True(AutoUpdater.VerifySha256(f, Sha(Payload).ToUpperInvariant()));
        Assert.False(AutoUpdater.VerifySha256(f, null));
        Assert.False(AutoUpdater.VerifySha256(f, "abc"));
        Assert.False(AutoUpdater.VerifySha256(f, new string('0', 64)));
        Assert.False(AutoUpdater.IsSha256Hex(new string('g', 64)));
    }

    [Theory]
    [InlineData("{0}  AudioTaskbar-Setup-5.3.0.exe")]
    [InlineData("{0} *AudioTaskbar-Setup-5.3.0.exe")]
    [InlineData("junk line\r\n{0}  AudioTaskbar-Setup-5.3.0.exe\r\n")]
    [InlineData("{0}  dist/AudioTaskbar-Setup-5.3.0.exe")]
    [InlineData("{0}  audiotaskbar-setup-5.3.0.EXE")]
    public void Sha256sums_formats(string template)
    {
        var hash = Sha(Payload);
        Assert.Equal(hash, AutoUpdater.ParseSha256Sums(string.Format(template, hash.ToUpperInvariant()), "AudioTaskbar-Setup-5.3.0.exe"));
    }

    [Fact]
    public void Sha256sums_missing_or_malformed()
    {
        Assert.Null(AutoUpdater.ParseSha256Sums("", "x.exe"));
        Assert.Null(AutoUpdater.ParseSha256Sums("nothex  x.exe", "x.exe"));
        Assert.Null(AutoUpdater.ParseSha256Sums(new string('a', 63) + "  x.exe", "x.exe"));
        Assert.Null(AutoUpdater.ParseSha256Sums(new string('a', 64) + "  y.exe", "x.exe"));
    }

    // ---- installer arguments / portable copies ----
    [Fact]
    public void Installed_copy_upgrades_in_place_without_D()
    {
        var a = AutoUpdater.BuildInstallerArguments(@"C:\Users\u\AppData\Local\Programs\AudioTaskbar", @"C:\Users\u\AppData\Local\Programs\AudioTaskbar\");
        Assert.Equal("/S /UPDATED /NODESKTOP /NOSTARTMENU", a);
    }

    [Theory]
    [InlineData(@"D:\Tools\AudioTaskbar", @"C:\Users\u\AppData\Local\Programs\AudioTaskbar")]
    [InlineData(@"D:\Tools\Audio Taskbar", null)]
    public void Portable_copy_upgrades_with_D_last(string exeDir, string? installed)
    {
        var a = AutoUpdater.BuildInstallerArguments(exeDir, installed);
        Assert.StartsWith("/S /UPDATED", a);
        Assert.EndsWith(" /D=" + exeDir, a);
    }

    [Fact]
    public void Dev_checkout_never_self_installs()
    {
        using var d = new TempDir();
        Directory.CreateDirectory(Path.Combine(d.Path, ".git"));
        Directory.CreateDirectory(Path.Combine(d.Path, "release"));
        Assert.True(AutoUpdater.IsDevCheckout(Path.Combine(d.Path, "release")));
        using var d2 = new TempDir();
        Directory.CreateDirectory(Path.Combine(d2.Path, "app"));
        Assert.False(AutoUpdater.IsDevCheckout(Path.Combine(d2.Path, "app")));
    }

    [Fact]
    public void Autostart_command_starts_minimized()
        => Assert.Equal("\"C:\\A B\\AudioTaskbar.exe\" --minimized", AutoStart.Command(@"C:\A B\AudioTaskbar.exe"));

    // ---- end-to-end against a real local HTTP server (mock release feed) ----
    [Fact]
    public async Task End_to_end_local_feed_check_download_verify()
    {
        using var srv = new LocalHttpServer();
        using var d = new TempDir();
        srv.Map("AudioTaskbar-Setup-5.3.1.exe", Payload);
        srv.Map("latest.json", "{\"version\":\"5.3.1\",\"fileName\":\"AudioTaskbar-Setup-5.3.1.exe\",\"downloadUrl\":\"AudioTaskbar-Setup-5.3.1.exe\",\"sha256\":\"" + Sha(Payload) + "\"}");
        using var u = new AutoUpdater("5.3.0", null, d.Path);
        var r = await u.CheckAsync(Cfg(srv.BaseUrl + "latest.json"));
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        var path = await u.DownloadAndVerifyAsync(r.Info!, true);
        Assert.Equal(Sha(Payload), AutoUpdater.ComputeSha256(path));

        // Tampered file on the server -> rejected and removed.
        srv.Map("AudioTaskbar-Setup-5.3.1.exe", Encoding.UTF8.GetBytes("evil"));
        File.Delete(path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => u.DownloadAndVerifyAsync(r.Info!, true));
        Assert.False(File.Exists(path));

        // Same version on the server -> up to date.
        srv.Map("latest.json", "{\"version\":\"5.3.0\",\"downloadUrl\":\"AudioTaskbar-Setup-5.3.0.exe\",\"sha256\":\"" + Sha(Payload) + "\"}");
        Assert.Equal(UpdateStatus.UpToDate, (await u.CheckAsync(Cfg(srv.BaseUrl + "latest.json"))).Status);
        srv.Map("latest.json", "gone", 404);
        Assert.Equal(UpdateStatus.NotFound, (await u.CheckAsync(Cfg(srv.BaseUrl + "latest.json"))).Status);
        Assert.True(srv.Hits >= 5);
    }

    [Fact]
    public async Task Connection_refused_is_offline()
    {
        int port;
        using (var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
        using var u = new AutoUpdater("5.3.0");
        Assert.Equal(UpdateStatus.Offline, (await u.CheckAsync(Cfg($"http://127.0.0.1:{port}/latest.json"))).Status);
    }
}
