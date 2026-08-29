namespace Winnow.Resolve.Matching;

/// <summary>One side of a soft-match comparison. Plain value with no IO references.</summary>
public sealed record MatchSubject
{
    /// <summary>The <c>releases.id</c> this subject represents.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>Raw title, exactly as the source supplied it.</summary>
    public required string Title { get; init; }

    /// <summary>First release year, if known. Falls back to a parenthesised year in the title.</summary>
    public int? ReleaseYear { get; init; }

    /// <summary>Publisher as the source names it; normalised before comparison.</summary>
    public string? Publisher { get; init; }

    /// <summary>64-bit perceptual hash of the cover art, or null when unavailable.</summary>
    public ulong? CoverPerceptualHash { get; init; }
}

/// <summary>
/// Supplies cover perceptual hashes for <see cref="MatchSubject"/> assembly.
/// Not implemented in Winnow.Resolve -- the cover pipeline owns imaging.
/// Implementations must use the same hash algorithm on both sides.
/// </summary>
public interface ICoverHashSource
{
    /// <summary>Cached perceptual hash for a release's cover, or null. Must never block on a download.</summary>
    Task<ulong?> TryGetCoverHashAsync(long releaseId, CancellationToken ct = default);
}
