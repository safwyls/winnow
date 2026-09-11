using Winnow.Core.Domain;

namespace Winnow.Core.Queries;

/// <summary>Visible evidence for one resolved game, with a separate, explicit action subject.</summary>
public sealed record RecommendationGame(
    long WorkId,
    OwnershipBucket Action,
    string Title,
    string Store,
    bool NameIsProvisional,
    bool Installed,
    int StoreCount,
    IReadOnlyList<OwnershipBucket> Members,
    ReleaseFacets Facets)
{
    public IReadOnlyList<long> OwnershipIds => Members.Select(row => row.OwnershipId).Distinct().ToArray();
    public IReadOnlyList<long> ReleaseIds => Members.Select(row => row.ReleaseId).Distinct().ToArray();

    /// <summary>
    /// Uses the bucket query's eligible population and grouped play facts. A viable installed
    /// copy wins the action subject; identity order breaks ties. Hidden copies do not testify.
    /// </summary>
    public static IReadOnlyList<RecommendationGame> Build(LibrarySnapshot snapshot, FacetSnapshot facets)
    {
        var ownerships = snapshot.Ownerships.ToDictionary(item => item.Id);
        var works = snapshot.Works.ToDictionary(item => item.Id);
        var releases = snapshot.Releases.ToDictionary(item => item.Id);
        var result = new List<RecommendationGame>();
        foreach (var group in snapshot.Buckets.GroupBy(row => row.ResolvedWorkId))
        {
            var members = group.Where(row => ownerships.ContainsKey(row.OwnershipId)
                    && releases.ContainsKey(row.ReleaseId) && works.ContainsKey(row.WorkId))
                .OrderBy(row => row.Lifecycle?.IsDerelict == true)
                .ThenBy(row => !ownerships[row.OwnershipId].Installed)
                .ThenBy(row => row.WorkId != row.ResolvedWorkId)
                .ThenBy(row => row.WorkId).ThenBy(row => row.OwnershipId).ToArray();
            if (members.Length == 0) continue;
            var action = members[0];
            var display = members.OrderBy(row => row.WorkId != row.ResolvedWorkId)
                .ThenBy(row => row.WorkId).ThenBy(row => row.OwnershipId).First();
            var release = releases[display.ReleaseId];
            var work = works[display.WorkId];
            var memberFacets = members.Select(row => row.ReleaseId).Distinct()
                .Select(id => facets.ByRelease.GetValueOrDefault(id)).OfType<ReleaseFacets>().ToArray();
            result.Add(new RecommendationGame(group.Key, action,
                string.IsNullOrWhiteSpace(release.Name) ? work.Name : release.Name,
                ownerships[action.OwnershipId].Store, work.NameIsProvisional,
                members.Any(row => row.Lifecycle?.IsDerelict != true && ownerships[row.OwnershipId].Installed),
                members.Select(row => ownerships[row.OwnershipId].Store).Distinct(StringComparer.Ordinal).Count(),
                members, new ReleaseFacets(action.ReleaseId,
                    memberFacets.SelectMany(item => item.FacetIds).Distinct().Order().ToArray(),
                    memberFacets.SelectMany(item => item.GameModes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())));
        }
        return result;
    }

    /// <summary>Verdicts stay release-scoped in storage; their current meaning follows complete identity state.</summary>
    public static IReadOnlySet<long> ResolveFeedback(LibrarySnapshot snapshot, IEnumerable<long> releaseIds)
    {
        var releases = snapshot.Releases.ToDictionary(item => item.Id);
        return releaseIds.Distinct().Where(releases.ContainsKey)
            .Select(id => snapshot.IdentityResolution.SameGame.Resolve(releases[id].WorkId)).ToHashSet();
    }
}
