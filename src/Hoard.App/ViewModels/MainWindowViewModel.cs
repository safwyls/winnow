using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        // Built from the library's own ramp rather than resolved separately:
        // two DormancyRamp instances would be a toggle wired to nothing the
        // tiles read, which fails silently and looks like a broken feature.
        Display = new DisplaySettingsViewModel(library.Ramp, settings);
    }

    public LibraryViewModel Library { get; }

    public MergeQueueViewModel MergeQueue { get; }

    /// <summary>The command bar's Display popover — §8's dimming preference.</summary>
    public DisplaySettingsViewModel Display { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible))]
    public partial bool IsMergeQueueVisible { get; set; }

    public bool IsLibraryVisible => !IsMergeQueueVisible;

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
