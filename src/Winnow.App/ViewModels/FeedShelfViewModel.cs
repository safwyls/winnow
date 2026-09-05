using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// One feed section: a titled group of cards with a reason (blurb) and a count.
/// </summary>
public sealed class FeedShelfViewModel : ObservableObject
{
    public FeedShelfViewModel(
        string id,
        string title,
        string blurb,
        IEnumerable<FeedCardViewModel> cards,
        IEnumerable<FeedItem>? reserve = null)
    {
        Id = id;
        Title = title;
        Blurb = blurb;
        Cards = new ObservableCollection<FeedCardViewModel>(cards);
        Reserve = new Queue<FeedItem>(reserve ?? []);

        Cards.CollectionChanged += OnCardsChanged;
    }

    /// <summary>Stable shelf id (<c>patched_while_away</c>…), never matched on prose.</summary>
    public string Id { get; }

    /// <summary>The engine's display title, in its own words.</summary>
    public string Title { get; }

    /// <summary>The engine's one-line pitch for why this shelf exists.</summary>
    public string Blurb { get; }

    /// <summary>
    /// The cards on screen. Observable so a dismissed card can be replaced where
    /// it stands: assigning one index realises one container and re-measures
    /// this shelf, where clearing the collection rebuilds every card in the feed
    /// and re-leases every cover, for one card the reader answered.
    /// </summary>
    public ObservableCollection<FeedCardViewModel> Cards { get; }

    /// <summary>
    /// Replacements this shelf is holding, in score order, computed by the same
    /// pass that filled <see cref="Cards"/>. Never rendered — a reserve item
    /// becomes a card only when it takes a dismissed card's place, which is also
    /// when it is logged as surfaced.
    /// </summary>
    internal Queue<FeedItem> Reserve { get; }

    /// <summary>Whether this shelf still has something to put in a dismissed card's place.</summary>
    internal bool HasReserve => Reserve.Count > 0;

    /// <summary>Formatted card count for display.</summary>
    public string CountText => Cards.Count.ToString("N0");

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A one-for-one swap leaves the count alone, but nothing here may
        // assume the swap is the only edit this collection will ever see.
        if (e.Action != NotifyCollectionChangedAction.Replace)
        {
            OnPropertyChanged(nameof(CountText));
        }
    }
}
