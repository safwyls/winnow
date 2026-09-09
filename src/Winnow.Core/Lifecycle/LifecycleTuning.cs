namespace Winnow.Core.Lifecycle;

/// <summary>Initial conservative product defaults, not calibrated probabilities.</summary>
public sealed record LifecycleTuning
{
    public static LifecycleTuning Default { get; } = new();
    public int EvidenceFreshDays { get; init; } = 30;
    public int ActivityFreshDays { get; init; } = 7;
    public int PlayerHistoryDays { get; init; } = 30;
    public int LowPlayerCeiling { get; init; } = 5;
    public int LowReviewCeiling { get; init; } = 2;
    public int MinimumPlayerSamples { get; init; } = 3;
    public int MinimumPlayerSpanDays { get; init; } = 14;
    public int DeadQuietDays { get; init; } = 365;
    public int AbandonedQuietDays { get; init; } = 730;
    public double CancelledConfidence { get; init; } = 0.99;
    public double OfflineConfidence { get; init; } = 0.98;
    public double DelistedConfidence { get; init; } = 0.93;
    public double AbandonedConfidence { get; init; } = 0.75;
    public double DeadConfidence { get; init; } = 0.8;
    public double InactiveConfidence { get; init; } = 0.55;
    public double ActiveConfidence { get; init; } = 0.65;
    public double UnknownConfidence { get; init; }
}
