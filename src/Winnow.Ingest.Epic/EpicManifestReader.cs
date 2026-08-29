using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Reads <c>Data\Manifests\*.item</c> -- the authoritative source for which Epic
/// titles are installed. Read-only; never throws for missing files.
/// </summary>
public sealed class EpicManifestReader
{
    private readonly ILogger<EpicManifestReader> _logger;

    /// <param name="logger">Optional logger.</param>
    public EpicManifestReader(ILogger<EpicManifestReader>? logger = null)
        => _logger = logger ?? NullLogger<EpicManifestReader>.Instance;

    /// <summary>
    /// Reads every <c>.item</c> in a manifests directory. Returns an empty list
    /// when the directory does not exist. The sibling <c>Pending\</c> directory
    /// is not descended into — those are in-flight installs the launcher has not
    /// committed.
    /// </summary>
    public IReadOnlyList<EpicManifest> ReadDirectory(string manifestsDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifestsDirectory);

        if (!Directory.Exists(manifestsDirectory))
        {
            _logger.LogDebug("Epic manifests directory {Path} does not exist", manifestsDirectory);
            return [];
        }

        var manifests = new List<EpicManifest>();
        IEnumerable<string> files;
        try
        {
            // Top-level only: Pending\ holds queued installs, not completed ones.
            files = Directory.EnumerateFiles(manifestsDirectory, "*.item", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate Epic manifests under {Path}", manifestsDirectory);
            return [];
        }

        foreach (var file in files)
        {
            // On Windows a glob with a short extension also matches longer ones
            // (the 8.3 short-name rule), so "*.item" can return "*.itemx".
            if (!file.EndsWith(".item", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var manifest = Read(file);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    /// <summary>
    /// Reads one <c>.item</c> file, or null when it is missing, unreadable or not
    /// a manifest (no <c>CatalogItemId</c>).
    /// </summary>
    public EpicManifest? Read(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var catalogItemId = EpicJson.String(root, "CatalogItemId");
            if (catalogItemId.Length == 0)
            {
                _logger.LogWarning("Epic manifest {Path} has no CatalogItemId; skipping", manifestPath);
                return null;
            }

            return new EpicManifest(
                CatalogItemId: catalogItemId,
                CatalogNamespace: EpicJson.String(root, "CatalogNamespace"),
                AppName: EpicJson.String(root, "AppName"),
                DisplayName: EpicJson.String(root, "DisplayName"),
                InstallLocation: EpicJson.String(root, "InstallLocation"),
                LaunchExecutable: EpicJson.String(root, "LaunchExecutable"),
                AppVersionString: EpicJson.String(root, "AppVersionString"),
                InstallSize: EpicJson.Int64(root, "InstallSize"),
                IsIncompleteInstall: EpicJson.Bool(root, "bIsIncompleteInstall"),
                MainGameCatalogItemId: EpicJson.String(root, "MainGameCatalogItemId"),
                MainGameAppName: EpicJson.String(root, "MainGameAppName"),
                AppCategories: EpicJson.StringArray(root, "AppCategories"),
                InstallationGuid: EpicJson.String(root, "InstallationGuid"),
                ManifestPath: manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A manifest being rewritten under us, or a truncated file, costs one
            // game — never the whole scan.
            _logger.LogWarning(ex, "Could not read Epic manifest {Path}; skipping", manifestPath);
            return null;
        }
    }
}
