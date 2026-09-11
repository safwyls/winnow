using System.Collections.ObjectModel;
using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Plugins;
using Winnow.PluginSdk;

namespace Winnow.App.Services;

public sealed record PluginFeedSnapshot(IReadOnlyList<FeedShelf> Shelves, int CandidateCount);

/// <summary>Plugin rankings operate on the same visible, resolved games and verdicts as the built-in feed.</summary>
public sealed class PluginFeedService(
    PluginCatalog catalog,
    ILibraryQueryRepository library,
    IFeedFeedbackRepository feedback,
    IFacetRepository facets)
{
    public async Task<PluginFeedSnapshot> GetShelvesAsync(DateTime now, CancellationToken ct = default)
    {
        var providers = catalog.GetActive<IRecommendationFeedPlugin>();
        if (providers.Count == 0) return new([], 0);

        var snapshot = await library.GetSnapshotAsync(BucketThresholds.Default, ct).ConfigureAwait(false);
        var verdicts = await feedback.GetActiveVerdictsAsync(now, ct).ConfigureAwait(false);
        var facetSnapshot = await facets.GetSnapshotAsync(ct).ConfigureAwait(false);
        var works = snapshot.Works.ToDictionary(work => work.Id);
        var releases = snapshot.Releases.ToDictionary(release => release.Id);
        var ownerships = snapshot.Ownerships.ToDictionary(ownership => ownership.Id);
        var externalIds = snapshot.ExternalIds.ToLookup(externalId => externalId.ReleaseId);
        var resolvedWorks = snapshot.Buckets.GroupBy(row => row.WorkId)
            .ToDictionary(group => group.Key, group => group.First().ResolvedWorkId);
        var suppressed = verdicts.Where(verdict => verdict.Kind is FeedVerdictKinds.NotInterested or FeedVerdictKinds.Snoozed)
            .Select(verdict => releases.GetValueOrDefault(verdict.ReleaseId))
            .OfType<Release>().Select(release => resolvedWorks.GetValueOrDefault(release.WorkId, release.WorkId)).ToHashSet();

        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var group in snapshot.Buckets.GroupBy(row => row.ResolvedWorkId))
        {
            // Pick a viable store copy before identity precedence, as the built-in engine does.
            var primary = group.OrderBy(row => row.Lifecycle?.IsDerelict == true)
                .ThenBy(row => row.WorkId != row.ResolvedWorkId).ThenBy(row => row.WorkId).ThenBy(row => row.OwnershipId).First();
            if (primary.Game.Bucket is LibraryBuckets.Retired or LibraryBuckets.Derelict
                || suppressed.Contains(group.Key) || !ownerships.ContainsKey(primary.OwnershipId)
                || !releases.TryGetValue(primary.ReleaseId, out var release)
                || !works.TryGetValue(primary.WorkId, out var work) || work.NameIsProvisional) continue;

            var title = string.IsNullOrWhiteSpace(release.Name) ? work.Name : release.Name;
            var id = primary.OwnershipId.ToString(CultureInfo.InvariantCulture);
            var orderedMembers = group.OrderBy(row => row.OwnershipId != primary.OwnershipId).ThenBy(row => row.OwnershipId).ToArray();
            var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in orderedMembers)
            {
                foreach (var external in externalIds[member.ReleaseId]) identifiers.TryAdd(external.Provider, external.ProviderId);
                if (works.TryGetValue(member.WorkId, out var memberWork) && memberWork.IgdbId is { } igdbId)
                    identifiers.TryAdd("igdb", igdbId.ToString(CultureInfo.InvariantCulture));
            }
            var descriptors = group.SelectMany(row => facetSnapshot.ByRelease.TryGetValue(row.ReleaseId, out var assigned)
                    ? assigned.FacetIds : [])
                .Distinct().Select(facetId => facetSnapshot.ById.GetValueOrDefault(facetId)).OfType<Facet>().ToArray();
            var game = new PluginGame(id, title, new ReadOnlyDictionary<string, string>(identifiers))
            {
                Installed = group.Any(row => ownerships.TryGetValue(row.OwnershipId, out var ownership) && ownership.Installed),
                PlaytimeMinutes = primary.Game.PlaytimeMinutes,
                LastPlayedAt = primary.Game.LastPlayedAt is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null,
                Genres = Array.AsReadOnly(descriptors.Where(facet => facet.Kind == FacetKinds.Genre).Select(facet => facet.Name).Distinct(StringComparer.Ordinal).ToArray()),
                Tags = Array.AsReadOnly(descriptors.Where(facet => facet.Kind == FacetKinds.Tag).Select(facet => facet.Name).Distinct(StringComparer.Ordinal).ToArray()),
            };
            candidates[id] = new(game, primary.ReleaseId);
        }

        if (candidates.Count == 0) return new([], 0);
        var games = Array.AsReadOnly(candidates.Values.Select(candidate => candidate.Game).ToArray());
        var shelves = new List<FeedShelf>();
        foreach (var descriptor in providers)
        {
            var ranked = await catalog.InvokeAsync<IReadOnlyList<PluginRecommendation>>(descriptor,
                (plugin, token) => ((IRecommendationFeedPlugin)plugin).GetRecommendationsAsync(games, token), ct).ConfigureAwait(false);
            if (ranked is null) continue;
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
            if (items.Length == 0) continue;
            shelves.Add(new FeedShelf("plugin:" + descriptor.Manifest.Id, descriptor.Manifest.Name,
                "Recommendations from " + descriptor.Manifest.Name + ".", items.Take(6).ToArray())
            {
                Reserve = items.Skip(6).ToArray(),
            });
        }
        return new(shelves, candidates.Count);
    }

    private sealed record Candidate(PluginGame Game, long ReleaseId);
}
