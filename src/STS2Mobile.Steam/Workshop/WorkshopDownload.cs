using System.IO.Compression;
using SteamKit2;
using STS2Mobile.Steam.Connection;
using STS2Mobile.Steam.Depot;
using STS2Mobile.Steam.Network;

namespace STS2Mobile.Steam.Workshop;

/// <summary>
/// Downloads a Workshop item's SteamPipe manifest (or its direct ZIP URL) into an
/// isolated temporary ZIP. The existing mod importer owns manifest validation,
/// code consent, replacement consent, and final installation.
/// </summary>
public static class WorkshopDownload
{
    /// <summary>Returns a temporary ZIP that the normal mod importer can consume.</summary>
    public static async Task<string> DownloadZipAsync(SteamConnection connection, WorkshopItem item, string stagingFolder,
        Action<long, long>? progress, CancellationToken cancellationToken)
    {
        if (item.Id == 0) throw new InvalidDataException("Invalid Workshop item ID.");
        Directory.CreateDirectory(stagingFolder);
        string archive = Path.Combine(stagingFolder, item.Id + ".zip");

        if (!string.IsNullOrEmpty(item.DirectUrl))
        {
            // Steam sometimes publishes one archive as an ordinary HTTPS file.
            // Never follow redirects to arbitrary servers, or save without a size cap.
            var uri = new Uri(item.DirectUrl, UriKind.Absolute);
            SteamNetworkPolicy.RequireAllowed(uri);
            using var http = SteamHttpClient.Create(TimeSpan.FromMinutes(5));
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > ModImportLimit)
                throw new InvalidDataException("Workshop download exceeds the 1 GB mod import limit.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var dest = File.Create(archive))
            {
                byte[] buffer = new byte[64 * 1024];
                long done = 0;
                while (true)
                {
                    int got = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (got == 0) break;
                    done += got;
                    if (done > ModImportLimit) throw new InvalidDataException("Workshop download exceeds the 1 GB mod import limit.");
                    await dest.WriteAsync(buffer.AsMemory(0, got), cancellationToken).ConfigureAwait(false);
                    progress?.Invoke(done, response.Content.Headers.ContentLength ?? done);
                }
            }
            using var zip = ZipFile.OpenRead(archive); // A loose PCK lacks the required mod manifest.
            return archive;
        }

        // Prefer Steam's current manifest; older GetDetails content handles may name
        // an obsolete UGC revision rather than the files presently served.
        ulong manifestId = 0;
        try { manifestId = await new WorkshopCatalog(connection).GetCurrentManifestAsync(item.Id, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { /* Fall back to the legacy GetDetails handle. */ }
        if (manifestId == 0) manifestId = item.ContentHandle;
        if (manifestId == 0)
            throw new InvalidDataException("Steam did not provide a downloadable file or Workshop manifest for this item.");

        // In SteamPipe Workshop the content handle is a depot manifest, normally
        // under a workshop depot bearing the consumer game's app ID.
        using var depot = await SteamDepot.OpenAsync(connection, SteamApp.Id, SteamApp.Id,
            manifestId, "public", cancellationToken).ConfigureAwait(false);
        // Steam lists directories as manifest entries even if their names have no
        // trailing slash (e.g. "BaseLib"). Never download them as zero-byte files:
        // that would block the real BaseLib/... paths from being created.
        var plan = BuildDownloadPlan(depot.Manifest, stagingFolder);

        await new DepotDownload(depot, plan, progress).RunAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // A ZIP, not loose files, enters the same consent-and-import path as Files.
        await using (var stream = File.Create(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetRelativePath(Path.Combine(stagingFolder, "files"), entry.Path).Replace('\\', '/');
                var destination = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var output = destination.Open();
                await using var input = File.OpenRead(entry.Path);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        return archive;
    }

    /// <summary>
    /// Validates a SteamPipe manifest and selects regular files. Directory
    /// entries are identified by EDepotFileFlag.Directory, not only by a
    /// slash suffix. No directories or symbolic links enter the ZIP.
    /// </summary>
    public static IReadOnlyList<DepotFile> BuildDownloadPlan(DepotManifest manifest, string stagingFolder)
    {
        var entries = manifest.Files ?? throw new InvalidDataException("Steam returned no Workshop file list.");
        if (entries.Count > 2048)
            throw new InvalidDataException("The Workshop item contains too many entries.");

        var plan = new List<DepotFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in entries)
        {
            if (entry.Flags.HasFlag(EDepotFileFlag.Symlink) || !string.IsNullOrEmpty(entry.LinkTarget))
                throw new InvalidDataException("The Workshop item contains a symbolic link.");

            // Steam's directory flag is authoritative; directories often have
            // no trailing slash, so suffix-only detection can create a file
            // where an actual subdirectory needs to exist.
            if (entry.Flags.HasFlag(EDepotFileFlag.Directory)
                || entry.FileName.EndsWith("/", StringComparison.Ordinal)
                || entry.FileName.EndsWith("\\", StringComparison.Ordinal))
                continue;

            string name = ValidatePath(entry.FileName);
            if (!names.Add(name))
                throw new InvalidDataException("Workshop item has duplicate file paths.");
            if (entry.TotalSize > (ulong)(ModImportLimit - total))
                throw new InvalidDataException("The Workshop item exceeds the 1 GB mod import limit.");
            total += (long)entry.TotalSize;
            plan.Add(new DepotFile(entry,
                Path.Combine(stagingFolder, "files", name.Replace('/', Path.DirectorySeparatorChar)), null));
        }

        if (plan.Count == 0) throw new InvalidDataException("The Workshop item has no files.");
        // Workshop uploads may have auxiliary files; the mod ZIP normalizer
        // selects the mod files before applying the smaller manual ZIP limit.
        if (plan.Count > 512)
            throw new InvalidDataException("The Workshop item contains too many files.");

        // A malformed manifest must not write a file named "BaseLib" and
        // subsequently try to create a folder of the same name.
        foreach (var name in names)
        {
            int slash = name.IndexOf('/');
            while (slash >= 0)
            {
                if (names.Contains(name[..slash]))
                    throw new InvalidDataException("The Workshop item uses the same path as a file and a folder.");
                slash = name.IndexOf('/', slash + 1);
            }
        }
        return plan;
    }

    private const long ModImportLimit = 1L << 30;

    public static string ValidatePath(string stored)
    {
        string path = stored.Replace('\\', '/');
        if (path.Length == 0 || path.StartsWith('/') || path.Contains(':') || path.Any(char.IsControl)
            || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("Workshop item has an unsafe file path.");
        return path;
    }
}
