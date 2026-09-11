using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>Admits provider observations only while the identity they describe is current.</summary>
public interface IIgdbObservationWriter
{
    Task<IgdbMappingVersion?> CaptureAsync(long workId, CancellationToken ct = default);

    /// <summary>
    /// Checks the captured mapping and holds the same write transaction through
    /// the callback and commit. The callback may call repositories using the
    /// same factory; it must contain no provider requests or other external IO.
    /// A stale or missing work returns false without invoking the callback.
    /// Failure or cancellation rolls back only this operation, including when
    /// its caller catches the failure and commits an ambient unit of work.
    /// </summary>
    Task<bool> TryWriteAsync(IgdbMappingVersion expected,
        Func<CancellationToken, Task> persist, CancellationToken ct = default);
}
