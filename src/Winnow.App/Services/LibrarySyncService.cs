using System.Diagnostics;
using Winnow.Core.Ingest;
using Winnow.Enrich.SteamWeb;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Gog;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// One scan-and-resolve pass over every store's local files: Steam appmanifests
/// and localconfig.vdf, Epic manifests and catcache, GOG registry and Galaxy
/// database. This path makes no network call, so it is safe on a 15-minute timer
/// and safe to run while the user is offline. An architecture test enforces the
/// guarantee by walking the implementation's constructor closure and rejecting
/// any <see cref="System.Net.Http.HttpClient"/>-backed dependency. Playtime
/// figures are floors — localconfig.vdf sees only what the client has synced to
/// this machine — so the pass never writes an ownership's series backwards.
/// </summary>
public interface ILocalLibrarySync
{
    /// <inheritdoc cref="LocalLibrarySyncService.SyncAsync"/>
    Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default);
}

/// <summary>
/// Entitlement backfill: Steam Web API GetOwnedGames (needs a user-supplied key)
/// and Epic's authenticated library API (needs an OAuth client pair plus a one-time
/// interactive sign-in). Both are optional; when neither is configured the pass
/// returns without scanning or resolving at all. This path touches the network and
/// must never gate a user-facing path; it runs on its own long interval rather than
/// the snapshot cadence. Each pass unions the remote answers with a local scan
/// before resolving: <see cref="CandidateOwnershipMerge"/> collapses overlapping
/// appids within the pass, and <see cref="PlaytimeView.LowerBound"/> clamps the
/// figures across passes so no source's blind spot writes the series backwards.
/// </summary>
public interface IRemoteOwnershipSync
{
    /// <inheritdoc cref="RemoteOwnershipSyncService.SyncAsync(CancellationToken)"/>
    Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default);

    /// <inheritdoc cref="RemoteOwnershipSyncService.SyncAsync(LocalLibraryScan, CancellationToken)"/>
    Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default);
}

/// <summary>
/// Process-wide <see cref="SemaphoreSlim"/>(1, 1) that both sync jobs acquire
/// before resolving. <see cref="ExternalIdResolver"/> runs a whole pass in one
/// SQLite transaction, and there are now two independently scheduled jobs plus a
/// startup pass that could otherwise overlap.
/// </summary>
public sealed class LibrarySyncGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the current pass to finish; dispose the result to release.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

/// <summary>Candidates from one filesystem pass, kept per store so the log can break them down.</summary>
public readonly record struct LocalLibraryScan(
    IReadOnlyList<CandidateOwnership> Steam,
    IReadOnlyList<CandidateOwnership> Epic,
    IReadOnlyList<CandidateOwnership> Gog)
{
    public int Count => Steam.Count + Epic.Count + Gog.Count;

    public IEnumerable<CandidateOwnership> All => Steam.Concat(Epic).Concat(Gog);
}

/// <summary>
/// Implements <see cref="ILocalLibrarySync"/>. Sequences ingest and resolve and
/// touches no repository itself, keeping the §5.1 module boundary intact.
/// </summary>
public sealed class LocalLibrarySyncService : ILocalLibrarySync
{
    private readonly SteamLibrarySource _steam;
    private readonly EpicLibrarySource _epic;
    private readonly GogLibrarySource _gog;
    private readonly ExternalIdResolver _resolver;
    private readonly LibrarySyncGate _gate;
    private readonly ILogger<LocalLibrarySyncService> _logger;

