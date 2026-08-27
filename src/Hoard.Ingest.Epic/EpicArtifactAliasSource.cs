using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Epic;

/// <summary>
/// Maps Epic's <c>CatalogItemId</c> — the id Hoard stores in
/// <c>external_ids</c> — to its <c>AppName</c>, the id every service that can
/// resolve an Epic title actually keys on.
///
/// <para><b>Why the two ids both exist and why we store the one we store.</b>
/// <c>docs/spikes/epic-gog-local-files.md</c> section 3 establishes
/// <c>(CatalogNamespace, CatalogItemId)</c> as the stable product identity;
/// <c>AppName</c> is a per-artifact release id and a codename — "Bluebird" is
/// Fez, "Wombat" is World War Z — which is why section 9 lists rendering it as
/// Epic trap #2. So the catalog item id is the right thing in the database and
/// stays there. But section 20 measured that <c>gamesdb.gog.com</c> keys its Epic
/// releases on <c>AppName</c> and nothing else: <c>epic/Bluebird</c> resolves,
/// and the catalog item id returns a clean 404 (re-verified while fixing the
/// missing-metadata bug). The alias is the bridge between the id we keep and
/// the id we must ask with.</para>
///
/// <para><b>Where the aliases come from.</b> <c>releaseInfo[0].appId</c> on each
/// catalog entry, which the spike verifies is byte-identical to the manifest's
/// <c>AppName</c>, with the installed manifests and the third-party records as
/// belt and braces for entries the catalog has not caught up with. All three
/// are files the launcher already wrote; nothing here opens a network
/// connection or a credential blob.</para>
///
/// <para><b>An empty map is "cannot say", never "no aliases exist".</b> A
/// machine with no Epic launcher, an unreadable <c>%PROGRAMDATA%</c>, or a
/// catalog the launcher has not written since login all produce an empty map,
/// and the caller must respond by leaving those rows alone. Recording the
/// silence as an answer is the failure this codebase has already paid for
/// twice.</para>
/// </summary>
public sealed class EpicArtifactAliasSource : IStoreArtifactAliasSource
{
    private readonly EpicCatalogReader _catalog;
    private readonly EpicManifestReader _manifests;
    private readonly EpicThirdPartyAppReader _thirdParty;
    private readonly string? _dataRootOverride;
    private readonly ILogger<EpicArtifactAliasSource> _logger;

    public EpicArtifactAliasSource(
        EpicCatalogReader catalog,
        EpicManifestReader manifests,
        EpicThirdPartyAppReader thirdParty,
        ILogger<EpicArtifactAliasSource>? logger = null,
        string? dataRoot = null)
    {
        _catalog = catalog;
        _manifests = manifests;
        _thirdParty = thirdParty;
        _logger = logger ?? NullLogger<EpicArtifactAliasSource>.Instance;
        _dataRootOverride = dataRoot;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(
        string provider, CancellationToken ct = default)
    {
        if (!string.Equals(provider, ExternalIdProviders.Epic, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var dataRoot = _dataRootOverride ?? EpicPaths.FindDataRoot();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dataRoot is null)
        {
            _logger.LogDebug("No Epic data root; no artifact aliases available.");
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(aliases);
        }

        // Catalog first: it covers the owned library whether installed or not,
        // which is the whole population enrichment asks about.
        foreach (var entry in _catalog.Read(EpicPaths.CatalogCachePath(dataRoot)))
        {
            Add(aliases, entry.CatalogItemId, entry.AppName);
        }

        // Manifests and third-party records only ever ADD, never overwrite. They
        // cover the same ids from a different file, and where they disagree the
        // catalog is the account-level record while a manifest describes one
        // installation. Preferring the catalog keeps a reinstall from silently
        // changing which id we look a title up by.
        foreach (var manifest in _manifests.ReadDirectory(EpicPaths.ManifestsDirectory(dataRoot)))
        {
            Add(aliases, manifest.CatalogItemId, manifest.AppName);
        }

        foreach (var app in _thirdParty.ReadDirectory(EpicPaths.ThirdPartyManagedAppsDirectory(dataRoot)))
        {
            Add(aliases, app.CatalogItemId, app.AppName);
        }

        _logger.LogDebug("Epic artifact aliases: {Count} catalog item ids carry an AppName.", aliases.Count);
        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(aliases);
    }

    private static void Add(Dictionary<string, string> aliases, string? catalogItemId, string? appName)
    {
        if (!string.IsNullOrWhiteSpace(catalogItemId) && !string.IsNullOrWhiteSpace(appName))
        {
            aliases.TryAdd(catalogItemId.Trim(), appName.Trim());
        }
    }
}
