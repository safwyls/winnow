using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// Read-only stats query over the account fact tables (migration 0014).
/// Nothing here is stored: every figure is computed on read, so thresholds
/// and definitions can be retuned without touching stored data (§6.1).
/// </summary>
public interface IAccountStatsRepository
{
    /// <summary>The whole read model for one source, computed from the fact tables.</summary>
    Task<AccountStats> GetAsync(string source, CancellationToken ct = default);
}
