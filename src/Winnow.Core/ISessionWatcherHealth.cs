namespace Winnow.Monitor;

/// <summary>Read-only session tracking health shared with frontend presentations.</summary>
public interface ISessionWatcherHealth
{
    bool HasFailures { get; }
    IReadOnlyList<SessionWatcherFailure> Failures { get; }
    event EventHandler? Changed;
}
