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
/// The two things a user can say about a card, in the UI's own vocabulary.
///
/// <para><b>They are two and not one, and that is the whole point.</b> "You
/// were right, I'm done with this" and "not now" are different intents; the
/// schema keeps them apart (migration 0011's CHECK) and so does the screen. A
/// single dismiss control that guessed which one was meant would throw the
/// difference away at the only moment the user knew it.</para>
///
/// <para><b>Why this is not <c>Hoard.Core.Domain.FeedVerdictKinds</c>.</b> Same
/// rule <see cref="FeedConfidence"/> follows: those are the storage's string
/// constants, and a view model passing <c>"not_interested"</c> across a command
/// would be writing schema vocabulary in the presentation layer. The
/// translation happens once, in <see cref="FeedService"/>, which is the
/// App-layer type allowed to see both sides.</para>
///
/// <para>There is deliberately no positive kind. The positive signal is
/// behavioural — a Hoard-launched session on a game the feed had just shown —
/// recorded with no UI and no asking (§6b). A thumbs-up would duplicate it with
/// strictly worse data, and unpressed it would teach the user this surface is
/// decoration.</para>
/// </summary>
public enum FeedVerdictKind
{
    /// <summary>"Not interested." Durable; holds until the user takes it back.</summary>
    NotInterested = 0,

    /// <summary>"Not now." Lapses by itself after the default snooze.</summary>
    Snoozed = 1,
}

/// <summary>
/// Where one stored verdict stands right now — computed by the service, which
/// owns the clock, so the inspection screen never has to hold one.
/// </summary>
public enum FeedVerdictStatus
{
    /// <summary>Still binding: not revoked, and (for a snooze) not yet lapsed.</summary>
    Active = 0,

    /// <summary>The user took it back. The row survives with its revocation stamp — undo does not cost history.</summary>
    Undone = 1,

    /// <summary>A snooze that ran out on its own. No write happened; expiry is evaluated at read time.</summary>
    Lapsed = 2,
}

/// <summary>
/// One row of "what you have told the feed", ready to draw.
/// </summary>
/// <param name="ReleaseId">
/// What the verdict was stored against — the identity the inspection screen
/// joins back to a library tile for a title and a cover, and what an undo is
/// addressed to.
/// </param>
/// <param name="Kind">Which of the two things the user said.</param>
/// <param name="CreatedAt">When they said it (UTC).</param>
/// <param name="ExpiresAt">When a snooze lapses (UTC). Null for a dismissal, which never does.</param>
/// <param name="RevokedAt">When they took it back (UTC), or null.</param>
/// <param name="Status">
/// The three-way answer, computed against the service's clock. Only
/// <see cref="FeedVerdictStatus.Active"/> rows can be undone — the rest are
/// history, and history is the point of keeping them.
/// </param>
public sealed record FeedVerdictRecord(
    long ReleaseId,
    FeedVerdictKind Kind,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    FeedVerdictStatus Status);

/// <summary>
/// What happened when the user pressed one of the two feedback controls.
/// </summary>
/// <param name="Saved">
/// False when the write did not land. The card must NOT then show a receipt: a
/// dismissal the app failed to store and reported as stored is the one lie this
/// surface cannot tell, because the game comes back tomorrow and the user
/// believes they already answered for it.
/// </param>
/// <param name="ExpiresAt">
/// When a snooze lapses, so the card can say the date rather than "in a while".
/// Null for a dismissal and for a failure.
/// </param>
public sealed record FeedVerdictOutcome(bool Saved, DateTime? ExpiresAt)
{
    /// <summary>The answer when there is nowhere to write, or when writing threw.</summary>
    public static FeedVerdictOutcome NotSaved { get; } = new(Saved: false, ExpiresAt: null);
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

    /// <summary>
    /// Stores one verdict about one release — the "not interested" and "not
    /// now" controls on a card. A snooze's expiry is the service's to compute
    /// (§6b's <c>DefaultSnooze</c>), so the screen never has to know how long
    /// "not now" is; it is told, and says so.
    /// </summary>
    /// <returns>
    /// Never throws. A write that could not land comes back as
    /// <see cref="FeedVerdictOutcome.NotSaved"/> so the card can say so rather
    /// than claim a dismissal it does not have.
    /// </returns>
    Task<FeedVerdictOutcome> RecordVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default);

    /// <summary>
    /// Takes one back — every active verdict of that kind on that release gets
    /// a revocation stamp. Append-and-revoke, never a delete: undo must not cost
    /// the history that makes this loop inspectable.
    /// </summary>
    /// <returns>
    /// True when something was actually revoked. False is not an error — a
    /// snooze that lapsed under the user's finger had already undone itself.
    /// Never throws.
    /// </returns>
    Task<bool> RevokeVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default);

    /// <summary>
    /// Everything the user has ever told the feed, newest first, revoked and
    /// lapsed rows included.
    ///
    /// <para>This is the charter's explainability requirement with a method
    /// name: dismissed → undone → dismissed again is two rows and a stamp, and
    /// all of it is visible, because a feedback loop nobody can audit is the
    /// black box §6b exists to prevent. Never throws — an empty list is the
    /// answer when there is nothing to read from.</para>
    /// </summary>
    Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default);
}
