using Winnow.Core.Queries;

namespace Winnow.Core.Domain;

/// <summary>
/// One source's maturity evidence for a work: the rating-board tokens and
/// content descriptors it reported. Projected from <c>work_maturity</c>
/// (migration 0024), one row per (work, source).
/// </summary>
public sealed record WorkMaturity
{
    /// <summary>The work this evidence describes.</summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// Which source reported it (a <see cref="MaturitySources"/> value).
    /// One row per source, so IGDB and the Steam store each keep their own
    /// reading.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Comma-joined rating tokens, verbatim. Null when the source reported none.</summary>
    public string? Ratings { get; init; }

    /// <summary>Comma-joined descriptor tokens, verbatim. Null when the source reported none.</summary>
    public string? Descriptors { get; init; }

    /// <summary>When this source's reading was taken (UTC).</summary>
    public required DateTime ObservedAt { get; init; }

    /// <summary>
    /// Derived, never stored. True when this row's evidence reaches the
    /// adults-only tier, evaluated by <see cref="MaturityRules.IsExplicit"/>.
    /// Retuning the rule changes every verdict without a migration.
    /// </summary>
    public bool IsExplicit => MaturityRules.IsExplicit(Ratings, Descriptors);
}
