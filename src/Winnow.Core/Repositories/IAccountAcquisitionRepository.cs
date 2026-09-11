using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IAccountAcquisitionRepository
{
    /// <summary>Appends distinct evidence; the first capture timestamp survives re-import.</summary>
    Task<bool> TryAppendAsync(OwnershipAcquisitionObservation observation, CancellationToken ct = default);

    /// <summary>All account acquisition observations for the requested ownerships.</summary>
    Task<IReadOnlyList<OwnershipAcquisitionObservation>> GetAsync(
        IReadOnlyList<long> ownershipIds, CancellationToken ct = default);

    /// <summary>Steam ownerships actually observed for the named account.</summary>
    Task<IReadOnlyList<long>> GetSteamOwnershipIdsAsync(string accountRef, CancellationToken ct = default);
}
