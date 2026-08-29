namespace Winnow.Core.Domain;

/// <summary>
/// Valid <see cref="FeedVerdict.Kind"/> values (CHECK-constrained in the schema, migration 0011).
/// </summary>
public static class FeedVerdictKinds
{
    /// <summary>Permanent dismissal. Never expires; holds until revoked.</summary>
    public const string NotInterested = "not_interested";

    /// <summary>Temporary deferral. Always carries an expiry (schema-enforced).</summary>
    public const string Snoozed = "snoozed";

    /// <summary>Default snooze length: 30 days. The UI may offer other lengths.</summary>
    public static readonly TimeSpan DefaultSnooze = TimeSpan.FromDays(30);
}

/// <summary>
/// User feedback on a release (not-interested or snoozed).
/// Appended and revoked, never edited or deleted.
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

    /// <summary>When the user undid this verdict (UTC). Null while it stands.</summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary>Whether this verdict binds at the given instant: not revoked, not lapsed.</summary>
    public bool IsActiveAt(DateTime asOfUtc)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > asOfUtc);
}

/// <summary>
/// Records that a release was shown on the feed on a given day, and which shelf claimed it.
/// Used for rotation memory and launch-endorsement joins.
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
/// Derived (never stored) positive signal: a Winnow-launched session that started
/// within a few days of the feed surfacing that game. Only sessions with
/// <c>attributed_by = 'launch'</c> qualify.
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
