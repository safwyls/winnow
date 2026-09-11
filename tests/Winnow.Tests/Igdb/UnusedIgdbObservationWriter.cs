using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Tests.Igdb;

/// <summary>Search-only fixtures must never enter the persistence boundary.</summary>
internal sealed class UnusedIgdbObservationWriter : IIgdbObservationWriter
{
    public Task<IgdbMappingVersion?> CaptureAsync(long workId, CancellationToken ct = default)
        => throw new InvalidOperationException("This fixture only searches metadata.");

    public Task<bool> TryWriteAsync(IgdbMappingVersion expected, Func<CancellationToken, Task> persist, CancellationToken ct = default)
        => throw new InvalidOperationException("This fixture only searches metadata.");
}
