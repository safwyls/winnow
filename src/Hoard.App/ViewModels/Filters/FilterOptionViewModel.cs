using CommunityToolkit.Mvvm.ComponentModel;

namespace Hoard.App.ViewModels.Filters;

/// <summary>
/// One checkable row in the filter panel: a name, and the number of titles it
/// would leave you with.
///
/// <para><b>The count is residual, not absolute.</b> It is computed with every
/// other group's selections applied and this group's own selections ignored, so
/// it answers the only question worth asking — "if I tick this, how many do I
/// get?" — rather than "how many of these do you own", which the user cannot
/// act on. Ignoring the group's own selections is what stops ticking one genre
/// from zeroing every other genre, since options inside a group widen the
/// result rather than narrowing it.</para>
/// </summary>
public partial class FilterOptionViewModel : ObservableObject
{
    private readonly Action<FilterOptionViewModel> _onToggled;

    /// <summary>
    /// True while a saved filter is being poured into the panel. The property
    /// still raises <c>PropertyChanged</c> — the tick has to appear — but it does
    /// not ask the panel to recount, because the panel is about to recount once
    /// for the whole set rather than once per option.
    /// </summary>
    private bool _applying;

    public FilterOptionViewModel(string key, string label, Action<FilterOptionViewModel> onToggled)
    {
        Key = key;
        Label = label;
        _onToggled = onToggled;
    }

    /// <summary>Stable identity — a facet id as text, a store name, a game-mode key.</summary>
    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsAvailable))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    public partial bool IsChecked { get; set; }

    /// <summary>Plex Mono, tabular — the panel's counts form a numeric column.</summary>
    public string CountText => Count.ToString("N0");

    /// <summary>
    /// A row that would empty the grid says so before it is clicked, and stops
    /// being a click target and a tab stop. A row already ticked stays live
    /// whatever its count says, because the way back out of an empty result must
    /// be the control that caused it.
    /// </summary>
    public bool IsAvailable => IsChecked || Count > 0;

    /// <summary>Sets the tick without asking the panel to recompute (see <see cref="_applying"/>).</summary>
    public void SetCheckedSilently(bool value)
    {
        _applying = true;
        try
        {
            IsChecked = value;
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnIsCheckedChanged(bool value)
    {
        _ = value;

        if (!_applying)
        {
            _onToggled(this);
        }
    }
}
