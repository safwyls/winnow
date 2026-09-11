using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// Every outcome is a word, never a percentage. No total is knowable in
/// advance, so no proportion can be honest.
/// </summary>
public enum GameRefetchOutcome
{
    Updated,
    NothingNew,
    NoSourceToAsk,
    NotConfigured,
    Unreachable,
    TooSoon,
    WorkNotFound,
}

public sealed record GameRefetchResult(GameRefetchOutcome Outcome)
{
    public int RowsWritten { get; init; }

    public bool AskedIgdb { get; init; }

    public bool AskedSteam { get; init; }

    public bool MetadataFilled { get; init; }

    public bool UsedPin { get; init; }

    public TimeSpan RetryAfter { get; init; }

    public bool Succeeded => Outcome is GameRefetchOutcome.Updated or GameRefetchOutcome.NothingNew;
}

/// <summary>
/// Tunables for <see cref="GameRefetchService"/>. The per-work cooldown is
/// per work, not per library, so a user working through several games is not
/// punished for the first one.
/// </summary>
public sealed class GameRefetchOptions
{
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// The App-layer seam the details modal's view model takes, so it holds an
/// interface rather than the concrete service — the same arrangement
/// <see cref="IIgdbAssignmentService"/> has.
/// </summary>
public interface IGameRefetch
{
    Task<GameRefetchResult> RefetchAsync(long workId, CancellationToken ct = default);
}

/// <summary>
/// Re-asks both sources about one work. A pinned work refetches against its
/// pinned IGDB id and is never re-resolved; an unpinned work refetches
/// against the IGDB id it already resolved to. Re-resolution is the
/// wrong-game control's job; this control is for data that is thin, not for
/// a match that is wrong.
///
/// <para><c>TimeSpan.Zero</c> is how both clients are told to ignore what is
/// cached: <c>IgdbClient.Cutoff</c> and <c>SteamStoreClient.Cutoff</c> both
/// read a non-positive TTL as <c>DateTime.MaxValue</c>, so no stored entry
/// is fresh enough and every id goes to the network.</para>
///
/// <para>Two independent brakes: the Polly rate limiters on both typed
/// clients still apply (IGDB 4 req/s, Steam 2 req/s), and a per-work
/// cooldown (default 5 min) refuses a second refetch of the same work.
/// The mark is taken before the requests go out, so a failed refetch also
/// waits.</para>
///
/// <para><see cref="RefetchAsync"/> never throws for an enrichment failure;
/// it reports <see cref="GameRefetchOutcome.Unreachable"/>. Cancellation on
/// the caller's token is rethrown, because the caller asking to stop is not
/// a failure.</para>
/// </summary>
public sealed class GameRefetchService : IGameRefetch
{
    private static readonly TimeSpan ForceRefetch = TimeSpan.Zero;

    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IWorkIgdbPinRepository _pins;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly WorkReceptionWriter _reception;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly GameRefetchOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<GameRefetchService> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<long, (long Revision, DateTimeOffset At)> _lastRefetch = [];

