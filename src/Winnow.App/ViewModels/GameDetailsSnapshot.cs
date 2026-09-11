using Winnow.Core.Domain;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>Read-only detail projections gathered before an open detail view is refreshed.</summary>
internal sealed record GameDetailsSnapshot(
    GameTileViewModel Tile,
    string BucketLabel,
    DateTime ReadAtUtc,
    IReadOnlyList<UpdateEvent> Events,
    IReadOnlyList<UpdateEventViewModel> Updates,
    IReadOnlyDictionary<long, DateTime> Acknowledgements,
    IReadOnlyList<PlaytimeSnapshot> History,
    IReadOnlyList<Session> Sessions,
    IReadOnlyList<WorkRating> Ratings,
    IReadOnlyList<WorkImages> Images,
    IReadOnlyList<Ownership> Ownerships,
    IReadOnlyList<SessionJournalEntry> JournalEntries,
    IReadOnlyList<GameListMembership> ListMemberships,
    GameCoverageViewModel? Coverage,
    GameExpansionsViewModel? Expansions,
    string? BackgroundUrl);

internal sealed record LibraryDetailsContext(
    IReadOnlyList<CoverageEntry> Coverage,
    SameGameResolution Resolution,
    ExpansionGrouping Expansions,
    IReadOnlyDictionary<long, Work> Works)
{
    public long? WorkIdFor(GameTileViewModel target)
    {
        var entry = Coverage.FirstOrDefault(entry => entry.OwnershipId == target.OwnershipId);
        return entry is null ? null : Resolution.Resolve(entry.WorkId);
    }
}