    public LocalLibrarySyncService(
        SteamLibrarySource steam,
        EpicLibrarySource epic,
        GogLibrarySource gog,
        ExternalIdResolver resolver,
        LibrarySyncGate gate,
        ILogger<LocalLibrarySyncService> logger)
    {
        _steam = steam;
        _epic = epic;
        _gog = gog;
        _resolver = resolver;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>
    /// The three filesystem scans, nothing else. The three stores occupy
    /// disjoint (Provider, ProviderId) key spaces, so nothing merges across
    /// them; a launcher that is not installed answers empty rather than
    /// failing.
    /// </summary>
    public LocalLibraryScan Scan() => new(_steam.Scan(), _epic.Scan(), _gog.Scan());

    /// <summary>
    /// Scans the local store files and resolves what they hold. Safe to call
    /// repeatedly: the resolver is idempotent by change detection, so a re-sync
    /// with unchanged playtime writes nothing. A machine with no launcher
    /// installed yields zero candidates and is not an error.
    /// </summary>
    public async Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var scan = Scan();
        if (scan.Count == 0)
        {
            _logger.LogInformation("Local library sync found no candidates; nothing to resolve.");
            return new LibrarySyncReport(0, null, stopwatch.Elapsed, scan);
        }

        // The gate covers the resolver and nothing else: reading store files
        // takes no lock, and holding one across the remote job's HTTP timeout
        // would put a stalled backfill in front of every snapshot tick.
        using var lease = await _gate.EnterAsync(ct).ConfigureAwait(false);

        // LowerBound, and this is the job that makes it matter: localconfig.vdf
        // sees only what the client has synced to this machine, so on any
        // library with a second PC its figure sits below the account-wide one
        // the remote job stored. Recording that as an observation would put a
        // permanent sawtooth in playtime_snapshots at 15-minute intervals.
        var result = await _resolver.ResolveAsync([.. scan.All], ct, PlaytimeView.LowerBound);
        stopwatch.Stop();

        _logger.LogInformation(
            "Local library sync: {Candidates} candidates ({Steam} steam, {Epic} epic, {Gog} gog) "
            + "in {Elapsed:n1}s — {Created} new, {Matched} matched, {PlayRecords} play records, "
            + "{Snapshots} snapshots, {Promoted} names promoted.",
            scan.Count, scan.Steam.Count, scan.Epic.Count, scan.Gog.Count,
            stopwatch.Elapsed.TotalSeconds,
            result.CreatedReleases, result.MatchedExisting, result.PlayRecordsWritten,
            result.SnapshotsWritten, result.NamesPromoted);

        return new LibrarySyncReport(scan.Count, result, stopwatch.Elapsed, scan);
    }
}

/// <summary>
/// Implements <see cref="IRemoteOwnershipSync"/>. Checks whether any remote
/// source is configured before scanning; an unconfigured install returns an
/// empty report with no filesystem or resolver work. The local scan can be
/// supplied by the caller so the startup pipeline does not walk every
/// appmanifest twice.
/// </summary>
public sealed class RemoteOwnershipSyncService : IRemoteOwnershipSync
{
    private readonly LocalLibrarySyncService _local;
    private readonly ExternalIdResolver _resolver;
    private readonly LibrarySyncGate _gate;
    private readonly ILogger<RemoteOwnershipSyncService> _logger;
    private readonly ISteamWebApiClient? _steamWeb;
    private readonly IEpicAccountClient? _epicApi;

    public RemoteOwnershipSyncService(
        LocalLibrarySyncService local,
        ExternalIdResolver resolver,
        LibrarySyncGate gate,
        ILogger<RemoteOwnershipSyncService> logger,
        ISteamWebApiClient? steamWeb = null,
        IEpicAccountClient? epicApi = null)
    {
        _local = local;
        _resolver = resolver;
        _gate = gate;
        _logger = logger;
        _steamWeb = steamWeb;
        _epicApi = epicApi;
    }

