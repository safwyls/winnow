using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.Services;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Covers;

namespace Hoard.App.ViewModels;

/// <summary>
/// The library view: rail buckets, command bar, cover grid, list view and the
/// game detail modal. Reads the database only (§5 — the UI never calls ingest
/// or enrichment); buckets come from the derived-bucket query, tile metadata
/// from an in-memory join over the repositories. Search, bucket filtering and
/// sorting are in-memory: the whole library is a few hundred kilobytes of
/// projection and re-querying SQLite per keystroke would buy nothing.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    /// <summary>Rail stub for the unrunnable bucket — no data source in M0, always zero.</summary>
    public const string WontRunKey = "wont_run";

    /// <summary>
    /// The rail's "All games" row. Not a bucket key — no tile is ever in it, and
    /// nothing queries it; it is the *absence* of a filter given a name, a count
    /// and a hit target. Selecting it clears <see cref="SelectedBucket"/>.
    /// </summary>
    public const string AllGamesKey = "all_games";

    private readonly ILibraryQueryRepository _libraryQueries;
    private readonly IOwnershipRepository _ownerships;
    private readonly IReleaseRepository _releases;
    private readonly IWorkRepository _works;
    private readonly IUpdateEventRepository _updateEvents;

    /// <summary>
    /// Cover art. Optional so the view still composes (on procedural art) when
    /// the host has not called <c>AddCoverCache</c> — DI fills the default.
    /// </summary>
    private readonly ICoverCache? _covers;

    private IReadOnlyList<GameTileViewModel> _allTiles = [];
    private bool _loaded;

    public LibraryViewModel(
        ILibraryQueryRepository libraryQueries,
        IOwnershipRepository ownerships,
        IReleaseRepository releases,
        IWorkRepository works,
        IUpdateEventRepository updateEvents,
        ICoverCache? covers = null,
        DormancyRamp? ramp = null)
    {
        _libraryQueries = libraryQueries;
        _ownerships = ownerships;
        _releases = releases;
        _works = works;
        _updateEvents = updateEvents;
        _covers = covers;

        Ramp = ramp ?? new DormancyRamp();

        // One subscription for the whole wall rather than one per tile: the
        // tiles are replaced wholesale on every reload, and 606 subscriptions to
        // a process-lifetime object would keep every superseded generation of
        // them alive. The library owns the tiles, so the library relays.
        Ramp.PropertyChanged += OnRampChanged;

        // §7 copy, exactly. Order matches the mock rail.
        Buckets =
        [
            new BucketViewModel(LibraryBuckets.StaleButPatched, "Patched since", showsFlarePip: true),
            new BucketViewModel(LibraryBuckets.NeverPlayed, "Never played"),
            new BucketViewModel(LibraryBuckets.Bounced, "Bounced off"),
            new BucketViewModel(LibraryBuckets.Retired, "Played out"),
            new BucketViewModel(WontRunKey, "Won't run"),
        ];

        // §4: the view mode is remembered per session — and so is the order, for
        // the same reason. Both live on this view model, which is a singleton
        // for the life of the process, so "per session" needs no storage.
        SortOptions =
        [
            new SortOptionViewModel(LibrarySort.DormantLongest, "Dormant longest"),
            new SortOptionViewModel(LibrarySort.RecentlyPlayed, "Recently played"),
            new SortOptionViewModel(LibrarySort.PlaytimeHighToLow, "Playtime high→low"),
            new SortOptionViewModel(LibrarySort.PlaytimeLowToHigh, "Playtime low→high"),
            new SortOptionViewModel(LibrarySort.NameAscending, "Name A–Z"),
            new SortOptionViewModel(LibrarySort.NameDescending, "Name Z–A"),
        ];

        MarkSelectedSortOption();
    }

    public IReadOnlyList<BucketViewModel> Buckets { get; }

    /// <summary>
    /// The rail's first row: the whole library, unfiltered. It exists because
    /// "no filter" was previously reachable only by clicking the bucket you were
    /// already on — a state the interface is in on every launch and had no way
    /// of naming. It is a <see cref="BucketViewModel"/> so the rail renders it
    /// through the one row template and it cannot drift from the bucket
    /// treatment, but it is deliberately NOT in <see cref="Buckets"/>: nothing
    /// counts it, filters by it, or labels a tile with it.
    /// </summary>
    public BucketViewModel AllGames { get; } = new(AllGamesKey, "All games") { IsSelected = true };

    /// <summary>
    /// Whether covers dim with age, and whether the hover restore animates.
    /// Owned here because the tiles resolve through it; the control that writes
    /// it is <see cref="DisplaySettingsViewModel"/>.
    /// </summary>
    public DormancyRamp Ramp { get; }

    /// <summary>The command bar's sort menu, and the labels the list headers share.</summary>
    public IReadOnlyList<SortOptionViewModel> SortOptions { get; }

    [ObservableProperty]
    public partial IReadOnlyList<GameTileViewModel> VisibleTiles { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BucketViewModel? SelectedBucket { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileHeight))]
    public partial double TileWidth { get; set; } = 148;

    /// <summary>2:3 portrait, always derived from the density slider's width.</summary>
    public double TileHeight => TileWidth * 1.5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGrid), nameof(ShowList))]
    public partial bool IsGridView { get; set; } = true;

    /// <summary>
    /// Current order (§4: remembered per session, like the view mode). Setting
    /// it re-sorts the visible set in place — the filter is unaffected, because
    /// sorting and filtering are independent axes and confusing them is how a
    /// sort control ends up silently hiding rows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(SortLabel),
        nameof(ShowTitleSortUp), nameof(ShowTitleSortDown),
        nameof(ShowPlaytimeSortUp), nameof(ShowPlaytimeSortDown),
        nameof(ShowIdleSortUp), nameof(ShowIdleSortDown))]
    public partial LibrarySort Sort { get; set; } = LibrarySort.DormantLongest;

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial string TotalCountText { get; set; } = "0";

    [ObservableProperty]
    public partial string SearchPlaceholder { get; set; } = "Search…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowGrid), nameof(ShowList))]
    public partial string? EmptyMessage { get; set; }

    [ObservableProperty]
    public partial GameTileViewModel? SelectedTile { get; set; }

    /// <summary>
    /// How many rows list view has selected. §6 asks for multi-select and the
    /// list gives it (shift/ctrl click, shift+arrows); the count is here so the
    /// selection is legible rather than an affordance with no readout. What it
    /// deliberately does NOT come with is bulk list assignment — lists are not
    /// built, and an action that silently does nothing is worse than no action.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiSelection), nameof(SelectedCountText))]
    public partial int SelectedCount { get; set; }

    public bool HasMultiSelection => SelectedCount > 1;

    public string SelectedCountText => $"{SelectedCount:N0} selected";

    /// <summary>
    /// The open detail modal, or null. §5.3 caps the tile at four facts, which
    /// is only a defensible cap because this exists.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsOpen))]
    public partial GameDetailsViewModel? Details { get; set; }

    public bool IsDetailsOpen => Details is not null;

    public bool ShowEmpty => EmptyMessage is not null;

    public bool ShowGrid => EmptyMessage is null && IsGridView;

    public bool ShowList => EmptyMessage is null && !IsGridView;

    /// <summary>Command-bar button face: the order currently in force.</summary>
    public string SortLabel => LabelFor(Sort);

    public bool ShowTitleSortUp => Sort == LibrarySort.NameAscending;

    public bool ShowTitleSortDown => Sort == LibrarySort.NameDescending;

    public bool ShowPlaytimeSortUp => Sort == LibrarySort.PlaytimeLowToHigh;

    public bool ShowPlaytimeSortDown => Sort == LibrarySort.PlaytimeHighToLow;

    /// <summary>Least idle first — i.e. recently played at the top.</summary>
    public bool ShowIdleSortUp => Sort == LibrarySort.RecentlyPlayed;

    public bool ShowIdleSortDown => Sort == LibrarySort.DormantLongest;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var bucketRows = await _libraryQueries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        var ownerships = await _ownerships.GetAllAsync();
        var works = await _works.GetAllAsync();

        // release id → its work, and release id → the provider id its cover art
        // is fetched under. Small library, per-work fetch is fine.
        var workByRelease = new Dictionary<long, Work>();
        var coverKeyByRelease = new Dictionary<long, CoverKey>();
        foreach (var work in works)
        {
            foreach (var release in await _releases.GetByWorkAsync(work.Id))
            {
                workByRelease[release.Id] = work;

                // Steam's portrait capsule is the first source; the cover cache
                // tries any further registered source for the same key, so IGDB
                // art can fill the gaps without this view model changing.
                var externalIds = await _releases.GetExternalIdsAsync(release.Id);
                var steam = externalIds.FirstOrDefault(x => x.Provider == ExternalIdProviders.Steam);
                if (steam is not null)
                {
                    coverKeyByRelease[release.Id] = CoverKey.Steam(steam.ProviderId);
                }
            }
        }

        var ownershipById = ownerships.ToDictionary(o => o.Id);
        var now = DateTime.UtcNow;

        var tiles = new List<GameTileViewModel>(bucketRows.Count);
        foreach (var row in bucketRows)
        {
            var work = workByRelease.GetValueOrDefault(row.ReleaseId);
            var ownership = ownershipById.GetValueOrDefault(row.OwnershipId);

            tiles.Add(new GameTileViewModel(
                ownershipId: row.OwnershipId,
                releaseId: row.ReleaseId,
                title: work?.Name ?? $"Release {row.ReleaseId}",
                store: ownership?.Store ?? "?",
                bucket: row.Bucket,
                playtimeMinutes: row.PlaytimeMinutes,
                lastPlayedUtc: row.LastPlayedAt,
                nowUtc: now,
                // The unread badge and the "Patched since" bucket count the
                // same fact (§5.2): an update landed after the last session.
                // The derived-bucket query already carries it, so the tile
                // badge is that bucket membership — nothing else earns Flare.
                hasUnread: row.Bucket == LibraryBuckets.StaleButPatched,
                coverKey: coverKeyByRelease.TryGetValue(row.ReleaseId, out var coverKey) ? coverKey : null,
                covers: _covers,
                work: work,
                ownership: ownership,
                ramp: Ramp));
        }

        _allTiles = tiles;

        foreach (var bucket in Buckets)
        {
            bucket.Count = bucketRows.Count(r => r.Bucket == bucket.Key);
        }

        // The rail's own total, on the row that selects it. This is the only
        // place the rail states the library size — the 22px "606 TITLES" header
        // that used to sit two rows above it said the same number with no
        // affordance attached, which is the duplication §7 warns about.
        AllGames.Count = _allTiles.Count;

        TotalCount = _allTiles.Count;
        TotalCountText = TotalCount.ToString("N0");
        SearchPlaceholder = $"Search {TotalCountText} titles…";
        _loaded = true;
        ApplyFilter();
    }

    [RelayCommand]
    private void ShowGridView() => IsGridView = true;

    [RelayCommand]
    private void ShowListView() => IsGridView = false;

    /// <summary>Command-bar sort menu.</summary>
    [RelayCommand]
    private void SelectSort(SortOptionViewModel? option)
    {
        if (option is not null)
        {
            Sort = option.Sort;
        }
    }

    /// <summary>
    /// List-view column headers. A header that is already the active sort
    /// flips direction; one that is not takes its own most useful direction
    /// first — nobody opens a playtime column wanting the smallest number.
    /// </summary>
    [RelayCommand]
    private void SortByTitle()
        => Sort = Sort == LibrarySort.NameAscending
            ? LibrarySort.NameDescending
            : LibrarySort.NameAscending;

    [RelayCommand]
    private void SortByPlaytime()
        => Sort = Sort == LibrarySort.PlaytimeHighToLow
            ? LibrarySort.PlaytimeLowToHigh
            : LibrarySort.PlaytimeHighToLow;

    [RelayCommand]
    private void SortByIdle()
        => Sort = Sort == LibrarySort.DormantLongest
            ? LibrarySort.RecentlyPlayed
            : LibrarySort.DormantLongest;

    /// <summary>
    /// Rail click. "All games" clears the filter; a bucket selects itself, or
    /// clears the filter when it was already the selection — the old, invisible
    /// escape hatch, kept because it costs nothing now that there is a visible
    /// one, and because a selected row that ignores its own click reads as
    /// broken.
    /// </summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket)
        => SelectedBucket = bucket is null
            || bucket.Key == AllGamesKey
            || ReferenceEquals(SelectedBucket, bucket)
                ? null
                : bucket;

    /// <summary>
    /// Opens the detail modal for a tile. The update events are the only thing
    /// it has to go to the database for — everything else was already joined at
    /// load — so the modal appears immediately and the update list fills in.
    /// </summary>
    [RelayCommand]
    private async Task OpenDetailsAsync(GameTileViewModel? tile)
    {
        var target = tile ?? SelectedTile;
        if (target is null)
        {
            return;
        }

        SelectTile(target);

        var events = await _updateEvents.GetByReleaseAsync(target.ReleaseId);

        // Newest first: the update the user missed most recently is the one
        // they are trying to catch up on (§5.2).
        var updates = events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(UpdateEventViewModel.Create)
            .ToList();

        Details = new GameDetailsViewModel(
            target,
            BucketLabelFor(target.Bucket),
            updates,
            publisher: target.Publisher,
            covers: _covers);
    }

    [RelayCommand]
    private void CloseDetails() => Details = null;

    public void SelectTile(GameTileViewModel? tile)
    {
        if (ReferenceEquals(SelectedTile, tile))
        {
            return;
        }

        SelectedTile = tile;
    }

    /// <summary>
    /// Keyboard navigation: moves selection by <paramref name="delta"/> visible
    /// tiles (±1 = left/right or list row, ±columns = up/down in the grid).
    /// Returns the new selected index, or -1 when the set is empty.
    /// </summary>
    public int MoveSelection(int delta)
    {
        if (VisibleTiles.Count == 0)
        {
            return -1;
        }

        var current = SelectedTile is null ? -1 : IndexOf(SelectedTile);
        var next = current < 0
            ? 0
            : Math.Clamp(current + delta, 0, VisibleTiles.Count - 1);
        SelectTile(VisibleTiles[next]);
        return next;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSortChanged(LibrarySort value)
    {
        MarkSelectedSortOption();
        ApplyFilter();
    }

    partial void OnSelectedTileChanged(GameTileViewModel? oldValue, GameTileViewModel? newValue)
    {
        // Selection lives on the tiles because both views bind to it; keeping
        // the flags in one place stops the grid and the list disagreeing about
        // what is selected.
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnSelectedBucketChanged(BucketViewModel? value)
    {
        foreach (var bucket in Buckets)
        {
            bucket.IsSelected = ReferenceEquals(bucket, value);
        }

        // No bucket filter IS the "All games" state — including the one the app
        // launches in, so the rail is never showing a library nothing in it
        // claims. Exactly one row in the rail carries the Volt edge, always.
        AllGames.IsSelected = value is null;

        ApplyFilter();
    }

    /// <summary>
    /// The dimming preference moved. Every built tile re-reads the ramp; nothing
    /// is rebuilt and nothing is re-fetched, so the floor variants the cover
    /// cache holds stay exactly as they were.
    /// </summary>
    private void OnRampChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var tile in _allTiles)
        {
            tile.RefreshDormancy();
        }
    }

    private void ApplyFilter()
    {
        if (!_loaded)
        {
            return;
        }

        IEnumerable<GameTileViewModel> query = _allTiles;
        if (SelectedBucket is { } bucket)
        {
            query = query.Where(t => t.Bucket == bucket.Key);
        }

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(t => t.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var visible = Order(query).ToList();
        VisibleTiles = visible;

        if (SelectedTile is { } selected && !visible.Contains(selected))
        {
            SelectTile(null);
        }

        SelectedCount = SelectedTile is null ? 0 : 1;
        EmptyMessage = BuildEmptyMessage(visible.Count, search);
    }

    /// <summary>
    /// Every order ties-breaks on title, so a re-sort of the same set is
    /// stable and the grid does not shuffle underneath a user who changed
    /// nothing but the window width.
    /// </summary>
    private IEnumerable<GameTileViewModel> Order(IEnumerable<GameTileViewModel> tiles) => Sort switch
    {
        // Never played counts as maximally dormant — DateTime.MinValue sorts it
        // to the front here and to the back under RecentlyPlayed, which is what
        // "you have never opened this" should do in both directions.
        LibrarySort.DormantLongest => tiles
            .OrderBy(t => t.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),

        LibrarySort.RecentlyPlayed => tiles
            .OrderByDescending(t => t.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),

        LibrarySort.PlaytimeHighToLow => tiles
            .OrderByDescending(t => t.PlaytimeMinutes)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),

        LibrarySort.PlaytimeLowToHigh => tiles
            .OrderBy(t => t.PlaytimeMinutes)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),

        LibrarySort.NameDescending => tiles
            .OrderByDescending(t => t.Title, StringComparer.OrdinalIgnoreCase),

        _ => tiles.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
    };

    private void MarkSelectedSortOption()
    {
        foreach (var option in SortOptions)
        {
            option.IsSelected = option.Sort == Sort;
        }
    }

    private string LabelFor(LibrarySort sort)
    {
        foreach (var option in SortOptions)
        {
            if (option.Sort == sort)
            {
                return option.Label;
            }
        }

        return string.Empty;
    }

    /// <summary>§7's bucket names, used by the detail view. Never a raw key.</summary>
    private string BucketLabelFor(string bucketKey)
    {
        foreach (var bucket in Buckets)
        {
            if (bucket.Key == bucketKey)
            {
                return bucket.Name;
            }
        }

        // LibraryBuckets.Active has no rail row: it is the healthy middle of the
        // library, which the rail deliberately does not offer as a pile.
        return bucketKey == LibraryBuckets.Active ? "In rotation" : bucketKey;
    }

    // §7: empty states are directions, not moods.
    private string? BuildEmptyMessage(int visibleCount, string search)
    {
        if (_allTiles.Count == 0)
        {
            return "Reading your Steam library. Covers and metadata fill in over the next few minutes — you can browse now.";
        }

        if (visibleCount > 0)
        {
            return null;
        }

        if (search.Length > 0)
        {
            return $"No titles match “{search}”.";
        }

        return SelectedBucket?.Key switch
        {
            LibraryBuckets.StaleButPatched =>
                "Nothing's been patched since you last played. This fills up on its own.",
            LibraryBuckets.NeverPlayed =>
                "You've played everything you own past the refund window. Genuinely rare.",
            _ => "Nothing here yet.",
        };
    }

    private int IndexOf(GameTileViewModel tile)
    {
        for (var i = 0; i < VisibleTiles.Count; i++)
        {
            if (ReferenceEquals(VisibleTiles[i], tile))
            {
                return i;
            }
        }

        return -1;
    }
}
