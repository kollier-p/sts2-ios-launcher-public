using System.Text;

namespace STS2Mobile.Platform.Tests;

public sealed class ModImportTests
{
    private static readonly byte[] Art = [0x52, 0x53, 0x52, 0x43, 1, 2, 3, 4];

    [Theory]
    [InlineData("hack.gd")]
    [InlineData("hack.gdc")]
    [InlineData("x.gdextension")]
    [InlineData(".godot/global_script_class_cache.cfg")]
    [InlineData("Launcher.tscn")]
    [InlineData("../x.png")]
    public void ModContentsAreLeftToGame(string path)
    {
        using var pack = new MemoryStream(TestPack.Build((path, Art)));
        ModImport.CheckPack(pack);
    }

    [Fact]
    public void OlderPackFormatIsAcceptedForGameToEvaluate()
    {
        using var pack = new MemoryStream(TestPack.Make([("a.png", Art)], format: 2));
        ModImport.CheckPack(pack);
    }

    [Fact]
    public void EncryptedAndRemovalFlagsAreNotImportPolicy()
    {
        using var encrypted = new MemoryStream(TestPack.Make([("a.png", Art)], packFlags: 1));
        using var removal = new MemoryStream(TestPack.Make([("a.png", Art)], fileFlags: 2));
        ModImport.CheckPack(encrypted);
        ModImport.CheckPack(removal);
    }

    [Fact]
    public void InvalidMagicIsRefused()
    {
        using var pack = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        Assert.Throws<InvalidDataException>(() => ModImport.CheckPack(pack));
    }

    [Fact]
    public void TruncatedHeaderIsRefused()
    {
        using var pack = new MemoryStream([1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => ModImport.CheckPack(pack));
    }
}
