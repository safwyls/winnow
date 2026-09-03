using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels;

/// <summary>
/// One section of the queue: a grouping kind, its title and blurb, and the
/// cards in it. Pending cards lead in the current sort; resolved strips
/// follow, newest act first, so what is still a question sits above what has
/// been answered.
/// </summary>
public partial class MergeSectionViewModel : ObservableObject
{
    public MergeSectionViewModel(MergeSectionKind kind, string title, string blurb)
    {
        Kind = kind;
        Title = title;
        Blurb = blurb;
        EmptyText = MergeCopy.SectionEmpty;
    }

    /// <summary>Which grouping kind this section holds.</summary>
    public MergeSectionKind Kind { get; }

    /// <summary>Uppercase section title.</summary>
    public string Title { get; }

    /// <summary>One sentence under the title saying what the kind is.</summary>
    public string Blurb { get; }

    /// <summary>Every card in the section, pending first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PendingCount), nameof(PendingCountText), nameof(IsEmpty), nameof(HasCards))]
    public partial IReadOnlyList<MergeCardViewModel> Cards { get; private set; } = [];

    /// <summary>False while the kind filter shows another section.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>What the section says when it holds nothing at all.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; }

    /// <summary>How many cards are still a question.</summary>
    public int PendingCount => Cards.Count(card => card.IsPending);

    /// <summary>The count beside the title, in the data face.</summary>
    public string PendingCountText => PendingCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>True when the section holds neither a card nor a strip.</summary>
    public bool IsEmpty => Cards.Count == 0;

    /// <summary>True when there is anything to draw.</summary>
    public bool HasCards => Cards.Count > 0;

    /// <summary>The pending cards, in the section's current order.</summary>
    public IEnumerable<MergeCardViewModel> Pending => Cards.Where(card => card.IsPending);

    /// <summary>Replaces the cards and orders them: pending in <paramref name="sort"/>, then strips.</summary>
    internal void Replace(IEnumerable<MergeCardViewModel> cards, MergeSort sort)
    {
        var pending = new List<MergeCardViewModel>();
        var resolved = new List<MergeCardViewModel>();
        foreach (var card in cards)
        {
            (card.IsPending ? pending : resolved).Add(card);
        }

        Sort(pending, sort);
        pending.AddRange(resolved);
        Cards = pending;
    }

    /// <summary>Re-orders the cards already in the section.</summary>
    internal void Resort(MergeSort sort) => Replace(Cards, sort);

    /// <summary>Removes one card, keeping the order of the rest.</summary>
    internal void Remove(MergeCardViewModel card)
    {
        var remaining = new List<MergeCardViewModel>(Cards.Count);
        foreach (var existing in Cards)
        {
            if (!ReferenceEquals(existing, card))
            {
                remaining.Add(existing);
            }
        }

        Cards = remaining;
    }

    /// <summary>Puts a card back at <paramref name="index"/>, clamped.</summary>
    internal void Insert(MergeCardViewModel card, int index)
    {
        var next = new List<MergeCardViewModel>(Cards);
        next.Insert(Math.Clamp(index, 0, next.Count), card);
        Cards = next;
    }

    /// <summary>Position of a card, or -1.</summary>
    internal int IndexOf(MergeCardViewModel card)
    {
        for (var i = 0; i < Cards.Count; i++)
        {
            if (ReferenceEquals(Cards[i], card))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Re-announces the counts after a card flipped between pending and resolved.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingCountText));
    }

    private static void Sort(List<MergeCardViewModel> cards, MergeSort sort)
    {
        switch (sort)
        {
            case MergeSort.PlaytimeAtStake:
                cards.Sort(static (a, b) =>
                {
                    var byMinutes = b.TotalMinutes.CompareTo(a.TotalMinutes);
                    return byMinutes != 0 ? byMinutes : ByStrength(a, b);
                });
                break;

            case MergeSort.Title:
                cards.Sort(static (a, b) =>
                {
                    var byTitle = string.Compare(
                        a.HeaderTitle, b.HeaderTitle, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
                    return byTitle != 0 ? byTitle : ByStrength(a, b);
                });
                break;

            default:
                cards.Sort(ByStrength);
                break;
        }
    }

    private static int ByStrength(MergeCardViewModel a, MergeCardViewModel b)
    {
        var byTier = a.Confidence.CompareTo(b.Confidence);
        if (byTier != 0)
        {
            return byTier;
        }

        var byScore = b.Score.CompareTo(a.Score);
        return byScore != 0
            ? byScore
            : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
    }
}
