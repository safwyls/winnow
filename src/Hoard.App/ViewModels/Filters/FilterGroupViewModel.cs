using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hoard.App.ViewModels.Filters;

/// <summary>
/// One labelled block of the filter panel — GENRE, STORE TAG, GAME MODE and so
/// on. Options inside a group are an OR; groups are AND'd together
/// (<see cref="Hoard.Core.Queries.LibraryFilter"/>).
///
/// <para><b>Two shapes, chosen by how long the tail is.</b> A short dimension —
/// game mode, on disk, controller — lists every option in alphabetical order,
/// because a fixed position is what makes a block scannable on the second visit.
/// A long one — store tag at 378 values, features at 42, genre at 23 — leads
/// with its commonest options and shows the head of the list, with a field to
/// reach the rest. The reference storefront makes STORE TAGS a free-text field
/// for the same reason and loses the counts doing it; this keeps both.</para>
/// </summary>
public partial class FilterGroupViewModel : ObservableObject
{
    /// <summary>
    /// How many rows a long group shows before "Show all". Eight is two more
    /// than the tallest short group, so a collapsed long group never reads as
    /// the shortest thing on the panel.
    /// </summary>
    private const int HeadCount = 8;

    private readonly Action _onChanged;
    private readonly List<FilterOptionViewModel> _all = [];

    /// <summary>
    /// Whether <see cref="_all"/> has been put into its display order yet.
    ///
    /// <para>A count-ordered group is ordered ONCE, on the first counts it is
    /// given, and then holds that order for the rest of the session. Re-sorting
    /// on every recount was the obvious reading of "commonest first" and it is
    /// wrong: every tick anywhere on the panel moves every count, so the rows
    /// would rearrange themselves under the pointer between one click and the
    /// next. A list you cannot click twice in the same place is not a list.</para>
    /// </summary>
    private bool _ordered;

    public FilterGroupViewModel(
        string key,
        string header,
        Action onChanged,
        bool sortByCount = false,
        string? findWatermark = null)
    {
        Key = key;
        Header = header;
        SortByCount = sortByCount;
        FindWatermark = findWatermark;
        _onChanged = onChanged;
    }

    /// <summary>Dimension key — one of <see cref="FilterPanelViewModel"/>'s group constants.</summary>
    public string Key { get; }

    public string Header { get; }

    /// <summary>Long dimensions lead with their biggest options; short ones stay alphabetical.</summary>
    public bool SortByCount { get; }

    /// <summary>Non-null on the groups that get a find field. Null hides the field entirely.</summary>
    public string? FindWatermark { get; }

    public bool IsFindable => FindWatermark is not null;

    /// <summary>Every option this dimension has, in display order.</summary>
    public IReadOnlyList<FilterOptionViewModel> AllOptions => _all;

    [ObservableProperty]
    public partial IReadOnlyList<FilterOptionViewModel> Options { get; set; } = [];

    [ObservableProperty]
    public partial string FindText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllText))]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllText))]
    public partial int HiddenCount { get; set; }

    /// <summary>The whole group is absent when its dimension has no options at all.</summary>
    public bool HasOptions => _all.Count > 0;

    public bool CanExpand => HiddenCount > 0 || IsExpanded;

    /// <summary>
    /// "Show all 214" — the number is the point, so it is in the button rather
    /// than left for the user to guess at.
    /// </summary>
    public string ShowAllText
        => IsExpanded ? "Show fewer" : $"Show all {_all.Count:N0}";

    public IEnumerable<FilterOptionViewModel> Checked => _all.Where(o => o.IsChecked);

    public bool HasSelection => _all.Exists(o => o.IsChecked);

    /// <summary>
    /// Rebuilds the option rows. Called once per library load, never per
    /// keystroke: the rows carry the checked state, and replacing them on every
    /// recount would drop the user's selection under their cursor.
    /// </summary>
    public void SetOptions(IEnumerable<(string Key, string Label)> options)
    {
        var previously = _all.Where(o => o.IsChecked).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);

        _all.Clear();
        _ordered = false;
        foreach (var (key, label) in options)
        {
            var option = new FilterOptionViewModel(key, label, _ => _onChanged())
            {
                IsChecked = previously.Contains(key),
            };
            _all.Add(option);
        }

        Reflow();
    }

    /// <summary>
    /// Writes fresh residual counts. The ORDER does not move (see
    /// <see cref="_ordered"/>) — only the numbers do, which is the whole point:
    /// the user watches the column change while the rows stay put.
    /// </summary>
    public void SetCounts(IReadOnlyDictionary<string, int> counts)
    {
        foreach (var option in _all)
        {
            option.Count = counts.TryGetValue(option.Key, out var count) ? count : 0;
        }

        Reflow();
    }

    /// <summary>Sets the checked set from a saved filter, without raising a change per option.</summary>
    public void ApplySelection(IEnumerable<string> keys, bool silent)
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var option in _all)
        {
            if (option.IsChecked == wanted.Contains(option.Key))
            {
                continue;
            }

            if (silent)
            {
                option.SetCheckedSilently(wanted.Contains(option.Key));
            }
            else
            {
                option.IsChecked = wanted.Contains(option.Key);
            }
        }
    }

    public void ClearSelection(bool silent)
        => ApplySelection([], silent);

    [RelayCommand]
    private void ToggleShowAll() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        _ = value;
        Reflow();
    }

    partial void OnFindTextChanged(string value)
    {
        _ = value;

        // Typing in a find field opens the group: the match the user is after is
        // very often past the eighth row, and a field that filters a list it is
        // not showing is a field that looks broken.
        Reflow();
    }

    private void Reflow()
    {
        if (!_ordered)
        {
            _all.Sort(SortByCount
                ? (a, b) => b.Count != a.Count
                    ? b.Count.CompareTo(a.Count)
                    : string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase)
                : (a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));

            // Alphabetical groups are settled from the start; a count-ordered one
            // is only settled once it has real counts to settle on.
            _ordered = !SortByCount || _all.Exists(o => o.Count > 0);
        }

        IEnumerable<FilterOptionViewModel> ordered = _all;

        var find = FindText.Trim();
        var searching = find.Length > 0;
        if (searching)
        {
            ordered = ordered.Where(o => o.Label.Contains(find, StringComparison.CurrentCultureIgnoreCase));
        }

        var matches = ordered.ToList();

        // A ticked option is never hidden behind "Show all". Losing sight of a
        // rule that is shaping the grid is how a filter panel becomes a haunting.
        var shown = IsExpanded || searching
            ? matches
            : matches.Where((o, i) => i < HeadCount || o.IsChecked).ToList();

        HiddenCount = matches.Count - shown.Count;
        Options = shown;
    }
}
