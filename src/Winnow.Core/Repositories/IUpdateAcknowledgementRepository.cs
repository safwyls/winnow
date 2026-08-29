using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// Storage for update-acknowledgement dismissals (migration 0012). The App layer
/// records/revokes acknowledgements; badge visibility is derived in the bucket
/// query, not read from here directly.
/// </summary>
public interface IUpdateAcknowledgementRepository
{
    /// <summary>Appends an acknowledgement (Id ignored) and returns the assigned id.</summary>
    Task<long> RecordAsync(UpdateAcknowledgement ack, CancellationToken ct = default);

    /// <summary>Revokes all standing acknowledgements on a release by stamping RevokedAt. Returns rows stamped.</summary>
    Task<int> RevokeAsync(long releaseId, DateTime revokedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// The un-revoked acknowledgement with the greatest AcknowledgedThrough watermark
    /// for a release, or null. Does not indicate whether the badge is currently suppressed.
    /// </summary>
    Task<UpdateAcknowledgement?> GetStandingAsync(long releaseId, CancellationToken ct = default);
}
