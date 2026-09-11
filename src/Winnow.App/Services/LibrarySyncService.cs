using System.Diagnostics;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Model;
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
    /// <summary>
    /// Carried beside the candidates so the sync pass can persist them without a
    /// second walk. Empty on a machine with no Epic install.
    /// </summary>
    public IReadOnlyList<EpicLaunchTriple> EpicLaunchTriples { get; init; } = [];

    /// <summary>Provenance from the outside-gate GOG read; null on Epic-only refreshes.</summary>
    public GogLibraryScan? GogEvidence { get; init; }

    /// <summary>
    /// The launcher install state this scan read, so a later pass handed the
    /// same scan can tell whether the files have moved since. Null when no
    /// <see cref="LibraryScanBaseline"/> is wired up, which reports every state
    /// as moved and re-reads exactly as the pass did before.
    /// </summary>
    public LibraryScanState? Covered { get; init; }

    public int Count => Steam.Count + Epic.Count + Gog.Count;

    public IEnumerable<CandidateOwnership> All => Steam.Concat(Epic).Concat(Gog);
}

/// <summary>
/// One scan-and-resolve pass over local store files. Writes no work, release or
/// ownership row itself — those go through the resolver — but does write one
/// <c>metadata_cache</c> row per Epic launch triple so the action band can build
/// launch URLs without a network call.
/// </summary>
public sealed class LocalLibrarySyncService : ILocalLibrarySync
{
    private readonly SteamLibrarySource _steam;
    private readonly EpicLibrarySource _epic;
    private readonly GogLibrarySource _gog;
    private readonly ExternalIdResolver _resolver;
    private readonly LibrarySyncGate _gate;
    private readonly ILogger<LocalLibrarySyncService> _logger;
    private readonly IEpicLaunchKeyStore? _epicLaunchKeys;
    private readonly Winnow.Core.Repositories.ISteamInstallStateRepository? _steamInstallState;
    private readonly LibraryScanBaseline? _baseline;
    private readonly Winnow.Core.Repositories.IGogInstallStateRepository? _gogInstallState;

    public LocalLibrarySyncService(
        SteamLibrarySource steam,
        EpicLibrarySource epic,
        GogLibrarySource gog,
        ExternalIdResolver resolver,
        LibrarySyncGate gate,
        ILogger<LocalLibrarySyncService> logger,
        IEpicLaunchKeyStore? epicLaunchKeys = null,
        Winnow.Core.Repositories.ISteamInstallStateRepository? steamInstallState = null,
        LibraryScanBaseline? baseline = null,
        Winnow.Core.Repositories.IGogInstallStateRepository? gogInstallState = null)
    {
        _steam = steam;
        _epic = epic;
        _gog = gog;
        _resolver = resolver;
        _gate = gate;
        _logger = logger;
        _epicLaunchKeys = epicLaunchKeys;
        _steamInstallState = steamInstallState;
        _baseline = baseline;
        _gogInstallState = gogInstallState;
    }

    /// <summary>
    /// The three filesystem scans, nothing else. The three stores occupy
    /// disjoint (Provider, ProviderId) key spaces, so nothing merges across
    /// them; a launcher that is not installed answers empty rather than
    /// failing.
    /// </summary>
    public LocalLibraryScan Scan()
    {
        var epic = _epic.ScanLibrary();
        var gog = _gog.ScanLibrary();
        return new LocalLibraryScan(_steam.Scan(), epic.Candidates, gog.Candidates)
        {
            EpicLaunchTriples = epic.LaunchTriples,
            GogEvidence = gog,
        };
    }

    /// <summary>
    /// Scans the local store files and resolves what they hold. Safe to call
    /// repeatedly: the resolver is idempotent by change detection, so a re-sync
    /// with unchanged playtime writes nothing. A machine with no launcher
    /// installed yields zero candidates and is not an error.
    /// </summary>
    public async Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Steam and Epic are read under the gate, after any earlier sync completes.
        var gog = _gog.ScanLibrary();
        var scan = new LocalLibraryScan([], [], gog.Candidates) { GogEvidence = gog };

