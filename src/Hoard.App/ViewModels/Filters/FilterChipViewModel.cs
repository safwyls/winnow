using CommunityToolkit.Mvvm.Input;

namespace Hoard.App.ViewModels.Filters;

/// <summary>
/// One rule, restated above the grid where the result is.
///
/// <para>The cut bar exists because a filtered library that does not say it is
/// filtered is the single most expensive confusion this screen can produce — the
/// panel can be closed, the rail scrolled past, and the user is left looking at
/// 41 of 926 games with nothing on screen admitting it. Each chip carries its
/// own way out, so undoing one rule never means finding the control that set
/// it.</para>
///
/// <para><b>Chips are Volt-edged, never Flare.</b> A chip is a selection, which
/// is what Volt is for (§2). Flare marks unread updates and nothing else.</para>
/// </summary>
public sealed partial class FilterChipViewModel
{
    private readonly Action _remove;

    public FilterChipViewModel(string label, string dimension, Action remove)
    {
        Label = label;
        Dimension = dimension;
        _remove = remove;
    }

    public string Label { get; }

    /// <summary>The group it came from — the chip's tooltip, so "Action" is legibly a genre.</summary>
    public string Dimension { get; }

    /// <summary>Screen-reader and hover text: which axis this chip is on.</summary>
    public string Description => $"{Dimension}: {Label}";

    [RelayCommand]
    private void Remove() => _remove();
}
