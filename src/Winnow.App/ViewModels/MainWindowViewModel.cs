using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.ViewModels.Filters;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>
/// Window shell: hosts the Feed, library, merge queue, STATS and the settings
/// surface (Platforms, Appearance). The rail navigates between the first four;
/// the gear at its foot opens the settings surface. One screen at a time.
///
/// <para>The settings surface had a third section, Purchases, until TASK-59
/// folded it into the Steam card on Platforms. This type no longer knows the
/// import screen exists; <see cref="StoresViewModel"/> owns it now and
/// refreshes it when the Platforms section opens.</para>
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
        AccountStatsViewModel accountStats,
        ISettingsRepository? settings = null,
        Services.SessionJournalService? journal = null)
    {
        Library = library;
        MergeQueue = mergeQueue;
        Stores = stores;
        Appearance = appearance;
        Feed = feed;
        AccountStats = accountStats;

        // Floating layout is structural (margins, radii, borders), not colour.
        appearance.Service.Applied += (_, _) => OnPropertyChanged(nameof(IsFloatingLayout));

        // Filter panel visibility depends on both library and filter state.
        Library.Filters.PropertyChanged += OnFiltersChanged;

        // The rail's Volt edge marks one location (§12.2), so the library's
        // bucket and ALL GAMES rows, and an open list's row, must drop the
        // mark while another screen is up. Seeded here because the window
        // opens on the Feed; kept in sync by OnPropertyChanged below.
        Library.IsCurrentScreen = IsLibraryVisible;
        Library.Lists.IsCurrentScreen = IsLibraryVisible;

        // The account-visibility toggle changes which rows the bucket query
        // returns, so the library and the feed both hold stale answers until
        // they ask again. Wired here because this is the only type holding the
        // Stores panel and the two screens the change shows up on.
        stores.ReloadLibrary = async () =>
        {
            await library.LoadCommand.ExecuteAsync(null);
            await feed.LoadCommand.ExecuteAsync(null);
        };

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

    /// <summary>
    /// The section the gear reopens on. Platforms the first time; after that,
    /// whichever section was showing when the user left. Without this the
    /// gear would always land on Platforms, and switching between the library
    /// and Appearance would cost two clicks instead of one.
    /// </summary>
    private SettingsSection _settingsSection = SettingsSection.Stores;

    private enum SettingsSection
    {
        Stores,
        Appearance,
    }

    public LibraryViewModel Library { get; }

    public MergeQueueViewModel MergeQueue { get; }

    public StoresViewModel Stores { get; }

    public FeedViewModel Feed { get; }

    /// <summary>The command bar's Display popover — §8's dimming preference.</summary>
    public DisplaySettingsViewModel Display { get; }

    public AppearanceViewModel Appearance { get; }

    /// <summary>
    /// The STATS screen, what the imported account pages add up to. A reading
    /// of the user's account rather than a cut of their library, which is why
    /// its rail row sits in its own ACCOUNT section below the buckets and not
    /// behind the gear beside the import screen that feeds it.
    /// </summary>
    public AccountStatsViewModel AccountStats { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsMergeQueueVisible { get; set; }

    /// <summary>The Platforms panel, the settings surface's first section.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsLibraryVisible), nameof(IsFilterPanelVisible), nameof(IsSettingsVisible))]
    public partial bool IsStoresVisible { get; set; }

    /// <summary>The Appearance screen, the settings surface's second section.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsLibraryVisible), nameof(IsFilterPanelVisible), nameof(IsSettingsVisible))]
    public partial bool IsAppearanceVisible { get; set; }

    /// <summary>The STATS screen, opened from the rail's ACCOUNT › STATS row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsAccountStatsVisible { get; set; }

    /// <summary>The Feed — the default landing screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsFeedVisible { get; set; } = true;

    /// <summary>Whether panes float as rounded cards (§15).</summary>
    public bool IsFloatingLayout => Appearance.Service.IsFloating;

    /// <summary>
    /// True while either settings section is up. The XAML binds the settings
    /// surface's visibility to this, and the gear's lit state to it.
    /// </summary>
    public bool IsSettingsVisible => IsStoresVisible || IsAppearanceVisible;

    public bool IsLibraryVisible =>
        !IsMergeQueueVisible && !IsStoresVisible && !IsAppearanceVisible
        && !IsAccountStatsVisible && !IsFeedVisible;

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

    /// <summary>
    /// Navigates to the Feed. Not a toggle: the rail's Volt edge marks where
    /// you are, and clicking the row you are on keeps you there. Scoring ran
    /// at startup; arriving here shows whatever the last pass produced.
    /// </summary>
    [RelayCommand]
    private void ShowFeed()
    {
        ShowLibraryPane();
        IsFeedVisible = true;
    }

    /// <summary>The rail row toggles, so the same click that opened the queue closes it.</summary>
    [RelayCommand]
    private void ToggleMergeQueue()
    {
        var open = !IsMergeQueueVisible;
        ShowLibraryPane();
        IsMergeQueueVisible = open;

        // The rail row carries the pending count, so it means "review these".
        // Landing on REVIEW even if HISTORY was the last segment open is
        // deliberate. Contrast with the settings surface, which reopens on its
        // last section: the gear carries no count and no claim about what is
        // waiting.
        if (open)
        {
            MergeQueue.IsHistoryVisible = false;
        }
    }

    /// <summary>
    /// The gear at the foot of the rail. Opens the settings surface on
    /// whichever section was showing last, Platforms the first time. Each
    /// section's own command writes <see cref="_settingsSection"/> on the
    /// way in, so the gear remembers without a separate "last visited" flag.
    /// </summary>
    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        switch (_settingsSection)
        {
            case SettingsSection.Appearance:
                ShowAppearance();
                break;

            default:
                await ShowStoresAsync();
                break;
        }
    }

    /// <summary>
    /// The settings surface's Appearance section. Synchronous: the theme
    /// service's state is already in memory and does not need a refresh pass.
    /// </summary>
    [RelayCommand]
    private void ShowAppearance()
    {
        _settingsSection = SettingsSection.Appearance;
        ShowLibraryPane();
        IsAppearanceVisible = true;
    }

    /// <summary>
    /// Toggles the STATS screen; recomputes the figures on open. Every stat is
    /// a query rather than a stored aggregate, and the import screen can change
    /// the answer between two opens, so a cached view would be a stale one.
    /// Awaited rather than fired and forgotten: the refresh is the screen.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAccountStatsAsync()
    {
        var open = !IsAccountStatsVisible;
        ShowLibraryPane();
        IsAccountStatsVisible = open;

        if (open)
        {
            await AccountStats.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// The settings surface's Platforms section. Refreshes store connection
    /// state on arrival so the screen opens on the truth rather than a
    /// stale cache.
    ///
    /// <para>The refresh also checks whether the embedded browser can run here,
    /// for the purchase import folded into the Steam card. The check opens no
    /// window and does no IO; arriving on this screen must never start either
    /// import route.</para>
    /// </summary>
    [RelayCommand]
    private async Task ShowStoresAsync()
    {
        _settingsSection = SettingsSection.Stores;
        ShowLibraryPane();
        IsStoresVisible = true;

        await Stores.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>Clears all screen flags, returning to the library.</summary>
    private void ShowLibraryPane()
    {
        IsMergeQueueVisible = false;
        IsStoresVisible = false;
        IsAppearanceVisible = false;
        IsAccountStatsVisible = false;
        IsFeedVisible = false;

        // Leaving the feed also closes its history view.
        Feed.IsHistoryOpen = false;
    }

    /// <summary>
    /// Keeps the library's rail marks in step with screen visibility.
    /// <see cref="LibraryViewModel.IsCurrentScreen"/> gates whether
    /// <c>MarkRailSelection</c> draws the Volt edge on any row, and
    /// <see cref="ListsViewModel.IsCurrentScreen"/> gates the same mark on
    /// an open list's row.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(IsLibraryVisible))
        {
            Library.IsCurrentScreen = IsLibraryVisible;
            Library.Lists.IsCurrentScreen = IsLibraryVisible;
        }
    }
}
