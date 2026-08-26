using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.ViewModels.Filters;
using Hoard.App.ViewModels.Lists;
using Hoard.Core.Repositories;

namespace Hoard.App.ViewModels;

/// <summary>
/// Window shell: hosts the library view and the merge confirm queue, and owns
/// which of the two the rail is currently pointing at.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// <paramref name="settings"/> is optional so an unregistered store costs
    /// persistence and nothing else: the display preference still works for the
    /// session rather than taking the window down at startup.
    /// </summary>
    public MainWindowViewModel(
        LibraryViewModel library,
        MergeQueueViewModel mergeQueue,
        ISettingsRepository? settings = null)
    {
        Library = library;
        MergeQueue = mergeQueue;

        // The panel is a column in the library's own layout, so it has to
        // disappear when the library does. Two sources, one answer, computed
        // here rather than composed in XAML — Avalonia has no boolean AND in a
        // binding, and a converter for one expression is more machinery than
        // the property it would replace.
        Library.Filters.PropertyChanged += OnFiltersChanged;

        // Built from the library's own ramp rather than resolved separately:
        // two DormancyRamp instances would be a toggle wired to nothing the
        // tiles read, which fails silently and looks like a broken feature.
        Display = new DisplaySettingsViewModel(
            library.Ramp,
            settings,
            reloadLibrary: async () =>
            {
                // The toggle owns the flag; the library owns the query. Setting
                // it here rather than having the library read the store keeps one
                // reader of the preference and no chance of the two drifting.
                library.ShowNonGameEntries = Display!.ShowNonGameEntries;
                await library.LoadCommand.ExecuteAsync(null);
            });
    }

    public LibraryViewModel Library { get; }

    public MergeQueueViewModel MergeQueue { get; }

    /// <summary>The command bar's Display popover — §8's dimming preference.</summary>
    public DisplaySettingsViewModel Display { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsMergeQueueVisible { get; set; }

    public bool IsLibraryVisible => !IsMergeQueueVisible;

    /// <summary>The filter panel is part of the library screen, not of the window.</summary>
    public bool IsFilterPanelVisible => IsLibraryVisible && Library.Filters.IsOpen;

    /// <summary>
    /// Rail list row. Like a bucket, it brings the library back — the rail never
    /// leaves the user on a screen their click did not describe — and it toggles,
    /// so clicking the open list closes it and returns the whole library.
    /// </summary>
    [RelayCommand]
    private void SelectList(GameListViewModel? list)
    {
        IsMergeQueueVisible = false;
        Library.OpenListCommand.Execute(
            ReferenceEquals(Library.Lists.Open, list) ? null : list);
    }

    private void OnFiltersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterPanelViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(IsFilterPanelVisible));
        }
    }

    /// <summary>
    /// Rail bucket click. Selecting a bucket is a statement about the library,
    /// so it also brings the library back — the rail never leaves the user on a
    /// screen their click did not describe.
    /// </summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket)
    {
        IsMergeQueueVisible = false;
        Library.SelectBucketCommand.Execute(bucket);
    }

    [RelayCommand]
    private void ShowLibrary() => IsMergeQueueVisible = false;

    /// <summary>The rail row toggles, so the same click that opened the queue closes it.</summary>
    [RelayCommand]
    private void ToggleMergeQueue() => IsMergeQueueVisible = !IsMergeQueueVisible;
}
