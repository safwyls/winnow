using System.Diagnostics;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Steam;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

/// <summary>
/// Replaces placeholder work names (<c>App 1203620</c>) with real titles.
///
/// <para><b>Two sources, deliberately ordered.</b> IGDB is the designed
/// metadata backbone (§4.4) and wins any disagreement — it carries the
/// canonical title plus the year, summary and cover the rest of M1 wants.
/// Steam's keyless store endpoint fills whatever IGDB does not answer for, and
/// covers the case where no IGDB credentials are configured at all, so the
/// library is never stuck showing appids because of a missing API key.</para>
///
/// <para>The Steam endpoint is undocumented, so it is strictly a fallback and
/// soft-fails to "no data" (see Hoard.Enrich.Steam). A run that resolves
/// nothing is a normal outcome, not an error.</para>
///
/// <para>§5.1: this composes ingest-adjacent sources and the repositories, and
/// the UI never calls it — Program sequences it, the view models read the
/// database afterwards.</para>
/// </summary>
public sealed class EnrichmentSyncService
{
    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly ILogger<EnrichmentSyncService> _logger;

    public EnrichmentSyncService(
        IWorkRepository works,
        IReleaseRepository releases,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        ILogger<EnrichmentSyncService> logger)
    {
        _works = works;
        _releases = releases;
        _igdb = igdb;
        _steamStore = steamStore;
        _logger = logger;
    }

    /// <summary>
    /// Names every work still carrying a placeholder. Idempotent: a promoted
    /// work drops out of the provisional set, so a second run has nothing to do
    /// and costs nothing beyond one indexed query.
    /// </summary>
    public async Task<EnrichmentReport> EnrichAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var targets = await _works.GetProvisionalNameTargetsAsync(ExternalIdProviders.Steam, ct);
        if (targets.Count == 0)
        {
            _logger.LogInformation("No provisional names outstanding; enrichment has nothing to do.");
            return new EnrichmentReport(0, 0, 0, stopwatch.Elapsed);
        }

        var appIds = targets.Select(t => t.ProviderId).Distinct().ToArray();
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);

        // 1. IGDB first — the backbone, and the only source that also carries
        //    year/summary/cover for the later metadata passes.
        var fromIgdb = 0;
        if (await _igdb.IsConfiguredAsync(ct))
        {
            foreach (var (appId, match) in await _igdb.ResolveBySteamAppIdsAsync(appIds, ct: ct))
            {
                if (!string.IsNullOrWhiteSpace(match.Name))
                {
                    titles[appId] = match.Name;
                    fromIgdb++;
                }
            }
        }
        else
        {
            _logger.LogInformation(
                "IGDB is not configured; falling back to the Steam store for titles. "
                + "Set Igdb__ClientId / Igdb__ClientSecret to enable the metadata backbone.");
        }

        // 2. Steam store for the remainder. Undocumented endpoint, soft-fails.
        var unresolved = appIds.Where(id => !titles.ContainsKey(id)).ToArray();
        var fromSteam = 0;
        if (unresolved.Length > 0)
        {
            foreach (var (appId, item) in await _steamStore.GetItemsAsync(unresolved, ct: ct))
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    titles[appId] = item.Name;
                    fromSteam++;
                }
            }
        }

        // 3. Promote. Work and release move together — releases.name holds the
        //    same placeholder and has no flag of its own.
        var promoted = 0;
        foreach (var target in targets)
        {
            if (!titles.TryGetValue(target.ProviderId, out var title))
            {
                continue;
            }

            await _works.UpdateNameAsync(target.WorkId, title, nameIsProvisional: false, ct);
            await _releases.UpdateNameAsync(target.ReleaseId, title, ct);
            promoted++;
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Enrichment: {Promoted} of {Outstanding} names promoted in {Elapsed:n1}s "
            + "({Igdb} from IGDB, {Steam} from the Steam store).",
            promoted, targets.Count, stopwatch.Elapsed.TotalSeconds, fromIgdb, fromSteam);

        return new EnrichmentReport(targets.Count, promoted, fromIgdb, stopwatch.Elapsed);
    }
}

/// <param name="Outstanding">Works carrying a placeholder name when the run began.</param>
/// <param name="Promoted">Works given a real title this run.</param>
/// <param name="FromIgdb">How many of the promotions came from IGDB rather than the fallback.</param>
/// <param name="Elapsed">Wall-clock time for the whole pass.</param>
public sealed record EnrichmentReport(
    int Outstanding,
    int Promoted,
    int FromIgdb,
    TimeSpan Elapsed);
