using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

public interface ISteamPlaytimeObservationRepository
{
    Task ObserveAsync(SteamPlaytimeObservation observation, CancellationToken ct = default);
    Task<IReadOnlyList<SteamPlaytimeObservation>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
    Task<IReadOnlyList<SteamReportedActivity>> GetActivityAsync(
        IReadOnlyCollection<long> ownershipIds, DateTime asOfUtc, string? accountRef = null,
        CancellationToken ct = default);
}
