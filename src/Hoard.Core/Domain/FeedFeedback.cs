namespace Hoard.Core.Domain;

/// <summary>
/// Valid <see cref="FeedVerdict.Kind"/> values (CHECK-constrained in the
/// schema, migration 0011).
///
/// <para><b>Two kinds, and the difference is the point.</b> "Not interested"
/// and "not now" are different intents — one is a verdict, the other a
/// deferral — and a single dismiss button would collapse them into whichever
/// meaning the implementer happened to pick. The vocabulary is ours and
/// closed, so like <see cref="SessionAttributions"/> it is CHECK-constrained:
/// a third kind is a schema change and should have to be one.</para>
///
/// <para><b>There is deliberately no explicit positive kind.</b> The positive
/// signal is behavioural: a session with
/// <see cref="SessionAttributions.Launch"/> on a game the feed had just
/// surfaced is the user answering the feed's pitch with their time, recorded
/// with no UI and no asking. A thumbs-up button would duplicate that with
/// strictly worse data (a click costs nothing; forty minutes costs forty
/// minutes) and, unpressed, would teach the user the feedback surface is
/// decoration. If one ever earns its place, adding a kind here is the
/// deliberate act the CHECK constraint makes it.</para>
/// </summary>
public static class FeedVerdictKinds
{
    /// <summary>
    /// Durable "stop showing me this" — the user's explicit "you were right,
    /// I'm done with this game". Never expires; holds until revoked.
    /// </summary>
    public const string NotInterested = "not_interested";

    /// <summary>
    /// "Not now": temporarily set aside. Always carries an expiry (the schema
    /// enforces it), because a snooze with no expiry is a dismissal wearing a
    /// different name.
    /// </summary>
    public const string Snoozed = "snoozed";

    /// <summary>
    /// Default snooze length: one calendar month. "Not now" naturally reads at
    /// month granularity ("not this month"); shorter and a snooze is just the
    /// rotation the surfacing memory already provides, longer and it drifts
    /// toward a dismissal the user didn't give. A UI may offer other lengths —
    /// the schema stores the explicit expiry, so this is a default, not a rule.
    /// </summary>
    public static readonly TimeSpan DefaultSnooze = TimeSpan.FromDays(30);
}

/// <summary>
/// One thing the user explicitly told the feed about one release — a fact,
/// stored (§6.1's line: what the user said is truth; what it does to a score
/// stays a query). Appended and revoked, never edited or deleted, so the full
/// history remains inspectable: the user can always see everything they have
/// told the system, including what they later took back.
/// </summary>
public sealed record FeedVerdict
{
    public long Id { get; init; }
    public required long ReleaseId { get; init; }

    /// <summary>One of <see cref="FeedVerdictKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>When the user said it (UTC).</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When a snooze lapses (UTC). Required for snoozes, null for
    /// not-interested — the schema enforces the pairing.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// When the user took it back (UTC). Null while the verdict stands. Undo
    /// is a timestamp, not a deletion: reversibility must not cost history.
    /// </summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary>Whether this verdict binds at the given instant: not revoked, not lapsed.</summary>
    public bool IsActiveAt(DateTime asOfUtc)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > asOfUtc);
}

/// <summary>
/// One release the feed put in front of the user on one day, and the shelf
/// that claimed it. The cross-day memory behind rotation, and one half of the
/// launch-endorsement join (the other half is
/// <see cref="Session.AttributedBy"/>). A fact about what the app did — not
/// derivable from anything else, which is why it is stored.
/// </summary>
public sealed record FeedSurfacing
{
    public required long ReleaseId { get; init; }

    /// <summary>The day it was on screen. A date, not an instant: the feed is stable within a day by design.</summary>
    public required DateOnly SurfacedOn { get; init; }

    /// <summary>The shelf that claimed it that day (ShelfIds vocabulary — informational, never joined on).</summary>
    public required string ShelfId { get; init; }
}

/// <summary>
/// The strongest positive feedback the system can receive, and it is derived,
/// never stored: a session Hoard itself launched
/// (<see cref="SessionAttributions.Launch"/>) that started within a few days
/// of the feed surfacing that game. The user answered a pitch by playing —
/// implicit, behavioural, and free.
///
/// <para>An approximation, stated: the launch is known to have come from
/// inside Hoard while the game was on (or had very recently been on) the
/// feed, not provably from a click on the feed card itself. Sessions with
/// <c>attributed_by</c> null or 'inferred' never qualify — null means "not
/// recorded", never "not launched here".</para>
/// </summary>
public sealed record FeedEndorsement
{
    public required long ReleaseId { get; init; }
    public required long SessionId { get; init; }

    /// <summary>When the endorsing session started (UTC).</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>The day of the qualifying surfacing (the latest one within the window).</summary>
    public required DateOnly SurfacedOn { get; init; }

    /// <summary>The shelf whose pitch was answered.</summary>
    public required string ShelfId { get; init; }
}
