using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace STS2Mobile.Platform;

/// <summary>
/// Adapts a Steam Workshop download to the launcher's single-mod ZIP importer.
/// Workshop items may contain descriptions, settings JSON, thumbnails, debug
/// symbols and an enclosing folder. Those are not additional mod manifests.
/// The selected DLL/PCK/manifest bytes are not modified.
/// </summary>
public static class WorkshopModPackage
{
    private const int MaxWorkshopEntries = 512;
    private const long MaxWorkshopBytes = ModImport.MaxUncompressedBytes;

    public sealed record Selection(string Id, bool GeneratedManifest);

    public static Selection Normalize(string downloadedZip, string installableZip)
    {
        if (Path.GetFullPath(downloadedZip).Equals(Path.GetFullPath(installableZip), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a separate output ZIP.", nameof(installableZip));
        return NormalizeCore(downloadedZip, installableZip, allowNestedZip: true);
    }

    /// <summary>
    /// A Steam Workshop publication can contain several actual mods. For example,
    /// BaseLib ships a BaseLibCompatibilityHelper with its own manifest. Prepare
    /// a distinct single-mod ZIP for each id, preserving the normal import checks
    /// and asking for code/replacement consent for each mod separately.
    /// </summary>
    public static IReadOnlyList<(Selection Mod, string Zip)> NormalizeAll(string downloadedZip, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        return NormalizeAllCore(downloadedZip, outputFolder, allowNestedZip: true);
    }

    private static IReadOnlyList<(Selection Mod, string Zip)> NormalizeAllCore(
        string input, string outputFolder, bool allowNestedZip)
    {
        using var zip = ZipFile.OpenRead(input);
        if (zip.Entries.Count > MaxWorkshopEntries)
            throw new InvalidDataException("The Workshop download contains too many entries.");

        var manifestEntries = new List<(string Id, string Path, string[] Dependencies)>();
        var nonDirectories = new List<ZipArchiveEntry>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/') || path.Contains(':') || path.Any(char.IsControl)
                || path.Split('/').Any(part => part is "." or "..")
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The Workshop download contains an unsafe path.");
            if (entry.Length > MaxWorkshopBytes - total)
                throw new InvalidDataException("The Workshop download exceeds the 1 GB import limit.");
            total += entry.Length;
            if (path.EndsWith('/') || path.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                continue;
            nonDirectories.Add(entry);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || entry.Length > 65536)
                continue;

            try
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                string json = reader.ReadToEnd();
                var (id, _, _) = ModPackageImport.ReadManifest(json);
                if (ModPackageImport.IsReservedId(id)) continue;
                var dependencies = new List<string>();
                using var parsed = JsonDocument.Parse(json);
                if (parsed.RootElement.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                    foreach (var dependency in deps.EnumerateArray())
                        if (dependency.ValueKind == JsonValueKind.Object
                            && dependency.TryGetProperty("id", out var depId)
                            && depId.ValueKind == JsonValueKind.String
                            && depId.GetString() is { } name && ModFiles.IsValidId(name))
                            dependencies.Add(name);
                manifestEntries.Add((id, path, dependencies.ToArray()));
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                // Auxiliary JSON is not a mod manifest.
            }
        }

        // A single ZIP supplied through SteamPipe is a container, not a mod.
        if (allowNestedZip && nonDirectories.Count == 1
            && nonDirectories[0].FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string nested = Path.Combine(outputFolder, "workshop-nested-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var target = File.Create(nested))
                using (var source = nonDirectories[0].Open()) source.CopyTo(target);
                return NormalizeAllCore(nested, outputFolder, allowNestedZip: false);
            }
            finally { if (File.Exists(nested)) File.Delete(nested); }
        }

        // No manifests: use the existing strict one-mod legacy inference.
        if (manifestEntries.Count == 0)
        {
            string output = Path.Combine(outputFolder, "workshop-mod-1.zip");
            return [(NormalizeCore(input, output, allowNestedZip: false), output)];
        }
        if (manifestEntries.Count > 32)
            throw new InvalidDataException("A Workshop item has too many separate mods.");
        var byId = new Dictionary<string, (string Path, string[] Dependencies)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifestEntries)
            if (!byId.TryAdd(entry.Id, (entry.Path, entry.Dependencies)))
                throw new InvalidDataException($"The Workshop download has multiple manifests for {entry.Id}.");

        // Install bundled dependencies ahead of the mods that declare them.
        var ordered = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id))
                throw new InvalidDataException("The Workshop mod dependencies contain a cycle.");
            foreach (string dependency in byId[id].Dependencies)
                if (byId.ContainsKey(dependency)) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(id);
        }
        foreach (string id in byId.Keys) Visit(id);

        var results = new List<(Selection Mod, string Zip)>();
        try
        {
            foreach (string id in ordered)
            {
                string path = Path.Combine(outputFolder, $"workshop-mod-{results.Count + 1}.zip");
                var selection = NormalizeCore(input, path, allowNestedZip: false, requestedId: id);
                results.Add((selection, path));
            }
            return results;
        }
        catch
        {
            foreach (var item in results) if (File.Exists(item.Zip)) File.Delete(item.Zip);
            throw;
        }
    }

    private static Selection NormalizeCore(string input, string output, bool allowNestedZip, string? requestedId = null)
    {
        using var zip = ZipFile.OpenRead(input);
        var entries = new List<(string Path, ZipArchiveEntry Entry)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        if (zip.Entries.Count > MaxWorkshopEntries)
            throw new InvalidDataException("The Workshop download contains too many entries.");

        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/') || path.Contains(':') || path.Any(char.IsControl)
                || path.Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException("The Workshop download contains an unsafe path.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The Workshop download contains a symbolic link.");
            if (entry.Length > MaxWorkshopBytes - total)
                throw new InvalidDataException("The Workshop download exceeds the 1 GB import limit.");
            total += entry.Length;
            if (path.EndsWith('/') || path.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path) is ".DS_Store" or "Thumbs.db")
                continue;
            if (!seen.Add(path)) throw new InvalidDataException("The Workshop download contains duplicate file paths.");
            entries.Add((path, entry));
        }

        // SteamPipe sometimes delivers one ZIP as the sole file. In that case
        // inspect its contents instead of zipping the ZIP again.
        if (allowNestedZip && entries.Count == 1 && entries[0].Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string inner = Path.Combine(Path.GetTempPath(), "sts2-workshop-inner-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var dest = File.Create(inner))
                using (var source = entries[0].Entry.Open()) source.CopyTo(dest);
                return NormalizeCore(inner, output, allowNestedZip: false);
            }
            finally { if (File.Exists(inner)) File.Delete(inner); }
        }

        var candidates = new List<(string Id, bool Dll, bool Pck, string Path, ZipArchiveEntry Entry)>();
        foreach (var (path, entry) in entries)
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || entry.Length > 65536) continue;
            try
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var (manifestId, manifestDll, manifestPack) = ModPackageImport.ReadManifest(reader.ReadToEnd());
                if (!ModPackageImport.IsReservedId(manifestId))
                    candidates.Add((manifestId, manifestDll, manifestPack, path, entry));
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                // Other JSON in the Workshop item is not necessarily a mod manifest.
            }
        }

        if (requestedId != null)
            candidates = candidates.Where(c => c.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count > 1)
            throw new InvalidDataException("This Workshop item contains multiple mod manifests for the same mod.");
        if (requestedId != null && candidates.Count == 0)
            throw new InvalidDataException($"The Workshop item is missing the expected manifest for {requestedId}.");
        string id;
        bool dll, pack;
        string folder;
        ZipArchiveEntry? manifest = null;
        bool generated = false;
        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            (id, dll, pack, manifest) = (candidate.Id, candidate.Dll, candidate.Pck, candidate.Entry);
            folder = Parent(candidate.Path);
        }
        else
        {
            // Some old Workshop uploads have a DLL/PCK but no sidecar JSON.
            // Only infer a manifest if the entire upload unambiguously names
            // one mod; never pick an arbitrary DLL out of multiple assemblies.
            var stems = entries.Where(e => e.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                            || e.Path.EndsWith(".pck", StringComparison.OrdinalIgnoreCase))
                .Select(e => Path.GetFileNameWithoutExtension(e.Path))
                .Where(ModFiles.IsValidId)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (stems.Length != 1 || ModPackageImport.IsReservedId(stems[0]))
                throw new InvalidDataException("No usable STS2 mod manifest was found. The Workshop item may contain multiple mods or no installable DLL/PCK.");
            id = stems[0];
            folder = "";
            dll = entries.Any(e => e.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            pack = entries.Any(e => e.Path.EndsWith(".pck", StringComparison.OrdinalIgnoreCase));
            generated = true;
        }

        ZipArchiveEntry? modDll = dll ? Locate(entries, id + ".dll", folder, required: true) : null;
        ZipArchiveEntry? modPack = pack ? Locate(entries, id + ".pck", folder, required: true) : null;
        ZipArchiveEntry? modConfig = Locate(entries, id + ".cfg", folder, required: false);

        // Never publish a half-formed ZIP that could replace a working mod.
        string partial = output + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = File.Create(partial))
            using (var installable = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                if (generated)
                {
                    string escaped = JsonEncodedText.Encode(id).ToString();
                    string json = $"{{\"id\":\"{escaped}\",\"has_dll\":{(dll ? "true" : "false")},\"has_pck\":{(pack ? "true" : "false")}}}";
                    var entry = installable.CreateEntry(id + ".json");
                    using var dest = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    dest.Write(json);
                }
                else Copy(manifest!, installable, id + ".json");
                if (modDll != null) Copy(modDll, installable, id + ".dll");
                if (modPack != null) Copy(modPack, installable, id + ".pck");
                if (modConfig != null) Copy(modConfig, installable, id + ".cfg");
            }
            File.Move(partial, output, overwrite: true);
            return new Selection(id, generated);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private static ZipArchiveEntry? Locate(
        List<(string Path, ZipArchiveEntry Entry)> entries, string name, string parent, bool required)
    {
        var matches = entries.Where(e => string.Equals(Path.GetFileName(e.Path), name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var alongside = matches.Where(e => Parent(e.Path) == parent).ToArray();
        if (alongside.Length == 1) return alongside[0].Entry;
        if (alongside.Length > 1 || matches.Length > 1)
            throw new InvalidDataException("Multiple Workshop files could be " + name + ".");
        if (matches.Length == 1) return matches[0].Entry;
        if (required) throw new InvalidDataException("The Workshop mod declares " + name + " but it was not downloaded.");
        return null;
    }

    private static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    private static void Copy(ZipArchiveEntry source, ZipArchive destination, string name)
    {
        var entry = destination.CreateEntry(name, CompressionLevel.Optimal);
        using var read = source.Open();
        using var write = entry.Open();
        read.CopyTo(write);
    }
}
