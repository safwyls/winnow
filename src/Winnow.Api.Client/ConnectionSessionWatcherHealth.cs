using Winnow.Api.Contracts.Connections;
using Winnow.Monitor;

namespace Winnow.Api.Client;

public sealed class ConnectionSessionWatcherHealth(WinnowApiClient api) : ISessionWatcherHealth
{
    private IReadOnlyList<SessionWatcherFailure> _failures = [];
    public bool HasFailures => Failures.Count > 0;
    public IReadOnlyList<SessionWatcherFailure> Failures => Volatile.Read(ref _failures);
    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var snapshot = await api.GetAsync<BackendDiagnostics>("diagnostics", ct);
        Volatile.Write(ref _failures, snapshot.SessionFailures);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
