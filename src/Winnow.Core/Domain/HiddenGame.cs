namespace Winnow.Core.Domain;

/// <summary>
/// One game the user has asked the library to hide. Projected from the live
/// rows of <c>hidden_games</c> (migration 0023) — the rows whose
/// <c>unhidden_at</c> is still null.
/// </summary>
public sealed record HiddenGame
{
    /// <summary>The hidden work's id. The exclusion is per work, not per release or ownership.</summary>
    public required long WorkId { get; init; }

    /// <summary>The work's display title, for the unhide screen.</summary>
    public required string Title { get; init; }

    /// <summary>When the user hid this game (UTC).</summary>
    public required DateTime HiddenAt { get; init; }

    /// <summary>
    /// How many ownerships exist across this work's releases. Carried so the
    /// unhide screen can say what it is offering back — "2 store entries" rather
    /// than a bare title.
    /// </summary>
    public int StoreEntryCount { get; init; }
}
