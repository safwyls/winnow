using System.Collections.ObjectModel;
using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Plugins;
using Winnow.PluginSdk;

namespace Winnow.App.Services;

/// <summary>Plugin rankings operate on the same visible, resolved games and verdicts as the built-in feed.</summary>
public sealed class PluginFeedService(
    PluginCatalog catalog,
    ILibraryQueryRepository library,
    IFeedFeedbackRepository feedback,
    IFacetRepository facets,
    TimeProvider? clock = null)
{
    /// <summary>One budget for snapshot reads, queued invocations and all providers together.</summary>
    public TimeSpan AggregateTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<FeedSupplement> GetShelvesAsync(DateTime now, CancellationToken ct = default)
    {
        using var budget = new CancellationTokenSource(AggregateTimeout, clock ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            var result = await ReadAsync(now, deadline.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Expiry during shared input reads has the same optional-result contract
            // as expiry while queued or executing a provider. Caller cancellation stays distinct.
            return new([], 0);
        }
    }

    private async Task<FeedSupplement> ReadAsync(DateTime now, CancellationToken ct)
    {
        var providers = catalog.GetActive<IRecommendationFeedPlugin>();
        if (providers.Count == 0) return new([], 0);

        var snapshot = await library.GetSnapshotAsync(BucketThresholds.Default, ct).ConfigureAwait(false);
        var verdicts = await feedback.GetActiveVerdictsAsync(now, ct).ConfigureAwait(false);
        var facetSnapshot = await facets.GetSnapshotAsync(ct).ConfigureAwait(false);
        var works = snapshot.Works.ToDictionary(work => work.Id);
        var externalIds = snapshot.ExternalIds.ToLookup(externalId => externalId.ReleaseId);
        var suppressed = RecommendationGame.ResolveFeedback(snapshot,
            verdicts.Where(verdict => verdict.Kind is FeedVerdictKinds.NotInterested or FeedVerdictKinds.Snoozed)
                .Select(verdict => verdict.ReleaseId));
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var resolved in RecommendationGame.Build(snapshot, facetSnapshot))
        {
            var primary = resolved.Action;
            if (primary.Game.Bucket is LibraryBuckets.Retired or LibraryBuckets.Derelict
                || suppressed.Contains(resolved.WorkId) || resolved.NameIsProvisional) continue;

            var id = primary.OwnershipId.ToString(CultureInfo.InvariantCulture);
            var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in resolved.Members)
            {
                foreach (var external in externalIds[member.ReleaseId]) identifiers.TryAdd(external.Provider, external.ProviderId);
                if (works.TryGetValue(member.WorkId, out var memberWork) && memberWork.IgdbId is { } igdbId)
                    identifiers.TryAdd("igdb", igdbId.ToString(CultureInfo.InvariantCulture));
            }
            var descriptors = resolved.Facets.FacetIds.Select(facetId => facetSnapshot.ById.GetValueOrDefault(facetId))
                .OfType<Facet>().ToArray();
            var game = new PluginGame(id, resolved.Title, new ReadOnlyDictionary<string, string>(identifiers))
            {
                Installed = resolved.Installed,
                PlaytimeMinutes = primary.Game.PlaytimeMinutes,
                LastPlayedAt = primary.Game.LastPlayedAt is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null,
                Genres = Array.AsReadOnly(descriptors.Where(facet => facet.Kind == FacetKinds.Genre).Select(facet => facet.Name).Distinct(StringComparer.Ordinal).ToArray()),
                Tags = Array.AsReadOnly(descriptors.Where(facet => facet.Kind == FacetKinds.Tag).Select(facet => facet.Name).Distinct(StringComparer.Ordinal).ToArray()),
            };
            candidates[id] = new(game, primary.ReleaseId);
        }
        if (candidates.Count == 0) return new([], 0);
        var games = Array.AsReadOnly(candidates.Values.Select(candidate => candidate.Game).ToArray());
        async Task<FeedShelf?> ReadProviderAsync(PluginDescriptor descriptor)
        {
            IReadOnlyList<PluginRecommendation>? ranked;
            try
            {
                ranked = await catalog.InvokeAsync<IReadOnlyList<PluginRecommendation>>(descriptor,
                    (plugin, token) => ((IRecommendationFeedPlugin)plugin).GetRecommendationsAsync(games, token), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
            if (ranked is null) return null;
            var items = ranked.Where(item => item is not null && item.GameId is not null && candidates.ContainsKey(item.GameId)
                    && double.IsFinite(item.Score) && item.Score is >= 0 and <= 1
                    && !string.IsNullOrWhiteSpace(item.Reason) && item.Reason.Length <= 300
                    && !item.Reason.Any(char.IsControl))
                .OrderByDescending(item => item.Score).ThenBy(item => item.GameId, StringComparer.Ordinal)
                .DistinctBy(item => item.GameId, StringComparer.Ordinal).Take(10)
                .Select(item =>
                {
                    var candidate = candidates[item.GameId];
                    return new FeedItem(long.Parse(candidate.Game.Id, CultureInfo.InvariantCulture), candidate.ReleaseId, candidate.Game.Title, item.Reason.Trim());
                }).ToArray();
            if (items.Length == 0) return null;
            return new FeedShelf("plugin:" + descriptor.Manifest.Id, descriptor.Manifest.Name,
                "Recommendations from " + descriptor.Manifest.Name + ".", items.Take(6).ToArray())
            {
                Reserve = items.Skip(6).ToArray(),
            };
        }
        var shelves = await Task.WhenAll(providers.Select(ReadProviderAsync)).ConfigureAwait(false);
        return new(shelves.OfType<FeedShelf>().ToArray(), candidates.Count);
    }

    private sealed record Candidate(PluginGame Game, long ReleaseId);
}
