using Winnow.Core.Queries;

namespace Winnow.Recommend;

/// <summary>Chronological history shares visible game identity, but no recommendation filters or scores.</summary>
internal static class RecentlyPlayedShelf
{
    // Six desktop cards and four held cards let fullscreen show more without changing membership.
    internal const int Capacity = 10;

    internal static RecommendationShelf? Build(IReadOnlyList<RecommendationGame> games, DateTime asOfUtc)
    {
        var items = games
            .Where(game => !game.NameIsProvisional && game.Action.Game.Bucket != LibraryBuckets.Derelict
                && game.Action.Game.LastPlayedAt is { } played && played <= asOfUtc)
            .OrderByDescending(game => game.Action.Game.LastPlayedAt)
            .ThenBy(game => game.WorkId)
            .Take(Capacity)
            .Select(Present)
            .ToArray();
        return items.Length == 0 ? null : new RecommendationShelf
        {
            Id = ShelfIds.RecentlyPlayed,
            Title = "Recently played",
            Blurb = "Your latest games, in the order you last played them.",
            SupportsFeedback = false,
            Items = items,
        };
    }

    private static Recommendation Present(RecommendationGame game)
    {
        var row = game.Action;
        var explanation = new RecommendationReason
        {
            Primary = ReasonSignal.LastPlayed,
            Evidence = new ReasonEvidence
            {
                ReleaseId = row.ReleaseId,
                Title = game.Title,
                Store = game.Store,
                LastPlayedAt = row.Game.LastPlayedAt,
                PlaytimeMinutes = row.Game.PlaytimeMinutes,
                EvidenceReleaseIds = game.ReleaseIds,
            },
        };
        return new Recommendation
        {
            OwnershipId = row.OwnershipId, ReleaseId = row.ReleaseId, WorkId = game.WorkId,
            Title = game.Title, Store = game.Store, Bucket = row.Game.Bucket,
            Score = 0, Signals = [], Explanation = explanation,
            Reason = ReasonBuilder.Build(explanation, new RecommendationTuning()),
        };
    }
}
