namespace Winnow.App.Services;

/// <summary>Publishes plugin inventories independently of optional metadata and artwork sweeps.</summary>
public sealed class PluginRefreshCoordinator
{
    private readonly CredentialMetadataRefresh _imports;

    public PluginRefreshCoordinator(Task discovery, Func<CancellationToken, Task> import,
        Func<CancellationToken, Task> enrich, Func<CancellationToken, Task> publish,
        Action<Exception> reportFailure, CancellationToken cancellationToken, Task? libraryStartup = null,
        Func<CancellationToken, Task>? refreshSuggestions = null)
    {
        var enrichment = new CredentialMetadataRefresh(discovery, async ct =>
        {
            try { await enrich(ct).ConfigureAwait(false); }
            finally { if (!ct.IsCancellationRequested) await publish(ct).ConfigureAwait(false); }
        }, reportFailure, cancellationToken);

        var suggestions = refreshSuggestions is null ? null : new CredentialMetadataRefresh(discovery, async ct =>
        {
            try { await refreshSuggestions(ct).ConfigureAwait(false); }
            finally { if (!ct.IsCancellationRequested) await publish(ct).ConfigureAwait(false); }
        }, reportFailure, cancellationToken);

        // Resolver writes still use LibrarySyncGate. Unrelated startup network work and a
        // previous artwork sweep must not hold a newly connected account's inventory back.
        _imports = new CredentialMetadataRefresh(discovery, async ct =>
        {
            try { await import(ct).ConfigureAwait(false); }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    try { await publish(ct).ConfigureAwait(false); }
                    finally { enrichment.Request(); suggestions?.Request(); }
                }
            }
        }, reportFailure, cancellationToken);
        var startupRefresh = libraryStartup is null ? Task.CompletedTask
            : RefreshAfterStartupAsync(libraryStartup, () => { enrichment.Request(); suggestions?.Request(); }, reportFailure, cancellationToken);
        Completion = Task.WhenAll(_imports.Completion, enrichment.Completion, suggestions?.Completion ?? Task.CompletedTask, startupRefresh);
    }

    public Task Completion { get; }
    public void Request() => _imports.Request();

    private static async Task RefreshAfterStartupAsync(Task startup, Action request,
        Action<Exception> reportFailure, CancellationToken ct)
    {
        try { await startup.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) { reportFailure(ex); }
        // The first plugin snapshot may predate Steam/Epic/GOG backfill. Include those
        // newly committed works in later enrichment and matching passes without delaying imports.
        if (!ct.IsCancellationRequested) request();
    }
}
