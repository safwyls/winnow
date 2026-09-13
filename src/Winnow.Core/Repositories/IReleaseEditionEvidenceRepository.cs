using Winnow.Core.Identity;

namespace Winnow.Core.Repositories;

public interface IReleaseEditionEvidenceRepository
{
    /// <summary>Records a validated observation only while its release and cached inputs still match.</summary>
    Task<ReleaseEditionEvidence?> RecordAsync(ReleaseEditionEvidence evidence, CancellationToken ct = default);
}
