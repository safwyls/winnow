namespace Hoard.Resolve.Matching;

/// <summary>
/// One side of a soft-match comparison (§5.3 step 2). Deliberately a plain
/// value with no repository or network reference: <see cref="SoftMatcher"/> is
/// pure, and whatever assembles these — a local release row, an IGDB search
/// result, a GOG catalogue entry — is the orchestrator's problem, not the
/// matcher's.
/// </summary>
public sealed record MatchSubject
{
    /// <summary>
    /// The <c>releases.id</c> this subject stands for. Used to key
    /// <c>merge_candidates</c> rows and to break scoring ties deterministically.
    /// </summary>
    public required long ReleaseId { get; init; }

    /// <summary>Raw title, exactly as the source supplied it.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// First release year, if known. Falls back to a parenthesised year in the
    /// title (<c>Prey (2006)</c>) when this is null.
    /// </summary>
    public int? ReleaseYear { get; init; }

    /// <summary>Publisher as the source names it; normalised before comparison.</summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// 64-bit perceptual hash of the cover art, or null when no cover has been
    /// fetched/hashed yet. Hoard.Resolve never computes this — see
    /// <see cref="ICoverHashSource"/>.
    /// </summary>
    public ulong? CoverPerceptualHash { get; init; }
}

/// <summary>
/// Supplies cover perceptual hashes to whoever assembles
/// <see cref="MatchSubject"/> values.
///
/// <para><b>Not implemented here, by design.</b> Hoard.Resolve owns matching,
/// not imaging: decoding cover art, computing a dHash/pHash and caching it
/// belongs to the cover pipeline (<c>src/Hoard.Covers</c>). This interface
/// exists so the contract — a 64-bit hash whose Hamming distance is the
/// similarity measure — is fixed in one place, and so the matcher can be wired
/// up the day that pipeline lands without any change to the scoring code.</para>
///
/// <para>Whatever implements it must produce hashes from the SAME algorithm on
/// both sides. Hamming distance between a dHash and a pHash of the same image
/// is meaningless noise, and the matcher has no way to detect the mix-up.</para>
/// </summary>
public interface ICoverHashSource
{
    /// <summary>
    /// The cached perceptual hash for a release's cover, or null when there is
    /// no cover or it has not been hashed. Must never block on a download —
    /// a missing hash is a normal, expected answer.
    /// </summary>
    Task<ulong?> TryGetCoverHashAsync(long releaseId, CancellationToken ct = default);
}
