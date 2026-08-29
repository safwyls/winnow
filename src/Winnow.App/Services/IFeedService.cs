namespace Winnow.App.Services;

/// <summary>How much longitudinal evidence the feed was computed from, in the UI's vocabulary.
/// Translated from <c>DataTier</c> in <see cref="FeedService"/>.</summary>
public enum FeedConfidence
{
    /// <summary>One sync per game, no sessions yet. Picks are real but evidence is thin.</summary>
    EarlyDays = 0,

    /// <summary>Weeks in: real playtime deltas or detected sessions are backing some of it.</summary>
    Settling = 1,

    /// <summary>Months in. Nothing is said on the screen at this tier.</summary>
    Established = 2,
}

/// <summary>One game on one shelf.</summary>
/// <param name="OwnershipId">Join key into the library's ownership tiles.</param>
/// <param name="Title">Fallback title when the tile cannot be found.</param>
/// <param name="Reason">The engine's own sentence, rendered verbatim on the card.</param>
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

/// <summary>One computed feed, reduced to what a screen can draw.</summary>
/// <param name="Shelves">Shelves in presentation order. Possibly empty.</param>
/// <param name="CandidateCount">Games scored. Distinguishes "quiet feed" from "empty library".</param>
/// <param name="Confidence">Data tier in the UI's vocabulary.</param>
/// <param name="Failed">True when the scoring pass did not complete.</param>
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

/// <summary>The two things a user can say about a card: "not interested" (durable) or "not now" (snooze).
/// Translated from <c>FeedVerdictKinds</c> in <see cref="FeedService"/>. No positive kind; that signal is behavioural.</summary>
public enum FeedVerdictKind
{
    /// <summary>"Not interested." Durable; holds until the user takes it back.</summary>
    NotInterested = 0,

    /// <summary>"Not now." Lapses by itself after the default snooze.</summary>
    Snoozed = 1,
}

/// <summary>Computed status of a stored verdict. The service owns the clock.</summary>
public enum FeedVerdictStatus
{
    /// <summary>Still binding: not revoked, and (for a snooze) not yet lapsed.</summary>
    Active = 0,

    /// <summary>The user took it back. The row survives with its revocation stamp — undo does not cost history.</summary>
    Undone = 1,

    /// <summary>A snooze that ran out on its own. No write happened; expiry is evaluated at read time.</summary>
    Lapsed = 2,
}

/// <summary>One row of "what you have told the feed", ready to draw.</summary>
/// <param name="ReleaseId">The release the verdict was stored against.</param>
/// <param name="Kind">Which of the two things the user said.</param>
/// <param name="CreatedAt">When they said it (UTC).</param>
/// <param name="ExpiresAt">When a snooze lapses (UTC). Null for a dismissal.</param>
/// <param name="RevokedAt">When they took it back (UTC), or null.</param>
/// <param name="Status">Computed against the service's clock. Only Active rows can be undone.</param>
public sealed record FeedVerdictRecord(
    long ReleaseId,
    FeedVerdictKind Kind,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    FeedVerdictStatus Status);

/// <summary>Outcome of pressing a feedback control.</summary>
/// <param name="Saved">Whether the write landed. False means the card must not show a receipt.</param>
/// <param name="ExpiresAt">When a snooze lapses. Null for a dismissal or failure.</param>
public sealed record FeedVerdictOutcome(bool Saved, DateTime? ExpiresAt)
{
    /// <summary>The answer when there is nowhere to write, or when writing threw.</summary>
    public static FeedVerdictOutcome NotSaved { get; } = new(Saved: false, ExpiresAt: null);
}

/// <summary>
/// App-layer seam for the Feed screen (§5.1). Scoring is expensive (~500 ms) and
/// runs off the UI thread. Never throws; failures come back as
/// <see cref="FeedSnapshot"/> with <see cref="FeedSnapshot.Failed"/> set.
/// </summary>
public interface IFeedService
{
    /// <summary>Computes today's feed. Deterministic within a day (shuffle seeded by date).</summary>
    Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default);

    /// <summary>Stores one verdict. The service computes snooze expiry. Never throws.</summary>
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
