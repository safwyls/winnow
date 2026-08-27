using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.ViewModels.Filters;
using Hoard.App.ViewModels.Lists;
using Hoard.Core.Repositories;

namespace Hoard.App.ViewModels;

/// <summary>
/// Window shell: hosts the library view, the merge confirm queue and the Stores
/// panel, and owns which of the three the rail is currently pointing at.
///
/// <para><b>Exactly one screen is up at a time and the rail is the only thing
/// that switches them.</b> The two non-library screens are mutually exclusive
/// booleans rather than an enum only because each has a rail row bound directly
/// to its own flag; every path that shows one clears the other, and every path
/// back to the library clears both. The rule that keeps this honest is
/// §12.2's: the rail never leaves the user on a screen their click did not
/// describe.</para>
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
        ISettingsRepository? settings = null)
    {
        Library = library;
        MergeQueue = mergeQueue;
        Stores = stores;
        Appearance = appearance;

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

    /// <summary>
    /// M4.6. Required rather than optional, deliberately: an unregistered Stores
    /// panel would be a rail row that opens an empty pane, which is the failure
    /// mode this codebase keeps hitting — build green, tests green, feature
    /// absent. Missing here, it throws at startup where somebody sees it.
    /// </summary>
    public StoresViewModel Stores { get; }

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

    public bool IsLibraryVisible => !IsMergeQueueVisible && !IsStoresVisible && !IsAppearanceVisible;

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
    }
}
