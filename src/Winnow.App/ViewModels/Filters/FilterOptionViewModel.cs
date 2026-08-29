using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels.Filters;

/// <summary>
/// One checkable row in the filter panel. The count is residual (computed with
/// other groups' selections applied but this group's own ignored).
/// </summary>
public partial class FilterOptionViewModel : ObservableObject
{
    private readonly Action<FilterOptionViewModel> _onToggled;

    /// <summary>Suppresses the onToggled callback during batch application.</summary>
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

    /// <summary>Formatted count for display.</summary>
    public string CountText => Count.ToString("N0");

    /// <summary>False when unchecked and count is zero (would produce empty results). Checked rows stay available.</summary>
    public bool IsAvailable => IsChecked || Count > 0;

    /// <summary>Sets checked state without triggering a panel recount.</summary>
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
