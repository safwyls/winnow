namespace Winnow.App.Services;

/// <summary>
/// Every outcome is a word, never a percentage. No total is knowable in
/// advance, so no proportion can be honest.
/// </summary>
public enum GameRefetchOutcome
{
    Updated,
    NothingNew,
    NoSourceToAsk,
    NotConfigured,
    Unreachable,
    TooSoon,
    WorkNotFound,
}

public sealed record GameRefetchResult(GameRefetchOutcome Outcome)
{
    public int RowsWritten { get; init; }

    public bool AskedIgdb { get; init; }

    public bool AskedSteam { get; init; }

    public bool MetadataFilled { get; init; }

    public bool UsedPin { get; init; }

    public TimeSpan RetryAfter { get; init; }

    public bool Succeeded => Outcome is GameRefetchOutcome.Updated or GameRefetchOutcome.NothingNew;
}

public interface IGameRefetch
{
 Task<GameRefetchResult> RefetchAsync(long workId, CancellationToken ct = default);
}
