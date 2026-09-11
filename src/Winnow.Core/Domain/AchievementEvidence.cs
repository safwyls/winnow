namespace Winnow.Core.Domain;

public enum AchievementAvailability { Unknown, Unavailable, NoSchema, Available }

public sealed record AchievementDefinition(string Key, string Name, string? Description, bool Hidden);

/// <summary>Null collections mean unanswered; empty schema means confirmed no achievements.</summary>
public sealed record AchievementFetch
{
    public required DateTime AttemptedAt { get; init; }
    public IReadOnlyList<AchievementDefinition>? Schema { get; init; }
    public IReadOnlyDictionary<string, DateTime?>? Unlocks { get; init; }
    public IReadOnlyDictionary<string, double>? GlobalPercentages { get; init; }
}
