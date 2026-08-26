using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Epic;

/// <summary>
/// Reads <c>Data\ThirPartyManagedApps\*.json</c>. Epic misspells the directory
/// — <c>ThirParty</c>, one word short of <c>ThirdParty</c> — and that
/// misspelling is the real name on disk, so it is reproduced verbatim in
/// <see cref="EpicPaths.ThirdPartyManagedAppsDirectoryName"/>.
/// </summary>
public sealed class EpicThirdPartyAppReader
{
    private readonly ILogger<EpicThirdPartyAppReader> _logger;

    /// <param name="logger">Optional logger.</param>
    public EpicThirdPartyAppReader(ILogger<EpicThirdPartyAppReader>? logger = null)
        => _logger = logger ?? NullLogger<EpicThirdPartyAppReader>.Instance;

    /// <summary>
    /// Reads every file in the directory. Empty when the directory is absent —
    /// most accounts own no third-party-managed titles at all.
    /// </summary>
    public IReadOnlyList<EpicThirdPartyApp> ReadDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("Epic third-party managed apps directory {Path} does not exist", directory);
            return [];
        }

        var apps = new List<EpicThirdPartyApp>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate {Path}", directory);
            return [];
        }

        foreach (var file in files)
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var app = Read(file);
            if (app is not null)
            {
                apps.Add(app);
            }
        }

        return apps;
    }

    /// <summary>Reads one file, or null when it is missing, unreadable or has no <c>CatalogID</c>.</summary>
    public EpicThirdPartyApp? Read(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        try
        {
            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // NOTE the casing: CatalogID here, CatalogItemId on a .item manifest.
            var catalogItemId = EpicJson.String(root, "CatalogID");
            if (catalogItemId.Length == 0)
            {
                return null;
            }

            return new EpicThirdPartyApp(
                CatalogItemId: catalogItemId,
                CatalogNamespace: EpicJson.String(root, "Namespace"),
                AppName: EpicJson.String(root, "AppName"),
                Title: EpicJson.String(root, "Title"),
                Provider: EpicJson.String(root, "Provider"),
                RegistryPath: EpicJson.String(root, "RegistryPath"),
                RegistryKey: EpicJson.String(root, "RegistryKey"),
                GameId: EpicJson.String(root, "GameID"),
                FilePath: filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read Epic third-party app {Path}; skipping", filePath);
            return null;
        }
    }
}
