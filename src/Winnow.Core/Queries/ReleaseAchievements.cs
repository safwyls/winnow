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

    /// <summary>
    /// True when this release defines any achievements at all. Absence is
    /// common — nothing ingests achievements yet — and a release with no row
    /// renders as no row rather than as zero of zero.
    /// </summary>
    public bool HasAny => Total > 0;

    /// <summary>
    /// Completion for THIS release, 0–100. Null when the release defines
    /// none, so a caller cannot divide by zero and cannot print "0%" about
    /// a game that has no achievements to unlock.
    /// </summary>
    public double? PercentComplete => Total == 0 ? null : Unlocked * 100.0 / Total;
}
