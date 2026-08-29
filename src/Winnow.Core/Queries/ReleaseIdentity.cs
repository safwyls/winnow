namespace Winnow.Core.Queries;

/// <summary>
/// Joined release + work projection for the soft matcher (§5.3 step 2).
/// One row per release, carrying the title, year, publisher, and app-type
/// metadata needed for matching and non-game filtering.
/// Uses init properties (not positional params) for Dapper Int64 compatibility.
/// </summary>
public sealed record ReleaseIdentity
{
    /// <summary><c>releases.id</c>.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>Its work id. Two releases of the same work are never compared.</summary>
    public required long WorkId { get; init; }

    /// <summary><c>releases.name</c> -- the primary match target (carries the edition).</summary>
    public required string ReleaseName { get; init; }

    /// <summary>The work's title, used when the release row has nothing usable.</summary>
    public required string WorkName { get; init; }

    /// <summary><c>works.first_release_year</c>, or null. Feeds the +/-1-year signal.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary><c>works.publisher</c>, or null. Feeds the publisher match signal.</summary>
    public string? Publisher { get; init; }

    /// <summary>True when the name is a placeholder (<c>App 1203620</c>). Excluded from matching.</summary>
    public bool NameIsProvisional { get; init; }

    /// <summary>Release name, falling back to work name.</summary>
    public string MatchTitle =>
        string.IsNullOrWhiteSpace(ReleaseName) ? WorkName : ReleaseName;
}
