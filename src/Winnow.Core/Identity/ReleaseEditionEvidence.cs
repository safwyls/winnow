using System.Security.Cryptography;
using System.Text;

namespace Winnow.Core.Identity;

/// <summary>A precise cached input; a null hash records an absent payload.</summary>
public sealed record CachedEvidenceSource(string Provider, string Key, string? PayloadSha256)
{
    public static CachedEvidenceSource FromPayload(string provider, string key, string? payload)
        => new(provider, key, payload is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
}

/// <summary>An explicit IGDB edition game, independently mapped from one native store identifier.</summary>
public sealed record ReleaseEditionEvidence
{
    public long Id { get; init; }
    public required long ReleaseId { get; init; }
    public required long WorkId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderId { get; init; }
    public required long EditionGameId { get; init; }
    public required long VersionParentId { get; init; }
    public required string VersionTitle { get; init; }
    public required DateTime ValidUntilUtc { get; init; }
    public required IReadOnlyList<CachedEvidenceSource> Sources { get; init; }
}
