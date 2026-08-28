namespace Hoard.App.ViewModels;

/// <summary>
/// One card on one shelf: a game the library already knows how to draw, and the
/// sentence that says why it is in front of you.
///
/// <para><b>The reason is on the FRONT, in full, always.</b> Not a tooltip, not
/// trimmed to "Patched", not paraphrased into a genre label — the sentence is
/// the product, and a wall of covers with the reasons hidden is the storefront
/// feed Hoard is trying to beat. The card is therefore sized to the sentence
/// (measured on the real library: median 115 characters, 90th percentile 155,
/// longest 256) rather than the sentence trimmed to a card.</para>
///
/// <para><b>And that is why the card does not flip.</b> The library's tiles turn
/// over because a cover has nowhere to put four facts; this card is already
/// showing the one fact that matters, and turning it over would hide the reason
/// to reveal a weaker restatement of it — the bucket name, the playtime and the
/// last-played date are all clauses of the sentence on the front. Both actions
/// the back face carried are on this face instead: the primary Play/Install and
/// the route to the detail modal.</para>
/// </summary>
public sealed class FeedCardViewModel
{
    public FeedCardViewModel(GameTileViewModel tile, string reason)
    {
        Tile = tile;
        Reason = reason;
        ReasonRuns = ReasonText.Split(reason);
    }

    /// <summary>
    /// The library's own tile. Shared instance, not a copy — see
    /// <see cref="IGameTileSource"/> for why the feed borrows rather than builds.
    /// </summary>
    public GameTileViewModel Tile { get; }

    /// <summary>
    /// The engine's sentence, verbatim, for the accessible name and for any
    /// caller that wants the text rather than the runs.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// The sentence split into prose and numbers, so the card can set every
    /// number in Plex Mono with tabular figures (§3) without rewriting a word
    /// of it. See <see cref="ReasonText"/>.
    /// </summary>
    public IReadOnlyList<ReasonRun> ReasonRuns { get; }
}
