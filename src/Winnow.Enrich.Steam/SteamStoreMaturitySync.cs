using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Steam.Model;
using Winnow.Enrich.Steam.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Steam;

/// <summary>
/// Re-parses cached Steam store bodies for <c>content_descriptorids</c> and writes
/// one <c>work_maturity</c> row per work that carries any. Reads through
/// <see cref="ISteamStoreClient.GetCachedItemsAsync"/>, which touches the network
/// on no path — the descriptors are already on disk.
/// </summary>
public sealed class SteamStoreMaturitySync
{
    private readonly ISteamStoreClient _store;
    private readonly ISteamMaturityTargetSource _targets;
    private readonly IWorkMaturityRepository _maturity;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamStoreMaturitySync> _log;

    public SteamStoreMaturitySync(
        ISteamStoreClient store,
        ISteamMaturityTargetSource targets,
        IWorkMaturityRepository maturity,
        TimeProvider clock,
        ILogger<SteamStoreMaturitySync> log)
    {
        _store = store;
        _targets = targets;
        _maturity = maturity;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Runs the Steam content-descriptor pass. Returns the number of
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
            _log.LogWarning(ex, "Steam store maturity pass failed; no maturity evidence written this run.");
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

        var items = await _store.GetCachedItemsAsync(
            targets.Select(t => t.AppId).Distinct(StringComparer.Ordinal), ct);
        if (items.Count == 0)
        {
            _log.LogDebug(
                "No cached Steam store bodies for {Targets} appids; nothing to read descriptors from.",
                targets.Count);
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
                if (items.TryGetValue(target.AppId, out var item))
                {
                    tokens.AddRange(SteamContentDescriptors.TokensFor(item.ContentDescriptorIds));
                }
            }

            var descriptors = MaturityRules.Join(tokens);
            if (descriptors is null)
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
                    Source = MaturitySources.SteamStore,
                    Ratings = null,
                    Descriptors = descriptors,
                    ObservedAt = observedAt,
                },
                ct);
            written++;
        }

        _log.LogInformation(
            "Steam store maturity: {Written} of {Works} works carry content descriptors.",
            written, targets.Select(t => t.WorkId).Distinct().Count());
        return written;
    }
}
