using Winnow.Core.Domain;

namespace Winnow.App.Services;

public sealed record AcquisitionCsv(string Content, int OwnershipCount);

/// <summary>A local, versioned ownership export; missing prices stay empty, never zero.</summary>
public interface IAcquisitionExport
{
    Task<AcquisitionCsv> ReadAsync(CancellationToken ct = default);
}

public interface IAccountAcquisitionReader
{
    /// <summary>Projects acquisition evidence for the same account scope as the library.</summary>
    Task<IReadOnlyList<Ownership>> ProjectAsync(IReadOnlyList<Ownership> ownerships, CancellationToken ct = default);
}
