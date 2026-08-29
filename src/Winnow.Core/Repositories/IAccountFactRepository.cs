using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// Append/read contract over the <c>account_transactions</c> and
/// <c>account_licenses</c> fact tables (migration 0014).
/// </summary>
public interface IAccountFactRepository
{
    /// <summary>
    /// Appends a transaction fact unless the same one is already stored.
    /// Returns the new id, or null when the fact was already recorded. The
    /// ON CONFLICT DO NOTHING identity from migration 0014 does the work, in
    /// the same shape as <see cref="IPlaytimeSnapshotRepository.TryAppendAsync"/>.
    /// Callers use the null to count "already recorded" rather than to detect
    /// an error.
    /// </summary>
    Task<long?> TryAppendAsync(AccountTransactionFact fact, CancellationToken ct = default);

    /// <summary>
    /// Appends a licence fact unless the same one is already stored. Same
    /// identity contract as the transaction overload.
    /// </summary>
    Task<long?> TryAppendAsync(AccountLicenseFact fact, CancellationToken ct = default);

    /// <summary>All transaction facts for one source, ordered by date then id.</summary>
    Task<IReadOnlyList<AccountTransactionFact>> GetTransactionsAsync(
        string source, CancellationToken ct = default);

    /// <summary>All licence facts for one source, ordered by date then id.</summary>
    Task<IReadOnlyList<AccountLicenseFact>> GetLicensesAsync(
        string source, CancellationToken ct = default);
}
