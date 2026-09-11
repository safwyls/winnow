using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IOwnershipInventoryRepository
{
    /// <summary>
    /// Starts a new attempt and retires any previous completeness assertion.
    /// Failure, partial answers and cancellation leave the attempt incomplete.
    /// Positive membership observations remain independent and are never deleted.
    /// </summary>
    Task<OwnershipInventoryAttempt> BeginAttemptAsync(string store, string accountRef, string source,
        CancellationToken ct = default);

    /// <summary>
    /// Publishes a suitable complete answer only after all candidates were resolved.
    /// The original response time is preserved on cache hits. A newer attempt
    /// makes this completion a no-op and returns false.
    /// </summary>
    Task<bool> CompleteAsync(OwnershipInventoryAttempt attempt, DateTime observedAt, int itemCount,
        CancellationToken ct = default);
}
