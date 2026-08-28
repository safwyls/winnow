namespace Hoard.App.ViewModels;

/// <summary>
/// One rail: a reason with games attached.
///
/// <para><b>A short shelf is a normal shelf.</b> The engine omits a shelf with
/// nothing to say rather than rendering it blank, so every rail that reaches
/// this class has real members — six of them on the author's real library for
/// "Installed and waiting", which has to read as a complete answer and not as a
/// half-loaded one. That is why the count is stated: a rail showing three cards
/// and saying <c>6</c> is a rail you know to scroll, and a rail showing six and
/// saying <c>6</c> is finished.</para>
///
/// <para><b>No shelf is styled differently from any other, and the patched
/// shelf least of all.</b> It is the app's headline story and therefore the most
/// tempting place in the interface to spend Flare on a heading or a card edge —
/// which would cost the badge on the covers its meaning (§2). The shelf leads by
/// being first, which is the engine's claim order, and by what its sentences
/// say.</para>
/// </summary>
public sealed class FeedShelfViewModel
{
    public FeedShelfViewModel(string id, string title, string blurb, IReadOnlyList<FeedCardViewModel> cards)
    {
        Id = id;
        Title = title;
        Blurb = blurb;
        Cards = cards;
    }

    /// <summary>Stable shelf id (<c>patched_while_away</c>…), never matched on prose.</summary>
    public string Id { get; }

    /// <summary>The engine's display title, in its own words.</summary>
    public string Title { get; }

    /// <summary>The engine's one-line pitch for why this shelf exists.</summary>
    public string Blurb { get; }

    public IReadOnlyList<FeedCardViewModel> Cards { get; }

    /// <summary>How many games are on the rail. Plex Mono, tabular, like every number.</summary>
    public string CountText => Cards.Count.ToString("N0");
}
