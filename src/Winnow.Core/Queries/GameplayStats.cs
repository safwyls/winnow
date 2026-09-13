namespace Winnow.Core.Queries;

/// <summary>The current visible library supplies identity; the database supplies the actual store.</summary>
public sealed record GameplayOwnershipScope(long OwnershipId, long ResolvedWorkId);

/// <summary>A half-open UTC interval, typically derived from local calendar boundaries.</summary>
public sealed record GameplayTimeBin(DateTime FromUtc, DateTime UntilUtc);

public sealed record GameplayStatsRequest
{
    /// <summary>Bounds query/chart output while allowing more than a year of daily bins.</summary>
    public const int MaximumTimeBins = 512;
    public required IReadOnlyList<GameplayOwnershipScope> Ownerships { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime UntilUtc { get; init; }
    public required DateTime AsOfUtc { get; init; }
    /// <summary>Contiguous intervals covering the requested period, in order. At most 512.</summary>
    public required IReadOnlyList<GameplayTimeBin> TimeBins { get; init; }
    public string? Store { get; init; }
}

public sealed record GameplayPeriodTotal(DateTime FromUtc, DateTime UntilUtc, double RecordedSeconds);
public sealed record GameplayGameTotal(long ResolvedWorkId, double RecordedSeconds);
public sealed record GameplayStoreTotal(string Store, double RecordedSeconds);
public sealed record GameplaySessionLength(long MinimumSeconds, long? MaximumSeconds, int Count);

/// <summary>
/// Observed game-hours, never combined with cumulative store counters. Concurrent games
/// contribute independently. Exact duplicate session evidence counts once per ownership.
/// </summary>
public sealed record GameplayStats
{
    /// <summary>A bounded ranking rather than a second full library listing.</summary>
    public const int MaximumTopGames = 10;
    public double RecordedSeconds { get; init; }
    public int GamesPlayedCount { get; init; }
    public int OverlappingSessionCount { get; init; }
    /// <summary>Valid completed sessions starting in the period; the histogram and median use this population.</summary>
    public int StartedSessionCount { get; init; }
    public double? MedianSessionSeconds { get; init; }
    /// <summary>Open/invalid records starting in the period, before duplicate evidence removal.</summary>
    public int ExcludedSessionCount { get; init; }
    public IReadOnlyList<GameplayPeriodTotal> Periods { get; init; } = [];
    /// <summary>At most ten games, ordered by clipped recorded time, then resolved work ID.</summary>
    public IReadOnlyList<GameplayGameTotal> TopGames { get; init; } = [];
    public IReadOnlyList<GameplaySessionLength> SessionLengths { get; init; } = [];
    public IReadOnlyList<GameplayStoreTotal> Stores { get; init; } = [];
}
