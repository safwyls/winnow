using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// The library-wide pass for screenshots, artworks and ratings, modelled on
/// <c>FacetSyncService</c> statement for statement. Runs over
/// <see cref="ILibraryQueryRepository.GetFacetTargetsAsync"/> — every release
/// with its IGDB id and Steam appid — and is cache-first through both
/// clients, so a warm library costs zero requests. It lives in
/// <c>Winnow.App.Services</c> for the reason game-library-design.md section 5.1
/// gives for <c>LocalLibrarySyncService</c> and
/// <c>RemoteOwnershipSyncService</c> being there. It soft-fails whole: an
/// unreachable IGDB leaves the Steam half running and stored figures as they
/// were, and a failure of the pass is a degraded run rather than a crashed
/// one. It deliberately does NOT ride <c>EnrichmentSyncService</c>, whose
/// target query returns only works still missing a metadata column — a fully
/// enriched work would never be revisited and would never get screenshots.
/// </summary>
public sealed class ReceptionSyncService
{
    private readonly ILibraryQueryRepository _libraryQueries;
    private readonly WorkReceptionWriter _writer;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly ILogger<ReceptionSyncService> _logger;

    public ReceptionSyncService(
        ILibraryQueryRepository libraryQueries,
        WorkReceptionWriter writer,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        ILogger<ReceptionSyncService> logger)
    {
        _libraryQueries = libraryQueries;
        _writer = writer;
        _igdb = igdb;
        _steamStore = steamStore;
        _logger = logger;
    }

    public async Task<ReceptionSyncReport> SyncAsync(CancellationToken ct = default)
    {
        try
        {
            return await RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Reception sync failed; stored screenshots and ratings stay as they were.");
            return ReceptionSyncReport.Empty;
        }
    }

    private async Task<ReceptionSyncReport> RunAsync(CancellationToken ct)
    {
        var targets = await _libraryQueries.GetFacetTargetsAsync(ct);
        if (targets.Count == 0)
        {
            return ReceptionSyncReport.Empty;
        }

        var games = await ReadIgdbAsync(targets, ct);
        var items = await ReadSteamAsync(targets, ct);

        var worksWritten = 0;
        var rowsWritten = 0;
        var igdbDone = new HashSet<long>();
        var steamDone = new HashSet<long>();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            var changed = 0;

            if (target.IgdbId is { } igdbId
                && games.TryGetValue(igdbId, out var game)
                && igdbDone.Add(target.WorkId))
            {
                changed += await _writer.ApplyIgdbAsync(target.IgdbMapping, game, ct);
            }

            if (target.SteamAppId is { } appId
                && items.TryGetValue(appId, out var item)
                && steamDone.Add(target.WorkId))
            {
                changed += await _writer.ApplySteamAsync(target.WorkId, item.Reviews, ct);
            }

            if (changed > 0)
            {
                worksWritten++;
                rowsWritten += changed;
            }
        }

        var report = new ReceptionSyncReport(
            WorksExamined: igdbDone.Count + steamDone.Count,
            IgdbGamesRead: games.Count,
            SteamItemsRead: items.Count,
            WorksWritten: worksWritten,
            RowsWritten: rowsWritten);

        _logger.LogInformation(
            "Reception sync: {Igdb} IGDB games and {Steam} store items read, "
            + "{Works} works updated ({Rows} rows).",
            report.IgdbGamesRead, report.SteamItemsRead, report.WorksWritten, report.RowsWritten);

        return report;
    }

    private async Task<IReadOnlyDictionary<long, IgdbGame>> ReadIgdbAsync(
        IReadOnlyList<FacetTarget> targets, CancellationToken ct)
    {
        var ids = targets.Select(t => t.IgdbId).OfType<long>().Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, IgdbGame>();
        }

        try
        {
            var games = await _igdb.GetGamesAsync(ids, ct: ct);
            return games.ToDictionary(g => g.IgdbId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "IGDB unavailable for reception sync; screenshots and ratings stay as they were.");
            return new Dictionary<long, IgdbGame>();
        }
    }

    private async Task<IReadOnlyDictionary<string, SteamStoreItem>> ReadSteamAsync(
        IReadOnlyList<FacetTarget> targets, CancellationToken ct)
    {
        var appIds = targets
            .Select(t => t.SteamAppId)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (appIds.Length == 0)
        {
            return new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
        }

        try
        {
            return await _steamStore.GetItemsAsync(appIds, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Steam store unavailable for reception sync; review figures stay as they were.");
            return new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
        }
    }
}

public sealed record ReceptionSyncReport(
    int WorksExamined,
    int IgdbGamesRead,
    int SteamItemsRead,
    int WorksWritten,
    int RowsWritten)
{
    public static ReceptionSyncReport Empty { get; } = new(0, 0, 0, 0, 0);
}
