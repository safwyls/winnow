using Winnow.Core.Domain;

namespace Winnow.Core.Queries;

/// <summary>
/// One release's achievement standing, and §6.2's rule made structural.
/// Achievements are stored per release and are never merged across
/// platforms — 100% on one platform and 30% on another are two facts, not
/// one average. This record describes exactly ONE release, and there is no
/// type, method or property anywhere that combines two of them, which is
/// what stops a blended cross-platform percentage from being written by
/// accident rather than by a rule somebody has to remember.
/// </summary>
public sealed record ReleaseAchievementSummary
{
    /// <summary>The release these achievements belong to. Never a work.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>How many achievements this release defines.</summary>
    public required int Total { get; init; }

    /// <summary>How many of them are unlocked on this release.</summary>
    public required int Unlocked { get; init; }

    public string? AccountRef { get; init; }
    public AchievementAvailability Availability { get; init; } = AchievementAvailability.Available;
    public DateTime? ObservedAt { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public DateTime? SchemaObservedAt { get; init; }
    public DateTime? GlobalObservedAt { get; init; }
    public bool HasKnownProgress { get; init; } = true;
    public bool IsStale { get; init; }

    /// <summary>One day bounds a progress display's freshness; it does not expire stored evidence.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromDays(1);

    /// <summary>
    /// True when the stored schema defines achievements. Availability and
    /// HasKnownProgress distinguish no schema from an unanswered progress query.
    /// </summary>
    public bool HasAny => Total > 0;

    /// <summary>
    /// Completion for THIS release, 0–100. Null when the release defines
    /// none, so a caller cannot divide by zero and cannot print "0%" about
    /// a game that has no achievements to unlock.
    /// </summary>
    public double? PercentComplete => !HasKnownProgress || Total == 0 ? null : Unlocked * 100.0 / Total;
}
