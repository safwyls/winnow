using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels;

/// <summary>A preferred source for pending merge headers; null leaves choices to the user.</summary>
public partial class MergePlatformOptionViewModel(string? store, string label) : ObservableObject
{
    public string? Store { get; } = store;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
