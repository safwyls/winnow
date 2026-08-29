using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.App.ViewModels.Filters;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.ViewModels;

/// <summary>
/// The library view: rail buckets, command bar, cover grid, list view and the
/// game detail modal. Reads the database only (§5 — the UI never calls ingest
/// or enrichment); buckets come from the derived-bucket query, tile metadata
/// from an in-memory join over the repositories. Search, bucket filtering and
/// sorting are in-memory: the whole library is a few hundred kilobytes of
/// projection and re-querying SQLite per keystroke would buy nothing.
/// </summary>
public partial class LibraryViewModel : ObservableObject, IStoreTitleCounts, IGameTileSource
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
    /// §1's longitudinal playtime series, read only when a detail panel opens.
    /// Optional so the view model still composes in tests and for any host that
    /// has not registered it; the detail view then simply states no record line.
    /// </summary>
    private readonly IPlaytimeSnapshotRepository? _snapshots;

    /// <summary>
    /// Cover art. Optional so the view still composes (on procedural art) when
    /// the host has not called <c>AddCoverCache</c> — DI fills the default.
    /// </summary>
    private readonly ICoverCache? _covers;

    /// <summary>
    /// What a tile hands to any surface that wants to show its art — each
    /// surface acquires its own lease from this pool. Optional for the same
    /// reason the cache is: an unregistered one costs the art, not the window.
    /// </summary>
    private readonly ICoverLeases? _leases;

    /// <summary>
    /// Genre / theme / store tag / game mode (migration 0007). Optional: with
    /// nothing registered the panel still cuts on store, on-disk and release
    /// year, and simply does not draw the groups that need enriched metadata.
    /// </summary>
    private readonly IFacetRepository? _facetRepository;

    /// <summary>
    /// Epic's composite launch keys, recovered from the catalog answers this app
    /// cached (see <see cref="Services.IEpicLaunchKeys"/>). Optional: with
    /// nothing registered every Epic tile simply has no launch target, which
    /// renders as no Play button rather than a broken one.
    /// </summary>
    private readonly Services.IEpicLaunchKeys? _epicLaunchKeys;

    /// <summary>
    /// M3b. Optional, like every other seam on this view model: with nothing
    /// registered the Play button is inert rather than absent, and the library
    /// still loads.
    /// </summary>
    private readonly Services.GameLaunchService? _launcher;

    private IReadOnlyList<GameTileViewModel> _allTiles = [];
    private FacetSnapshot _facets = FacetSnapshot.Empty;
    private bool _loaded;

    /// <summary>
    /// Depth of <see cref="Batched"/>. Entering and leaving a list writes the
    /// rail, the search box and every group in the panel in one act; each of
    /// those writes would otherwise rebuild the whole grid against a rule set
    /// that is half torn down and briefly describes a library the user never
    /// asked for.
    /// </summary>
    private int _suspended;

    public LibraryViewModel(
        ILibraryQueryRepository libraryQueries,
        IOwnershipRepository ownerships,
        IReleaseRepository releases,
        IWorkRepository works,
        IUpdateEventRepository updateEvents,
        ICoverCache? covers = null,
        DormancyRamp? ramp = null,
        IPlaytimeSnapshotRepository? snapshots = null,
        IFacetRepository? facets = null,
        IGameListRepository? lists = null,
        Services.IEpicLaunchKeys? epicLaunchKeys = null,
        Services.GameLaunchService? launcher = null,
        LaunchStatusViewModel? launchStatus = null,
        JournalPromptViewModel? journal = null,
        ICoverLeases? leases = null)
    {
        _libraryQueries = libraryQueries;
        _ownerships = ownerships;
        _releases = releases;
        _works = works;
        _updateEvents = updateEvents;
        _covers = covers;
        _leases = leases;
        _snapshots = snapshots;
        _facetRepository = facets;
        _epicLaunchKeys = epicLaunchKeys;
        _launcher = launcher;

        // Both optional for the reason every seam here is: an unregistered one
        // costs the feature and not the window. With no launcher the Play button
        // is a command that does nothing rather than a crash, and with no status
        // strip the launch still happens and is still attributed — the strip is
        // the acknowledgement, not the mechanism.
        LaunchStatus = launchStatus ?? new LaunchStatusViewModel();
        Journal = journal ?? new JournalPromptViewModel();

        // The prompt names the game; only the loaded library can. Assigned here
        // rather than injected because the dependency runs this way round — see
        // JournalPromptViewModel.TitleFor.
        Journal.TitleFor = TitleForOwnership;

        Filters = new FilterPanelViewModel(ApplyFilter);
        Lists = new ListsViewModel(lists);

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

    /// <summary>The order in force before a manual list was opened, to restore on the way out.</summary>
    private LibrarySort _sortBeforeList = LibrarySort.DormantLongest;

    /// <summary>The "List order" row, added to the menu only while one is open.</summary>
    private readonly SortOptionViewModel _listOrderOption =
        new(LibrarySort.ListOrder, "List order");

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
    /// The filter panel — every axis the rail does not carry. It sits beside the
    /// rail rather than repeating it: the rail's bucket IS the filter's bucket
    /// dimension, and two controls writing one axis is how a panel starts
    /// disagreeing with the screen behind it. See the panel's own remarks.
    /// </summary>
    public FilterPanelViewModel Filters { get; }

    /// <summary>The rail's LISTS and LIVE LISTS sections.</summary>
    public ListsViewModel Lists { get; }

    /// <summary>
    /// M3b's ambient launch strip. Lives on the library rather than the window
    /// because the library is what raises the launch; the shell just gives it a
    /// corner to sit in.
    /// </summary>
    public LaunchStatusViewModel LaunchStatus { get; }

    /// <summary>§5.2's journal prompt (opt-in, §9 pitfall 7).</summary>
    public JournalPromptViewModel Journal { get; }

    /// <summary>
    /// M3b: Play / Install. Hands the URI to the shell and returns immediately;
    /// the session strip resolves later off the watcher's own signal.
    /// </summary>
    [RelayCommand]
    private async Task LaunchAsync(GameTileViewModel? tile)
    {
        if (tile?.PrimaryAction is not { } action)
        {
            return;
        }

        if (_launcher is null)
        {
            return;
        }

        var outcome = await _launcher.LaunchAsync(tile.OwnershipId, action);

        // Only a Play is worth acknowledging. An Install hands off to a download
        // the store will show its own progress for, over minutes or hours; a
        // Winnow strip alongside it would be a second, worse progress indicator
        // for something Winnow cannot see.
        if (!action.StartsGame)
        {
            return;
        }

        switch (outcome)
        {
            case Services.LaunchDispatch.HandedOff:
                LaunchStatus.Waiting(tile.OwnershipId, tile.Title);
                break;

            case Services.LaunchDispatch.Refused:
                LaunchStatus.Refused(tile.Title, StoreName(tile.Store));
                break;

            case Services.LaunchDispatch.AlreadyRunning:
                // The first click's strip is still up and still correct.
                break;
        }
    }

    /// <summary>
    /// The store, as a person would say it. Only ever reaches the user inside
    /// the one message that names a store, so it is deliberately the client's
    /// name rather than the storefront's — the thing that did not answer is the
    /// application, not the shop.
    /// </summary>
    private static string StoreName(string store) => store switch
    {
        ExternalIdProviders.Steam => "Steam",
        ExternalIdProviders.Epic => "the Epic Games Launcher",
        ExternalIdProviders.Gog => "GOG Galaxy",
        _ => "the store",
    };

    /// <summary>
    /// The name of the game behind an ownership, from the tiles already loaded.
    /// Null when the library does not hold it — see
    /// <see cref="JournalPromptViewModel.TitleFor"/> for why that is a reason to
    /// stay quiet rather than to go and ask the database.
    /// </summary>
    private string? TitleForOwnership(long ownershipId) => TileForOwnership(ownershipId)?.Title;

    /// <inheritdoc/>
    public event EventHandler? TilesChanged;

    /// <inheritdoc/>
    public bool HasTiles => _allTiles.Count > 0;

    /// <summary>
    /// The tile for an ownership, out of the set this view model has already
    /// built. <see cref="IGameTileSource"/> is why it is public: the Feed renders
    /// the library's own tiles rather than assembling a second projection of the
    /// same games, so the two screens cannot disagree about a cover, an install
    /// state or a launch route.
    ///
    /// <para>A linear walk over the whole library rather than a dictionary, and
    /// deliberately: the feed asks fewer than fifty times per load, the journal
    /// prompt once per session, and an index would be a second structure to keep
    /// in step with <c>_allTiles</c> across every reload for no measurable
    /// gain.</para>
    /// </summary>
    public GameTileViewModel? TileForOwnership(long ownershipId)
    {
        foreach (var tile in _allTiles)
        {
            if (tile.OwnershipId == ownershipId)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>
    /// The tile for a release. The Feed's inspection surface starts from stored
    /// verdict rows, which are keyed by release rather than by ownership (§6b),
    /// and this is how it puts a title and a cover against one.
    ///
    /// <para>The same linear walk as above, and for the same reason: it is asked
    /// once per row of a list that holds one entry per game the user has ever
    /// dismissed. <b>First match wins</b> — a release owned on two stores is two
    /// tiles, and either of them names the same game, which is the only thing
    /// this lookup is for.</para>
    /// </summary>
    public GameTileViewModel? TileForRelease(long releaseId)
    {
        foreach (var tile in _allTiles)
        {
            if (tile.ReleaseId == releaseId)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether covers dim with age, and whether the hover restore animates.
    /// Owned here because the tiles resolve through it; the control that writes
    /// it is <see cref="DisplaySettingsViewModel"/>.
    /// </summary>
    public DormancyRamp Ramp { get; }

    /// <summary>
    /// Whether tools, dedicated servers, soundtracks and videos appear in the
    /// library. Read by <see cref="LoadAsync"/> rather than filtering afterwards,
    /// because the rail's counts are computed from the rows the query returns —
    /// filtering anywhere else would let the counts and the grid disagree.
    /// Written by <see cref="DisplaySettingsViewModel"/>, which reloads on change.
    /// </summary>
    public bool ShowNonGameEntries { get; set; }

    /// <summary>
    /// The command bar's sort menu, and the labels the list headers share.
    /// Mutable because one order is conditional: <c>List order</c> exists only
    /// while a hand-built list is open, since it is the only context in which
    /// the stored positions mean anything.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<SortOptionViewModel> SortOptions { get; }

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
    /// Everything currently picked, in both views. The grid selects one tile and
    /// the list selects many, and list membership is written from this rather
    /// than from <see cref="SelectedTile"/> so that "Add to list" is the same
    /// control and the same command in either view — which is what §7 means by
    /// an action keeping its name through the whole flow.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(AddToListLabel))]
    [NotifyCanExecuteChangedFor(
        nameof(BeginAddToListCommand),
        nameof(RemoveFromOpenListCommand),
        nameof(MoveUpInListCommand),
        nameof(MoveDownInListCommand))]
    public partial IReadOnlyList<GameTileViewModel> SelectedTiles { get; set; } = [];

    public bool HasSelection => SelectedTiles.Count > 0;

    /// <summary>The button names the number it is about once there is more than one.</summary>
    public string AddToListLabel
        => SelectedTiles.Count > 1 ? $"Add {SelectedTiles.Count:N0} to list" : "Add to list";

    /// <summary>
    /// The open detail modal, or null. §5.3 caps the tile at four facts, which
    /// is only a defensible cap because this exists.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsOpen))]
    public partial GameDetailsViewModel? Details { get; set; }

    public bool IsDetailsOpen => Details is not null;

    // ══ The cut bar ═════════════════════════════════════════════════════════
    // One strip under the command bar that says what you are looking at. It is
    // the seam between the rail and the panel: the bucket appears here as the
    // first chip alongside the panel's, so the two controls read as one filter
    // even though they live on opposite sides of a divider. It is also the only
    // place a filtered library admits it is filtered once the panel is closed.

    /// <summary>Every rule in force, each with its own way out.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FilterChipViewModel> CutChips { get; set; } = [];

    /// <summary>
    /// "926 → 41" as one string, for the tests and for any reader that wants the
    /// whole sentence. The bar itself sets the three parts separately so the
    /// result can be Volt while the total stays TextDim.
    /// </summary>
    [ObservableProperty]
    public partial string CutText { get; set; } = string.Empty;

    /// <summary>What is left after the cut. Plex Mono, tabular, like every count.</summary>
    [ObservableProperty]
    public partial string VisibleCountText { get; set; } = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCutBar), nameof(ShowActionBar))]
    public partial bool IsCut { get; set; }

    /// <summary>
    /// The transient half of the strip — naming a live list, picking a list to
    /// add to, confirming a delete. Replaces the cut bar while it is up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptOpen), nameof(ShowCutBar), nameof(ShowActionBar))]
    public partial ActionPromptViewModel? Prompt { get; set; }

    public bool IsPromptOpen => Prompt is not null;

    public bool ShowCutBar => Prompt is null && IsCut;

    public bool ShowActionBar => Prompt is not null || IsCut;

    /// <summary>Whether saving the current cut as a live list is a meaningful act.</summary>
    public bool CanSaveLiveList => !BuildFilter().IsEmpty;

    /// <summary>
    /// The open live list's rules no longer match what is on screen. Both ways
    /// out are offered by name — <c>Update</c> and <c>Revert</c> — because
    /// neither is obviously right and neither should happen by accident.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLiveListEdited { get; set; }

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
        var bucketRows = await _libraryQueries.GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = ShowNonGameEntries });
        var ownerships = await _ownerships.GetAllAsync();
        var works = await _works.GetAllAsync();

        // One read for the whole library, not one per tile. Absent facets are a
        // normal state, not an error: the backfill runs behind a library the
        // user is already browsing (§7), and a release with no cached metadata
        // is simply not in the snapshot.
        _facets = _facetRepository is null
            ? FacetSnapshot.Empty
            : await _facetRepository.GetSnapshotAsync();

        // release id → its work, and release id → the provider id its cover art
        // is fetched under. Small library, per-work fetch is fine.
        var workByRelease = new Dictionary<long, Work>();
        var coverKeyByRelease = new Dictionary<long, CoverKey>();

        // The same appid the cover key is built from, kept as itself: it is what
        // the detail view's steam:// and store.steampowered.com targets are made
        // of, and re-querying external_ids per opened panel would be a second
        // trip for a string this loop already has in hand.
        var steamAppIdByRelease = new Dictionary<long, string>();

        // The other two stores' ids, for the same reason and out of the same
        // loop. Until this landed, BuildLinks returned early without a Steam
        // appid, so every Epic and GOG tile in the library had no primary action
        // and no links at all — 113 rows on the author's machine.
        var gogProductIdByRelease = new Dictionary<long, string>();
        var epicCatalogIdByRelease = new Dictionary<long, string>();

        // catalogItemId → namespace:catalogItemId:artifactId. One read for the
        // whole library, and legitimately empty: it is recovered from the
        // catalog answers the app has cached so far (§7 — enrichment runs behind
        // a library the user is already browsing), and a title it has not
        // reached yet simply gets no Epic launch target.
        var epicLaunchKeys = _epicLaunchKeys is null
            ? new Dictionary<string, EpicLaunchKey>()
            : (IReadOnlyDictionary<string, EpicLaunchKey>)await _epicLaunchKeys.GetAllAsync();

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

                if (externalIds.FirstOrDefault(x => x.Provider == ExternalIdProviders.Gog) is { } gog)
                {
                    gogProductIdByRelease[release.Id] = gog.ProviderId;
                }

                if (externalIds.FirstOrDefault(x => x.Provider == ExternalIdProviders.Epic) is { } epic)
                {
                    epicCatalogIdByRelease[release.Id] = epic.ProviderId;
                }

                if (steam is not null)
                {
                    coverKeyByRelease[release.Id] = CoverKey.Steam(steam.ProviderId);
                    steamAppIdByRelease[release.Id] = steam.ProviderId;
                }
                else if (IgdbImageUrl.ImageId(work.CoverUrl) is { Length: > 0 } imageId)
                {
                    // No Steam appid, so no Steam capsule and no external_games
                    // lookup — which is why every Epic and GOG tile rendered a
                    // placeholder even after enrichment learned their covers.
                    // The stored cover_url names IGDB's asset outright, so the
                    // key is the artwork id and the fetch is a plain CDN GET.
                    //
                    // This is the ONLY path that works for the cross-store
                    // duplicates: works.igdb_id is UNIQUE, so of an Epic title
                    // and its Steam twin only one row may hold the id, while
                    // both hold the same cover_url.
                    coverKeyByRelease[release.Id] = CoverKey.Igdb(imageId);
                }
            }
        }

        var ownershipById = ownerships.ToDictionary(o => o.Id);
        var now = DateTime.UtcNow;

        EpicLaunchKey? EpicKeyFor(long releaseId)
            => epicCatalogIdByRelease.TryGetValue(releaseId, out var catalogItemId)
                && epicLaunchKeys.TryGetValue(catalogItemId, out var key)
                    ? key
                    : null;

        var tiles = new List<GameTileViewModel>(bucketRows.Count);
        foreach (var row in bucketRows)
        {
            var work = workByRelease.GetValueOrDefault(row.ReleaseId);
            var ownership = ownershipById.GetValueOrDefault(row.OwnershipId);

            var tile = new GameTileViewModel(
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
                covers: _leases,
                work: work,
                ownership: ownership,
                ramp: Ramp,
                steamAppId: steamAppIdByRelease.GetValueOrDefault(row.ReleaseId),
                gogProductId: gogProductIdByRelease.GetValueOrDefault(row.ReleaseId),
                epicLaunchKey: EpicKeyFor(row.ReleaseId),
                // The §7 name, so the back of the card says "Never played" and
                // not "never_played". Resolved here because the rail owns that
                // vocabulary and the tile should not hold a second copy of it.
                bucketLabel: BucketLabelFor(row.Bucket));

            // The two commands the back face raises. Wired rather than reached
            // for: §5.1 keeps a tile a projection of the database, so it holds
            // the command the library already publishes instead of a route to a
            // repository. "Add to list" is the SAME command the command bar
            // runs — the flip is the single-game door onto it, the bar is the
            // bulk one (§12.3), and two implementations would be two behaviours.
            tile.AddToListCommand = BeginAddToListCommand;
            tile.OpenDetailsCommand = OpenDetailsCommand;
            tile.PrimaryActionCommand = LaunchCommand;

            // What the game IS, for the filter panel to cut on. Read from the
            // one snapshot rather than passed through the constructor: it is a
            // library-wide join, and it is legitimately empty on a database the
            // backfill has not reached yet.
            var facets = _facets.ByRelease.TryGetValue(row.ReleaseId, out var found)
                ? found
                : ReleaseFacets.Empty(row.ReleaseId);

            tile.Facets = TileFacets.From(facets, _facets.ById);

            // Build the filterable row once so panel and live lists share one implementation.
            tile.Row = new FilterableRow(
                ReleaseId: row.ReleaseId,
                OwnershipId: row.OwnershipId,
                Bucket: row.Bucket,
                Store: tile.Store,
                Title: tile.Title,
                // Two-valued: unknown install state counts as not-on-disk for filtering.
                Installed: tile.IsOnDisk,
                HasUnread: tile.HasUnread,
                FirstReleaseYear: tile.ReleaseYear,
                FacetIds: facets.FacetIds,
                GameModes: facets.GameModes);

            tiles.Add(tile);
        }

        _allTiles = tiles;

        foreach (var bucket in Buckets)
        {
            bucket.Count = bucketRows.Count(r => r.Bucket == bucket.Key);
        }

        // Rail's "All games" count.
        AllGames.Count = _allTiles.Count;

        TotalCount = _allTiles.Count;
        TotalCountText = TotalCount.ToString("N0");
        SearchPlaceholder = $"Search {TotalCountText} titles…";

        // Rebuild filter options; selections survive by key across reloads.
        Filters.Rebuild(_allTiles, _facets);

        await Lists.LoadAsync();

        // Re-derive rail selection marks after list objects are replaced.
        MarkRailSelection();

        _loaded = true;
        ApplyFilter();

        // Notify listeners that the tile set has been replaced.
        TilesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowGridView() => IsGridView = true;

    /// <summary>The flip is the grid's gesture; list view has the command bar (§12.3).</summary>
    [RelayCommand]
    private void ShowListView()
    {
        ClearFlip();
        IsGridView = false;
    }

    /// <summary>Command-bar sort menu.</summary>
    [RelayCommand]
    private void SelectSort(SortOptionViewModel? option)
    {
        if (option is not null)
        {
            Sort = option.Sort;
        }
    }

    /// <summary>Toggles title sort direction. First click sorts A-Z.</summary>
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

    /// <summary>Selects a bucket (toggling off if already selected). Also leaves the open list.</summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket) => Batched(() =>
    {
        // Toggle-off does not apply inside a live list (the bucket is the list's rule).
        var toggling = Lists.Open is not { IsLive: true }
            && bucket is not null
            && ReferenceEquals(SelectedBucket, bucket);

        LeaveListContext();

        SelectedBucket = bucket is null || bucket.Key == AllGamesKey || toggling
            ? null
            : bucket;
    });

    /// <summary>Opens the detail modal, loading update events and playtime history.</summary>
    [RelayCommand]
    private async Task OpenDetailsAsync(GameTileViewModel? tile)
    {
        var target = tile ?? SelectedTile;
        if (target is null)
        {
            return;
        }

        SelectTile(target);

        // Clear card flip on the way into the modal.
        ClearFlip();

        var events = await _updateEvents.GetByReleaseAsync(target.ReleaseId);

        // Newest first; each row knows whether it landed after last play.
        var updates = events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => UpdateEventViewModel.Create(e, target.LastPlayedUtc))
            .ToList();

        IReadOnlyList<PlaytimeSnapshot> history = _snapshots is null
            ? []
            : await _snapshots.GetByOwnershipAsync(target.OwnershipId);

        Details = new GameDetailsViewModel(
            target,
            BucketLabelFor(target.Bucket),
            updates,
            DateTime.UtcNow,
            snapshots: history,
            covers: _covers);
    }

    [RelayCommand]
    private void CloseDetails() => Details = null;

    // ══ Lists ═══════════════════════════════════════════════════════════════

    /// <summary>Opens a list: manual adds an AND term, live restores its saved rules into the panel.</summary>
    [RelayCommand]
    private void OpenList(GameListViewModel? list) => Batched(() =>
    {
        Prompt = null;
        LeaveListContext();

        if (list is null)
        {
            return;
        }

        Lists.Select(list);

        if (list.IsLive)
        {
            ApplySavedFilter(list.Filter);
            Filters.IsOpen = true;
        }
        else
        {
            SearchText = string.Empty;
            EnterListOrder();
        }
    });

    /// <summary>Leaves the open list. A live list's rules leave with it.</summary>
    [RelayCommand]
    private void CloseList()
    {
        if (Lists.Open is null)
        {
            return;
        }

        Batched(LeaveListContext);
    }

    /// <summary>Leaves the open list, clearing a live list's contributed rules. Panel stays open.</summary>
    private void LeaveListContext()
    {
        if (Lists.Open is not { } open)
        {
            return;
        }

        var wasLive = open.IsLive;

        Lists.Select(null);
        LeaveListOrder();

        if (!wasLive)
        {
            return;
        }

        // Clear all rules the live list contributed.
        SelectedBucket = null;
        SearchText = string.Empty;
        Filters.Clear();
    }

    /// <summary>Switches to list order and adds the sort menu row.</summary>
    private void EnterListOrder()
    {
        if (!SortOptions.Contains(_listOrderOption))
        {
            _sortBeforeList = Sort;
            SortOptions.Insert(0, _listOrderOption);
        }

        Sort = LibrarySort.ListOrder;
    }

    private void LeaveListOrder()
    {
        if (!SortOptions.Contains(_listOrderOption))
        {
            return;
        }

        SortOptions.Remove(_listOrderOption);
        Sort = _sortBeforeList;
    }

    /// <summary>Opens the "Add to list" prompt for the current selection.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BeginAddToList()
    {
        var picked = SelectedTiles.Select(t => t.ReleaseId).ToList();
        if (picked.Count == 0)
        {
            return;
        }

        var target = SelectedTiles.Count == 1
            ? SelectedTiles[0].Title
            : $"{SelectedTiles.Count:N0} titles";

        Prompt = new ActionPromptViewModel(
            question: $"Add {target} to",
            confirmLabel: "New list",
            confirm: async prompt =>
            {
                var created = await Lists.CreateListAsync(prompt.Text, picked);
                Prompt = null;
                if (created is not null)
                {
                    OpenList(created);
                }
            },
            cancel: () => Prompt = null,
            inputWatermark: "New list name",
            choices: [.. Lists.Lists],
            choose: async list =>
            {
                await Lists.AddToListAsync(list, picked);
                Prompt = null;
                ApplyFilter();
            });
    }

    /// <summary>Prompts the user to name and save the current filter as a live list.</summary>
    [RelayCommand]
    private void BeginSaveLiveList()
        => Prompt = new ActionPromptViewModel(
            question: "Name this live list",
            confirmLabel: "Save",
            confirm: async prompt =>
            {
                var created = await Lists.CreateLiveListAsync(prompt.Text, BuildFilter());
                Prompt = null;
                if (created is not null)
                {
                    OpenList(created);
                }
            },
            cancel: () => Prompt = null,
            inputWatermark: "Live list name",
            initialText: SuggestedListName(),
            note: "It finds its own members every time your library changes.");

    /// <summary>Rewrites the open live list's rules to the filter now in force.</summary>
    [RelayCommand]
    private async Task UpdateLiveListAsync()
    {
        if (Lists.Open is not { IsLive: true } live)
        {
            return;
        }

        await Lists.UpdateFilterAsync(live, BuildFilter());
        ApplyFilter();
    }

    /// <summary>Puts the open live list's saved rules back, discarding the edit.</summary>
    [RelayCommand]
    private void RevertLiveList()
    {
        if (Lists.Open is { IsLive: true } live)
        {
            Batched(() => ApplySavedFilter(live.Filter));
        }
    }

    [RelayCommand]
    private void BeginRenameList()
    {
        if (Lists.Open is not { } list)
        {
            return;
        }

        Prompt = new ActionPromptViewModel(
            question: "Rename this list",
            confirmLabel: "Rename",
            confirm: async prompt =>
            {
                await Lists.RenameAsync(list, prompt.Text);
                Prompt = null;
                ApplyFilter();
            },
            cancel: () => Prompt = null,
            inputWatermark: "List name",
            initialText: list.Name);
    }

    /// <summary>Confirms and deletes the open list. Titles remain in the library.</summary>
    [RelayCommand]
    private void BeginDeleteList()
    {
        if (Lists.Open is not { } list)
        {
            return;
        }

        Prompt = new ActionPromptViewModel(
            question: $"Delete \u201C{list.Name}\u201D?",
            confirmLabel: "Delete list",
            confirm: async _ =>
            {
                await Lists.DeleteAsync(list);
                Prompt = null;
                ApplyFilter();
            },
            cancel: () => Prompt = null,
            note: "The titles stay in your library.",
            isDestructive: true);
    }

    /// <summary>Drops the selection out of the open manual list.</summary>
    [RelayCommand(CanExecute = nameof(CanEditOpenList))]
    private async Task RemoveFromOpenListAsync()
    {
        if (Lists.Open is not { IsManual: true } list)
        {
            return;
        }

        await Lists.RemoveFromListAsync(list, SelectedTiles.Select(t => t.ReleaseId));
        ApplyFilter();
    }

    [RelayCommand(CanExecute = nameof(CanMoveUpInList))]
    private Task MoveUpInListAsync() => MoveInListAsync(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDownInList))]
    private Task MoveDownInListAsync() => MoveInListAsync(1);

    /// <summary>Moves the selected row by <paramref name="delta"/> positions in the open manual list.</summary>
    public async Task MoveInListAsync(int delta)
    {
        if (Lists.Open is not { IsManual: true } list || SelectedTiles.Count != 1)
        {
            return;
        }

        if (await Lists.MoveAsync(list, SelectedTiles[0].ReleaseId, delta))
        {
            ApplyFilter();
        }
    }

    /// <summary>Manual-list membership can be edited; a live list's is computed.</summary>
    public bool CanEditOpenList => Lists.Open is { IsManual: true } && SelectedTiles.Count > 0;

    public bool CanReorderOpenList
        => Lists.Open is { IsManual: true } && SelectedTiles.Count == 1 && Sort == LibrarySort.ListOrder;

    /// <summary>Index of the selected title in the open list, or -1. Used to disable move buttons at bounds.</summary>
    private int PositionInOpenList
        => CanReorderOpenList && Lists.Open is { } list
            ? list.ReleaseIds.ToList().IndexOf(SelectedTiles[0].ReleaseId)
            : -1;

    public bool CanMoveUpInList => PositionInOpenList > 0;

    public bool CanMoveDownInList
        => PositionInOpenList >= 0
            && Lists.Open is { } list
            && PositionInOpenList < list.ReleaseIds.Count - 1;

    // ── What the cut bar offers, and when ───────────────────────────────────

    /// <summary>"Save as live list" — offered on any cut that is not already one.</summary>
    public bool ShowSaveLiveList => CanSaveLiveList && !Lists.IsLiveListOpen;

    /// <summary>Rename / Delete list: a list is open and nothing is picked inside it.</summary>
    public bool ShowListMetaActions => Lists.IsListOpen && SelectedTiles.Count == 0;

    /// <summary>Remove from list: a hand-built list is open and rows are picked.</summary>
    public bool ShowListMemberActions => CanEditOpenList;

    private void RaiseActionState()
    {
        OnPropertyChanged(nameof(CanSaveLiveList));
        OnPropertyChanged(nameof(CanEditOpenList));
        OnPropertyChanged(nameof(CanReorderOpenList));
        OnPropertyChanged(nameof(CanMoveUpInList));
        OnPropertyChanged(nameof(CanMoveDownInList));
        OnPropertyChanged(nameof(ShowSaveLiveList));
        OnPropertyChanged(nameof(ShowListMetaActions));
        OnPropertyChanged(nameof(ShowListMemberActions));
        RemoveFromOpenListCommand.NotifyCanExecuteChanged();
        MoveUpInListCommand.NotifyCanExecuteChanged();
        MoveDownInListCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTilesChanged(IReadOnlyList<GameTileViewModel> value)
    {
        _ = value;
        RaiseActionState();
    }

    /// <summary>Suggests a list name from the first two active filter chips.</summary>
    private string SuggestedListName()
    {
        var parts = CutChips
            .Where(c => c.Dimension != "LIST" && c.Dimension != "LIVE LIST")
            .Select(c => c.Label)
            .Take(2)
            .ToList();

        return parts.Count == 0 ? string.Empty : string.Join(" \u00B7 ", parts);
    }

    /// <summary>Pours a saved rule set back into the rail and the panel together.</summary>
    private void ApplySavedFilter(LibraryFilter filter)
    {
        var bucketKey = filter.Buckets.Count > 0 ? filter.Buckets[0] : null;
        var bucket = bucketKey is null ? null : Buckets.FirstOrDefault(b => b.Key == bucketKey);

        // Rail first, then panel; panel's Apply triggers one recompute.
        SelectedBucket = bucket;
        SearchText = filter.Search ?? string.Empty;
        Filters.Apply(filter);
    }

    public void SelectTile(GameTileViewModel? tile)
    {
        if (ReferenceEquals(SelectedTile, tile))
        {
            return;
        }

        SelectedTile = tile;
    }

    // ══ The card flip ═══════════════════════════════════════════════════════
    // Exactly one tile is flipped at a time. State lives on the tile VM.

    /// <summary>The flipped tile, or null. Not observable; the per-tile flag is what the view binds.</summary>
    private GameTileViewModel? _flipped;

    /// <summary>The turned-over tile, for the keyboard and for tests.</summary>
    public GameTileViewModel? FlippedTile => _flipped;

    /// <summary>Flips a tile (or unflips if already flipped). Also selects it.</summary>
    [RelayCommand]
    public void FlipTile(GameTileViewModel? tile)
    {
        if (tile is null)
        {
            ClearFlip();
            return;
        }

        SelectTile(tile);

        if (ReferenceEquals(_flipped, tile))
        {
            ClearFlip();
            return;
        }

        ClearFlip();
        tile.IsFlipped = true;
        _flipped = tile;
    }

    /// <summary>Turn every card face-up. Safe to call when none is turned.</summary>
    public void ClearFlip()
    {
        if (_flipped is null)
        {
            return;
        }

        _flipped.IsFlipped = false;
        _flipped = null;
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
        // Selection flags live on tiles so grid and list views stay in sync.
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        // Arrowing off a flipped card unflips it.
        if (!ReferenceEquals(_flipped, newValue))
        {
            ClearFlip();
        }

        // Grid view: derive SelectedTiles here so keyboard nav keeps "Add to list" visible.
        // List view excluded; its SelectionChanged handler owns the multi-select set.
        if (IsGridView)
        {
            SelectedTiles = newValue is null ? [] : [newValue];
            SelectedCount = newValue is null ? 0 : 1;
        }
    }

    partial void OnSelectedBucketChanged(BucketViewModel? value)
    {
        _ = value;
        MarkRailSelection();
        ApplyFilter();
    }

    /// <summary>Updates rail selection: one Volt edge for the active location, IsRule for a bucket inside a list.</summary>
    private void MarkRailSelection()
    {
        var inList = Lists.IsListOpen;

        foreach (var bucket in Buckets)
        {
            var current = ReferenceEquals(bucket, SelectedBucket);
            bucket.IsSelected = current && !inList;
            bucket.IsRule = current && inList;
        }

        AllGames.IsSelected = SelectedBucket is null && !inList;
    }

    /// <summary>The open live list's saved rules, or null when no live list is open.</summary>
    private LibraryFilter? ContextFilter
        => Lists.Open is { IsLive: true } live ? live.Filter : null;

    /// <summary>Suspends filter recomputation during a multi-write operation, then applies once.</summary>
    private void Batched(Action write)
    {
        _suspended++;
        try
        {
            write();
        }
        finally
        {
            _suspended--;
        }

        MarkRailSelection();
        ApplyFilter();
    }

    /// <summary>Counts titles per store over all tiles (not just visible). Computed on demand.</summary>
    public IReadOnlyDictionary<string, int> TitlesByStore()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in _allTiles)
        {
            counts[tile.Store] = counts.GetValueOrDefault(tile.Store) + 1;
        }

        return counts;
    }

    /// <summary>Refreshes dormancy on all tiles when the ramp preference changes.</summary>
    private void OnRampChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var tile in _allTiles)
        {
            tile.RefreshDormancy();
        }
    }

    /// <summary>Applies all filter terms (bucket, list, panel, search) and rebuilds the visible set.</summary>
    private void ApplyFilter()
    {
        if (!_loaded || _suspended > 0)
        {
            return;
        }

        // Unflip before rebuilding; the flipped tile may leave the visible set.
        ClearFlip();

        IEnumerable<GameTileViewModel> query = _allTiles;
        if (SelectedBucket is { } bucket)
        {
            query = query.Where(t => t.Bucket == bucket.Key);
        }

        // Manual list: AND term on membership. Live list: no term (rules are in the panel).
        if (Lists.Open is { IsManual: true } openList)
        {
            var members = openList.ReleaseIds.ToHashSet();
            query = query.Where(t => members.Contains(t.ReleaseId));
        }

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(t => t.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var baseline = query.ToList();

        // Recount panel options against the baseline (bucket + list + search, before panel).
        Filters.Recount(filter => Matching(baseline, filter));

        var visible = Order(Matching(baseline, Filters.ToFilter())).ToList();
        VisibleTiles = visible;

        if (SelectedTile is { } selected && !visible.Contains(selected))
        {
            SelectTile(null);
        }

        SelectedCount = SelectedTile is null ? 0 : 1;
        SelectedTiles = SelectedTile is null ? [] : [SelectedTile];
        EmptyMessage = BuildEmptyMessage(visible.Count, search);
        RefreshListCounts();
        RefreshCutBar(visible.Count);
    }

    /// <summary>Returns tiles matching the filter. Keyed on ownership id (not release) to preserve cross-store dupes.</summary>
    private static IReadOnlyList<GameTileViewModel> Matching(
        IReadOnlyList<GameTileViewModel> tiles, LibraryFilter filter)
    {
        if (filter.IsEmpty)
        {
            return tiles;
        }

        var kept = filter.Apply(tiles.Select(t => t.Row))
            .Select(r => r.OwnershipId)
            .ToHashSet();

        return [.. tiles.Where(t => kept.Contains(t.OwnershipId))];
    }

    /// <summary>Recounts list membership for both manual and live lists against the current library.</summary>
    private void RefreshListCounts()
    {
        foreach (var list in Lists.Lists)
        {
            var members = list.ReleaseIds.ToHashSet();
            list.Count = _allTiles.Count(t => members.Contains(t.ReleaseId));
        }

        foreach (var list in Lists.LiveLists)
        {
            // Counted against the whole library, not the current cut.
            list.Count = Matching(_allTiles, list.Filter).Count;
        }
    }

    /// <summary>Rebuilds the cut bar chips and text from the active filters.</summary>
    private void RefreshCutBar(int visibleCount)
    {
        var chips = new List<FilterChipViewModel>();
        var context = ContextFilter;

        // Open list chip leads the bar; dropping it leaves the list.
        if (Lists.Open is { } list)
        {
            chips.Add(new FilterChipViewModel(
                list.Name,
                list.KindLabel,
                () => CloseListCommand.Execute(null),
                FilterChipOrigin.Context));
        }

        // Bucket chip; dropping it clears the rail.
        if (SelectedBucket is { } bucket)
        {
            chips.Add(new FilterChipViewModel(
                bucket.Name,
                "BUCKET",
                () => SelectBucketCommand.Execute(null),
                context is null
                    ? FilterChipOrigin.User
                    : context.Buckets.Contains(bucket.Key)
                        ? FilterChipOrigin.List
                        : FilterChipOrigin.Unsaved));
        }

        chips.AddRange(Filters.BuildChips(context));

        CutChips = chips;
        CutText = $"{TotalCount:N0} \u2192 {visibleCount:N0}";
        VisibleCountText = visibleCount.ToString("N0");
        IsCut = chips.Count > 0 || visibleCount != TotalCount;

        // Detect unsaved edits to the open live list (LibraryFilter uses set equality).
        IsLiveListEdited = Lists.Open is { IsLive: true } live
            && BuildFilter() != live.Filter;

        RaiseActionState();
    }

    /// <summary>Builds a <see cref="LibraryFilter"/> from the current bucket, panel, and search.</summary>
    private LibraryFilter BuildFilter()
    {
        var search = SearchText.Trim();
        return Filters.ToFilter() with
        {
            Buckets = SelectedBucket is { } bucket ? [bucket.Key] : [],
            Search = search.Length > 0 ? search : null,
        };
    }

    /// <summary>Sorts tiles by the current order, tie-breaking on title for stability.</summary>
    private IEnumerable<GameTileViewModel> Order(IEnumerable<GameTileViewModel> tiles) => Sort switch
    {
        // Never-played = MinValue: front for DormantLongest, back for RecentlyPlayed.
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

        // User-defined positions; falls through to title order if no manual list is open.
        LibrarySort.ListOrder when Lists.Open is { IsManual: true } list => OrderByPosition(tiles, list),

        _ => tiles.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Orders tiles by their stored position in the list, using an index map for O(n) lookup.</summary>
    private static IEnumerable<GameTileViewModel> OrderByPosition(
        IEnumerable<GameTileViewModel> tiles, GameListViewModel list)
    {
        var position = new Dictionary<long, int>(list.ReleaseIds.Count);
        for (var i = 0; i < list.ReleaseIds.Count; i++)
        {
            position[list.ReleaseIds[i]] = i;
        }

        return tiles
            .OrderBy(t => position.TryGetValue(t.ReleaseId, out var at) ? at : int.MaxValue)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase);
    }

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

        // Active has no rail row; mapped to a human label here.
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

        // Filter empty state (most common) tested before bucket-specific messages.
        if (Filters.HasSelection)
        {
            return "No titles match these filters. Drop one to widen the cut.";
        }

        if (Lists.Open is { IsManual: true } list)
        {
            return $"\u201C{list.Name}\u201D is empty. Select titles in the library and choose Add to list.";
        }

        if (Lists.Open is { IsLive: true } live)
        {
            return $"Nothing matches \u201C{live.Name}\u201D yet. It will fill itself in as your library changes.";
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
