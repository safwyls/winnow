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
    private readonly TimeProvider _clock;
    private readonly ILogger<IgdbMaturitySync> _log;

    public IgdbMaturitySync(
        IIgdbClient igdb,
        IIgdbMaturityTargetSource targets,
        IWorkMaturityRepository maturity,
        TimeProvider clock,
        ILogger<IgdbMaturitySync> log)
    {
        _igdb = igdb;
        _targets = targets;
        _maturity = maturity;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Runs the IGDB age-rating pass. Returns the number of
    /// <c>work_maturity</c> rows written. Soft-fails: a maturity lookup that
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

        foreach (var group in targets.GroupBy(t => t.WorkId))
        {
            ct.ThrowIfCancellationRequested();

            var tokens = new List<string>();
            foreach (var target in group)
            {
                if (ratings.TryGetValue(target.IgdbId, out var rated))
                {
                    tokens.AddRange(rated.RatingTokens);
                }
            }

            var joined = MaturityRules.Join(tokens);
            if (joined is null)
            {
                // No tokens means no row. An empty row would assert that a
                // work is not explicit, which is a claim nobody established;
                // absence is what makes a work not explicit.
                continue;
            }

            await _maturity.UpsertAsync(
                new WorkMaturity
                {
                    WorkId = group.Key,
                    Source = MaturitySources.Igdb,
                    Ratings = joined,
                    Descriptors = null,
                    ObservedAt = observedAt,
                },
                ct);
            written++;
        }

        _log.LogInformation(
            "IGDB maturity: {Written} of {Works} works carry an age rating.", written, targets.Count);
        return written;
    }
}
