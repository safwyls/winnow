using System.Diagnostics;
using Hoard.Core.Ingest;
using Hoard.Enrich.SteamWeb;
using Hoard.Ingest.Epic;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Gog;
using Hoard.Ingest.Steam;
using Hoard.Resolve;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

/// <summary>
/// One local Steam scan-and-resolve pass. Exists purely as a seam:
/// <see cref="SnapshotSchedulerService"/> drives this on a timer and must be
/// testable without a Steam install on the machine running the tests.
/// <see cref="SteamSyncService"/> is the only production implementation.
/// </summary>
public interface ISteamSync
{
    /// <inheritdoc cref="SteamSyncService.SyncAsync"/>
    Task<SteamSyncReport> SyncAsync(CancellationToken ct = default);
}

/// <summary>
/// Joins the two halves of M0: the Steam local-file scan (§4.1) and the
/// external-id resolver (§5.3 step 1). Ingest emits candidates, Resolve
/// persists them — this type only sequences the two and never touches a
/// repository itself, keeping the §5.1 boundary intact.
///
/// <para>The scan is filesystem-only, so it is fast enough to run before the
/// window opens; there is no network call anywhere in this path. Enrichment,
/// which is the slow part (§4.3's 200 req/5min), is M1 and explicitly stays
/// out of the startup path — pitfall 3.</para>
/// </summary>
public sealed class SteamSyncService : ISteamSync
{
    private readonly SteamLibrarySource _steam;
    private readonly EpicLibrarySource _epic;
    private readonly GogLibrarySource _gog;
    private readonly ExternalIdResolver _resolver;
    private readonly ISteamWebApiClient? _steamWeb;
    private readonly IEpicAccountClient? _epicApi;
    private readonly ILogger<SteamSyncService> _logger;

    public SteamSyncService(
        SteamLibrarySource steam,
        EpicLibrarySource epic,
        GogLibrarySource gog,
        ExternalIdResolver resolver,
        ILogger<SteamSyncService> logger,
        ISteamWebApiClient? steamWeb = null,
        IEpicAccountClient? epicApi = null)
    {
        _steam = steam;
        _epic = epic;
        _gog = gog;
        _resolver = resolver;
        _logger = logger;
        _steamWeb = steamWeb;
        _epicApi = epicApi;
    }

    /// <summary>
    /// Scans the local Steam install and resolves what it finds. Safe to call
    /// repeatedly: the resolver is idempotent by change detection, so a re-sync
    /// with unchanged playtime writes nothing. A machine with no Steam install
    /// yields zero candidates and is not an error.
    /// </summary>
    public async Task<SteamSyncReport> SyncAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var local = _steam.Scan();
        var owned = await OwnedCandidatesAsync(local, ct);

        // Union, never a reconciliation. Neither source is authoritative for the
        // SET: localconfig.vdf only records games that have been PLAYED, so it
        // cannot see the never-launched library at all; and GetOwnedGames only
        // knows licences, so it cannot see the demos, free weekends and delisted
        // apps the user has genuinely played. On this machine that is 330 games
        // the local files miss and 105 the web API misses. Nothing is dropped
        // for being absent from one side.
        //
        // Order is presentation, not precedence. Every field a source cannot
        // speak to arrives null, and the write rules resolve conflicts on who
        // knows — so resolving web-then-local and local-then-web reach the same
        // rows. That property is asserted in the tests, because when it did not
        // hold the failure was silent: the web candidates, resolved second only
        // because of how this line was written, reported Installed: false for
        // games they had never looked for on disk and cleared the entire
        // library's install state on every sync.
        //
        // The overlap between the two sources is handed over as-is: the resolver
        // collapses the appids both of them saw into one observation apiece
        // (CandidateOwnershipMerge) before it compares anything against the
        // database. That belongs there, not here — this type only sequences the
        // two halves and must not start deciding what the data means (§5.1).
        // M4. The three stores occupy disjoint (Provider, ProviderId) key
        // spaces, so CandidateOwnershipMerge never merges across them — a game
        // owned on both Steam and Epic stays two ownerships of (eventually) one
        // work, which is what §5.3's four layers are for. Both scans are
        // filesystem-only and answer empty when that launcher is absent.
        var epic = _epic.Scan();
        var gog = _gog.Scan();

        // The same union rule again, one store along. Epic's local files and
        // Epic's library API overlap on catalog item id and each sees what the
        // other cannot: the files know install state, install paths and the
        // titles delivered through another launcher, and they know them with no
        // network at all; the API knows the true entitlement list, when each
        // title was acquired, and — uniquely — playtime, which Epic writes
        // nowhere on disk.
        //
        // Order is presentation, not precedence, exactly as above. The API
        // candidates carry Installed: null because the library service cannot see
        // the local disk, so they cannot clear an install flag the manifests just
        // set no matter which side is resolved first. CandidateOwnershipMerge
        // collapses the overlap, in the resolver, where it belongs.
        var epicOwned = await EpicApiCandidatesAsync(ct);

