using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Monitor;

public enum SessionWatcherOperation
{
    ExecutableIndex,
    ProcessDiscovery,
    SessionRecovery,
    SessionPersistence,
    Tick,
    Configuration,
    Shutdown,
}

public sealed record SessionWatcherFailure(
    SessionWatcherOperation Operation,
    DateTimeOffset FirstFailedAtUtc,
    DateTimeOffset LastFailedAtUtc,
    long FailureCount,
    string ExceptionType);

/// <summary>Independent operation health; a successful poll cannot hide a failed index or write.</summary>
public sealed class SessionWatcherHealth(
    ILogger<SessionWatcherHealth>? logger = null,
    TimeProvider? timeProvider = null)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<SessionWatcherOperation, SessionWatcherFailure> _failures = [];
    private readonly Dictionary<SessionWatcherOperation, DateTimeOffset> _lastLogged = [];
    private readonly ILogger<SessionWatcherHealth> _logger = logger ?? NullLogger<SessionWatcherHealth>.Instance;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public bool HasFailures { get { lock (_gate) { return _failures.Count > 0; } } }
    public IReadOnlyList<SessionWatcherFailure> Failures
    {
        get { lock (_gate) { return _failures.Values.OrderBy(f => f.Operation).ToArray(); } }
    }

    public event EventHandler? Changed;

    public void ReportFailure(SessionWatcherOperation operation, Exception exception)
    {
        var now = _time.GetUtcNow();
        bool log;
        SessionWatcherFailure failure;
        lock (_gate)
        {
            failure = _failures.TryGetValue(operation, out var prior)
                ? prior with { LastFailedAtUtc = now, FailureCount = prior.FailureCount + 1,
                    ExceptionType = exception.GetType().FullName ?? exception.GetType().Name }
                : new(operation, now, now, 1, exception.GetType().FullName ?? exception.GetType().Name);
            _failures[operation] = failure;
            log = !_lastLogged.TryGetValue(operation, out var last) || now < last
                || now - last >= TimeSpan.FromMinutes(5);
            if (log) _lastLogged[operation] = now;
        }

        if (log)
            _logger.LogWarning(exception,
                "Session watcher operation {Operation} failed; {FailureCount} failure(s) over {ElapsedSeconds:n0}s. Session tracking may be incomplete.",
                operation, failure.FailureCount, (now - failure.FirstFailedAtUtc).TotalSeconds);
        Announce();
    }

    public void ReportSuccess(SessionWatcherOperation operation)
    {
        SessionWatcherFailure? failure;
        lock (_gate)
        {
            if (!_failures.Remove(operation, out failure)) return;
            _lastLogged.Remove(operation);
        }

        _logger.LogInformation(
            "Session watcher operation {Operation} recovered after {FailureCount} failure(s), {ElapsedSeconds:n0}s since first failure.",
            operation, failure.FailureCount, (_time.GetUtcNow() - failure.FirstFailedAtUtc).TotalSeconds);
        Announce();
    }

    private void Announce()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.LogDebug(ex, "Session watcher health subscriber failed."); }
        }
    }
}
