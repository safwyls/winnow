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