        var candidates = local.Concat(owned).Concat(epic).Concat(epicOwned).Concat(gog).ToList();

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Steam sync found no candidates; nothing to resolve.");
            return new SteamSyncReport(0, null, stopwatch.Elapsed);
        }

        var result = await _resolver.ResolveAsync(candidates, ct);
        stopwatch.Stop();

        _logger.LogInformation(
            "Library sync: {Candidates} candidates ({Local} steam local, {Owned} steam owned, "
            + "{Epic} epic local, {EpicOwned} epic api, {Gog} gog) in {Elapsed:n1}s — {Created} new, "
            + "{Matched} matched, {PlayRecords} play records, {Snapshots} snapshots, "
            + "{Promoted} names promoted.",
            candidates.Count, local.Count, owned.Count, epic.Count, epicOwned.Count, gog.Count,
            stopwatch.Elapsed.TotalSeconds,
            result.CreatedReleases, result.MatchedExisting, result.PlayRecordsWritten,
            result.SnapshotsWritten, result.NamesPromoted);

        return new SteamSyncReport(candidates.Count, result, stopwatch.Elapsed);
    }

    /// <summary>
    /// The owned-library half of the union (§4.2). Needs a user-supplied Web API
    /// key and the account's SteamID64, which is derived from the steam3 folder
    /// name the local scan already enumerated — so this runs only when the local
    /// scan found an account and the key is configured.
    ///
    /// <para>Total: an unconfigured key, an undisclosed profile or a dead
    /// network yields no candidates and leaves the local scan untouched. §5.1
    /// forbids enrichment blocking a user-facing path, and this one is on the
    /// startup path.</para>
    /// </summary>
    private async Task<IReadOnlyList<CandidateOwnership>> OwnedCandidatesAsync(
        IReadOnlyList<CandidateOwnership> local, CancellationToken ct)
    {
        if (_steamWeb is null || !await _steamWeb.IsConfiguredAsync(ct))
        {
            return [];
        }

        var accounts = local
            .Select(c => c.AccountRef)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var owned = new List<CandidateOwnership>();
        foreach (var account in accounts)
        {
            if (!SteamId.TryParse(account!, out var steamId))
            {
                continue;
            }

            try
            {
                owned.AddRange(await _steamWeb.GetOwnershipCandidatesAsync(steamId, ct: ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One account's profile being private or the endpoint being down
                // must not cost the local scan, which needs no network at all.
                _logger.LogWarning(
                    ex, "Owned-library lookup failed for one account; continuing with local files.");
            }
        }

        return owned;
    }

    /// <summary>
    /// The authenticated Epic half of the union. Needs a user-supplied OAuth
    /// client pair and a one-time interactive sign-in, so it runs only when both
    /// are present.
    ///
    /// <para><b>Every way this can fail yields no candidates and leaves the local
    /// Epic scan untouched.</b> Not configured, configured but never signed in, a
    /// refresh token that lapsed while the app was closed, Epic unreachable, a
    /// 429 the retries could not outlast — all of them return an empty list. That
    /// is the fallback, and it is deliberately expressed as "this source
    /// contributed nothing this pass" rather than as an error: §5.1 forbids
    /// enrichment blocking a user-facing path, and this one is on the startup
    /// path.</para>
    ///
    /// <para>Note that <c>_epic.Scan()</c> above has already run and its
    /// candidates are already in the union by the time this is called. Nothing
    /// here can subtract from them.</para>
    /// </summary>
    private async Task<IReadOnlyList<CandidateOwnership>> EpicApiCandidatesAsync(CancellationToken ct)
    {
        if (_epicApi is null || !await _epicApi.IsConfiguredAsync(ct) || !await _epicApi.IsSignedInAsync(ct))
        {
            return [];
        }

        try
        {
            return await _epicApi.GetOwnershipCandidatesAsync(ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The client is written not to throw, so reaching here means a bug
            // rather than a network condition. It is still caught: a defect in an
            // opt-in enrichment source must not cost the user the local scan that
            // needs no network at all.
            _logger.LogWarning(
                ex, "Epic library lookup failed unexpectedly; continuing with the local Epic files.");
            return [];
        }
    }
}

/// <param name="Candidates">Candidates the local scan produced.</param>
/// <param name="Result">Resolver outcome, or null when there was nothing to resolve.</param>
/// <param name="Elapsed">Wall-clock time for scan plus resolve.</param>
public sealed record SteamSyncReport(
    int Candidates,
    ResolveResult? Result,
    TimeSpan Elapsed);
