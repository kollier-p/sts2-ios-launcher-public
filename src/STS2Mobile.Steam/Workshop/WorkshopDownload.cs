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
        var files = depot.Manifest.Files!.Where(f => !f.FileName.EndsWith("/", StringComparison.Ordinal)
            && !f.FileName.EndsWith("\\", StringComparison.Ordinal)).ToArray();
        if (files.Length == 0) throw new InvalidDataException("The Workshop item has no files.");
        if (files.Length > 64) throw new InvalidDataException("The Workshop item exceeds the 64-file ZIP import limit.");
        long total = 0;
        var plan = new List<DepotFile>(files.Length);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = ValidatePath(file.FileName);
            if (!used.Add(name)) throw new InvalidDataException("Workshop item has duplicate file paths.");
            if (file.TotalSize > (ulong)(ModImportLimit - total))
                throw new InvalidDataException("The Workshop item exceeds the 1 GB mod import limit.");
            total += (long)file.TotalSize;
            plan.Add(new DepotFile(file, Path.Combine(stagingFolder, "files", name.Replace('/', Path.DirectorySeparatorChar)), null));
        }

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
