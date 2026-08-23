using System.Diagnostics;
using Hoard.Ingest.Steam;
using Hoard.Resolve;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

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
public sealed class SteamSyncService
{
    private readonly SteamLibrarySource _steam;
    private readonly ExternalIdResolver _resolver;
    private readonly ILogger<SteamSyncService> _logger;

    public SteamSyncService(
        SteamLibrarySource steam,
        ExternalIdResolver resolver,
        ILogger<SteamSyncService> logger)
    {
        _steam = steam;
        _resolver = resolver;
        _logger = logger;
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

        var candidates = _steam.Scan();
        if (candidates.Count == 0)
        {
            _logger.LogInformation("Steam sync found no candidates; nothing to resolve.");
            return new SteamSyncReport(0, null, stopwatch.Elapsed);
        }

        var result = await _resolver.ResolveAsync(candidates, ct);
        stopwatch.Stop();

        _logger.LogInformation(
            "Steam sync: {Candidates} candidates in {Elapsed:n1}s — {Created} new, {Matched} matched, "
            + "{PlayRecords} play records, {Snapshots} snapshots, {Promoted} names promoted.",
            candidates.Count, stopwatch.Elapsed.TotalSeconds, result.CreatedReleases,
            result.MatchedExisting, result.PlayRecordsWritten, result.SnapshotsWritten,
            result.NamesPromoted);

        return new SteamSyncReport(candidates.Count, result, stopwatch.Elapsed);
    }
}

/// <param name="Candidates">Candidates the local scan produced.</param>
/// <param name="Result">Resolver outcome, or null when there was nothing to resolve.</param>
/// <param name="Elapsed">Wall-clock time for scan plus resolve.</param>
public sealed record SteamSyncReport(
    int Candidates,
    ResolveResult? Result,
    TimeSpan Elapsed);
