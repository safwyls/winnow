using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Igdb;

/// <summary>
/// Fetches IGDB age ratings for every work that has an <c>igdb_id</c> and writes
/// one <c>work_maturity</c> row per work that carries at least one mappable
/// rating. Independent of the Steam pass: each module owns its own target query
/// and writer, and neither references the other.
/// </summary>
public sealed class IgdbMaturitySync
{
    private readonly IIgdbClient _igdb;
    private readonly IIgdbMaturityTargetSource _targets;
    private readonly IWorkMaturityRepository _maturity;
    private readonly IIgdbObservationWriter _observations;
    private readonly TimeProvider _clock;
    private readonly ILogger<IgdbMaturitySync> _log;

    public IgdbMaturitySync(
        IIgdbClient igdb,
        IIgdbMaturityTargetSource targets,
        IWorkMaturityRepository maturity,
        IIgdbObservationWriter observations,
        TimeProvider clock,
        ILogger<IgdbMaturitySync> log)
    {
        _igdb = igdb;
        _targets = targets;
        _maturity = maturity;
        _observations = observations;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Runs the IGDB age-rating pass. Returns the number of
    /// <c>work_maturity</c> rows written or retired. Soft-fails: a maturity lookup that
    /// fails must not fail the enrichment pass around it (section 5.1).
    /// </summary>
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        try
        {
            return await RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // §5.1: enrichment degrades, never breaks its caller.
            _log.LogWarning(ex, "IGDB maturity pass failed; no maturity evidence written this run.");
            return 0;
        }
    }

    private async Task<int> RunAsync(CancellationToken ct)
    {
        var targets = await _targets.GetTargetsAsync(ct);
        if (targets.Count == 0)
        {
            return 0;
        }

        var ratings = await _igdb.GetAgeRatingsAsync(targets.Select(t => t.IgdbId), ct: ct);
        if (ratings.Count == 0)
        {
            _log.LogDebug("IGDB reported no age ratings for any of {Targets} works.", targets.Count);
            return 0;
        }

        var observedAt = _clock.GetUtcNow().UtcDateTime;
        var written = 0;

        foreach (var target in targets.DistinctBy(t => t.WorkId))
        {
            ct.ThrowIfCancellationRequested();

            if (!ratings.TryGetValue(target.IgdbId, out var rated)) continue;
            var joined = MaturityRules.Join(rated.RatingTokens);
            if (joined is null)
            {
                var removed = false;
                await _observations.TryWriteAsync(target.IgdbMapping, async token =>
                    removed = await _maturity.DeleteAsync(target.WorkId, MaturitySources.Igdb, token), ct);
                if (removed) written++;
                continue;
            }

            var accepted = await _observations.TryWriteAsync(target.IgdbMapping, token => _maturity.UpsertAsync(
                new WorkMaturity
                {
                    WorkId = target.WorkId,
                    Source = MaturitySources.Igdb,
                    Ratings = joined,
                    Descriptors = null,
                    ObservedAt = observedAt,
                },
                token), ct);
            if (accepted) written++;
        }

        _log.LogInformation(
            "IGDB maturity: {Written} of {Works} works carry an age rating.", written, targets.Count);
        return written;
    }
}
