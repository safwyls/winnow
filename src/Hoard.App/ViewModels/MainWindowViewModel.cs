using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hoard.App.ViewModels;

/// <summary>
/// Window shell: hosts the library view and the merge confirm queue, and owns
/// which of the two the rail is currently pointing at.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(LibraryViewModel library, MergeQueueViewModel mergeQueue)
    {
        Library = library;
        MergeQueue = mergeQueue;
    }

    public LibraryViewModel Library { get; }

    public MergeQueueViewModel MergeQueue { get; }

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
