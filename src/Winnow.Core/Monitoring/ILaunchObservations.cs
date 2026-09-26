namespace Winnow.Monitor;

public readonly record struct LaunchObserved(long OwnershipId, DateTime ObservedAtUtc);

public interface ILaunchObservations
{
    TimeSpan Window { get; }
    event EventHandler<LaunchObserved>? Observed;
}
