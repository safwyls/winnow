namespace Winnow.Core.Identity;

/// <summary>
/// One write, whatever its size. The unit of undo: retracting an act reverses
/// every link it created.
/// </summary>
public sealed record IdentityAct
{
    /// <summary>Primary key in <c>identity_acts</c>.</summary>
    public long Id { get; init; }

    /// <summary>One of <see cref="IdentityActKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>UTC instant the act was recorded.</summary>
    public required DateTime PerformedAt { get; init; }

    /// <summary>Optional free-text note attached by the user or the system.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// One child-points-at-parent row in <c>identity_links</c>. Append-only:
/// retracting stamps <see cref="RetractedAt"/> rather than deleting, so the
/// history is the table itself.
/// </summary>
public sealed record IdentityLink
{
    /// <summary>Primary key in <c>identity_links</c>.</summary>
    public long Id { get; init; }

    /// <summary>The act that created this link.</summary>
    public required long ActId { get; init; }

    /// <summary>The work absorbed into the parent's identity or group.</summary>
    public required long ChildWorkId { get; init; }

    /// <summary>The work that represents the identity or group.</summary>
    public required long ParentWorkId { get; init; }

    /// <summary>One of <see cref="IdentityLinkKinds"/>: same_game or expansion_of.</summary>
    public required string Kind { get; init; }

    /// <summary>One of <see cref="IdentityLinkSources"/>: user or hard_id.</summary>
    public required string Source { get; init; }

    /// <summary>Optional JSON evidence blob (e.g. matching external ids).</summary>
    public string? EvidenceJson { get; init; }

    /// <summary>UTC instant the link was written.</summary>
    public required DateTime AppliedAt { get; init; }

    /// <summary>UTC instant the link was retracted, or null if still live.</summary>
    public DateTime? RetractedAt { get; init; }

    /// <summary>
    /// The act that displaced this link, or null if still live. This is what
    /// makes retraction restorable: "which live links did act N displace" is a
    /// foreign-key lookup, not a timestamp heuristic.
    /// </summary>
    public long? RetractedByActId { get; init; }

    /// <summary>
    /// True when <see cref="RetractedAt"/> is null, the same predicate the
    /// partial unique index <c>ux_identity_links_live</c> uses. The C# property
    /// and the index must agree, or the two disagree about what "live" means.
    /// </summary>
    public bool IsLive => RetractedAt is null;
}

/// <summary>
/// A request to link one or more children to a parent under one act.
/// Defaults to <see cref="IdentityLinkKinds.SameGame"/> and
/// <see cref="IdentityLinkSources.User"/> because that is the answer the
/// Same Game screen produces.
/// </summary>
public sealed record IdentityLinkRequest
{
    /// <summary>The work that will represent the identity or group.</summary>
    public required long ParentWorkId { get; init; }

    /// <summary>
    /// The works to link under the parent. A list rather than a single id so
    /// the base-game-plus-six-expansions case is one act, not six sequential
    /// pairwise operations each invalidating the next.
    /// </summary>
    public required IReadOnlyList<long> ChildWorkIds { get; init; }

    /// <summary>One of <see cref="IdentityLinkKinds"/>. Defaults to same_game.</summary>
    public string Kind { get; init; } = IdentityLinkKinds.SameGame;

    /// <summary>One of <see cref="IdentityLinkSources"/>. Defaults to user.</summary>
    public string Source { get; init; } = IdentityLinkSources.User;

    /// <summary>Optional JSON evidence blob (e.g. the matching external ids that prompted this link).</summary>
    public string? EvidenceJson { get; init; }

    /// <summary>Optional free-text note stored on the act.</summary>
    public string? Note { get; init; }
}
