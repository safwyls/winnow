using Winnow.Api.Contracts.Companion;
using Winnow.Application.Library;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Application.Companion;

/// <summary>
/// Builds what a paired phone may read: the visible library as the user's own
/// visibility preferences show it, with store and IGDB identifiers for merging.
/// Account references, install paths, prices and credentials never enter it.
/// </summary>
public sealed class CompanionSnapshotBuilder(ILibraryQueryRepository library, ILibraryApplication preferencesSource, TimeProvider clock)
{
    public async Task<CompanionSnapshot> BuildAsync(CancellationToken ct = default)
    {
        var preferences = await preferencesSource.GetPreferencesAsync(ct);
        var snapshot = await library.GetSnapshotAsync(BucketThresholds.Default with
        {
            ShowNonGameEntries = preferences.ShowNonGameEntries,
            ShowExplicitContent = preferences.ShowExplicitContent,
            MaturityCap = BucketThresholds.ParseMaturityCap(preferences.MaturityCap),
        }, ct);
        var works = snapshot.Works.ToDictionary(x => x.Id);
        var releases = snapshot.Releases.ToDictionary(x => x.Id);
        var ownerships = snapshot.Ownerships.ToDictionary(x => x.Id);
        var storeIds = snapshot.ExternalIds
            .Where(x => x.Provider is not Core.Domain.ExternalIdProviders.Igdb)
            .GroupBy(x => x.ReleaseId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g
                .GroupBy(x => x.Provider, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First().ProviderId, StringComparer.Ordinal));
        var empty = new Dictionary<string, string>();

        var games = snapshot.Buckets.GroupBy(x => x.ResolvedWorkId).Select(group =>
        {
            var work = works[group.Key];
            var game = group.First().Game;
            var entries = group.Select(bucket =>
            {
                var release = releases[bucket.ReleaseId];
                var ownership = ownerships[bucket.OwnershipId];
                return new CompanionEntry(release.Id, ownership.Store, release.Name, release.EditionNote, release.IgdbVersionId,
                    release.Platform, storeIds.GetValueOrDefault(release.Id, empty), bucket.PlaytimeMinutes, bucket.LastPlayedAt,
                    ownership.AcquiredAt);
            }).OrderBy(x => x.ReleaseId).ToArray();
            return new CompanionGame(work.Id, work.Name, work.IgdbId, work.FirstReleaseYear, work.Summary, work.CoverUrl,
                game.Bucket, game.PlaytimeMinutes, game.LastPlayedAt, entries);
        }).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.WorkId).ToArray();

        var visibleReleases = games.SelectMany(g => g.Entries).Select(e => e.ReleaseId).ToHashSet();
        var items = snapshot.ListItems.ToLookup(x => x.ListId);
        var lists = snapshot.Lists.Where(x => !x.IsLive).Select(list => new CompanionList(list.Id, list.Name,
            items[list.Id].OrderBy(x => x.Position).Select(x => x.ReleaseId).Where(visibleReleases.Contains).ToArray())).ToArray();

        return new CompanionSnapshot(CompanionSnapshot.CurrentVersion, clock.GetUtcNow().UtcDateTime, games, lists);
    }
}
