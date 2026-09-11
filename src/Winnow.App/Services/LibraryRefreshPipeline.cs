using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

public sealed record LibraryRefreshStep(string Name, Func<CancellationToken, Task> Run,
    bool PublishAfter = false, bool IgdbRelevant = false);

/// <summary>One ordered set of downstream application operations, with independent failure and publication boundaries.</summary>
public sealed class LibraryRefreshPipeline(
    IReadOnlyList<LibraryRefreshStep> steps,
    Func<CancellationToken, Task> publish,
    ILogger<LibraryRefreshPipeline> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(CancellationToken ct = default, bool igdbOnly = false)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var published = false;
            foreach (var step in steps.Where(step => !igdbOnly || step.IgdbRelevant))
            {
                ct.ThrowIfCancellationRequested();
                published = false;
                try { await step.Run(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning("Library refresh step {Step} failed ({FaultType}); remaining steps will continue.", step.Name, ex.GetType().Name);
                }
                if (step.PublishAfter)
                {
                    await PublishAsync(ct).ConfigureAwait(false);
                    published = true;
                }
            }
            if (!published) await PublishAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task PublishAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { await publish(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Committed library refresh could not be displayed ({FaultType}); a later publication will retry.", ex.GetType().Name);
        }
    }
}
