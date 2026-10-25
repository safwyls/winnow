namespace Winnow.App.Services;

/// <summary>Publishes committed changes without depending on a window or dispatcher.</summary>
public sealed class LibraryChangePublisher(Func<CancellationToken, Task> publish)
{
    public Task PublishAsync(CancellationToken ct) => publish(ct);
}
