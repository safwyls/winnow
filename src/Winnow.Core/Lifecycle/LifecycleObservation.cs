namespace Winnow.Core.Lifecycle;

/// <summary>A dated source answer about one release; null signals mean unknown.</summary>
public sealed record LifecycleObservation
{
    public long Id { get; init; }
    public required long ReleaseId { get; init; }
    public required string Source { get; init; }
    public string? SourceId { get; init; }
    public required DateTime ObservedAt { get; init; }
    public LifecycleSignals Signals { get; init; } = new();
    public string? RawJson { get; init; }
}

public sealed record LifecycleSignals
{
    public string? IgdbStatus { get; init; }
    public bool? IsMultiplayer { get; init; }
    public bool? HasSinglePlayer { get; init; }
    public bool? IsUnfinished { get; init; }
    public bool? StoreListed { get; init; }
    public int? CurrentPlayers { get; init; }
    public int? RecentReviewCount { get; init; }
    public DateTime? LastDevelopmentAt { get; init; }
    public DateTime? LastCommunicationAt { get; init; }
    public DateTime? LastStoreChangeAt { get; init; }
    public DateTime? OfficialShutdownAt { get; init; }
    public bool? DeveloperDefunct { get; init; }
}

public enum GameLifecycleStatus { Unknown, Active, Inactive, Dead, Abandoned, Offline, Delisted, Cancelled }

public sealed record GameLifecycle(GameLifecycleStatus Status, double Confidence, string Reason)
{
    public bool IsDerelict => Status is GameLifecycleStatus.Dead or GameLifecycleStatus.Abandoned
        or GameLifecycleStatus.Offline or GameLifecycleStatus.Delisted or GameLifecycleStatus.Cancelled;
}
