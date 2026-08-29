using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.ViewModels.Filters;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>
/// Window shell: hosts the Feed, library, merge queue, Platforms and
/// Appearance screens. The rail switches between them; one screen at a time.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <param name="settings">Optional; without it the preference works for the session only.</param>
    public MainWindowViewModel(
        LibraryViewModel library,
        MergeQueueViewModel mergeQueue,
        StoresViewModel stores,
        AppearanceViewModel appearance,
        FeedViewModel feed,
        SteamAccountImportViewModel accountImport,
        ISettingsRepository? settings = null,
        Services.SessionJournalService? journal = null)
    {
        Library = library;
        MergeQueue = mergeQueue;
        Stores = stores;
        Appearance = appearance;
        Feed = feed;
        AccountImport = accountImport;

        // Floating layout is structural (margins, radii, borders), not colour.
        appearance.Service.Applied += (_, _) => OnPropertyChanged(nameof(IsFloatingLayout));

        // Filter panel visibility depends on both library and filter state.
        Library.Filters.PropertyChanged += OnFiltersChanged;

        // Shares the library's DormancyRamp so the toggle and tiles agree.
        Display = new DisplaySettingsViewModel(
            library.Ramp,
            settings,
            journal: journal,
            reloadLibrary: async () =>
            {
                library.ShowNonGameEntries = Display!.ShowNonGameEntries;
                await library.LoadCommand.ExecuteAsync(null);
            });
    }

    public LibraryViewModel Library { get; }

    public MergeQueueViewModel MergeQueue { get; }

    public StoresViewModel Stores { get; }

    public FeedViewModel Feed { get; }

    /// <summary>The command bar's Display popover — §8's dimming preference.</summary>
    public DisplaySettingsViewModel Display { get; }

    public AppearanceViewModel Appearance { get; }

    /// <summary>The Steam account-page import screen (ROADMAP M5 item 3).</summary>
    public SteamAccountImportViewModel AccountImport { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsMergeQueueVisible { get; set; }

    /// <summary>The Stores panel, opened from the rail's SETTINGS › STORES row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsStoresVisible { get; set; }

    /// <summary>The Appearance screen, opened from the rail's SETTINGS › APPEARANCE row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsAppearanceVisible { get; set; }

    /// <summary>The Steam account-page import screen, a third SETTINGS row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsAccountImportVisible { get; set; }

    /// <summary>The Feed — the default landing screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsFeedVisible { get; set; } = true;

    /// <summary>Whether panes float as rounded cards (§15).</summary>
    public bool IsFloatingLayout => Appearance.Service.IsFloating;

    public bool IsLibraryVisible =>
        !IsMergeQueueVisible && !IsStoresVisible && !IsAppearanceVisible
        && !IsAccountImportVisible && !IsFeedVisible;

    /// <summary>The filter panel is part of the library screen, not of the window.</summary>
    public bool IsFilterPanelVisible => IsLibraryVisible && Library.Filters.IsOpen;

    /// <summary>Rail list row; shows the library and toggles the selected list.</summary>
    [RelayCommand]
    private void SelectList(GameListViewModel? list)
    {
        ShowLibraryPane();
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

    /// <summary>Rail bucket click; shows the library and selects the bucket.</summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket)
    {
        ShowLibraryPane();
        Library.SelectBucketCommand.Execute(bucket);
    }

    [RelayCommand]
    private void ShowLibrary() => ShowLibraryPane();

    /// <summary>Toggles the feed screen. No scoring on toggle — shows cached results.</summary>
    [RelayCommand]
    private void ToggleFeed()
    {
        var open = !IsFeedVisible;
        ShowLibraryPane();
        IsFeedVisible = open;
    }

    /// <summary>The rail row toggles, so the same click that opened the queue closes it.</summary>
    [RelayCommand]
    private void ToggleMergeQueue()
    {
        var open = !IsMergeQueueVisible;
        ShowLibraryPane();
        IsMergeQueueVisible = open;
    }

    /// <summary>Toggles the Appearance screen.</summary>
    [RelayCommand]
    private void ToggleAppearance()
    {
        var open = !IsAppearanceVisible;
        ShowLibraryPane();
        IsAppearanceVisible = open;
    }

    /// <summary>
    /// Toggles the import screen. Opening it asks the embedded browser whether
    /// it could run here — a question that opens no window and does no IO — so
    /// that the screen can say so before anything is pressed. Opening this
    /// screen must never start either route.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAccountImportAsync()
    {
        var open = !IsAccountImportVisible;
        ShowLibraryPane();
        IsAccountImportVisible = open;

        if (open)
        {
            await AccountImport.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Toggles the Platforms screen; refreshes store state on open.</summary>
    [RelayCommand]
    private async Task ToggleStoresAsync()
    {
        var open = !IsStoresVisible;
        ShowLibraryPane();
        IsStoresVisible = open;

        if (open)
        {
            await Stores.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Clears all screen flags, returning to the library.</summary>
    private void ShowLibraryPane()
    {
        IsMergeQueueVisible = false;
        IsStoresVisible = false;
        IsAppearanceVisible = false;
        IsAccountImportVisible = false;
        IsFeedVisible = false;

        // Leaving the feed also closes its history view.
        Feed.IsHistoryOpen = false;
    }
}
