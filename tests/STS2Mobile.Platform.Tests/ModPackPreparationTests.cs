using System.Text;

namespace STS2Mobile.Platform.Tests;

/// <summary>Pack preparation leaves Godot compatibility decisions to the game.</summary>
public sealed class ModPackPreparationTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("sts2-pack-preparation-").FullName;
    private string Input => Path.Combine(_folder, "input.pck");
    private string Output => Path.Combine(_folder, "output.pck");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ResourcePackIsCopiedWithoutChanges()
    {
        byte[] pack = TestPack.Build(("images/card.png", [1, 2, 3]));
        File.WriteAllBytes(Input, pack);
        Assert.Empty(ModPackPreparation.Prepare(Input, Output));
        Assert.Equal(pack, File.ReadAllBytes(Input));
        Assert.Equal(pack, File.ReadAllBytes(Output));
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }

    [Theory]
    [InlineData("project.binary")]
    [InlineData(".godot/global_script_class_cache.cfg")]
    [InlineData("QuickRestart.cs")]
    [InlineData("lib.dll")]
    [InlineData("addons/plugin.gd")]
    [InlineData("Launcher.tscn")]
    public void CodeAndEngineResourcesArePreserved(string path)
    {
        byte[] pack = TestPack.Build((path, Encoding.UTF8.GetBytes("original payload")));
        File.WriteAllBytes(Input, pack);
        File.WriteAllText(Output, "previous output");
        Assert.Empty(ModPackPreparation.Prepare(Input, Output));
        Assert.Equal(pack, File.ReadAllBytes(Input));
        Assert.Equal(pack, File.ReadAllBytes(Output));
    }

    [Fact]
    public void OldPackFormatsAreCopiedForGameToDecide()
    {
        byte[] pack = TestPack.Make([("images/card.png", new byte[] { 1, 2, 3 })], format: 2);
        File.WriteAllBytes(Input, pack);
        Assert.Empty(ModPackPreparation.Prepare(Input, Output));
        Assert.Equal(pack, File.ReadAllBytes(Output));
    }

    [Fact]
    public void CorruptMagicLeavesPreviousOutputUntouched()
    {
        File.WriteAllBytes(Input, [1, 2, 3, 4, 5]);
        File.WriteAllText(Output, "previous output");
        Assert.Throws<InvalidDataException>(() => ModPackPreparation.Prepare(Input, Output));
        Assert.Equal("previous output", File.ReadAllText(Output));
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public void InputCannotBeItsOwnOutput()
    {
        byte[] pack = TestPack.Build(("images/card.png", [1, 2, 3]));
        File.WriteAllBytes(Input, pack);
        Assert.Throws<ArgumentException>(() => ModPackPreparation.Prepare(Input, Input));
        Assert.Equal(pack, File.ReadAllBytes(Input));
    }
}
