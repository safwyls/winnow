namespace Winnow.Core.Queries;

/// <summary>Approximate counter evidence, never a session or an additional gameplay total.</summary>
public sealed record SteamReportedActivity
{
    public required long Id { get; init; }
    public required long OwnershipId { get; init; }
    public required string AccountRef { get; init; }
    public required DateTime WindowStartedAt { get; init; }
    public required DateTime WindowEndedAt { get; init; }
    public required long SteamDeltaMinutes { get; init; }
    public double? MatchedRecordedMinutes { get; init; }
    public double? UnexplainedMinutes { get; init; }
    public bool ComparisonUnavailable { get; init; }
}
