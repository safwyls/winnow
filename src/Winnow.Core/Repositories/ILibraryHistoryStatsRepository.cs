using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// One global aggregate over the longitudinal tables. Exists so the tier can
/// be counted rather than estimated: a single query over the sessions and
/// snapshot tables answers what no affordable sample can. Callers without this
/// repository retain a sampled fallback.
/// </summary>
public interface ILibraryHistoryStatsRepository
{
    /// <summary>The whole-library history aggregate, computed on read.</summary>
    Task<LibraryHistoryStats> GetAsync(CancellationToken ct = default);

    /// <summary>The aggregate over history already observed at this instant.</summary>
    Task<LibraryHistoryStats> GetAsync(DateTime asOfUtc, CancellationToken ct = default) => GetAsync(ct);
}
