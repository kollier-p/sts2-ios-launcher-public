using STS2Mobile.Steam.Workshop;
using SteamKit2;

namespace STS2Mobile.Steam.Tests;

public sealed class WorkshopDownloadTests
{
    [Theory]
    [InlineData("SomeMod/SomeMod.json")]
    [InlineData("SomeMod/SomeMod.pck")]
    [InlineData("SomeMod\\SomeMod.dll")]
    public void RelativeWorkshopPathsStayInStagingFolder(string path)
    {
        Assert.Equal(path.Replace('\\', '/'), WorkshopDownload.ValidatePath(path));
    }

    [Theory]
    [InlineData("../escape.pck")]
    [InlineData("folder/../../escape.dll")]
    [InlineData("/absolute.json")]
    [InlineData("C:\\outside.dll")]
    [InlineData("folder//double-slash.pck")]
    [InlineData("./file.json")]
    [InlineData("folder/./file.json")]
    [InlineData("folder/..")]
    public void UnsafeWorkshopPathsCannotEscapeStagingFolder(string path)
    {
        Assert.Throws<InvalidDataException>(() => WorkshopDownload.ValidatePath(path));
    }

    [Fact]
    public void SteamDirectoryFlagWithoutSlashDoesNotCreateAFile()
    {
        var manifest = new DepotManifest
        {
            Files =
            [
                new DepotManifest.FileData { FileName = "BaseLib", Flags = EDepotFileFlag.Directory },
                new DepotManifest.FileData { FileName = "BaseLib/BaseLib.json", TotalSize = 12 },
                new DepotManifest.FileData { FileName = "BaseLib/BaseLib.pck", TotalSize = 24 },
            ]
        };
        var files = WorkshopDownload.BuildDownloadPlan(manifest, "/tmp/sts2-workshop-test");

        Assert.Equal(2, files.Count);
        Assert.Equal("BaseLib.json", Path.GetFileName(files[0].Path));
        Assert.Equal("BaseLib.pck", Path.GetFileName(files[1].Path));
        Assert.Equal("BaseLib", Path.GetFileName(Path.GetDirectoryName(files[0].Path)));
    }

    [Fact]
    public void DirectoryEntriesEndingWithSlashAreAlsoSkipped()
    {
        var manifest = new DepotManifest
        {
            Files =
            [
                new DepotManifest.FileData { FileName = "BaseLib/", Flags = EDepotFileFlag.Directory },
                new DepotManifest.FileData { FileName = "BaseLib/nested/" },
                new DepotManifest.FileData { FileName = "BaseLib/nested/BaseLib.dll", TotalSize = 10 }
            ]
        };
        var files = WorkshopDownload.BuildDownloadPlan(manifest, "/tmp/sts2-workshop-test");
        Assert.Single(files);
        Assert.Equal("BaseLib.dll", Path.GetFileName(files[0].Path));
    }

    [Fact]
    public void SymlinkEntriesAreRejectedBeforeWritingFiles()
    {
        var manifest = new DepotManifest
        {
            Files =
            [
                new DepotManifest.FileData { FileName = "BaseLib/BaseLib.dll", TotalSize = 10 },
                new DepotManifest.FileData { FileName = "BaseLib/link", Flags = EDepotFileFlag.Symlink,
                    LinkTarget = "../outside" }
            ]
        };
        Assert.Throws<InvalidDataException>(() => WorkshopDownload.BuildDownloadPlan(manifest, "/tmp/sts2-workshop-test"));
    }

    [Fact]
    public void ActualFileFolderPathCollisionIsRejectedBeforeDownloading()
    {
        var manifest = new DepotManifest
        {
            Files =
            [
                new DepotManifest.FileData { FileName = "BaseLib" },
                new DepotManifest.FileData { FileName = "BaseLib/BaseLib.json" }
            ]
        };
        var error = Assert.Throws<InvalidDataException>(() =>
            WorkshopDownload.BuildDownloadPlan(manifest, "/tmp/sts2-workshop-test"));
        Assert.Contains("file and a folder", error.Message);
    }
}
