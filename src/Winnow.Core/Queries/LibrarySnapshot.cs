using Winnow.Core.Domain;

namespace Winnow.Core.Queries;

/// <summary>The library's stored facts and derived buckets from one read snapshot.</summary>
public sealed record LibrarySnapshot(
    IReadOnlyList<OwnershipBucket> Buckets,
    IReadOnlyList<Work> Works,
    IReadOnlyList<Ownership> Ownerships,
    IReadOnlyList<Release> Releases,
    IReadOnlyList<ExternalId> ExternalIds,
    IReadOnlyList<GameList> Lists,
    IReadOnlyList<ListItem> ListItems);
