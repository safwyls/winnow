namespace Winnow.App.ViewModels;

/// <summary>
/// One feed section: a titled group of cards with a reason (blurb) and a count.
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

    /// <summary>Formatted card count for display.</summary>
    public string CountText => Cards.Count.ToString("N0");
}
