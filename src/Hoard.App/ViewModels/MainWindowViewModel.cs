using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.ViewModels.Filters;
using Hoard.App.ViewModels.Lists;
using Hoard.Core.Repositories;

namespace Hoard.App.ViewModels;

/// <summary>
/// Window shell: hosts the Feed, the library view, the merge confirm queue and
/// the Stores and Appearance screens, and owns which of them the rail is
/// currently pointing at.
///
/// <para><b>Exactly one screen is up at a time and the rail is the only thing
/// that switches them.</b> The non-library screens are mutually exclusive
/// booleans rather than an enum only because each has a rail row bound directly
/// to its own flag; every path that shows one clears the others, and every path
/// back to the library clears them all. The rule that keeps this honest is
/// §12.2's: the rail never leaves the user on a screen their click did not
/// describe.</para>
///
/// <para><b>The window opens on the Feed (M8), and that costs the library
/// nothing.</b> The feed is a peer screen rather than a mode of the library, so
/// ALL GAMES, every bucket and every list still reach the wall in one click —
/// each of those paths already went through <c>ShowLibraryPane</c>, which is now
/// also what closes the feed.</para>
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
        StoresViewModel stores,
        AppearanceViewModel appearance,
        FeedViewModel feed,
        ISettingsRepository? settings = null,
        Services.SessionJournalService? journal = null)
    {
        Library = library;
        MergeQueue = mergeQueue;
        Stores = stores;
        Appearance = appearance;
        Feed = feed;

        // The one piece of appearance state the SHELL has to read rather than
        // the palette: the floating layout moves margins, corner radii and
        // borders, which are structure and not colour, so a repainted brush
        // cannot deliver them. One style class on the window carries all of it
        // (§15), and this is what drives it.
        appearance.Service.Applied += (_, _) => OnPropertyChanged(nameof(IsFloatingLayout));

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
            journal: journal,
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

    /// <summary>
    /// M4.6. Required rather than optional, deliberately: an unregistered Stores
    /// panel would be a rail row that opens an empty pane, which is the failure
    /// mode this codebase keeps hitting — build green, tests green, feature
    /// absent. Missing here, it throws at startup where somebody sees it.
    /// </summary>
    public StoresViewModel Stores { get; }

    /// <summary>
    /// M8, and the screen the app opens on. Required rather than optional for
    /// the reason <see cref="Stores"/> is: a rail row that opens an empty pane
    /// is the failure mode this codebase keeps hitting — build green, tests
    /// green, feature absent. Missing here, it throws at startup where somebody
    /// sees it.
    /// </summary>
    public FeedViewModel Feed { get; }

    /// <summary>The command bar's Display popover — §8's dimming preference.</summary>
    public DisplaySettingsViewModel Display { get; }

    /// <summary>
    /// The rail's SETTINGS › APPEARANCE row. Required rather than optional for
    /// the reason <see cref="Stores"/> is: a rail row that opens an empty pane
    /// is the failure mode this codebase keeps hitting — build green, tests
    /// green, feature absent.
    /// </summary>
    public AppearanceViewModel Appearance { get; }

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

    /// <summary>
    /// The Feed, and the state the window opens in (ROADMAP: "recommender
    /// surfaced as the app's primary view"). It is a peer of the other three
    /// screens rather than a mode of the library, which is what keeps ALL GAMES
    /// exactly one rail click away — landing on the feed must never be a trap
    /// for somebody who came to browse.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryVisible), nameof(IsFilterPanelVisible))]
    public partial bool IsFeedVisible { get; set; } = true;

    /// <summary>
    /// Whether the content panes float as rounded cards on the window's ground
    /// (§15). Read by one style class on the window; everything downstream of it
    /// is a style selector rather than a binding, so the flush layout costs
    /// nothing at runtime and nothing in the markup it did not already cost.
    /// </summary>
    public bool IsFloatingLayout => Appearance.Service.IsFloating;

    public bool IsLibraryVisible =>
        !IsMergeQueueVisible && !IsStoresVisible && !IsAppearanceVisible && !IsFeedVisible;

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

    /// <summary>
    /// Rail bucket click. Selecting a bucket is a statement about the library,
    /// so it also brings the library back — the rail never leaves the user on a
    /// screen their click did not describe.
    /// </summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket)
    {
        ShowLibraryPane();
        Library.SelectBucketCommand.Execute(bucket);
    }

    [RelayCommand]
    private void ShowLibrary() => ShowLibraryPane();

    /// <summary>
    /// The rail's FEED row. Toggles like every other screen row, so the same
    /// click that opened it gives the library back — and unlike the others it is
    /// where the window starts.
    ///
    /// <para>Nothing is loaded here. The scoring pass costs ~500ms over a
    /// thousand games and must not ride on a click any more than on startup
    /// (§5.1 pitfall 3): the feed is computed once after the library loads, and
    /// re-entering the screen shows what it already worked out.</para>
    /// </summary>
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

    /// <summary>
    /// The rail's APPEARANCE row, under SETTINGS. Toggles like the others, so
    /// the same click that opened the screen closes it and gives the library
    /// back. Nothing is read on the way in: the theme service already holds
    /// both preferences, and the screen is a view of them.
    /// </summary>
    [RelayCommand]
    private void ToggleAppearance()
    {
        var open = !IsAppearanceVisible;
        ShowLibraryPane();
        IsAppearanceVisible = open;
    }

    /// <summary>
    /// The rail's STORES row, under SETTINGS. Toggles like the queue's, so the same click that
    /// opened the panel closes it and gives the library back.
    ///
    /// <para><b>Async because opening is when the panel reads its state.</b>
    /// Whether Steam has a key and whether the Epic session is still live are
    /// both things that can change while the app is running — a sign-in
    /// completes, a refresh token lapses — and neither raises an event this
    /// shell could listen for. Reading on open costs one settings row and one
    /// DPAPI unprotect, and it means the panel is never showing a state the user
    /// left behind. Nothing here touches the network (§5.1).</para>
    /// </summary>
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

    /// <summary>
    /// Back to the library, from wherever. One method so a screen added later
    /// cannot be left up by a path that only remembered to clear the other one.
    /// </summary>
    private void ShowLibraryPane()
    {
        IsMergeQueueVisible = false;
        IsStoresVisible = false;
        IsAppearanceVisible = false;
        IsFeedVisible = false;

        // The Feed's inspection surface is a state of that screen, so leaving
        // the screen leaves it. It also means the rail's FEED row always lands
        // on the shelves: this runs on the way in as well as on the way out, and
        // a landing state that is sometimes a list of past dismissals is not a
        // landing state.
        Feed.IsHistoryOpen = false;
    }
}
