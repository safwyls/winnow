using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Ingest.Epic.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Maps Epic's <c>CatalogItemId</c> (the id stored in <c>external_ids</c>) to
/// its <c>AppName</c> (the id gamesdb keys on). Sources: catalog cache, installed
/// manifests, third-party records, and optionally the authenticated library API.
/// </summary>
public sealed class EpicArtifactAliasSource : IStoreArtifactAliasSource
{
    private readonly EpicCatalogReader _catalog;
    private readonly EpicManifestReader _manifests;
    private readonly EpicThirdPartyAppReader _thirdParty;
    private readonly IEpicAccountClient? _account;
    private readonly string? _dataRootOverride;
    private readonly ILogger<EpicArtifactAliasSource> _logger;

    /// <param name="catalog">Reader for the launcher's catalog cache.</param>
    /// <param name="manifests">Reader for the installed-manifest directory.</param>
    /// <param name="thirdParty">Reader for the third-party-managed app records.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="dataRoot">Launcher <c>Data</c> root, or null to discover it.</param>
    /// <param name="account">
    /// The authenticated library client, or null on a host that did not register
    /// the opt-in Epic API module. Optional by design: this class must behave
    /// identically on an install that has never signed in to Epic, where the
    /// local files are the whole story.
    /// </param>
    public EpicArtifactAliasSource(
        EpicCatalogReader catalog,
        EpicManifestReader manifests,
        EpicThirdPartyAppReader thirdParty,
        ILogger<EpicArtifactAliasSource>? logger = null,
        string? dataRoot = null,
        IEpicAccountClient? account = null)
    {
        _catalog = catalog;
        _manifests = manifests;
        _thirdParty = thirdParty;
        _account = account;
        _logger = logger ?? NullLogger<EpicArtifactAliasSource>.Instance;
        _dataRootOverride = dataRoot;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(
        string provider, CancellationToken ct = default)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(provider, ExternalIdProviders.Epic, StringComparison.Ordinal))
        {
            return aliases;
        }

        var dataRoot = _dataRootOverride ?? EpicPaths.FindDataRoot();
        if (dataRoot is null)
        {
            // Not "there are no aliases". A machine with no launcher says nothing
            // about the account's library, and the API half below may know all of
            // it.
            _logger.LogDebug("No Epic data root; local artifact aliases unavailable.");
        }
        else
        {
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
        }

        var local = aliases.Count;
        var fromApi = await AddApiAliasesAsync(aliases, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Epic artifact aliases: {Count} catalog item ids carry an AppName "
            + "({Local} from local launcher files, {Api} known only to the account API).",
            aliases.Count, local, fromApi);
        return aliases;
    }

    /// <summary>
    /// Adds aliases from the authenticated library API for entitlements the local
    /// files never held. Additive only; failures add nothing.
    /// </summary>
    private async Task<int> AddApiAliasesAsync(Dictionary<string, string> aliases, CancellationToken ct)
    {
        if (_account is null)
        {
            return 0;
        }

        try
        {
            // The cached library in the ordinary case: the ownership feed has
            // already fetched it this run and the TTL is hours.
            var library = await _account.GetOwnedLibraryAsync(ct: ct).ConfigureAwait(false);
            if (!library.Succeeded)
            {
                return 0;
            }

            var added = 0;
            foreach (var item in library.Items)
            {
                if (Add(aliases, item.CatalogItemId, item.AppName))
                {
                    added++;
                }
            }

            return added;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The account client soft-fails internally, so reaching here means
            // something unforeseen. It must not take the local aliases with it
            // (§5.1).
            _logger.LogWarning(
                "Reading the Epic account library for artifact aliases failed ({ExceptionType}); "
                + "continuing with the {Count} aliases the local launcher files supplied.",
                ex.GetType().Name, aliases.Count);
            return 0;
        }
    }

    /// <summary>
    /// Adds an alias when both halves are real and the id is not already mapped.
    /// True when it was actually added — which is how the API pass counts what it
    /// contributed rather than what it looked at.
    /// </summary>
    private static bool Add(Dictionary<string, string> aliases, string? catalogItemId, string? appName)
        => !string.IsNullOrWhiteSpace(catalogItemId)
           && !string.IsNullOrWhiteSpace(appName)
           && aliases.TryAdd(catalogItemId.Trim(), appName.Trim());
}
