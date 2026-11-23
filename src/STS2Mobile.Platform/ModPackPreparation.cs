namespace STS2Mobile.Platform;

/// <summary>
/// Keeps imported PCK files byte-for-byte intact. The launcher does not
/// rewrite mod resources or decide whether a pack is compatible with Godot.
/// </summary>
public static class ModPackPreparation
{
    public const string PolicyVersion = "mod-pack-2";

    /// <summary>
    /// Copies the original PCK without filtering its resources. The returned
    /// omitted-path list is always empty.
    /// </summary>
    public static IReadOnlyList<string> Prepare(string inputPath, string outputPath)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pack preparation needs a separate output file.", nameof(outputPath));

        string temporary = outputPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = File.OpenRead(inputPath))
            {
                ModImport.CheckPack(input);
                input.Position = 0;
                using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, outputPath, overwrite: true);
            return [];
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
