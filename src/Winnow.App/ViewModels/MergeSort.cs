using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels;

/// <summary>The three orders a section's pending cards can take.</summary>
public enum MergeSort
{
    /// <summary>EXACT MATCH, then LIKELY, then WORTH A LOOK; score breaks ties.</summary>
    StrongestMatch,

    /// <summary>Summed hours across the group, descending.</summary>
    PlaytimeAtStake,

    /// <summary>The current header title, in the user's culture.</summary>
    Title,
}

/// <summary>One entry of the sort menu.</summary>
public partial class MergeSortOptionViewModel : ObservableObject
{
    public MergeSortOptionViewModel(MergeSort sort, string label)
    {
        Sort = sort;
        Label = label;
    }

    /// <summary>The order this entry selects.</summary>
    public MergeSort Sort { get; }

    /// <summary>The words on the menu row.</summary>
    public string Label { get; }

    /// <summary>True on the row the queue is currently sorted by.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>One segment of the kind filter. A null kind is ALL.</summary>
public partial class MergeKindOptionViewModel : ObservableObject
{
    public MergeKindOptionViewModel(MergeSectionKind? kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    /// <summary>The section this segment shows alone, or null for every section.</summary>
    public MergeSectionKind? Kind { get; }

    /// <summary>Uppercase segment label.</summary>
    public string Label { get; }

    /// <summary>True on the lit segment.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
