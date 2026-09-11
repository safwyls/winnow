using Winnow.Core.Ingest;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic;

/// <summary>A partial read can establish positive facts, but cannot establish absence.</summary>
public sealed record EpicManifestScan(IReadOnlyList<EpicManifest> Manifests, bool IsComplete, string? Fingerprint);

/// <summary>
/// Reads <c>Data\Manifests\*.item</c> -- the authoritative source for which Epic
/// titles are installed. Read-only; never throws for missing files.
/// </summary>
public sealed class EpicManifestReader
{
    private readonly ILogger<EpicManifestReader> _logger;
    private readonly StorefrontParserLimits _limits;
    private readonly Func<string, IEnumerable<string>> _enumerateFiles;

    /// <param name="logger">Optional logger.</param>
    public EpicManifestReader(ILogger<EpicManifestReader>? logger = null, StorefrontParserLimits? limits = null,
        Func<string, IEnumerable<string>>? enumerateFiles = null)
    {
        _logger = logger ?? NullLogger<EpicManifestReader>.Instance;
        _limits = limits ?? new StorefrontParserLimits();
        _limits.Validate();
        _enumerateFiles = enumerateFiles ?? (directory => Directory.EnumerateFiles(directory, "*.item", SearchOption.TopDirectoryOnly));
    }

    /// <summary>
    /// Reads every <c>.item</c> in a manifests directory. Returns an empty list
    /// when the directory does not exist. The sibling <c>Pending\</c> directory
    /// is not descended into — those are in-flight installs the launcher has not
    /// committed.
    /// </summary>
    public IReadOnlyList<EpicManifest> ReadDirectory(string manifestsDirectory)
        => ScanDirectory(manifestsDirectory).Manifests;

    /// <summary>Reads manifests and reports whether their absence is authoritative for this pass.</summary>
    public EpicManifestScan ScanDirectory(string manifestsDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifestsDirectory);

        if (!Directory.Exists(manifestsDirectory))
        {
            _logger.LogDebug("Epic manifests directory {Path} does not exist", manifestsDirectory);
            return new([], false, null);
        }

        var manifests = new List<EpicManifest>();
        var complete = true;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            // Materialization is inside the catch: enumeration can fail during MoveNext.
            var files = _enumerateFiles(manifestsDirectory)
                .Where(file => file.EndsWith(".item", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var file in files)
            {
                var (manifest, bytes) = ReadWithBytes(file);
                if (manifest is null)
                {
                    complete = false;
                    continue;
                }
                manifests.Add(manifest);
                if (!manifest.HasInstallState) complete = false;
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file)));
                hash.AppendData(bytes!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate Epic manifests under {Path}", manifestsDirectory);
            complete = false;
        }

        return new(manifests, complete, complete ? Convert.ToHexString(hash.GetHashAndReset()) : null);
    }

    /// <summary>
    /// Reads one <c>.item</c> file, or null when it is missing, unreadable or not
    /// a manifest (no <c>CatalogItemId</c>).
    /// </summary>
    public EpicManifest? Read(string manifestPath)
        => ReadWithBytes(manifestPath).Manifest;

    private (EpicManifest? Manifest, byte[]? Bytes) ReadWithBytes(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        try
        {
            var bytes = StorefrontFile.Read(manifestPath, _limits);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = _limits.MaxDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var catalogItemId = EpicJson.String(root, "CatalogItemId");
            if (string.IsNullOrWhiteSpace(catalogItemId))
            {
                _logger.LogWarning("Epic manifest {Path} has no CatalogItemId; skipping", manifestPath);
                return (null, null);
            }

            var hasInstallState = root.TryGetProperty("bIsIncompleteInstall", out var incomplete)
                && incomplete.ValueKind is JsonValueKind.True or JsonValueKind.False;
            return (new EpicManifest(
                CatalogItemId: catalogItemId,
                CatalogNamespace: EpicJson.String(root, "CatalogNamespace"),
                AppName: EpicJson.String(root, "AppName"),
                DisplayName: EpicJson.String(root, "DisplayName"),
                InstallLocation: EpicJson.String(root, "InstallLocation"),
                LaunchExecutable: EpicJson.String(root, "LaunchExecutable"),
                AppVersionString: EpicJson.String(root, "AppVersionString"),
                InstallSize: EpicJson.Int64(root, "InstallSize"),
                // Only an explicit completion bit may turn Install into Play.
                IsIncompleteInstall: !hasInstallState || incomplete.ValueKind != JsonValueKind.False,
                MainGameCatalogItemId: EpicJson.String(root, "MainGameCatalogItemId"),
                MainGameAppName: EpicJson.String(root, "MainGameAppName"),
                AppCategories: EpicJson.StringArray(root, "AppCategories"),
                InstallationGuid: EpicJson.String(root, "InstallationGuid"),
                ManifestPath: manifestPath) { HasInstallState = hasInstallState }, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Keep positive rows from other files, but withhold absence authority.
            _logger.LogWarning(ex, "Could not read Epic manifest {Path}; skipping", manifestPath);
            return (null, null);
        }
    }
}
