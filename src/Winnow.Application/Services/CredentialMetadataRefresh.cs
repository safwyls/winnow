using System.Threading.Channels;

namespace Winnow.App.Services;

/// <summary>Coalesces credential changes behind startup and runs one metadata pass at a time.</summary>
public sealed class CredentialMetadataRefresh
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public CredentialMetadataRefresh(Task startup, Func<CancellationToken, Task> refresh,
        Action<Exception> reportFailure, CancellationToken cancellationToken)
    {
        Completion = Task.Run(async () =>
        {
            try
            {
                await startup.WaitAsync(cancellationToken);
                await foreach (var _ in _requests.Reader.ReadAllAsync(cancellationToken))
                {
                    try { await refresh(cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { reportFailure(ex); }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }, CancellationToken.None);
    }

    public Task Completion { get; }

    public void Request() => _requests.Writer.TryWrite(true);
}