    /// <summary>
    /// Fetches the owned libraries both stores will disclose, unions them with
    /// the local scan and resolves the lot. An unconfigured key, an undisclosed
    /// profile or a dead network yields no remote candidates and degrades to
    /// exactly what <see cref="LocalLibrarySyncService"/> would have written.
    /// </summary>
    public Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default)
        => BackfillAsync(reusable: null, ct);

    /// <summary>
    /// The same pass over a scan the caller has already paid for. The startup
    /// pipeline runs the local job first, so re-reading every appmanifest
    /// seconds later would be a second full filesystem walk for a byte-identical
    /// answer.
    /// </summary>
    public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default)
        => BackfillAsync(scan, ct);

    private async Task<LibrarySyncReport> BackfillAsync(LocalLibraryScan? reusable, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // Checked before anything is read. "Registered and idle" is the
        // overwhelmingly common state, and without this line an unconfigured
        // machine pays a full scan-and-resolve here to produce exactly the rows
        // the local job just wrote.
        if (!await AnyRemoteSourceConfiguredAsync(ct))
        {
            _logger.LogDebug("No remote ownership source is configured; nothing to back-fill.");
            return new LibrarySyncReport(0, null, stopwatch.Elapsed);
        }

        var scan = reusable ?? _local.Scan();
        var owned = await OwnedCandidatesAsync(scan.Steam, ct);

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
        //
        // The same union rule again, one store along. Epic's local files and
        // Epic's library API overlap on catalog item id and each sees what the
        // other cannot: the files know install state, install paths and the
        // titles delivered through another launcher, and they know them with no
        // network at all; the API knows the true entitlement list, when each
        // title was acquired, and — uniquely — playtime, which Epic writes
        // nowhere on disk. The API candidates carry Installed: null because the
        // library service cannot see the local disk, so they cannot clear an
        // install flag the manifests just set no matter which side is resolved
        // first.
        var epicOwned = await EpicApiCandidatesAsync(ct);

        var candidates = scan.Steam
            .Concat(owned)
            .Concat(scan.Epic)
            .Concat(epicOwned)
            .Concat(scan.Gog)
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Remote ownership sync found no candidates; nothing to resolve.");
            return new LibrarySyncReport(0, null, stopwatch.Elapsed, scan);
        }

        // Taken here and not around the fetches above, so a stalled endpoint
        // never sits in front of the snapshot scheduler's local tick.
        using var lease = await _gate.EnterAsync(ct).ConfigureAwait(false);

        // LowerBound here too. The union of both sources is the best estimate
        // available, but it is still an estimate: a session played offline on
        // this machine since the last cloud sync is in neither. The clamp is a
        // no-op on the normal path and keeps the series monotonic on the
        // abnormal one.
        var result = await _resolver.ResolveAsync(candidates, ct, PlaytimeView.LowerBound);
        stopwatch.Stop();

        _logger.LogInformation(
            "Remote ownership sync: {Candidates} candidates ({Local} steam local, {Owned} steam owned, "
            + "{Epic} epic local, {EpicOwned} epic api, {Gog} gog) in {Elapsed:n1}s — {Created} new, "
            + "{Matched} matched, {PlayRecords} play records, {Snapshots} snapshots, "
            + "{Promoted} names promoted.",
            candidates.Count, scan.Steam.Count, owned.Count, scan.Epic.Count, epicOwned.Count,
            scan.Gog.Count, stopwatch.Elapsed.TotalSeconds,
            result.CreatedReleases, result.MatchedExisting, result.PlayRecordsWritten,
            result.SnapshotsWritten, result.NamesPromoted);

        return new LibrarySyncReport(candidates.Count, result, stopwatch.Elapsed, scan);
    }

    /// <summary>
    /// Whether either backfill could produce anything. Both checks read stored
    /// settings only — <c>IsSignedInAsync</c> asks whether a session exists that
    /// is worth trying, not the network.
    /// </summary>
    private async Task<bool> AnyRemoteSourceConfiguredAsync(CancellationToken ct)
    {
        if (_steamWeb is not null && await _steamWeb.IsConfiguredAsync(ct))
        {
            return true;
        }

        return _epicApi is not null
            && await _epicApi.IsConfiguredAsync(ct)
            && await _epicApi.IsSignedInAsync(ct);
    }

    /// <summary>
    /// The owned-library half of the union (§4.2). Needs a user-supplied Web API
    /// key and the account's SteamID64, which is derived from the steam3 folder
    /// name the local scan already enumerated — so this runs only when the local
    /// scan found an account and the key is configured.
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
    /// The authenticated Epic half of the union. Returns empty on any failure,
    /// leaving the local Epic scan untouched.
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

/// <param name="Candidates">Candidates the scan produced.</param>
/// <param name="Result">Resolver outcome, or null when there was nothing to resolve.</param>
/// <param name="Elapsed">Wall-clock time for scan plus resolve.</param>
/// <param name="Scan">
/// The filesystem answer this pass read, so the next pass in a startup pipeline
/// can reuse it rather than walking every appmanifest again. Null when the pass
/// did not scan.
/// </param>
public sealed record LibrarySyncReport(
    int Candidates,
    ResolveResult? Result,
    TimeSpan Elapsed,
    LocalLibraryScan? Scan = null);
