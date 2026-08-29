namespace Winnow.Core.Domain;

/// <summary>
/// Records that the user dismissed an update badge for a release (migration 0012).
/// Stores a watermark (<see cref="AcknowledgedThrough"/>) rather than a boolean,
/// so a newer correlated push automatically re-raises the badge with no write.
/// Appended and revoked, never edited or deleted.
/// </summary>
public sealed record UpdateAcknowledgement
{
    public long Id { get; init; }

    public required long ReleaseId { get; init; }

    /// <summary>The dismissed build push's <c>occurred_at</c> (UTC) watermark; pushes strictly after it re-raise the badge.</summary>
    public required DateTime AcknowledgedThrough { get; init; }

    /// <summary>When the user dismissed it (UTC). The clock, kept separately from the watermark.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the user undid the dismissal (UTC). Null while the acknowledgement stands.</summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary>Whether this acknowledgement has not been revoked (derived, not stored).</summary>
    public bool IsStanding => RevokedAt is null;
}
