using System.Diagnostics;
using Hoard.Core.Ingest;
using Hoard.Enrich.SteamWeb;
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
    private readonly ExternalIdResolver _resolver;
    private readonly ISteamWebApiClient? _steamWeb;
    private readonly ILogger<SteamSyncService> _logger;

    public SteamSyncService(
        SteamLibrarySource steam,
        ExternalIdResolver resolver,
        ILogger<SteamSyncService> logger,
        ISteamWebApiClient? steamWeb = null)
    {
        _steam = steam;
        _resolver = resolver;
        _logger = logger;
        _steamWeb = steamWeb;
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
        var candidates = local.Concat(owned).ToList();

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Steam sync found no candidates; nothing to resolve.");
            return new SteamSyncReport(0, null, stopwatch.Elapsed);
        }

        var result = await _resolver.ResolveAsync(candidates, ct);
        stopwatch.Stop();

        _logger.LogInformation(
            "Steam sync: {Candidates} candidates ({Local} local, {Owned} owned) in {Elapsed:n1}s — "
            + "{Created} new, {Matched} matched, {PlayRecords} play records, {Snapshots} snapshots, "
            + "{Promoted} names promoted.",
            candidates.Count, local.Count, owned.Count, stopwatch.Elapsed.TotalSeconds,
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
}

/// <param name="Candidates">Candidates the local scan produced.</param>
/// <param name="Result">Resolver outcome, or null when there was nothing to resolve.</param>
/// <param name="Elapsed">Wall-clock time for scan plus resolve.</param>
public sealed record SteamSyncReport(
    int Candidates,
    ResolveResult? Result,
    TimeSpan Elapsed);
