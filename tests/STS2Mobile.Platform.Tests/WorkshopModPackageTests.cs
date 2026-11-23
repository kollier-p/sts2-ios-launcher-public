using System.IO.Compression;
using System.Text;

namespace STS2Mobile.Platform.Tests;

public sealed class WorkshopModPackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workshop-package-").FullName;
    private string Source => Path.Combine(_root, "workshop.zip");
    private string Output => Path.Combine(_root, "installable.zip");
    private string Mods => Path.Combine(_root, "mods");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Build(params (string Name, byte[] Bytes)[] entries) => Zip(Source, entries);

    private static void Zip(string path, params (string Name, byte[] Bytes)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(bytes);
        }
    }

    private static byte[] Manifest(string id, bool dll = true, bool pck = false) =>
        Encoding.UTF8.GetBytes($"{{\"id\":\"{id}\",\"has_dll\":{dll.ToString().ToLowerInvariant()},\"has_pck\":{pck.ToString().ToLowerInvariant()}}}");

    [Fact]
    public void OtherJsonAndOtherFilesDoNotCountAsModManifests()
    {
        byte[] pack = TestPack.Build(("scripts/mod.gd", [1, 2, 3]));
        byte[] code = [4, 5, 6];
        Build(
            ("workshop/Example/Example.json", Manifest("Example", pck: true)),
            ("workshop/Example/Example.dll", code),
            ("workshop/Example/Example.pck", pack),
            ("workshop/Example/config.json", Encoding.UTF8.GetBytes("""{"volume":50}""")),
            ("workshop/Example/images/labels.json", Encoding.UTF8.GetBytes("""{"hello":"world"}""")),
            ("workshop/README.md", Encoding.UTF8.GetBytes("ignore me"))
        );

        var selected = WorkshopModPackage.Normalize(Source, Output);
        Assert.Equal("Example", selected.Id);
        Assert.False(selected.GeneratedManifest);
        using (var zip = ZipFile.OpenRead(Output))
            Assert.Equal(["Example.dll", "Example.json", "Example.pck"],
                zip.Entries.Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        var installed = ModPackageImport.Install(Output, Mods, replaceConfirmed: false, codeConfirmed: true);
        Assert.Equal(ModImportStatus.Installed, installed.Status);
        Assert.Equal(code, File.ReadAllBytes(Path.Combine(Mods, "Example", "Example.dll")));
        Assert.Equal(pack, File.ReadAllBytes(Path.Combine(Mods, "Example", "Example.pck")));
    }

    [Fact]
    public void AlternateManifestFilenameIsNormalizedWithoutRewritingJson()
    {
        var manifest = Manifest("Test");
        Build(
            ("folder/mod_manifest.json", manifest),
            ("folder/Test.dll", [9]),
            ("folder/settings.json", Encoding.UTF8.GetBytes("""{"theme":"dark"}""")));
        var selected = WorkshopModPackage.Normalize(Source, Output);
        Assert.Equal("Test", selected.Id);
        using var zip = ZipFile.OpenRead(Output);
        var entry = Assert.Single(zip.Entries, x => x.Name == "Test.json");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Assert.Equal(manifest, buffer.ToArray());
    }

    [Fact]
    public void LegacyWorkshopItemWithOneAssemblyGeneratesMinimalManifest()
    {
        Build(("old/Classic.dll", [7, 8]), ("old/Classic.pck", TestPack.Build(("image.png", [1]))),
            ("old/settings.json", Encoding.UTF8.GetBytes("""{"volume":1}""")));
        var selected = WorkshopModPackage.Normalize(Source, Output);
        Assert.Equal("Classic", selected.Id);
        Assert.True(selected.GeneratedManifest);
        Assert.Equal(ModImportStatus.Installed, ModPackageImport.Install(Output, Mods, false, true).Status);
    }

    [Fact]
    public void SteamPipeNestedZipIsUnwrapped()
    {
        string inner = Path.Combine(_root, "inner.zip");
        Zip(inner, ("Mod/Mod.json", Manifest("Mod")), ("Mod/Mod.dll", [11, 12]));
        Build(("published.zip", File.ReadAllBytes(inner)));
        Assert.Equal("Mod", WorkshopModPackage.Normalize(Source, Output).Id);
        Assert.Equal(ModImportStatus.Installed, ModPackageImport.Install(Output, Mods, false, true).Status);
    }

    [Fact]
    public void MultipleRealModManifestsRequireExplicitChoice()
    {
        Build(("First.json", Manifest("First")), ("First.dll", [1]),
            ("Second.json", Manifest("Second")), ("Second.dll", [2]));
        Assert.Contains("multiple mod manifests", Assert.Throws<InvalidDataException>(
            () => WorkshopModPackage.Normalize(Source, Output)).Message);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void BaseLibAndItsBundledCompatibilityHelperAreBothPreparedAndInstalled()
    {
        byte[] baseManifest = Encoding.UTF8.GetBytes("""
            {"id":"BaseLib","has_dll":true,"has_pck":true,
             "dependencies":[{"id":"BaseLibCompatibilityHelper","min_version":"0.0.0"}]}
            """);
        byte[] helperManifest = Manifest("BaseLibCompatibilityHelper");
        byte[] basePack = TestPack.Build(("scripts/a.gd", [1]));
        Build(
            ("BaseLib/BaseLib.json", baseManifest),
            ("BaseLib/BaseLib.dll", [3, 4]),
            ("BaseLib/BaseLib.pck", basePack),
            ("BaseLib/BaseLibCompatibilityHelper.json", helperManifest),
            ("BaseLib/BaseLibCompatibilityHelper.dll", [5, 6]),
            ("BaseLib/localization/eng/settings.json", Encoding.UTF8.GetBytes("""{"description":"not a manifest"}""")));

        string folder = Path.Combine(_root, "prepared");
        var packages = WorkshopModPackage.NormalizeAll(Source, folder);
        Assert.Equal(2, packages.Count);
        Assert.Equal("BaseLibCompatibilityHelper", packages[0].Mod.Id);
        Assert.Equal("BaseLib", packages[1].Mod.Id);
        foreach (var package in packages)
        {
            Assert.False(package.Mod.GeneratedManifest);
            Assert.Equal(ModImportStatus.Installed,
                ModPackageImport.Install(package.Zip, Mods, replaceConfirmed: false, codeConfirmed: true).Status);
        }
        Assert.True(File.Exists(Path.Combine(Mods, "BaseLib", "BaseLib.pck")));
        Assert.True(File.Exists(Path.Combine(Mods, "BaseLibCompatibilityHelper", "BaseLibCompatibilityHelper.dll")));
    }

    [Fact]
    public void OneModInWorkshopStillProducesOnePackage()
    {
        Build(("Tiny/Tiny.json", Manifest("Tiny")), ("Tiny/Tiny.dll", [9]));
        var packages = WorkshopModPackage.NormalizeAll(Source, Path.Combine(_root, "single"));
        var package = Assert.Single(packages);
        Assert.Equal("Tiny", package.Mod.Id);
        Assert.Equal(ModImportStatus.Installed,
            ModPackageImport.Install(package.Zip, Mods, replaceConfirmed: false, codeConfirmed: true).Status);
    }

    [Fact]
    public void DuplicateManifestIdsAreRejectedRatherThanArbitrarilySelected()
    {
        Build(("one/BaseLib.json", Manifest("BaseLib")),
            ("two/BaseLib.json", Manifest("BaseLib")),
            ("one/BaseLib.dll", [1]), ("two/BaseLib.dll", [2]));
        Assert.Contains("multiple manifests for BaseLib",
            Assert.Throws<InvalidDataException>(() =>
                WorkshopModPackage.NormalizeAll(Source, Path.Combine(_root, "ambiguous"))).Message);
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("folder/../../escape.dll")]
    [InlineData("/absolute.json")]
    [InlineData("C:\\absolute.dll")]
    public void InvalidPathsAreNeverUsed(string name)
    {
        Build((name, [1]), ("Okay.json", Manifest("Okay")), ("Okay.dll", [2]));
        Assert.Throws<InvalidDataException>(() => WorkshopModPackage.Normalize(Source, Output));
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void MissingDeclaredAssemblyIsReported()
    {
        Build(("Mod.json", Manifest("Mod")), ("other.json", Encoding.UTF8.GetBytes("""{"x":1}""")));
        var error = Assert.Throws<InvalidDataException>(() => WorkshopModPackage.Normalize(Source, Output));
        Assert.Contains("Mod.dll", error.Message);
    }
}
