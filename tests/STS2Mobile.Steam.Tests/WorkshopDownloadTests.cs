using STS2Mobile.Steam.Workshop;

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
}