    public GameRefetchService(
        IWorkRepository works,
        IReleaseRepository releases,
        IWorkIgdbPinRepository pins,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        WorkReceptionWriter reception,
        IUnitOfWorkFactory unitOfWork,
        ILogger<GameRefetchService> logger,
        GameRefetchOptions? options = null,
        TimeProvider? clock = null)
    {
        _works = works;
        _releases = releases;
        _pins = pins;
        _igdb = igdb;
        _steamStore = steamStore;
        _reception = reception;
        _unitOfWork = unitOfWork;
        _options = options ?? new GameRefetchOptions();
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<GameRefetchResult> RefetchAsync(long workId, CancellationToken ct = default)
    {
        try
        {
            return await RunAsync(workId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Refetch for work {WorkId} failed; nothing was written.", workId);
            return new GameRefetchResult(GameRefetchOutcome.Unreachable);
        }
    }

    private async Task<GameRefetchResult> RunAsync(long workId, CancellationToken ct)
    {
        var work = await _works.GetAsync(workId, ct);
        if (work is null)
        {
            return new GameRefetchResult(GameRefetchOutcome.WorkNotFound);
        }

        if (RemainingCooldown(work.IgdbMapping) is { } retryAfter)
        {
            return new GameRefetchResult(GameRefetchOutcome.TooSoon) { RetryAfter = retryAfter };
        }

        var pin = await _pins.GetAsync(workId, ct);
        var igdbId = work.IgdbId;
        var appIds = await SteamAppIdsAsync(workId, ct);

        if (igdbId is null && appIds.Count == 0)
        {
            return new GameRefetchResult(GameRefetchOutcome.NoSourceToAsk) { UsedPin = pin is not null };
        }

        var configured = igdbId is null || await _igdb.IsConfiguredAsync(ct);
        MarkRefetched(work.IgdbMapping);

        var game = igdbId is { } id && configured ? await FetchGameAsync(id, ct) : null;
        var items = appIds.Count > 0
            ? await _steamStore.GetItemsAsync(appIds, ForceRefetch, ct)
            : new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);

        if (game is null && items.Count == 0)
        {
            return new GameRefetchResult(
                igdbId is not null && !configured
                    ? GameRefetchOutcome.NotConfigured
                    : GameRefetchOutcome.Unreachable)
            {
                AskedIgdb = igdbId is not null && configured,
                AskedSteam = appIds.Count > 0,
                UsedPin = pin is not null,
            };
        }

        var rows = 0;
        if (game is not null)
        {
            rows += await _reception.ApplyIgdbAsync(work.IgdbMapping, game, ct);
        }

        var storeItem = appIds
            .Select(appId => items.GetValueOrDefault(appId))
            .FirstOrDefault(item => item is not null);

        if (storeItem is not null)
        {
            rows += await _reception.ApplySteamAsync(workId, storeItem.Reviews, ct);
        }

        var metadataFilled = game is not null
                             && await FillMetadataAsync(work, game, ct);

        _logger.LogInformation(
            "Refetch for work {WorkId}: asked IGDB {AskedIgdb}, asked the Steam store for {AppIds} appid(s), "
            + "{Rows} reception row(s) changed, metadata filled {Metadata}.",
            workId, game is not null, appIds.Count, rows, metadataFilled);

        return new GameRefetchResult(
            rows > 0 || metadataFilled ? GameRefetchOutcome.Updated : GameRefetchOutcome.NothingNew)
        {
            RowsWritten = rows,
            AskedIgdb = game is not null,
            AskedSteam = storeItem is not null,
            MetadataFilled = metadataFilled,
            UsedPin = pin is not null,
        };
    }

    private async Task<IgdbGame?> FetchGameAsync(long igdbId, CancellationToken ct)
    {
        var games = await _igdb.GetGamesAsync([igdbId], ForceRefetch, ct);
        return games.FirstOrDefault(g => g.IgdbId == igdbId);
    }

    private async Task<IReadOnlyList<string>> SteamAppIdsAsync(long workId, CancellationToken ct)
    {
        var appIds = new List<string>();
        foreach (var release in await _releases.GetByWorkAsync(workId, ct))
        {
            foreach (var externalId in await _releases.GetExternalIdsAsync(release.Id, ct))
            {
                if (externalId.Provider == ExternalIdProviders.Steam
                    && !string.IsNullOrWhiteSpace(externalId.ProviderId)
                    && !appIds.Contains(externalId.ProviderId, StringComparer.Ordinal))
                {
                    appIds.Add(externalId.ProviderId);
                }
            }
        }

        return appIds;
    }

    private async Task<bool> FillMetadataAsync(Work work, IgdbGame game, CancellationToken ct)
    {
        var patch = new WorkEnrichment(
            work.Id,
            Name: work.NameIsProvisional && !string.IsNullOrWhiteSpace(game.Name) ? game.Name : null,
            IgdbId: work.IgdbId is null ? game.IgdbId : null,
            FirstReleaseYear: work.FirstReleaseYear is null ? game.FirstReleaseYear : null,
            Summary: string.IsNullOrWhiteSpace(work.Summary) ? game.Summary : null,
            CoverUrl: string.IsNullOrWhiteSpace(work.CoverUrl) ? game.CoverUrl : null,
            Publisher: string.IsNullOrWhiteSpace(work.Publisher)
                ? EnrichmentSyncService.PrimaryPublisher(game)
                : null)
        {
            IgdbGameType = work.IgdbGameType is null ? game.GameType : null,
            IgdbParentId = work.IgdbParentId is null ? game.ParentGameId : null,
            IgdbVersionParentId = work.IgdbVersionParentId is null ? game.VersionParentId : null,
            NameSource = FieldSources.Igdb,
            ExpectedIgdbMapping = work.IgdbMapping,
            RefetchCurrentIgdbMapping = true,
        };

        if (patch.IsEmpty)
        {
            return false;
        }

        using var scope = _unitOfWork.Begin();
        var current = await _works.GetAsync(work.Id, ct);
        if (current?.IgdbMapping != work.IgdbMapping) return false;
        var namePromoted = await _works.ApplyEnrichmentAsync(patch, ct);
        if (namePromoted)
        {
            foreach (var release in await _releases.GetByWorkAsync(work.Id, ct))
            {
                await _releases.UpdateNameAsync(release.Id, patch.Name!, ct);
            }
        }

        var changed = current != await _works.GetAsync(work.Id, ct);
        scope.Commit();
        return changed;
    }

    private TimeSpan? RemainingCooldown(IgdbMappingVersion mapping)
    {
        var cooldown = _options.Cooldown;
        if (cooldown <= TimeSpan.Zero)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_lastRefetch.TryGetValue(mapping.WorkId, out var last) || last.Revision != mapping.Revision)
            {
                return null;
            }

            var elapsed = _clock.GetUtcNow() - last.At;
            return elapsed >= cooldown ? null : cooldown - elapsed;
        }
    }

    private void MarkRefetched(IgdbMappingVersion mapping)
    {
        lock (_gate)
        {
            _lastRefetch[mapping.WorkId] = (mapping.Revision, _clock.GetUtcNow());
        }
    }
}
