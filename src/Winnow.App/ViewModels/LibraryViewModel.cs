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
        JournalPromptViewModel? journal = null)
    {
        _libraryQueries = libraryQueries;
        _ownerships = ownerships;
        _releases = releases;
        _works = works;
        _updateEvents = updateEvents;
        _covers = covers;
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
    /// M3b: Play / Install, for the tile's back face and the detail panel alike.
    ///
    /// <para><b>Every branch below ends without a dialog</b>, which is the whole
    /// acceptance criterion for this milestone. The store client not running is
    /// not an error and is not mentioned — starting it is what the URI is for. A
    /// user who cancels at the store's own prompt gets silence, because they did
    /// not fail at anything. A second click while the first launch is in flight
    /// does nothing at all rather than firing a second prompt. The one case that
    /// says anything is the one Winnow can actually diagnose: the shell had no
    /// handler for the scheme, so nothing was even asked to start.</para>
    ///
    /// <para><b>It does not await the game.</b> The strip resolves later, off the
    /// watcher's own signal, so this command completes as soon as the URI is
    /// handed over — nothing in the UI is blocked on another application's
    /// startup time.</para>
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
                covers: _covers,
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

            // The flat projection LibraryFilter matches on. Built once per tile
            // so the panel and a saved live list ask the SAME question of the
            // SAME row — one implementation of what a filter means (see
            // LibraryFilter's remarks), rather than a second one in the view.
            tile.Row = new FilterableRow(
                ReleaseId: row.ReleaseId,
                OwnershipId: row.OwnershipId,
                Bucket: row.Bucket,
                Store: tile.Store,
                Title: tile.Title,
                // The filter row's install flag is two-valued because the "on
                // disk" facet is a two-way cut: an unknown state is not KNOWN to
                // be on disk, so it falls on the same side of that question as a
                // known "no". This is not the conflation the tile refuses to
                // make — the tile refuses to NAME a button it cannot back, while
                // this is a filter answering "which of these do I know are on
                // disk", where the honest answer for an unknown is "not these".
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

        // The rail's own total, on the row that selects it. This is the only
        // place the rail states the library size — the 22px "606 TITLES" header
        // that used to sit two rows above it said the same number with no
        // affordance attached, which is the duplication §7 warns about.
        AllGames.Count = _allTiles.Count;

        TotalCount = _allTiles.Count;
        TotalCountText = TotalCount.ToString("N0");
        SearchPlaceholder = $"Search {TotalCountText} titles…";

        // Options are rebuilt from the library as it now stands, and selections
        // survive by key — a reload after an enrichment pass must not silently
        // drop a rule the user set five minutes ago.
        Filters.Rebuild(_allTiles, _facets);

        await Lists.LoadAsync();

        // A reload replaces every list row object and re-finds the open one by
        // id, so the rail's marks have to be re-derived from the new objects —
        // otherwise a live list that survives a reload keeps the Volt edge on a
        // bucket that is no longer where you are.
        MarkRailSelection();

        _loaded = true;
        ApplyFilter();

        // Every tile object above is new, so anything holding the previous
        // generation is now holding a projection of a library that no longer
        // exists. Raised last, with the new set fully in place, so a listener
        // that reads back through TileForOwnership can only see the finished
        // state (IGameTileSource).
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
    ///
    /// <para><b>It also leaves whatever list you were in, and takes that list's
    /// rules with it</b> (<see cref="LeaveListContext"/>). A bucket and a list
    /// are both answers to "what am I looking at", and clicking one is a
    /// statement that you have stopped looking at the other.</para>
    /// </summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket) => Batched(() =>
    {
        // The click-the-row-you-are-on escape hatch does NOT apply inside a live
        // list: there the lit bucket is the LIST's rule and not the user's own
        // click, so clicking it means "give me that bucket and nothing else",
        // not "give me it a second time, which means clear it".
        var toggling = Lists.Open is not { IsLive: true }
            && bucket is not null
            && ReferenceEquals(SelectedBucket, bucket);

        LeaveListContext();

        SelectedBucket = bucket is null || bucket.Key == AllGamesKey || toggling
            ? null
            : bucket;
    });

    /// <summary>
    /// Opens the detail modal for a tile. Two reads, both keyed on rows this
    /// view model already holds: the release's update events, and the
    /// ownership's playtime history. Everything else was joined at load, so the
    /// modal appears with its identity and its gap already correct and only the
    /// two lists fill in.
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

        // The modal is the richer version of the back face, so the card goes
        // face-up on the way in: coming back from Escape to a turned card would
        // be the wall remembering a step the user has already taken.
        ClearFlip();

        var events = await _updateEvents.GetByReleaseAsync(target.ReleaseId);

        // Newest first: the update the user missed most recently is the one
        // they are trying to catch up on (§5.2). Each row is told the last-
        // played date so it can say whether it is one the user actually missed
        // — the distinction the Flare dot marks and the rail plots.
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
    // Two kinds, one rail section each. A LIST holds the games you put in it; a
    // LIVE LIST holds the rules and finds them again. Everything below keeps
    // that distinction visible rather than papering over it — a manual list is
    // opened by loading its membership, a live one by loading its rules into
    // the very controls that made them.

    /// <summary>
    /// Opens a list. A manual one becomes an extra AND term over the library; a
    /// live one pours its saved rules back into the rail and the panel, so the
    /// user is looking at the filter that defines it and can edit it in place.
    /// Either way the search box is cleared, because a list is a fresh question.
    ///
    /// <para><b>It leaves the list you were in first.</b> A list is a PLACE, and
    /// you are only ever in one — so opening list B cannot leave list A's rules
    /// lying in the panel underneath it.</para>
    /// </summary>
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

    /// <summary>
    /// Steps out of the open list, whichever kind it is.
    ///
    /// <para><b>This is the whole of the live-list fix.</b> §12.2 has a live list
    /// add no AND term of its own: opening one pours its rules into the rail and
    /// the panel so they are editable in place, which is what makes the two kinds
    /// of list visibly different. What was missing was the other half of that
    /// bargain. The poured-in rules were indistinguishable from rules the user
    /// had set, so clicking "All games" cleared the bucket and left the list's
    /// genre, mode and tag terms silently applied — and the user believed they
    /// were looking at their whole library when they were looking at a live list
    /// with extra filters on top. That is the most expensive confusion this
    /// screen can produce (§11.3), arriving by the one door the cut bar could
    /// not watch.</para>
    ///
    /// <para>So: <b>a list is a place, and its rules are the place's, not
    /// yours.</b> Entering one is entering a context; leaving takes the context's
    /// contribution with it. That is the same contract a manual list already
    /// honours — its membership term goes when you leave — stated once for both
    /// kinds. A manual list contributes only membership, so leaving it still
    /// leaves the rail, the panel and the search box exactly as the user set
    /// them; §12.2's "the ones in Couch co-op night I haven't installed" is
    /// untouched.</para>
    ///
    /// <para>The panel is deliberately left OPEN on the way out of a live list.
    /// Closing it would hide the very thing that proves the rules went, and the
    /// user did not open it — entering the list did.</para>
    /// </summary>
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

        // Everything ApplySavedFilter poured in, poured back out. Batched, so
        // the grid is rebuilt once against the state the caller lands on rather
        // than three times against a half-torn-down rule set.
        SelectedBucket = null;
        SearchText = string.Empty;
        Filters.Clear();
    }

    /// <summary>
    /// A hand-built list opens in the order it was built. The row is added to
    /// the sort menu at the same moment, so the order the grid is in is always
    /// one the menu can name.
    /// </summary>
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

    /// <summary>
    /// "Add to list". One control for both views: the grid selects one tile and
    /// the list selects many, and this reads whichever is in force.
    /// </summary>
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

    /// <summary>
    /// §7's exact words, and the exact place they belong: on the bar that
    /// already states what the filter has left you with. What you save is what
    /// that line describes.
    /// </summary>
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

    /// <summary>
    /// Deleting asks first, and the question says what survives. A list is the
    /// only thing in this application a user can destroy, and the one fear worth
    /// answering out loud is that the games go with it.
    /// </summary>
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

    /// <summary>
    /// Reordering is one row at a time, by the keyboard as well as the buttons
    /// (Alt+Up / Alt+Down). Not drag and drop: the rows are virtualized, a drag
    /// across 400 of them is a scroll fight, and §8 asks for the whole interface
    /// to be reachable without a pointer anyway.
    /// </summary>
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

    /// <summary>
    /// Where the selected title sits in the open list, or -1.
    ///
    /// <para>The two move buttons test it rather than merely testing that
    /// something is selected: the top row cannot go up and the bottom row cannot
    /// go down, and a live button that does nothing when pressed is exactly the
    /// thing §7 is about — it makes the user doubt the control rather than the
    /// position they are in.</para>
    /// </summary>
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
    // The strip is 40px and the buttons on it are small, so the rule is that at
    // most four are ever up at once. Membership actions and list-metadata
    // actions are mutually exclusive on purpose: with rows selected you are
    // editing what is IN the list, with nothing selected you are editing the
    // list itself, and those are two different jobs sharing one row of chrome.

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

    /// <summary>
    /// The name the save prompt opens with: the rules, read out. Two chips at
    /// most — past that the name stops being a name — and the user can type over
    /// it. An untitled list is not offered, because a rail full of "Live list 3"
    /// is a rail nobody reads.
    /// </summary>
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

        // The rail moves first, then the panel; the panel's Apply raises exactly
        // one recompute at the end, so the grid is never rebuilt against a
        // half-restored rule set.
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
    // One click turns a cover over. The back carries a few facts and the actions
    // for that one game — the primary action, Add to list, and the route on to
    // the detail modal, whose gesture the flip took.
    //
    // The state lives on the tile view model (see GameTileViewModel.IsFlipped
    // for why that is forced rather than chosen), and this is the rule that
    // keeps it from becoming a wall of face-down cards: EXACTLY ONE tile is
    // turned over at a time. §1 says the art is the interface, so a grid showing
    // its backs is a grid with nothing in it.

    /// <summary>
    /// The one tile currently showing its back, or null. Not an
    /// <c>ObservableProperty</c>: nothing binds to it, and the flag the view
    /// reads is the one on each tile.
    /// </summary>
    private GameTileViewModel? _flipped;

    /// <summary>The turned-over tile, for the keyboard and for tests.</summary>
    public GameTileViewModel? FlippedTile => _flipped;

    /// <summary>
    /// Turn a card over — or back, when it is the one already turned.
    ///
    /// <para>Flipping also selects, because turning a card over IS picking it,
    /// and because "Add to list" reads the selection (§12.3): a back face whose
    /// button acted on some other tile would be the worst kind of working
    /// button.</para>
    /// </summary>
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

        // Selection and the flip move together: exactly one card is ever turned
        // over, and it is always the selected one. Arrowing off a turned card
        // therefore turns it back, which is also what makes the keyboard route
        // out of the back face a key the user already knows.
        if (!ReferenceEquals(_flipped, newValue))
        {
            ClearFlip();
        }

        // The grid selects exactly one tile, and it selects it by arrow key as
        // often as by pointer. Deriving the picked set here rather than in the
        // pointer handler is what makes that true: with it only in the handler,
        // walking the wall with the arrows moved the Volt outline and left "Add
        // to list" hidden, which is §8's keyboard floor failing quietly on the
        // one control the whole lists feature hangs off.
        //
        // List view is excluded because it can hold more than one selection, and
        // its SelectionChanged handler — which runs after this — is the only
        // thing that knows the whole set.
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

    /// <summary>
    /// Which rail row is lit, and how.
    ///
    /// <para><b>Exactly one row ever carries the Volt edge, and it always means
    /// the same thing: this is where you are.</b> No bucket IS the "All games"
    /// state — including the one the app launches in, so the rail is never
    /// showing a library nothing in it claims.</para>
    ///
    /// <para>With a list open, the place you are is the list, so its row takes
    /// the Volt edge and a bucket in force takes <see cref="BucketViewModel.IsRule"/>
    /// instead — the same fill, a TextDim edge. Before this, both rows drew the
    /// Volt edge at once: a live list carrying <c>Bounced off</c> lit "Bounced
    /// off" AND the list, and clicking "All games" then lit a THIRD row while
    /// leaving the list's genres applied. Three rows claiming to be where you
    /// are is how a user ends up certain they are looking at their whole
    /// library.</para>
    /// </summary>
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

    /// <summary>
    /// The rules the open live list is contributing, exactly as they were saved
    /// — or null whenever the filter surface belongs to the user. Everything
    /// that needs to tell a list's rule from the user's own reads this and
    /// nothing else, so there is one answer to that question in the view model.
    /// </summary>
    private LibraryFilter? ContextFilter
        => Lists.Open is { IsLive: true } live ? live.Filter : null;

    /// <summary>
    /// Runs a group of writes as one change to the cut. Entering or leaving a
    /// list touches the rail, the search box and every group in the panel; each
    /// of those raises its own recompute, and without this the grid is rebuilt
    /// once per write against a rule set nobody asked for and the counts in the
    /// panel flicker through states that never existed.
    /// </summary>
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

    /// <summary>
    /// <inheritdoc cref="IStoreTitleCounts.TitlesByStore"/>
    ///
    /// <para>Computed on demand rather than cached: the Stores panel asks once
    /// per opening, and a cached copy would be one more thing that has to be
    /// invalidated when the library reloads behind the enrichment pass — which
    /// is exactly the class of bug where the build is green and the number on
    /// screen is last week's.</para>
    ///
    /// <para>Counted over <c>_allTiles</c>, never over
    /// <see cref="VisibleTiles"/>: the panel is answering "where did your
    /// library come from", and the answer must not move because a bucket is
    /// selected or a search box has three letters in it.</para>
    /// </summary>
    public IReadOnlyDictionary<string, int> TitlesByStore()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in _allTiles)
        {
            counts[tile.Store] = counts.GetValueOrDefault(tile.Store) + 1;
        }

        return counts;
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

    /// <summary>
    /// The whole cut, in one place. Five AND terms, in the order they were added
    /// to the product: the rail's bucket, an open list, the filter panel, the
    /// release-year range and the search box.
    ///
    /// <para><b>The baseline the panel counts against is everything except the
    /// panel.</b> That is what makes the number beside each option true — it is
    /// what you would get if you ticked it, given the bucket you are in and the
    /// text you have typed, rather than how many of that genre you happen to
    /// own. The panel then lifts each group's own selections when counting that
    /// group, because options inside a group widen rather than narrow (see
    /// <see cref="FilterPanelViewModel.Recount"/>).</para>
    /// </summary>
    private void ApplyFilter()
    {
        if (!_loaded || _suspended > 0)
        {
            return;
        }

        // The wall is about to be rebuilt under whatever was turned over. A card
        // that stays face-down through a search or a bucket change is a card the
        // user did not leave there — and it may not even be in the new set.
        ClearFlip();

        IEnumerable<GameTileViewModel> query = _allTiles;
        if (SelectedBucket is { } bucket)
        {
            query = query.Where(t => t.Bucket == bucket.Key);
        }

        // A manual list is one more AND term rather than a separate screen, so
        // the rail, the panel and the search box all still work inside it: "the
        // ones in Co-op night I have not installed" is a question the user can
        // now ask without leaving the list. A LIVE list adds no term — opening
        // one loads its rules into the panel and the rail, which is the whole
        // difference between the two kinds made visible.
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

        // One matcher implementation for the panel, the grid and every live
        // list: LibraryFilter.Apply. The panel supplies the rule set, this
        // supplies the rows, and a residual count is the same call with one
        // group's selections lifted.
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

    /// <summary>
    /// The tiles in <paramref name="tiles"/> that satisfy <paramref name="filter"/>.
    /// Ownership id is the key because it is what a library row IS — one release
    /// owned on two stores is two tiles, and matching back on release id would
    /// merge them.
    /// </summary>
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

    /// <summary>
    /// Both kinds of list count themselves against the library as it now stands.
    /// A manual list loses a member when the game behind it is consolidated
    /// away, and a live list's number moving on its own IS the feature — so the
    /// rail states both from the same pass and neither is a stored total.
    /// </summary>
    private void RefreshListCounts()
    {
        foreach (var list in Lists.Lists)
        {
            var members = list.ReleaseIds.ToHashSet();
            list.Count = _allTiles.Count(t => members.Contains(t.ReleaseId));
        }

        foreach (var list in Lists.LiveLists)
        {
            // Against the whole library, not the current cut: a rail count that
            // moved when you clicked a different bucket would be describing the
            // screen instead of the list.
            list.Count = Matching(_allTiles, list.Filter).Count;
        }
    }

    /// <summary>
    /// The strip that says what you are looking at. It appears the moment the
    /// grid stops showing the whole library, because a library that has been cut
    /// down and does not admit it is the most expensive confusion this screen
    /// can produce — the panel can be closed and the rail scrolled past.
    /// </summary>
    private void RefreshCutBar(int visibleCount)
    {
        var chips = new List<FilterChipViewModel>();
        var context = ContextFilter;

        // The open list leads the bar, ahead of the rules, because it is not a
        // rule — it is the place the rules belong to, and "which live list am I
        // in" is the question this strip was failing to answer. Its chip shows
        // its kind rather than hiding it in a tooltip, and dropping it leaves
        // the list, taking the list's rules with it.
        if (Lists.Open is { } list)
        {
            chips.Add(new FilterChipViewModel(
                list.Name,
                list.KindLabel,
                () => CloseListCommand.Execute(null),
                FilterChipOrigin.Context));
        }

        // Then the rail's bucket, because it is the pile you are standing in and
        // because this is the only place the rail and the panel are stated as
        // one filter — which is the whole of that claim now that the panel is no
        // longer physically joined to the rail (§11.1). Dropping it clears the
        // rail.
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

        // A live list whose rules have been changed but not saved says so, and
        // offers both answers by name. LibraryFilter compares as a SET rather
        // than by reference — its own remarks explain why the compiler-generated
        // equality would have answered "different" on every keystroke — so this
        // is one operator and not a hand-rolled fingerprint.
        IsLiveListEdited = Lists.Open is { IsLive: true } live
            && BuildFilter() != live.Filter;

        RaiseActionState();
    }

    /// <summary>
    /// The whole cut as one saveable rule: the rail's bucket, the panel's
    /// groups, and the search box. All three, because all three are visibly
    /// shaping the grid the user is about to name — saving "co-op" and silently
    /// dropping the word they typed would produce a list that does not match the
    /// screen it was saved from.
    /// </summary>
    private LibraryFilter BuildFilter()
    {
        var search = SearchText.Trim();
        return Filters.ToFilter() with
        {
            Buckets = SelectedBucket is { } bucket ? [bucket.Key] : [],
            Search = search.Length > 0 ? search : null,
        };
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

        // The positions the user put them in. Falls through to title order when
        // no manual list is open, which is the only state in which it can be
        // asked for and not answerable.
        LibrarySort.ListOrder when Lists.Open is { IsManual: true } list => OrderByPosition(tiles, list),

        _ => tiles.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Hand-built order. The index map is built once per sort rather than
    /// searched per comparison — a list of four hundred would otherwise be a
    /// quadratic scan on every keystroke in the search box.
    /// </summary>
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

        // A filter that has emptied the grid is the most common empty state
        // there is, and the only one with an obvious next move. It is tested
        // before the bucket messages because those describe a library, and this
        // describes a control the user is holding.
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
