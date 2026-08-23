using CommunityToolkit.Mvvm.ComponentModel;

namespace Hoard.App.ViewModels;

/// <summary>
/// Placeholder shell view model for M0. The real library view (tiles,
/// rails, buckets) arrives with the UI wave.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Hoard";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "Library view under construction";
}
