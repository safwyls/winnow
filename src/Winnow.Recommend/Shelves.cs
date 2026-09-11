namespace Winnow.Recommend;

/// <summary>
/// Stable shelf identifiers, so callers and tests never match on display
/// prose. Order here is the presentation order and the claim order — see
/// <see cref="ShelfBuilder"/> for why the two must be the same order.
/// </summary>
public static class ShelfIds
{
    /// <summary>Lifecycle evidence for review, excluded from play recommendations.</summary>
    public const string Derelict = "derelict";

    /// <summary>Bucket stale_but_patched: a major update landed after the user walked away. The headline shelf.</summary>
    public const string PatchedWhileAway = "patched_while_away";

    /// <summary>Bounced past the refund line, not probably-done: they committed, then drifted.</summary>
    public const string WorthAnotherLook = "worth_another_look";

    /// <summary>Installed and under the refund line: zero friction, nothing sunk.</summary>
    public const string ReadyToPlay = "ready_to_play";

    /// <summary>Sampled (1..refund-line minutes): opened once, never really tried.</summary>
    public const string BarelyTouched = "barely_touched";

    /// <summary>Never opened, and it carries a descriptor the user's hours concentrate in.</summary>
    public const string OnYourTaste = "on_your_taste";

    /// <summary>Never-opened games without an installation or a strong taste match.</summary>
    public const string WaitingToBeOpened = "waiting_to_be_opened";
}

/// <summary>
/// One shelf of the feed: a themed slice of the same scored candidates, with
/// its own one-line pitch. A Netflix-style surface is several shelves with
/// different reasons, not one ranked list. Play shelves query the same scores;
/// Derelict carries lifecycle evidence separately without scoring.
/// </summary>
public sealed record RecommendationShelf
{
    /// <summary>One of <see cref="ShelfIds"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Display title ("Patched while you were away").</summary>
    public required string Title { get; init; }

    /// <summary>The one-line pitch for why this shelf exists — the shelf-level reason, matching the per-item ones.</summary>
    public required string Blurb { get; init; }

    /// <summary>Ranked items, each carrying its own reason. Never empty — empty shelves are omitted from the feed.</summary>
    public required IReadOnlyList<Recommendation> Items { get; init; }
}

/// <summary>The shelf-shaped answer: several themed rails over one scoring pass.</summary>
public sealed record ShelfFeed
{
    /// <summary>Shelves in presentation order. A shelf with nothing to say is absent, not empty.</summary>
    public required IReadOnlyList<RecommendationShelf> Shelves { get; init; }

    /// <summary>See <see cref="DataTier"/> — how much history backed this feed.</summary>
    public required DataTier Tier { get; init; }

    /// <summary>Ownerships that survived the hard exclusions, same meaning as <see cref="RecommendationFeed.CandidateCount"/>.</summary>
    public required int CandidateCount { get; init; }

    /// <inheritdoc cref="RecommendationFeed.WorkCount"/>
    public int WorkCount { get; init; }

    /// <inheritdoc cref="RecommendationFeed.HistoryProbeCount"/>
    public int HistoryProbeCount { get; init; }
}
