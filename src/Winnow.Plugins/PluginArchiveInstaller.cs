using System.IO.Compression;
using System.Text.Json;

namespace Winnow.Plugins;

/// <summary>Installs ZIP packages without loading or enabling their code.</summary>
public static class PluginArchiveInstaller
{
    public const long MaximumArchiveBytes = 256L * 1024 * 1024;
    public const long MaximumUncompressedBytes = 256L * 1024 * 1024;
    public const int MaximumEntries = 2048;
    public const int MaximumPathCharacters = 1024;
    public const int MaximumPathDepth = 32;

    public static async Task<string?> TryInstallAsync(string archivePath, string userRoot,
        IReadOnlySet<string> installedIds, Action<string, string> reportIssue,
        CancellationToken cancellationToken = default)
    {
        string? staging = null;
        string? installedDirectory = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            userRoot = Path.GetFullPath(userRoot);
            RejectReparsePoint(userRoot);
            RejectReparsePoint(archivePath);
            await using (var input = File.OpenRead(archivePath))
            {
                Require(input.Length <= MaximumArchiveBytes, "The plugin ZIP exceeds the 256 MiB size limit.");
                using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                Require(zip.Entries.Count is > 0 and <= MaximumEntries, "The plugin ZIP must contain between 1 and 2048 entries.");
                var entries = ValidateEntries(zip, cancellationToken);
                var manifests = entries.Where(x => !x.Directory && Path.GetFileName(x.Path) == "plugin.json").ToArray();
                Require(manifests.Length == 1, "The plugin ZIP must contain exactly one plugin.json.");
                var manifestParts = manifests[0].Path.Split('/');
                Require(manifestParts.Length <= 2, "Place the plugin files at the ZIP root or inside one enclosing folder.");
                var prefix = manifestParts.Length == 2 ? manifestParts[0] + "/" : "";
                Require(prefix.Length == 0 || entries.All(x => x.Path.StartsWith(prefix, StringComparison.Ordinal)
                    || (x.Directory && x.Path + "/" == prefix)), "The plugin ZIP contains files outside its package folder.");

                staging = Path.Combine(userRoot, ".unpack-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                long copiedBytes = 0;
                var buffer = new byte[81920];
                foreach (var item in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = item.Path.Length < prefix.Length ? "" : item.Path[prefix.Length..];
                    if (relative.Length == 0) continue;
                    var destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (item.Directory)
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = item.Entry.Open();
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    long entryBytes = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        copiedBytes += count;
                        entryBytes += count;
                        Require(copiedBytes <= MaximumUncompressedBytes && entryBytes <= item.Entry.Length,
                            "The plugin ZIP exceeds its declared size or the 256 MiB unpacked size limit.");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    }
                    Require(entryBytes == item.Entry.Length, "The plugin ZIP contains an incomplete file.");
                }

                var manifest = await PluginManifestReader.ReadAsync(Path.Combine(staging, "plugin.json"), cancellationToken)
                    .ConfigureAwait(false);
                ValidateComponent(manifest.Id);
                Require(File.Exists(Path.Combine(staging, manifest.EntryAssembly)), "The plugin ZIP is missing its entry DLL.");
                Require(!installedIds.Any(id => string.Equals(id, manifest.Id, StringComparison.OrdinalIgnoreCase)),
                    "A plugin with this ID is already installed. ZIP imports do not replace existing plugins.");
                Require(!Directory.EnumerateFileSystemEntries(userRoot).Any(path =>
                    string.Equals(Path.GetFileName(path), manifest.Id, StringComparison.OrdinalIgnoreCase)),
                    "The plugin destination already exists. Remove its existing folder before installing a replacement.");
                cancellationToken.ThrowIfCancellationRequested();
                var destinationDirectory = Path.Combine(userRoot, manifest.Id);
                Directory.Move(staging, destinationDirectory);
                installedDirectory = destinationDirectory;
            }

            // Publication has succeeded. Retention problems must not hide the installed plugin.
            var archives = Path.Combine(userRoot, ".archives");
            RejectReparsePoint(archives);
            Directory.CreateDirectory(archives);
            File.Move(archivePath, Path.Combine(archives,
                Path.GetFileNameWithoutExtension(archivePath) + "-" + Guid.NewGuid().ToString("N") + ".zip"));
        }
        catch (Exception error) when (error is ArchiveValidationException or IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or ArgumentException or NotSupportedException)
        {
            reportIssue(archivePath, installedDirectory is null
                ? error is ArchiveValidationException ? "Could not unpack the plugin ZIP: " + error.Message
                    : "Could not unpack the plugin ZIP. Check that it is a valid, compatible plugin package and that the plugin folder is writable."
                : "The plugin was installed, but its ZIP could not be moved to .archives.");
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        return installedDirectory;
    }

    private static List<ArchiveItem> ValidateEntries(ZipArchive zip, CancellationToken cancellationToken)
    {
        var entries = new List<ArchiveItem>();
        var explicitPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, (string Spelling, bool Directory)>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName;
            Require(name.Length <= MaximumPathCharacters
                && name.Count(c => c == '/') + (name.EndsWith('/') ? 0 : 1) <= MaximumPathDepth,
                "The plugin ZIP paths must be at most 1024 characters and 32 components deep.");
            Require(!name.Contains('\\') && !name.StartsWith('/'), "The plugin ZIP contains an unsafe path.");
            var directory = name.EndsWith('/');
            var path = directory ? name[..^1] : name;
            var parts = path.Split('/');
            foreach (var component in parts) ValidateComponent(component);
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            Require(unixType is 0 or 0x8000 or 0x4000
                && (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) == 0,
                "The plugin ZIP contains a link or special filesystem entry.");
            Require(unixType != 0x4000 || directory, "The plugin ZIP contains inconsistent directory attributes.");
            Require(explicitPaths.Add(path), "The plugin ZIP contains duplicate or case-colliding paths.");
            for (var i = 1; i <= parts.Length; i++)
            {
                var partial = string.Join('/', parts.Take(i));
                var isDirectory = i < parts.Length || directory;
                if (paths.TryGetValue(partial, out var prior))
                    Require(prior.Spelling == partial && prior.Directory == isDirectory,
                        "The plugin ZIP contains conflicting paths.");
                else paths.Add(partial, (partial, isDirectory));
            }
            Require(entry.Length >= 0 && entry.Length <= MaximumUncompressedBytes - size,
                "The plugin ZIP exceeds the 256 MiB unpacked size limit.");
            size += entry.Length;
            Require(!directory || entry.Length == 0, "The plugin ZIP contains a directory with file data.");
            entries.Add(new ArchiveItem(entry, path, directory));
        }
        return entries;
    }

    private static void ValidateComponent(string component)
    {
        Require(component.Length is > 0 and <= 255 && component is not "." and not ".."
            && !component.EndsWith('.') && !component.EndsWith(' ')
            && !component.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)),
            "The plugin ZIP contains an unsafe filename.");
        var stem = component.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        Require(stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(stem[3])), "The plugin ZIP contains a reserved filename.");
    }

    private static void RejectReparsePoint(string path)
    {
        if (Path.Exists(path))
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0,
                "The plugin ZIP or installation directory must not be a filesystem link.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArchiveValidationException(message);
    }

    private sealed record ArchiveItem(ZipArchiveEntry Entry, string Path, bool Directory);
    private sealed class ArchiveValidationException(string message) : Exception(message);
}
