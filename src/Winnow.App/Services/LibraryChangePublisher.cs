using Avalonia.Threading;
using Winnow.App.ViewModels;

namespace Winnow.App.Services;

/// <summary>Publishes committed application changes through the shared library notification path.</summary>
public sealed class LibraryChangePublisher(LibraryViewModel library, MergeQueueViewModel mergeQueue)
{
    public Task PublishAsync(CancellationToken ct)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await library.RefreshCommittedAsync(ct);
            ct.ThrowIfCancellationRequested();
            // Active fullscreen contexts observe TilesChanged; inactive ones
            // mark themselves stale and refresh on entry.
            await mergeQueue.NoteQueueMayHaveMovedAsync(ct);
        }).WaitAsync(ct);
}
