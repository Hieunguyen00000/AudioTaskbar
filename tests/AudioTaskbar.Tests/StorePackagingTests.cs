using System.Drawing;
using System.Xml.Linq;

namespace AudioTaskbar.Tests;

/// <summary>Microsoft Store / MSIX packaging contract and Store-edition behaviour flags.</summary>
public class StorePackagingTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

#if STORE_BUILD
    [Fact]
    public void Store_build_is_the_store_edition()
        => Assert.True(StoreEdition.IsStorePackage);
#else
    [Fact]
    public void Portable_test_host_is_not_a_store_package()
        => Assert.False(StoreEdition.IsStorePackage);

    [Fact]
    public void Portable_build_does_not_use_store_updates()
        => Assert.False(StoreEdition.UsesStoreUpdates);
#endif

    [Fact]
    public void Autostart_command_is_unchanged_for_portable_path()
        => Assert.Equal("\"C:\\A B\\AudioTaskbar.exe\" --minimized", AutoStart.Command(@"C:\A B\AudioTaskbar.exe"));

    [Fact]
    public void Startup_task_id_matches_the_manifest()
        => Assert.Equal("AudioTaskbarStartup", StoreEdition.StartupTaskId);

    [Fact]
    public void Store_update_label_is_stable()
        => Assert.Equal("Cập nhật tự động qua Microsoft Store", StoreEdition.StoreUpdateMessage);

    [Fact]
    public void Manifest_matches_partner_center_identity()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot, "packaging", "msix", "AppxManifest.xml"));
        var id = doc.Root!.Element(Ns + "Identity")!;
        Assert.Equal("HubertNguyen.AudioTaskbar", (string?)id.Attribute("Name"));
        Assert.Equal("CN=826CBF8C-383C-4259-B039-4075F5205C32", (string?)id.Attribute("Publisher"));
        Assert.Equal("5.3.3.0", (string?)id.Attribute("Version"));
        Assert.Equal("x64", (string?)id.Attribute("ProcessorArchitecture"));

        var props = doc.Root.Element(Ns + "Properties")!;
        Assert.Equal("Hubert Nguyen", props.Element(Ns + "PublisherDisplayName")!.Value);
        Assert.Equal("Audio Taskbar", props.Element(Ns + "DisplayName")!.Value);

        var family = doc.Root.Element(Ns + "Dependencies")!.Element(Ns + "TargetDeviceFamily")!;
        Assert.Equal("Windows.Desktop", (string?)family.Attribute("Name"));
        Assert.Equal("10.0.22000.0", (string?)family.Attribute("MinVersion"));
        Assert.Equal("10.0.26100.0", (string?)family.Attribute("MaxVersionTested"));

        var app = doc.Root.Element(Ns + "Applications")!.Element(Ns + "Application")!;
        Assert.Equal("AudioTaskbar.exe", (string?)app.Attribute("Executable"));
        Assert.Equal("mediumIL", (string?)app.Attribute(Uap10 + "TrustLevel"));
        Assert.Equal("packagedClassicApp", (string?)app.Attribute(Uap10 + "RuntimeBehavior"));

        var cap = doc.Root.Element(Ns + "Capabilities")!.Element(Rescap + "Capability");
        Assert.Equal("runFullTrust", (string?)cap!.Attribute("Name"));

        var task = doc.Descendants(Desktop + "StartupTask").Single();
        Assert.Equal("AudioTaskbarStartup", (string?)task.Attribute("TaskId"));
        Assert.Equal("false", (string?)task.Attribute("Enabled"));
        Assert.Equal("Audio Taskbar", (string?)task.Attribute("DisplayName"));
    }

    [Fact]
    public void Manifest_version_fourth_part_is_zero_and_matches_csproj()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot, "src", "AudioTaskbar.csproj"));
        string version = csproj.Root!.Elements().Where(e => e.Name.LocalName == "PropertyGroup")
            .SelectMany(g => g.Elements())
            .First(e => e.Name.LocalName == "Version" && e.Parent!.Attribute("Condition") is null).Value.Trim();
        var id = XDocument.Load(Path.Combine(RepoRoot, "packaging", "msix", "AppxManifest.xml"))
            .Root!.Element(Ns + "Identity")!;
        Assert.Equal(version + ".0", (string?)id.Attribute("Version"));
    }

    [Theory]
    [InlineData("StoreLogo.png", 50, 50)]
    [InlineData("StoreLogo.scale-200.png", 100, 100)]
    [InlineData("Square44x44Logo.png", 44, 44)]
    [InlineData("Square44x44Logo.scale-200.png", 88, 88)]
    [InlineData("Square44x44Logo.targetsize-48.png", 48, 48)]
    [InlineData("Square150x150Logo.png", 150, 150)]
    [InlineData("Square150x150Logo.scale-200.png", 300, 300)]
    [InlineData("Wide310x150Logo.png", 310, 150)]
    public void Package_asset_has_the_required_pixel_size(string name, int w, int h)
    {
        var path = Path.Combine(RepoRoot, "packaging", "msix", "Assets", name);
        Assert.True(File.Exists(path), path);
        using var img = Image.FromFile(path);
        Assert.Equal(w, img.Width);
        Assert.Equal(h, img.Height);
    }

    [Fact]
    public void Partner_center_tile_is_300x300()
    {
        var path = Path.Combine(RepoRoot, "packaging", "msix", "StoreListing", "StoreTile300x300.png");
        Assert.True(File.Exists(path), path);
        using var img = Image.FromFile(path);
        Assert.Equal(300, img.Width);
        Assert.Equal(300, img.Height);
    }

    [Fact]
    public void Privacy_policy_explains_store_managed_updates()
    {
        var privacy = File.ReadAllText(Path.Combine(RepoRoot, "PRIVACY.md"));
        Assert.Contains("Microsoft Store", privacy);
        Assert.Contains("does **not** contact GitHub", privacy);
        Assert.Contains("exclusively through the Microsoft Store", privacy);
    }

    [Fact]
    public void Build_msix_script_exists()
        => Assert.True(File.Exists(Path.Combine(RepoRoot, "scripts", "build-msix.ps1")));

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
