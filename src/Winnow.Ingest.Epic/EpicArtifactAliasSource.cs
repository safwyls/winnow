using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Ingest.Epic.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Maps Epic's <c>CatalogItemId</c> — the id Winnow stores in
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
/// belt and braces for entries the catalog has not caught up with. All three are
/// files the launcher already wrote.</para>
///
/// <para><b>And, since 2026-08-26, the authenticated library service — because
/// the local files alone left a measurable hole.</b> Every source above is
/// derived from <c>catcache.bin</c> or from an installation, so a title the
/// account owns but the launcher has never cached has no alias, therefore no
/// gamesdb hop, therefore no IGDB record, therefore no name, year, cover or
/// summary. On the author's library that was 29 of 99 Epic rows, sitting in the
/// grid as <c>App &lt;32 hex&gt;</c> and enriching for none of them — not a low
/// match rate but a question nobody could ask. <c>/library/api/public/items</c>
/// returns <c>appName</c> on every entitlement, which is exactly the missing
/// half, so it is read here as the LAST source: the local files still win every
/// id they know, and the API only fills what they have never held.</para>
///
/// <para>That read is free in the ordinary case — it is the same cached library
/// the ownership feed already fetched — and it is optional in every sense: the
/// account client arrives as a nullable dependency, an install with no Epic
/// session contributes nothing, and a failed fetch contributes nothing. None of
/// those is distinguishable from "this account owns no extra titles", and none of
/// them may remove an alias the local files supplied.</para>
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
    /// Adds aliases for entitlements the local files have never held, and returns
    /// how many were new.
    ///
    /// <para><b>Last, and additive only.</b> <see cref="Add"/> is a
    /// <c>TryAdd</c>, so every id the launcher's own files supplied keeps the
    /// value they gave it — this can only fill gaps. The ordering is deliberate:
    /// the local files describe what this machine will actually install and
    /// launch, so where the two disagree about an artifact codename the local
    /// record wins.</para>
    ///
    /// <para><b>Every failure is silence, and silence adds nothing.</b> No client
    /// registered, no credentials, no session, a lapsed refresh token, Epic
    /// unreachable — all end here having added zero aliases and left the local map
    /// exactly as it was built. None of them may be allowed to look like "this
    /// account owns nothing else".</para>
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
