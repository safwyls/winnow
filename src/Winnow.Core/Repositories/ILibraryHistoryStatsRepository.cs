using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// One global aggregate over the longitudinal tables. Exists so the tier can
/// be counted rather than estimated: a single query over the sessions and
/// snapshot tables answers what no affordable sample can. Optional by design,
/// and unimplemented in Winnow.Data today, so callers must keep a fallback.
/// </summary>
public interface ILibraryHistoryStatsRepository
{
    /// <summary>The whole-library history aggregate, computed on read.</summary>
    Task<LibraryHistoryStats> GetAsync(CancellationToken ct = default);
}
