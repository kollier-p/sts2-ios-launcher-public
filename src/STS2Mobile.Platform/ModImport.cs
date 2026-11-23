namespace STS2Mobile.Platform;

/// <summary>
/// Import-time limits for archives and a minimal Godot pack identification check.
/// Pack compatibility and contents are deliberately left to the game engine.
/// </summary>
public static class ModImport
{
    public const long MaxArchiveEntries = 64;
    public const long MaxUncompressedBytes = 1L << 30;
    private const uint PackMagic = 0x43504447; // "GDPC"

    /// <summary>
    /// Verifies only that a picked file looks like a Godot PCK. Do not reject
    /// pack format revisions, script resources, engine cache files, native
    /// libraries, or other mod content here: the game decides compatibility.
    /// </summary>
    public static void CheckPack(Stream pack)
    {
        if (!pack.CanRead || !pack.CanSeek)
            throw new InvalidDataException("The .pck cannot be read.");
        if (pack.Length < sizeof(uint))
            throw new InvalidDataException("The .pck is not a Godot pack.");
        pack.Position = 0;
        using var reader = new BinaryReader(pack, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != PackMagic)
            throw new InvalidDataException("The .pck is not a Godot pack.");
    }
}
