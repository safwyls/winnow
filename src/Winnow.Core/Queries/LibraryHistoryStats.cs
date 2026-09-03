namespace Winnow.Core.Queries;

/// <summary>
/// Library-wide longitudinal history, aggregated over every ownership.
/// The maturity tier is a claim about the whole library, so it needs a figure
/// for the whole library: any sample small enough to be cheap is drawn from
/// rows chosen for some other reason and is biased by that choice.
/// </summary>
public sealed record LibraryHistoryStats
{
    /// <summary>Total recorded sessions across the whole library.</summary>
    public required int SessionCount { get; init; }

    /// <summary>Earliest recorded session start, or null when there are none.</summary>
    public DateTime? FirstSessionAt { get; init; }

    /// <summary>Latest recorded session start, or null when there are none.</summary>
    public DateTime? LastSessionAt { get; init; }

    /// <summary>How many ownerships hold a snapshot series with at least one rise.</summary>
    public required int OwnershipsWithSnapshotRises { get; init; }

    /// <summary>
    /// False when every figure is an exact aggregate; true when it came from a
    /// sample and was scaled. A scaled figure is right about the tier and wrong
    /// about the count, so it may gate behaviour but must never be shown to the
    /// user as a total.
    /// </summary>
    public bool IsEstimate { get; init; }

    /// <summary>Nothing recorded anywhere.</summary>
    public static LibraryHistoryStats Empty { get; } = new()
    {
        SessionCount = 0,
        OwnershipsWithSnapshotRises = 0,
    };
}
