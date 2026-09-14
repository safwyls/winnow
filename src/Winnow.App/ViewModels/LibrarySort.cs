using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels;

/// <summary>
/// The library orders. Users choose a startup default in Library settings;
/// DormantLongest is the fallback when no preference has been saved.
/// <para>Sort is view-agnostic: the grid and the list are two renderings of the
/// same ordered sequence, so a column header in list view and the command-bar
/// menu write the same state (§6 — the list is the power-user view of the same
/// data, not a second data set).</para>
/// </summary>
public enum LibrarySort
{
    /// <summary>Longest since last played first; never-opened counts as maximally dormant.</summary>
    DormantLongest,

    /// <summary>Most recently played first; never-opened sinks to the bottom.</summary>
    RecentlyPlayed,

    /// <summary>Most minutes first.</summary>
    PlaytimeHighToLow,

    /// <summary>Fewest minutes first.</summary>
    PlaytimeLowToHigh,

    /// <summary>Title, ascending.</summary>
    NameAscending,

    /// <summary>Title, descending.</summary>
    NameDescending,

    /// <summary>
    /// The order the user dragged the rows into. Only reachable while a manual
    /// list is open, and the order that list opens in — a hand-built list whose
    /// stored positions are invisible has no reason to store them, and "move up"
    /// under an alphabetical sort is a control that appears to do nothing.
    /// </summary>
    ListOrder,
}

/// <summary>
/// One row of the command-bar sort menu. Carries its own label so the button
/// can show the active order without a converter, and its own selected flag so
/// the menu can mark it.
/// </summary>
public partial class SortOptionViewModel : ObservableObject
{
    public SortOptionViewModel(LibrarySort sort, string label)
    {
        Sort = sort;
        Label = label;
    }

    public LibrarySort Sort { get; }

    /// <summary>§7 voice: says what the order is, never how it is computed.</summary>
    public string Label { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
