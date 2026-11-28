using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AudioTaskbar.Tests;

/// <summary>
/// SignPath / Smart App Control preparation: SAC decision table, integrity HRESULT
/// mapping, required policy documents, and consistent PE VERSIONINFO metadata.
/// </summary>
public class SigningPrepTests
{
    [Theory]
    [InlineData(false, false, SacTapDecision.Proceed)]
    [InlineData(false, true, SacTapDecision.Proceed)]
    [InlineData(false, null, SacTapDecision.Proceed)]
    [InlineData(true, false, SacTapDecision.SkipUnsigned)]
    [InlineData(true, true, SacTapDecision.TrySigned)]
    [InlineData(true, null, SacTapDecision.TrySigned)]
    public void Sac_policy_skips_only_unsigned_dlls_when_sac_is_on(bool sacOn, bool? signed, SacTapDecision expected)
        => Assert.Equal(expected, SacTapPolicy.Decide(sacOn, signed));

    [Fact]
    public void Unsigned_dll_under_sac_is_skipped_immediately()
    {
        Assert.True(SacTapPolicy.ShouldSkipUnsignedTap(sacEnabled: true, hasAuthenticodeSignature: false));
        Assert.False(SacTapPolicy.ShouldSkipUnsignedTap(sacEnabled: true, hasAuthenticodeSignature: true));
        Assert.False(SacTapPolicy.ShouldSkipUnsignedTap(sacEnabled: true, hasAuthenticodeSignature: null));
        Assert.False(SacTapPolicy.ShouldSkipUnsignedTap(sacEnabled: false, hasAuthenticodeSignature: false));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070241), true)]  // HRESULT_FROM_WIN32(ERROR_INVALID_IMAGE_HASH)
    [InlineData(577, true)]                         // ERROR_INVALID_IMAGE_HASH
    [InlineData(unchecked((int)0x80070005), true)]  // HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED)
    [InlineData(5, true)]                           // ERROR_ACCESS_DENIED
    [InlineData(unchecked((int)0x800B0100), true)]  // TRUST_E_NOSIGNATURE
    [InlineData(unchecked((int)0x800B0109), true)]  // CERT_E_UNTRUSTEDROOT
    [InlineData(unchecked((int)0x80004005), false)] // E_FAIL — generic, not treated as CI
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Integrity_or_trust_failure_codes_are_recognised(int hr, bool expected)
        => Assert.Equal(expected, SacTapPolicy.IsIntegrityOrTrustFailure(hr));

    [Fact]
    public void Unsigned_temp_file_has_no_authenticode_signature()
    {
        using var dir = new TempDir();
        var path = dir.File("unsigned.bin");
        File.WriteAllBytes(path, "this is not a signed PE"u8.ToArray());
        Assert.False(TapSignature.HasAuthenticodeSignature(path));
        Assert.Null(TapSignature.HasAuthenticodeSignature(dir.File("missing.dll")));
        Assert.Null(TapSignature.HasAuthenticodeSignature(null));
        Assert.Null(TapSignature.HasAuthenticodeSignature(""));
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("PRIVACY.md")]
    [InlineData("SECURITY.md")]
    [InlineData("CODE_OF_CONDUCT.md")]
    [InlineData("docs/CODE_SIGNING.md")]
    [InlineData("README.md")]
    [InlineData("src/TaskbarTap/TaskbarTap.rc")]
    public void Required_signpath_documents_and_resources_exist(string relative)
        => Assert.True(File.Exists(Path.Combine(RepoRoot, relative)), relative + " is missing");

    [Fact]
    public void Code_signing_policy_has_the_required_signpath_attribution_and_roles()
    {
        var policy = File.ReadAllText(Path.Combine(RepoRoot, "docs/CODE_SIGNING.md"));
        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        const string attribution = "Free code signing provided by SignPath.io, certificate by SignPath Foundation";
        Assert.Contains(attribution, policy);
        Assert.Contains(attribution, readme);
        Assert.Contains("https://github.com/Hieunguyen00000", policy);
        Assert.Contains("Hieu Nguyen Van", policy);
        Assert.Contains("InitializeXamlDiagnosticsEx", policy);
        Assert.Contains("CreateRemoteThread", policy);
        Assert.Contains("PRIVACY.md", policy);
        Assert.Contains("pending", policy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Privacy_policy_documents_every_outbound_network_call()
    {
        var privacy = File.ReadAllText(Path.Combine(RepoRoot, "PRIVACY.md"));
        foreach (var needle in new[]
        {
            "ipapi.co", "get.geojs.io", "ip-api.com",
            "api.open-meteo.com", "geocoding-api.open-meteo.com",
            "api.github.com", "github.com",
            "autoUpdateEnabled", "weatherFollowsRegion", "regionMode",
        })
            Assert.Contains(needle, privacy);
        Assert.DoesNotContain("telemetry is collected", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pe_metadata_is_consistent_across_csproj_rc_and_installer()
    {
        var root = RepoRoot;
        var csproj = XDocument.Load(Path.Combine(root, "src/AudioTaskbar.csproj"));
        string Prop(string name) =>
            csproj.Root!.Elements().Where(e => e.Name.LocalName == "PropertyGroup")
                .SelectMany(g => g.Elements())
                .First(e => e.Name.LocalName == name).Value.Trim();

        var version = Prop("Version");
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        Assert.Equal(version + ".0", Prop("FileVersion"));
        Assert.Equal("Audio Taskbar", Prop("Product"));
        Assert.Equal("Audio Taskbar", Prop("AssemblyTitle"));
        Assert.Equal("Hieunguyen00000", Prop("Company"));
        Assert.Equal("Copyright (c) 2026 Hieunguyen00000", Prop("Copyright"));

        var rc = File.ReadAllText(Path.Combine(root, "src/TaskbarTap/TaskbarTap.rc"));
        // 5.4.2 → 5,4,2,0
        var comma = version.Replace('.', ',') + ",0";
        Assert.Contains($"FILEVERSION     {comma}", rc);
        Assert.Contains($"PRODUCTVERSION  {comma}", rc);
        Assert.Contains($"\"FileVersion\",      \"{version}.0\"", rc);
        Assert.Contains($"\"ProductVersion\",   \"{version}\"", rc);
        Assert.Contains("\"ProductName\",      \"Audio Taskbar\"", rc);
        Assert.Contains("\"FileDescription\",  \"Audio Taskbar XAML Backdrop Helper\"", rc);
        Assert.Contains("\"CompanyName\",      \"Hieunguyen00000\"", rc);
        Assert.Contains("\"LegalCopyright\",   \"Copyright (c) 2026 Hieunguyen00000\"", rc);
        Assert.Contains("\"OriginalFilename\", \"TaskbarTap.dll\"", rc);
        Assert.Contains("\"InternalName\",     \"TaskbarTap\"", rc);

        var nsi = File.ReadAllText(Path.Combine(root, "installer/AudioTaskbar.nsi"));
        Assert.Contains("!define APP_PUBLISHER      \"Hieunguyen00000\"", nsi);
        Assert.Contains("VIAddVersionKey \"ProductName\"        \"${APP_NAME}\"", nsi);
        Assert.Contains("VIAddVersionKey \"CompanyName\"        \"${APP_PUBLISHER}\"", nsi);
        Assert.Contains("Copyright (c) 2026 ${APP_PUBLISHER}", nsi);
        Assert.Contains("PRIVACY.md", nsi);
        Assert.Contains("Open-Meteo", nsi);
    }

    [Fact]
    public void Release_workflow_keeps_unsigned_releases_working_and_wires_two_phase_signpath()
    {
        var yml = File.ReadAllText(Path.Combine(RepoRoot, ".github/workflows/release.yml"));
        Assert.Contains("vars.SIGNPATH_ENABLED == 'true'", yml);
        Assert.Contains("signpath/github-action-submit-signing-request@v3", yml);
        Assert.Contains("secrets.SIGNPATH_API_TOKEN", yml);
        Assert.Contains("vars.SIGNPATH_ORGANIZATION_ID", yml);
        Assert.Contains("vars.SIGNPATH_PROJECT_SLUG", yml);
        Assert.Contains("vars.SIGNPATH_SIGNING_POLICY_SLUG", yml);
        Assert.Contains("vars.SIGNPATH_DLL_ARTIFACT_CONFIG_SLUG", yml);
        Assert.Contains("vars.SIGNPATH_APP_ARTIFACT_CONFIG_SLUG", yml);
        Assert.Contains("windres", yml);
        Assert.Contains("msystem: UCRT64", yml);
        Assert.Contains("actions/checkout@v6", yml);
        Assert.Contains("actions/setup-dotnet@v6", yml);
        Assert.Contains("actions/upload-artifact@v7", yml);
        Assert.Contains("wait-for-completion-timeout-in-seconds: 3600", yml);
        // Unsigned path must still publish when the flag is off: publish / manifest steps
        // are not gated on SIGNPATH_ENABLED, only on not being a pull_request.
        Assert.Contains("./scripts/build.ps1", yml);
        Assert.Contains("make-release-manifest.ps1", yml);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent!)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LICENSE")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "docs")))
                    return dir.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the AudioTaskbar repository root from " + AppContext.BaseDirectory);
        }
    }
}
