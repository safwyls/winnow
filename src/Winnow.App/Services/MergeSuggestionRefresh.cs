using Winnow.Resolve;

namespace Winnow.App.Services;

public interface IMergeSuggestionRefresh
{
    /// <summary>Changes after every successful pass, including a pass with an unchanged pending count.</summary>
    long Revision { get; }
    Task<SoftMatchSweepReport> RefreshAsync(CancellationToken ct = default);
}

/// <summary>Serializes startup, plugin and user-requested matching without accepting any proposals.</summary>
public sealed class MergeSuggestionRefresh : IMergeSuggestionRefresh
{
    private readonly Func<CancellationToken, Task<SoftMatchSweepReport>> _sweep;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _revision;

    public MergeSuggestionRefresh(LibrarySoftMatchSweep sweep) : this(sweep.SweepAsync) { }
    internal MergeSuggestionRefresh(Func<CancellationToken, Task<SoftMatchSweepReport>> sweep) => _sweep = sweep;

    public long Revision => Interlocked.Read(ref _revision);

    public async Task<SoftMatchSweepReport> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Title blocking and scoring include synchronous CPU work; manual refresh must not run it on the UI thread.
            var report = await Task.Run(() => _sweep(ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _revision);
            return report;
        }
        finally { _gate.Release(); }
    }
}