        return await ResolveScanAsync(scan, stopwatch, ct).ConfigureAwait(false);
    }

    /// <summary>Refreshes launcher installation state after Epic manifests change.</summary>
    public async Task<LibrarySyncReport> SyncEpicAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        return await ResolveScanAsync(new LocalLibraryScan([], [], []), stopwatch, ct).ConfigureAwait(false);
    }

    /// <summary>Called under the sync gate so a queued scan cannot restore obsolete Epic install state.</summary>
    internal LocalLibraryScan RefreshEpic(LocalLibraryScan scan)
    {
        var epic = _epic.ScanLibrary();
        return scan with { Epic = epic.Candidates, EpicLaunchTriples = epic.LaunchTriples };
    }

    /// <summary>
    /// Brings a scan's Steam and Epic halves up to date, per store and only
    /// where the install fingerprint says the files have moved. The backfill
    /// waits on HTTP holding a scan the local pass already paid for, and used to
    /// re-read every appmanifest on the way back in case an install completed
    /// while it waited; when nothing moved that read produced a byte-identical
    /// answer. The fingerprints are read before the scan, so a rewrite during
    /// the pass reads as moved rather than as covered.
    /// </summary>
    internal async Task<LocalLibraryScan> RefreshInstallStateAsync(
        LocalLibraryScan scan, CancellationToken ct, bool retainGalaxyInstallState = true)
    {
        var now = _baseline?.Read() ?? default;
        var covered = scan.Covered;

        if (Moved(covered?.Steam, now.Steam))
        {
            var steam = _steam.Scan(out var complete);
            if (complete && _steamInstallState is not null)
                await _steamInstallState.ClearMissingAsync(
                    steam.Where(candidate => candidate.Installed != false).Select(candidate => candidate.ProviderId).ToArray(), ct)
                    .ConfigureAwait(false);
            scan = scan with { Steam = steam };
        }

        if (Moved(covered?.Epic, now.Epic))
        {
            scan = RefreshEpic(scan);
        }

        scan = await RefreshGogInstallStateAsync(scan, retainGalaxyInstallState, ct).ConfigureAwait(false);
        return scan with { Covered = now };

        // A reader answering null cannot say the state is unchanged: null means
        // "no complete inventory to compare", so it always re-reads.
        static bool Moved(string? covered, string? current)
            => current is null || !string.Equals(covered, current, StringComparison.Ordinal);
    }

    private async Task<LocalLibraryScan> RefreshGogInstallStateAsync(
        LocalLibraryScan scan, bool retainGalaxyInstallState, CancellationToken ct)
    {
        if (scan.GogEvidence is not { } evidence) return scan;
        var current = _gog.ScanRegistry();
        var games = current.Games.DistinctBy(game => game.GameId)
            .ToDictionary(game => game.GameId, StringComparer.Ordinal);
        var galaxyInstalled = retainGalaxyInstallState ? evidence.GalaxyInstalledProductIds : [];
        var absent = _gogInstallState is null ? [] : await _gogInstallState.ReconcileAsync(
            evidence.RegistryProductIds,
            games.Values.Select(game => new Winnow.Core.Repositories.GogRegistryInstallation(game.GameId, game.InstallPath)).ToArray(),
            current.IsComplete, galaxyInstalled, ct).ConfigureAwait(false);
        var missing = absent.ToHashSet(StringComparer.Ordinal);
        var galaxyPositive = galaxyInstalled.ToHashSet(StringComparer.Ordinal);
        return scan with { Gog = scan.Gog.Select(candidate =>
        {
            if (games.TryGetValue(candidate.ProviderId, out var game))
                return candidate with { Installed = true, InstallPath = game.InstallPath ?? candidate.InstallPath };
            if (missing.Contains(candidate.ProviderId))
                return candidate with { Installed = false, InstallPath = null };
            if (retainGalaxyInstallState && (galaxyPositive.Contains(candidate.ProviderId)
                || (current.IsComplete && candidate.Installed == false))) return candidate;
            // A delayed remote snapshot or an incomplete registry supplies no
            // current install fact. Keep its independent ownership/history facts.
            return candidate with { Installed = null, InstallPath = null };
        }).ToArray() };
    }

    /// <summary>
    /// Publishes what a completed pass covered, so the manifest watchers can
    /// adopt it instead of scanning the same files again. Called by the remote
    /// backfill too: it resolves its own union, but the scan underneath it is
    /// this one's.
    /// </summary>
    internal void PublishScanBaseline(LocalLibraryScan scan)
    {
        if (scan.Covered is { } covered)
        {
            _baseline?.Publish(covered);
        }
    }

    private async Task<LibrarySyncReport> ResolveScanAsync(
        LocalLibraryScan scan, Stopwatch stopwatch, CancellationToken ct)
    {
        using var lease = await _gate.EnterAsync(ct).ConfigureAwait(false);
        scan = await RefreshInstallStateAsync(scan, ct).ConfigureAwait(false);
        await PersistEpicLaunchTriplesAsync(scan, ct).ConfigureAwait(false);

        if (scan.Count == 0)
        {
            // Published even here: a machine with no launcher installed is a
            // state the watchers must not keep re-reading either.
            PublishScanBaseline(scan);
            _logger.LogInformation("Local library sync found no candidates; nothing to resolve.");
            return new LibrarySyncReport(0, null, stopwatch.Elapsed, scan);
        }

        // LowerBound, and this is the job that makes it matter: localconfig.vdf
        // sees only what the client has synced to this machine, so on any
        // library with a second PC its figure sits below the account-wide one
        // the remote job stored. Recording that as an observation would put a
        // permanent sawtooth in playtime_snapshots at 15-minute intervals.
        var result = await _resolver.ResolveAsync([.. scan.All], ct, PlaytimeView.LowerBound);
        PublishScanBaseline(scan);
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

    /// <summary>
    /// Soft-failing on purpose. An unwritable cache row costs the action band its
    /// Epic buttons; it must never cost the user the ownership rows this pass
    /// resolved. Skipped when no store is wired up or the scan found no triples.
    /// </summary>
    private async Task PersistEpicLaunchTriplesAsync(LocalLibraryScan scan, CancellationToken ct)
    {
        if (_epicLaunchKeys is null || scan.EpicLaunchTriples.Count == 0)
        {
            return;
        }

        try
        {
            await _epicLaunchKeys.SaveAsync(scan.EpicLaunchTriples, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Stored {Count} Epic launch triples from the local catalog.",
                scan.EpicLaunchTriples.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unwritable cache row costs the action band an Epic button; it
            // must never cost the user the ownership rows this pass resolved.
            _logger.LogWarning(ex, "Could not store the Epic launch triples; Epic actions may not draw.");
        }
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
    private readonly IOwnershipInventoryRepository _inventories;
    private readonly ISettingsRepository? _settings;
    private readonly TimeProvider _clock;
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
        IOwnershipInventoryRepository inventories,
        ISteamWebApiClient? steamWeb = null,
        IEpicAccountClient? epicApi = null,
        ISettingsRepository? settings = null,
        TimeProvider? clock = null)
    {
        _inventories = inventories;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
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
        var inventoryBatch = await OwnedCandidatesAsync(scan.Steam, ct);
        var owned = inventoryBatch.Candidates;

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

        // HTTP and GOG stay outside the gate. Steam/Epic completion can change
        // while backfill is waiting, including a reusable startup scan.
        using var lease = await _gate.EnterAsync(ct).ConfigureAwait(false);
        scan = await _local.RefreshInstallStateAsync(scan, ct, retainGalaxyInstallState: false).ConfigureAwait(false);

        var candidates = scan.Steam
            .Concat(owned)
            .Concat(scan.Epic)
            .Concat(epicOwned)
            .Concat(scan.Gog)
            .ToList();

        if (candidates.Count == 0)
        {
            await CompleteInventoriesAsync(inventoryBatch, ct);
            _local.PublishScanBaseline(scan);
            _logger.LogInformation("Remote ownership sync found no candidates; nothing to resolve.");
            return new LibrarySyncReport(0, null, stopwatch.Elapsed, scan);
        }

        // LowerBound here too. The union of both sources is the best estimate
        // available, but it is still an estimate: a session played offline on
        // this machine since the last cloud sync is in neither. The clamp is a
        // no-op on the normal path and keeps the series monotonic on the
        // abnormal one.
        var result = await _resolver.ResolveAsync(candidates, ct, PlaytimeView.LowerBound);
        await CompleteInventoriesAsync(inventoryBatch, ct);
        _local.PublishScanBaseline(scan);
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
    /// credential and the account's SteamID64. Targets include accounts named by
    /// the local scan and the confirmed signed-in account, including one whose
    /// empty or never-played library has no local game records.
    /// </summary>
    private async Task<OwnedLibraryBatch> OwnedCandidatesAsync(
        IReadOnlyList<CandidateOwnership> local, CancellationToken ct)
    {
        if (_steamWeb is null || !await _steamWeb.IsConfiguredAsync(ct))
        {
            return new([], []);
        }

        // The union of every account the scan named, not just the ones that won
        // their candidate's AccountRef.
        //
        // That column carries the play tuple's winner, so on a PC with two
        // accounts an account that never out-played the other appears in it
        // nowhere at all — and this loop, reading only that column, would never
        // ask Steam about them. Their owned library would then be invisible to
        // Winnow, which is exactly the population the account filter has to be
        // able to see before it can honestly hide anything. The per-account list
        // names every account each reader saw, so a second user is asked about
        // on the strength of one game they have played.
        //
        // Ordinal-distinct and ordered by first appearance: the same account
        // reached through two candidates is one account, and a stable order keeps
        // the request sequence reproducible in a log.
        var accounts = local
            .SelectMany(c => c.Accounts
                .Select(a => a.AccountRef)
                .Append(c.AccountRef))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (_settings is not null && SteamOwnedAccount.Clean(await _settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct)) is { } confirmed
            && !accounts.Contains(confirmed, StringComparer.Ordinal)) accounts.Add(confirmed);

        var owned = new List<CandidateOwnership>();
        var completions = new List<(OwnershipInventoryAttempt Attempt, SteamOwnedLibrary Library)>();
        foreach (var account in accounts)
        {
            if (!SteamId.TryParse(account!, out var steamId))
            {
                continue;
            }

            var attempt = await _inventories.BeginAttemptAsync(ExternalIdProviders.Steam, steamId.AccountRef,
                OwnershipInventorySources.SteamOwnedGames, ct);
            try
            {
                var library = await _steamWeb.GetOwnedGamesAsync(steamId, ct: ct);
                if (!library.Succeeded || library.SteamId != steamId) continue;
                owned.AddRange(library.ToCandidates(SteamWebApiClient.SourceName, _clock.GetUtcNow().UtcDateTime));
                if (library.IsComplete) completions.Add((attempt, library));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One account's profile being private or the endpoint being down
                // must not cost the local scan, which needs no network at all.
                _logger.LogWarning(
                    ex, "Owned-library lookup failed for one account; continuing with local files.");
            }
        }

        return new(owned, completions);
    }

    private async Task CompleteInventoriesAsync(OwnedLibraryBatch batch, CancellationToken ct)
    {
        foreach (var (attempt, library) in batch.Completions)
            await _inventories.CompleteAsync(attempt, library.ObservedAt, library.Games.Count, ct);
    }

    private sealed record OwnedLibraryBatch(IReadOnlyList<CandidateOwnership> Candidates,
        IReadOnlyList<(OwnershipInventoryAttempt Attempt, SteamOwnedLibrary Library)> Completions);

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
