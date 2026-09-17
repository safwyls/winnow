namespace Winnow.Core.Domain;

public enum ArtworkSlot { Hero, Cover, Icon }

public enum ArtworkChoiceKind { Manual, Collection }

/// <summary>A saved selection on its original work. AssetKey addresses a retained local image.</summary>
public sealed record ArtworkChoice
{
    public long WorkId { get; init; }
    public ArtworkSlot Slot { get; init; }
    public ArtworkChoiceKind Kind { get; init; }
    public required string AssetKey { get; init; }
    public required string SourceId { get; init; }
    public required string AssetId { get; init; }
    public string? SourceUrl { get; init; }
    public string? Creator { get; init; }
    public string? PageUrl { get; init; }
    public string? CollectionId { get; init; }
    public long Revision { get; init; }
}

public static class ArtworkChoices
{
    /// <summary>
    /// Share choices through the current confirmed same-game group on reads only.
    /// Unlinking exposes each original work's own choices again. Manual choices win;
    /// otherwise the most recently saved choice wins, independently for each slot.
    /// </summary>
    public static ArtworkChoice? Effective(
        IEnumerable<ArtworkChoice> choices, IReadOnlyCollection<long> workIds, ArtworkSlot slot)
        => choices.Where(choice => choice.Slot == slot && workIds.Contains(choice.WorkId))
            .OrderBy(choice => choice.Kind == ArtworkChoiceKind.Manual ? 0 : 1)
            .ThenByDescending(choice => choice.Revision)
            .ThenBy(choice => choice.WorkId)
            .FirstOrDefault();
}
