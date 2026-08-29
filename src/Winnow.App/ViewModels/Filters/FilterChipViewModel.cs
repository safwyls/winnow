using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels.Filters;

/// <summary>Where a filter chip on the cut bar came from: user-set vs. list-contributed.</summary>
public enum FilterChipOrigin
{
    /// <summary>The user set this rule directly.</summary>
    User,

    /// <summary>User-set while inside a live list; an unsaved edit to that list's rules.</summary>
    Unsaved,

    /// <summary>Part of the open live list's saved rules. Neutral-edged.</summary>
    List,

    /// <summary>The open list itself — not a rule but the place the rules belong to.</summary>
    Context,
}

/// <summary>
/// One dismissable filter chip on the cut bar above the grid.
/// Edge colour indicates origin: Volt for user-set, neutral for list-contributed.
/// </summary>
public sealed partial class FilterChipViewModel
{
    private readonly Action _remove;

    public FilterChipViewModel(
        string label,
        string dimension,
        Action remove,
        FilterChipOrigin origin = FilterChipOrigin.User)
    {
        Label = label;
        Dimension = dimension;
        Origin = origin;
        _remove = remove;
    }

    public string Label { get; }

    /// <summary>The filter group this chip belongs to (used as tooltip).</summary>
    public string Dimension { get; }

    /// <summary>Who set this rule: the user, or the list they are inside.</summary>
    public FilterChipOrigin Origin { get; }

    /// <summary>True when this is a user-set rule (Volt edge).</summary>
    public bool IsUserRule => Origin is FilterChipOrigin.User or FilterChipOrigin.Unsaved;

    /// <summary>True when contributed by the open live list (neutral edge).</summary>
    public bool IsListRule => Origin == FilterChipOrigin.List;

    /// <summary>True when this chip represents the open list itself, not a filter rule.</summary>
    public bool IsContext => Origin == FilterChipOrigin.Context;

    /// <summary>Accessible description including dimension, label, and origin.</summary>
    public string Description => Origin switch
    {
        FilterChipOrigin.Context =>
            $"{Dimension}: {Label} — from context",
        FilterChipOrigin.List =>
            $"{Dimension}: {Label} — from this live list",
        FilterChipOrigin.Unsaved =>
            $"{Dimension}: {Label} — yours, not saved to this list",
        _ => $"{Dimension}: {Label}",
    };

    /// <summary>Tooltip for the dismiss button.</summary>
    public string RemoveTip => IsContext ? "Leave this list" : "Drop this filter";

    [RelayCommand]
    private void Remove() => _remove();
}
