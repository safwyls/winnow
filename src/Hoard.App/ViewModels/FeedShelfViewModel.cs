namespace Hoard.App.ViewModels;

/// <summary>
/// One section: a reason with games attached.
///
/// <para><b>A short section is a normal section.</b> The engine omits a shelf
/// with nothing to say rather than rendering it blank, so every one that
/// reaches this class has real members — six of them on the author's real
/// library for "Installed and waiting", which has to read as a complete answer
/// and not as a half-loaded one.</para>
///
/// <para><b>The count still earns its place, and it says something different
/// now.</b> While the sections were horizontal rails it told you how much was
/// hidden off the right edge. They are wrapping grids, so nothing is hidden and
/// the count is the section agreeing with what you can already see — which is
/// what makes six cards under a <c>6</c> read as finished rather than as
/// half-loaded, and what would catch a card the view had to drop.</para>
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

    /// <summary>How many games are in the section. Plex Mono, tabular, like every number.</summary>
    public string CountText => Cards.Count.ToString("N0");
}
