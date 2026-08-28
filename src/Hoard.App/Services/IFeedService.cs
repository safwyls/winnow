namespace Hoard.App.Services;

/// <summary>
/// How much longitudinal evidence the feed was computed from, in the UI's own
/// vocabulary.
///
/// <para><b>Why this is not just <c>DataTier</c>.</b> Same rule
/// <see cref="StoreSignInProblem"/> follows for the Stores panel: §5.1 says the
/// view model reads through a service, and a view model that switched on
/// <c>Hoard.Recommend.DataTier</c> would be naming a scoring type in the
/// presentation layer — the boundary deleted by an enum rather than by a call.
/// The translation happens once, in <see cref="FeedService"/>, which is an
/// App-layer service and is allowed to see both sides.</para>
///
/// <para>The cases are kept apart because the <i>copy</i> differs: two of them
/// say out loud that the picks will sharpen, and the third says nothing at all,
/// because a feed that has earned its confidence should not keep apologising.</para>
/// </summary>
public enum FeedConfidence
{
    /// <summary>
    /// One sync per game, no sessions — every signal behind the feed is a
    /// retroactive fact. The picks are real; the feed says so rather than
    /// overclaiming.
    /// </summary>
    EarlyDays = 0,

    /// <summary>Weeks in: real playtime deltas or detected sessions are backing some of it.</summary>
    Settling = 1,

    /// <summary>Months in. Nothing is said on the screen at this tier.</summary>
    Established = 2,
}

/// <summary>
/// One game on one shelf: the identity the UI joins on, and the sentence that
/// is the whole reason it is here.
/// </summary>
/// <param name="OwnershipId">
/// The join key. The library's own tiles are keyed by ownership, and the feed
/// renders them rather than building a second projection of the same game —
/// see <see cref="Hoard.App.ViewModels.IGameTileSource"/>.
/// </param>
/// <param name="Title">The work's name, for a card whose tile could not be found.</param>
/// <param name="Reason">
/// The engine's own sentence, verbatim. Never truncated, never paraphrased,
/// never moved into a tooltip: a card that does not state its reason is a cover
/// in a grid, which is the incumbent's feed.
/// </param>
public sealed record FeedItem(
    long OwnershipId,
    long ReleaseId,
    string Title,
    string Reason);

/// <summary>One themed rail: a reason with games attached.</summary>
/// <param name="Id">Stable shelf id, so tests and code never match on display prose.</param>
/// <param name="Title">Display title ("Patched while you were away").</param>
/// <param name="Blurb">The shelf's own one-line pitch.</param>
/// <param name="Items">Ranked items. Never empty — an empty shelf is omitted, never rendered blank.</param>
public sealed record FeedShelf(
    string Id,
    string Title,
    string Blurb,
    IReadOnlyList<FeedItem> Items);

/// <summary>
/// One computed feed, reduced to what a screen can draw.
/// </summary>
/// <param name="Shelves">Shelves in presentation order — strongest story first. Possibly empty.</param>
/// <param name="CandidateCount">
/// How many owned games were actually scored. This is what lets the screen tell
/// "quiet feed" from "empty library", which are two completely different
/// sentences to write.
/// </param>
/// <param name="Confidence">See <see cref="FeedConfidence"/>.</param>
/// <param name="Failed">
/// True when the scoring pass did not complete. The distinction matters more
/// than it looks: a failed feed must never be worded as "nothing to show you",
/// which would be the app telling the user a falsehood about their own library.
/// </param>
public sealed record FeedSnapshot(
    IReadOnlyList<FeedShelf> Shelves,
    int CandidateCount,
    FeedConfidence Confidence,
    bool Failed)
{
    /// <summary>The answer when there is no engine to ask, or when asking threw.</summary>
    public static FeedSnapshot Unavailable { get; } =
        new([], 0, FeedConfidence.EarlyDays, Failed: true);
}

/// <summary>
/// Everything the Feed screen needs, expressed without naming a scoring type or
/// a repository.
///
/// <para><b>This is the §5.1 seam for the whole screen.</b> The temptation is to
/// resolve <c>IRecommendationEngine</c> in the view model and call
/// <c>GetShelvesAsync</c> — which reads as harmless and is the same boundary
/// violation as calling ingest to do work, arriving through a nicer-looking
/// method. <see cref="StoreConnections"/> is the precedent: the App-layer
/// service is the only type that sees both sides.</para>
///
/// <para><b>The scoring pass is expensive and this interface says so.</b>
/// Measured on the author's real library (997 candidates, 1,059 ownerships):
/// <b>~490–640 ms</b> per call, Release build, warm file. That is far past what
/// a UI thread may spend, so implementations must move it off — §5.1 pitfall 3 —
/// and callers must never await it before the window is up.</para>
///
/// <para><b>Nothing here throws.</b> A feed that cannot be computed is a
/// <see cref="FeedSnapshot"/> with <see cref="FeedSnapshot.Failed"/> set, so the
/// screen draws a sentence instead of the app going down over a recommendation.</para>
/// </summary>
public interface IFeedService
{
    /// <summary>
    /// Computes today's feed. Deterministic within a day: the engine seeds its
    /// near-tie shuffle from the date, so re-asking does not deal a new hand.
    /// </summary>
    Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default);
}
