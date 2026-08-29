using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels.Filters;

/// <summary>
/// One labelled group of checkable options in the filter panel (e.g. GENRE, GAME MODE).
/// Options within a group are OR'd; groups are AND'd together.
/// Short groups list all options alphabetically; long groups show a truncated head sorted by count.
/// </summary>
public partial class FilterGroupViewModel : ObservableObject
{
    /// <summary>Rows shown before "Show all" in a long group.</summary>
    private const int HeadCount = 8;

    private readonly Action _onChanged;
    private readonly List<FilterOptionViewModel> _all = [];

    /// <summary>
    /// Whether <see cref="_all"/> has been sorted. Count-ordered groups sort once
    /// on first real counts, then hold that order to avoid rows shifting on every recount.
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

    /// <summary>Toggle text: "Show all N" or "Show fewer".</summary>
    public string ShowAllText
        => IsExpanded ? "Show fewer" : $"Show all {_all.Count:N0}";

    public IEnumerable<FilterOptionViewModel> Checked => _all.Where(o => o.IsChecked);

    public bool HasSelection => _all.Exists(o => o.IsChecked);

    /// <summary>Rebuilds option rows from scratch, preserving checked state by key.</summary>
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

    /// <summary>Updates residual counts without reordering rows.</summary>
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

            // Count-ordered groups wait for real counts before locking order.
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

        // Checked options are always shown, even when collapsed.
        var shown = IsExpanded || searching
            ? matches
            : matches.Where((o, i) => i < HeadCount || o.IsChecked).ToList();

        HiddenCount = matches.Count - shown.Count;
        Options = shown;
    }
}
