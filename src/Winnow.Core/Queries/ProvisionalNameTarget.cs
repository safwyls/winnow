namespace Winnow.Core.Queries;

/// <summary>A work with a placeholder name, paired with its external id for enrichment lookup.</summary>
/// <param name="WorkId">The work to rename.</param>
/// <param name="ReleaseId">Its release (promoted together so neither keeps a placeholder forever).</param>
/// <param name="Provider">External id provider, e.g. <c>steam</c>.</param>
/// <param name="ProviderId">The provider's id, e.g. the Steam appid.</param>
public sealed record ProvisionalNameTarget(
    long WorkId,
    long ReleaseId,
    string Provider,
    string ProviderId);
