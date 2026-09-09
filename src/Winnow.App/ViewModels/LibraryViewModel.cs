using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.App.ViewModels.Filters;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
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
    /// The details modal's journal read/write seam. Optional like the other
    /// detail additions: without it the library still loads and the section is
    /// simply absent.
    /// </summary>
    private readonly ISessionRepository? _sessions;

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
    private readonly Winnow.Core.Repositories.IStorefrontRepository? _storefrontCache;

    /// <summary>
    /// M3b. Optional, like every other seam on this view model: with nothing
    /// registered the Play button is inert rather than absent, and the library
    /// still loads.
    /// </summary>
    private readonly Services.GameLaunchService? _launcher;

    /// <summary>
    /// Identity links (migration 0018). Optional for the reason every seam
    /// here is optional: with nothing registered every work resolves to
    /// itself and the library is exactly the pre-link library, which is the
    /// degradation the whole link model was chosen for.
    /// </summary>
    private readonly IIdentityLinkRepository? _identityLinks;

    /// <summary>
    /// Per-release achievements (§6.2). Optional; absent means the coverage
    /// rows simply carry no achievement line.
    /// </summary>
    private readonly IAchievementQueryRepository? _achievements;

    /// <summary>
    /// The write half of hiding: the tile context menu's Hide and the details
    /// modal's Hide. The read half is one NOT EXISTS inside the bucket query,
    /// so nothing here decides whether a tile is drawn. Optional, and without
    /// it the Hide action is a command that does nothing rather than a crash,
    /// which is the same degradation every other seam on this type takes.
    /// </summary>
    private readonly IHiddenGameRepository? _hidden;

    /// <summary>
    /// Embedded patch-notes reader, handed down to the details modal. Optional:
    /// unregistered means the patch-notes button falls back to the system browser,
    /// the same degradation every other optional seam on this type takes.
    /// </summary>
    private readonly Core.Reading.IPatchNotesReader? _patchNotes;

    /// <summary>
    /// Manual IGDB assignment, handed down to the details modal. Optional,
    /// and without it the modal's left column carries no reassignment
    /// control — the same degradation every other seam here takes.
    /// </summary>
    private readonly Services.IIgdbAssignmentService? _igdb;

    /// <summary>
    /// Per-field metadata editing, handed down to the details modal.
    /// Optional, and without it the modal carries no Edit details link —
    /// the same degradation every other optional seam here takes.
    /// </summary>
    private readonly Services.IWorkMetadataEditService? _metadataEdits;

    /// <summary>
    /// The OS file dialog behind the editor's two art rows. Optional
    /// independently of <see cref="_metadataEdits"/>: omitting it costs the
    /// "choose a file" route on cover and background art and leaves the URL
    /// route untouched.
    /// </summary>
    private readonly Services.IImageFilePicker? _imagePicker;

    /// <summary>
    /// Carries the assignment confirmation note across the reload and reopen,
    /// since the view model that made the choice does not survive the rebuild.
    /// Read once by the next modal instance this library builds.
    /// </summary>
    private string? _igdbNote;

    /// <summary>
    /// Carries the art confirmation note across the reload and reopen, for
    /// the metadata editor. Read once by the next modal instance this
    /// library builds. The same carrier as <see cref="_igdbNote"/>.
    /// </summary>
    private string? _metadataNote;

    private IReadOnlyList<GameTileViewModel> _allTiles = [];
    private FacetSnapshot _facets = FacetSnapshot.Empty;

    /// <summary>
    /// One entry per VISIBLE ownership, built in the same pass as the tiles
    /// so the modal cannot report a figure the grid does not show.
    /// </summary>
    private IReadOnlyList<CoverageEntry> _coverage = [];

    /// <summary>
    /// The live same-game map, read once per load. Expansion links are a
    /// different type and cannot reach any number from here.
    /// </summary>
    private SameGameResolution _resolution = SameGameResolution.Empty;

    /// <summary>
    /// The live expansion map, read from the same snapshot.
    /// A different TYPE from <see cref="_resolution"/>, deliberately:
    /// <c>ExpansionGrouping</c> has no <c>Resolve</c>, so it cannot be handed
    /// to anything that computes a count, a bucket, a playtime or a
    /// recommendation. It reaches the details modal and nothing else.
    /// </summary>
    private ExpansionGrouping _expansions = ExpansionGrouping.Empty;

    /// <summary>
    /// Every work by id, for the display title and cover of a linked child.
    /// </summary>
    private IReadOnlyDictionary<long, Work> _workById = new Dictionary<long, Work>();

    private readonly IWorkRatingRepository? _workRatings;

    private readonly IWorkImageRepository? _workImages;

    private readonly Services.IGameRefetch? _refetch;

    private string? _refetchNote;

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
        DormancyRamp? ramp = null,
        IPlaytimeSnapshotRepository? snapshots = null,
        IFacetRepository? facets = null,
        IGameListRepository? lists = null,
        Services.IEpicLaunchKeys? epicLaunchKeys = null,
        Services.GameLaunchService? launcher = null,
        LaunchStatusViewModel? launchStatus = null,
        JournalPromptViewModel? journal = null,
        ICoverLeases? leases = null,
        IIdentityLinkRepository? identityLinks = null,
        IAchievementQueryRepository? achievements = null,
        IHiddenGameRepository? hidden = null,
        Core.Reading.IPatchNotesReader? patchNotes = null,
        Services.IIgdbAssignmentService? igdb = null,
        Services.IWorkMetadataEditService? metadataEdits = null,
        Services.IImageFilePicker? imagePicker = null,
        IWorkRatingRepository? workRatings = null,
        IWorkImageRepository? workImages = null,
        Services.IGameRefetch? refetch = null,
        Winnow.Core.Repositories.IStorefrontRepository? storefrontCache = null,
        ISessionRepository? sessions = null)
    {
        _storefrontCache = storefrontCache;
        _workRatings = workRatings;
        _workImages = workImages;
        _refetch = refetch;
        _igdb = igdb;
        _metadataEdits = metadataEdits;
        _imagePicker = imagePicker;
        _patchNotes = patchNotes;
        _identityLinks = identityLinks;
        _achievements = achievements;
        _hidden = hidden;
        _libraryQueries = libraryQueries;
        _ownerships = ownerships;
        _releases = releases;
        _works = works;
        _updateEvents = updateEvents;
        _sessions = sessions;
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
        Lists.MembershipChanged += (_, _) => ApplyFilter();

        Ramp = ramp ?? new DormancyRamp();

        // One subscription for the whole wall rather than one per tile: the
        // tiles are replaced wholesale on every reload, and 606 subscriptions to
        // a process-lifetime object would keep every superseded generation of
        // them alive. The library owns the tiles, so the library relays.
        Ramp.PropertyChanged += OnRampChanged;

        // §7 copy, exactly. Order matches the mock rail.
        Buckets =
        [
            new BucketViewModel(LibraryBuckets.StaleButPatched, "Patched", showsFlarePip: true),
            new BucketViewModel(LibraryBuckets.NeverPlayed, "Never played"),
            new BucketViewModel(LibraryBuckets.Bounced, "Started"),
            new BucketViewModel(LibraryBuckets.Retired, "Played out"),
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
            // Any entry, not just the primary. A collapsed tile stands for
            // every ownership it folded, and the feed and the journal both
            // ask by the ownership they happened to record.
            if (tile.Covers(ownershipId))
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
            if (tile.CoversRelease(releaseId))
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
    /// Whether works whose stored maturity evidence reads as 18+ appear in the
    /// library. False by default; a work with no evidence is never explicit and
    /// is unaffected either way. Applied in the bucket query for exactly the
    /// reason <see cref="ShowNonGameEntries"/> is — every rail count is computed
    /// from the rows that query returns, so filtering anywhere else would let
    /// the counts and the grid disagree. Written by
    /// <see cref="LibrarySettingsViewModel"/>, which reloads on change.
    /// </summary>
    public bool ShowExplicitContent { get; set; }

    public MaturityTier MaturityCap { get; set; } = BucketThresholds.NoMaturityCap;

    /// <summary>
    /// Whether an expansion is drawn folded into its base game's tile rather
    /// than as a tile of its own (TASK-70.5 AC6). OFF BY DEFAULT: with it off
    /// an expansion link changes no count, no playtime, no bucket and no
    /// recommendation, which is the guarantee <c>expansion_of</c> makes.
    ///
    /// <para>Applied HERE and not in the bucket query, unlike
    /// <see cref="ShowNonGameEntries"/>. <c>RecommendationEngine</c> reads the
    /// same <c>ILibraryQueryRepository</c> the grid does, so a fold applied
    /// down there would take an unplayed expansion out of the feed as well as
    /// out of the grid — the one thing this task's AC5 forbids. Folding above
    /// the shared query keeps the recommender's view of the library exactly
    /// what it was.</para>
    ///
    /// <para>Playtime never rolls up either way. A folded expansion's minutes
    /// are not added to its base; they stay the expansion's own figure and are
    /// reported on the base game's details modal, which reads coverage rows
    /// this fold deliberately leaves in place.</para>
    ///
    /// Written by <see cref="DisplaySettingsViewModel"/>, which reloads on change.
    /// </summary>
    public bool GroupExpansions { get; set; }

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
    [NotifyPropertyChangedFor(
        nameof(HasAlphabetSections), nameof(ShowBrowseSpine), nameof(DisplayedAlphabetSections))]
    public partial IReadOnlyList<AlphabetSectionViewModel> AlphabetSections { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BucketViewModel? SelectedBucket { get; set; }

    /// <summary>
    /// Whether the library is the screen on show. The rail's Volt edge means
    /// "this is where you are" and exactly one row ever carries it, so while
    /// the Feed or another screen is up the rows keep their underlying
    /// selection (<see cref="SelectedBucket"/> is untouched) and drop the
    /// visible mark. Written by the shell whenever IsLibraryVisible changes.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCurrentScreen { get; set; } = true;

    /// <summary>Narrowest tile the density slider can produce (108x162 at 2:3).</summary>
    public const double MinimumTileWidth = 108;

    /// <summary>Widest tile the density slider can produce (200x300 at 2:3).</summary>
    public const double MaximumTileWidth = 200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileHeight), nameof(Density))]
    public partial double TileWidth { get; set; } = 148;

    /// <summary>2:3 portrait, always derived from the density slider's width.</summary>
    public double TileHeight => TileWidth * 1.5;

    /// <summary>
    /// The slider's own value: TileWidth mirrored about the midpoint of the 108..200 range,
    /// so dragging right narrows tiles and fits more of them. TileWidth stays the stored
    /// quantity, leaving grid geometry, cover-decode width buckets and every existing binding
    /// untouched.
    /// </summary>
    public double Density
    {
        get => MinimumTileWidth + MaximumTileWidth - TileWidth;
        set => TileWidth = Math.Clamp(
            MinimumTileWidth + MaximumTileWidth - value,
            MinimumTileWidth,
            MaximumTileWidth);
    }

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
        nameof(ShowIdleSortUp), nameof(ShowIdleSortDown),
        nameof(ShowBrowseSpine), nameof(ShowAlphabetLabels), nameof(ShowSortNotches),
        nameof(BrowseSpineTooltip), nameof(DisplayedAlphabetSections))]
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
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(AddToListLabel), nameof(HideLabel))]
    [NotifyCanExecuteChangedFor(
        nameof(BeginAddToListCommand),
        nameof(RemoveFromOpenListCommand),
        nameof(HideSelectionCommand),
        nameof(MoveUpInListCommand),
        nameof(MoveDownInListCommand))]
    public partial IReadOnlyList<GameTileViewModel> SelectedTiles { get; set; } = [];

    public bool HasSelection => SelectedTiles.Count > 0;

    /// <summary>The button names the number it is about once there is more than one.</summary>
    public string AddToListLabel
        => SelectedTiles.Count > 1 ? $"Add {SelectedTiles.Count:N0} to list" : "Add to list";

    /// <summary>
    /// The context menu's Hide, naming the number once there is more than one,
    /// on the same rule <see cref="AddToListLabel"/> follows.
    /// </summary>
    public string HideLabel
        => SelectedTiles.Count > 1
            ? string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LibrarySettingsCopy.HideManyMenuFormat,
                SelectedTiles.Count)
            : LibrarySettingsCopy.HideMenuItem;

    /// <summary>Tooltip on Hide, in the context menu and on the details modal.</summary>
    public string HideTooltip => LibrarySettingsCopy.HideTooltip;

    /// <summary>The details modal's own Hide, which is always about one game.</summary>
    public string HideDetailsLabel => LibrarySettingsCopy.HideDetailsButton;

    /// <summary>
    /// The open detail modal, or null. §5.3 caps the tile at four facts, which
    /// is only a defensible cap because this exists.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsOpen))]
    public partial GameDetailsViewModel? Details { get; set; }

    public bool IsDetailsOpen => Details is not null;

    /// <summary>
    /// The screenshot lightbox, one instance for the life of this view model.
    /// It lives here rather than on <see cref="Details"/> so the window can
    /// bind <c>Library.Lightbox.IsOpen</c> down a path that is never null — an
    /// <c>IsVisible</c> binding that resolves to <c>UnsetValue</c> falls back
    /// to the property's default of <c>true</c>, so a null path would draw an
    /// empty overlay over the library.
    /// </summary>
    public ScreenshotLightboxViewModel Lightbox { get; } = new();

    // Every route that replaces or drops the modal — closing, hiding,
    // reopening after a refetch or an assignment — takes the overlay down
    // with it. One hook here rather than a call at each site, so a route
    // added later cannot leave an orphaned overlay over the library.
    // The outgoing modal is disposed here for the same reason: its cover, its
    // screenshot thumbnails, its IGDB candidate rows and its metadata previews
    // are all held under leases, and a lease nobody releases is decoded art the
    // memory cache may not free.
    partial void OnDetailsChanged(GameDetailsViewModel? oldValue, GameDetailsViewModel? newValue)
    {
        Lightbox.Close();

        if (!ReferenceEquals(oldValue, newValue))
        {
            oldValue?.Dispose();
        }
    }

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
    /// The modal for naming a list, choosing a membership target or confirming a delete.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptOpen), nameof(ShowCutBar), nameof(ShowActionBar))]
    public partial ActionPromptViewModel? Prompt { get; set; }

    public bool IsPromptOpen => Prompt is not null;

    public bool ShowCutBar => IsCut;

    public bool ShowActionBar => IsCut;

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

    public bool HasAlphabetSections => AlphabetSections.Any(section => section.IsAvailable);

    public bool ShowBrowseSpine => HasAlphabetSections;

    public bool ShowAlphabetLabels
        => Sort is LibrarySort.NameAscending or LibrarySort.NameDescending;

    public bool ShowSortNotches => !ShowAlphabetLabels;

    public string BrowseSpineTooltip
        => ShowAlphabetLabels ? "Drag to browse by letter" : "Drag to browse this order";

    /// <summary>
    /// The drag surface follows the list's direction, so moving down the spine
    /// always moves down the alphabetically sorted library.
    /// </summary>
    public IEnumerable<AlphabetSectionViewModel> DisplayedAlphabetSections
        => Sort == LibrarySort.NameDescending
            ? AlphabetSections.Reverse()
            : AlphabetSections;

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
        var thresholds = BucketThresholds.Default with
        {
            ShowNonGameEntries = ShowNonGameEntries,
            ShowExplicitContent = ShowExplicitContent,
            MaturityCap = MaturityCap,
        };
        // Microsoft.Data.Sqlite executes synchronously even through async APIs.
        // Read on a worker; only publish models after returning to the UI context.
        var loaded = await Task.Run(async () =>
        {
            var snapshot = await _libraryQueries.GetSnapshotAsync(thresholds);
            var identity = _identityLinks is null ? IdentityResolution.Empty : await _identityLinks.GetResolutionAsync();
            var facets = _facetRepository is null ? FacetSnapshot.Empty : await _facetRepository.GetSnapshotAsync();
            var pins = _igdb is null ? new HashSet<long>() : await _igdb.GetLivePinnedWorkIdsAsync();
            var epic = _epicLaunchKeys is null
                ? new Dictionary<string, EpicLaunchKey>()
                : (IReadOnlyDictionary<string, EpicLaunchKey>)await _epicLaunchKeys.GetAllAsync();
            var storefronts = _storefrontCache is null
                ? new Dictionary<string, StorefrontDetails>()
                : await _storefrontCache.ReadAllAsync();
            return (snapshot, identity, facets, pins, epic, storefronts);
        });
        var bucketRows = loaded.snapshot.Buckets;
        var ownerships = loaded.snapshot.Ownerships;
        var works = loaded.snapshot.Works;
        _resolution = loaded.identity.SameGame;
        _expansions = loaded.identity.Expansions;
        _facets = loaded.facets;
        var pinnedWorkIds = loaded.pins;
        var releasesByWork = loaded.snapshot.Releases.ToLookup(release => release.WorkId);
        var externalIdsByRelease = loaded.snapshot.ExternalIds.ToLookup(id => id.ReleaseId);
        // release id → its work, and release id → the provider id its cover art
        // is fetched under. Resolved in memory from the bulk snapshot.
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
        var epicLaunchKeys = loaded.epic;
        var storefronts = loaded.storefronts;

        foreach (var work in works)
        {
            foreach (var release in releasesByWork[work.Id])
            {
                workByRelease[release.Id] = work;

                // Steam's portrait capsule is the first source; the cover cache
                // tries any further registered source for the same key, so IGDB
                // art can fill the gaps without this view model changing.
                var externalIds = externalIdsByRelease[release.Id];
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
                    steamAppIdByRelease[release.Id] = steam.ProviderId;
                }

                // Cover-key precedence:
                //   0. user-set art (migration 0027)
                //   1. a live IGDB pin on this work, when it yields an image id
                //   2. the Steam portrait capsule for this release's appid
                //   3. the image id in the work's stored cover_url
                //
                // Rule 0 outranks the live pin because the value in cover_url
                // IS the user's under the field-source model — there is nothing
                // to outrank it — and a later metadata fetch replaces that value
                // rather than layering over it.
                //
                // A pin outranks the capsule because the user is saying the
                // storefront art is wrong too. Read off the release's OWN work
                // row, never the resolved work: the pin and the cover_url it
                // rewrote are columns of the same row, and resolving through the
                // same-game map would pair one work's pin with another's URL.
                //
                // A pinned entry with no IGDB cover falls through to the
                // capsule, not to the placeholder. Nothing is evicted from the
                // cache: a CoverKey.Igdb names the artwork asset, so a pin
                // produces a key that has never been fetched, and clearing one
                // returns to the Steam key whose cached bytes are still correct.
                //
                // Rule 3 is the only cover path an Epic or GOG tile can
                // reach: no Steam appid means no portrait capsule and no
                // external_games lookup, so the stored cover_url — which
                // names the IGDB artwork asset directly — is all there is.
                // The key must be the image id, not works.igdb_id, because
                // that column is UNIQUE: of an Epic title and its Steam
                // twin only one row holds the id, but both hold the same
                // cover_url. Keying on the game id would leave one half
                // of every cross-store duplicate pair without a cover key.
                var pinnedImageId = pinnedWorkIds.Contains(work.Id)
                    ? IgdbImageUrl.ImageId(work.CoverUrl)
                    : null;

                // Rule 0: user-set art — see the precedence block above.
                if (UserArtRef.Token(work.CoverUrl) is { Length: > 0 } userArtToken)
                {
                    coverKeyByRelease[release.Id] = CoverKey.User(userArtToken);
                }
                else if (pinnedImageId is { Length: > 0 })
                {
                    coverKeyByRelease[release.Id] = CoverKey.Igdb(pinnedImageId);
                }
                else if (steam is not null)
                {
                    coverKeyByRelease[release.Id] = CoverKey.Steam(steam.ProviderId);
                }
                else if (IgdbImageUrl.ImageId(work.CoverUrl) is { Length: > 0 } imageId)
                {
                    coverKeyByRelease[release.Id] = CoverKey.Igdb(imageId);
                }
            }
        }

        _workById = works.ToDictionary(w => w.Id);

        // The art a linked child borrows: the primary work's own cover, taken
        // from its lowest release id that has one so the choice is stable across
        // loads. Built for every work, used only by a child.
        var coverKeyByWork = new Dictionary<long, CoverKey>();
        foreach (var releaseId in coverKeyByRelease.Keys.OrderBy(id => id))
        {
            if (workByRelease.TryGetValue(releaseId, out var owner))
            {
                coverKeyByWork.TryAdd(owner.Id, coverKeyByRelease[releaseId]);
            }
        }

        var ownershipById = ownerships.ToDictionary(o => o.Id);
        var now = DateTime.UtcNow;
        var coverage = new List<CoverageEntry>(bucketRows.Count);

        EpicLaunchKey? EpicKeyFor(long releaseId)
            => epicCatalogIdByRelease.TryGetValue(releaseId, out var catalogItemId)
                && epicLaunchKeys.TryGetValue(catalogItemId, out var key)
                    ? key
                    : null;

        // One tile per resolved work, not one per ownership (TASK-70.6).
        // The bucket query already resolved same-game links (and only
        // same-game links, so an expansion cannot collapse anything) and
        // already folded each game's figures, so all that happens here is
        // that the rows of one game are gathered in order and handed to one
        // tile. With nothing linked every group is one row and the grid is
        // exactly the grid it was.
        var groups = new Dictionary<long, List<OwnershipBucket>>();
        var groupOrder = new List<long>();
        foreach (var row in bucketRows)
        {
            if (!groups.TryGetValue(row.ResolvedWorkId, out var members))
            {
                groups[row.ResolvedWorkId] = members = [];
                groupOrder.Add(row.ResolvedWorkId);
            }

            members.Add(row);
        }

        // TASK-70.5 AC6. Which groups give up their own tile to their base
        // game's, decided BEFORE any tile is built so the answer cannot depend
        // on the order groups happen to arrive in.
        //
        // An expansion folds only when its base is itself visible here. A pack
        // whose base is not owned, or is filtered out of this view, keeps its
        // tile: it is the only copy of that game the user has, and folding it
        // into something the grid is not drawing would delete it. That is the
        // same shape VariantGrouping.CountsAsTitle uses for a demo.
        //
        // The rows of a folded group are still walked below, because the
        // details modal's Expansions section reads the coverage entries this
        // loop writes. Only the TILE is withheld.
        var foldedInto = new Dictionary<long, long>();
        if (GroupExpansions && !_expansions.IsEmpty)
        {
            foreach (var resolvedWorkId in groupOrder)
            {
                if (_expansions.BaseOf(resolvedWorkId) is not { } baseWorkId)
                {
                    continue;
                }

                var baseResolved = _resolution.Resolve(baseWorkId);
                if (baseResolved != resolvedWorkId && groups.ContainsKey(baseResolved))
                {
                    foldedInto[resolvedWorkId] = baseResolved;
                }
            }
        }

        var folded = new List<(long BaseResolvedWorkId, GameTileViewModel Tile)>();
        var tileByResolvedWorkId = new Dictionary<long, GameTileViewModel>(groupOrder.Count);

        var tiles = new List<GameTileViewModel>(groupOrder.Count);
        foreach (var resolvedWorkId in groupOrder)
        {
            // The primary work's own entries lead, then the covered titles'
            // by work and ownership — the same order IdentityCoverage puts
            // its rows in, so the chips on a tile and the rows in ALSO
            // COVERS read in the same sequence. The order is total, so the
            // primary entry and the chip order do not shuffle between loads.
            var members = groups[resolvedWorkId];
            members.Sort((a, b) =>
            {
                var aOwn = a.WorkId == resolvedWorkId ? 0 : 1;
                var bOwn = b.WorkId == resolvedWorkId ? 0 : 1;
                if (aOwn != bOwn)
                {
                    return aOwn - bOwn;
                }

                return a.WorkId == b.WorkId
                    ? a.OwnershipId.CompareTo(b.OwnershipId)
                    : a.WorkId.CompareTo(b.WorkId);
            });

            var primaryRow = members[0];

            // The work the user is SHOWN. On a collapsed tile it is the primary
            // work, whose name and art both store entries have read since 70.4;
            // the collapse simply stops drawing the second tile.
            var display = _workById.GetValueOrDefault(resolvedWorkId)
                ?? workByRelease.GetValueOrDefault(primaryRow.ReleaseId);

            // The primary entry's own art, which for an unlinked tile is the
            // exact key it has always had. A group whose primary release has no
            // key of its own falls back to the primary work's, so a collapsed
            // tile is never left on a placeholder while one of its members has
            // a cover.
            var tileCoverKey = coverKeyByRelease.TryGetValue(primaryRow.ReleaseId, out var own)
                ? own
                : coverKeyByWork.TryGetValue(resolvedWorkId, out var primaryKey)
                    ? primaryKey
                    : (CoverKey?)null;

            var entries = new List<TileEntry>(members.Count);
            foreach (var member in members)
            {
                var ownership = ownershipById.GetValueOrDefault(member.OwnershipId);
                var memberWork = workByRelease.GetValueOrDefault(member.ReleaseId);

                entries.Add(TileEntry.For(
                    ownershipId: member.OwnershipId,
                    releaseId: member.ReleaseId,
                    workId: member.WorkId,
                    store: ownership?.Store ?? "?",
                    playtimeMinutes: member.PlaytimeMinutes,
                    lastPlayedAt: member.LastPlayedAt,
                    ownership: ownership,
                    steamAppId: steamAppIdByRelease.GetValueOrDefault(member.ReleaseId),
                    gogProductId: gogProductIdByRelease.GetValueOrDefault(member.ReleaseId),
                    epicLaunchKey: EpicKeyFor(member.ReleaseId),
                    storefront: storefronts.GetValueOrDefault(ownership?.Store == "epic"
                        ? "epic:" + EpicKeyFor(member.ReleaseId)?.Namespace
                        : "gog:" + gogProductIdByRelease.GetValueOrDefault(member.ReleaseId))));

                coverage.Add(new CoverageEntry
                {
                    OwnershipId = member.OwnershipId,
                    ReleaseId = member.ReleaseId,
                    WorkId = member.WorkId,
                    Title = memberWork?.Name ?? $"Release {member.ReleaseId}",
                    Store = ownership?.Store ?? "?",
                    PlaytimeMinutes = member.PlaytimeMinutes,
                    LastPlayedAt = member.LastPlayedAt,
                });
            }

            var tile = new GameTileViewModel(
                entries: entries,
                // Playtime, last-played, the bucket and therefore the unread
                // badge all come off this one object, which the read model
                // folded with CoveragePlaytime.Across.
                game: primaryRow.Game,
                title: display?.Name ?? $"Release {primaryRow.ReleaseId}",
                nowUtc: now,
                coverKey: tileCoverKey,
                covers: _leases,
                work: display,
                ramp: Ramp,
                // The §7 name, shared with the detail view rather than storing a
                // second copy of the rail's vocabulary.
                bucketLabel: BucketLabelFor(primaryRow.Game.Bucket));

            // The compact grid actions raise library-owned commands; the tile
            // remains a projection and never reaches into repositories itself.
            tile.OpenDetailsCommand = OpenDetailsCommand;
            tile.PrimaryActionCommand = LaunchCommand;

            // What the game IS, for the filter panel to cut on. Read from the
            // one snapshot rather than passed through the constructor: it is a
            // library-wide join, and it is legitimately empty on a database the
            // backfill has not reached yet.
            //
            // Unioned across the tile's entries. A genre the Steam entry
            // carries and the Epic one does not is still true of the game,
            // and a cut that dropped the tile because the primary happened
            // to be the unenriched entry would hide a game the panel says
            // it is showing.
            var facetIds = new List<long>();
            var gameModes = new List<string>();
            foreach (var entry in entries)
            {
                var releaseFacets = _facets.ByRelease.TryGetValue(entry.ReleaseId, out var found)
                    ? found
                    : ReleaseFacets.Empty(entry.ReleaseId);

                foreach (var facetId in releaseFacets.FacetIds)
                {
                    if (!facetIds.Contains(facetId))
                    {
                        facetIds.Add(facetId);
                    }
                }

                foreach (var mode in releaseFacets.GameModes)
                {
                    if (!gameModes.Contains(mode, StringComparer.Ordinal))
                    {
                        gameModes.Add(mode);
                    }
                }
            }

            // One merged row per GAME, split by kind once — the same call a
            // single-entry tile has always made, over a set that is identical
            // when there is only one entry.
            tile.Facets = ViewModels.Filters.TileFacets.From(
                new ReleaseFacets(tile.ReleaseId, facetIds, gameModes), _facets.ById);

            // Build the filterable row once so panel and live lists share one implementation.
            tile.Row = new FilterableRow(
                ReleaseId: tile.ReleaseId,
                OwnershipId: tile.OwnershipId,
                Bucket: tile.Bucket,
                Stores: tile.Stores,
                Title: tile.Title,
                // Two-valued: unknown install state counts as not-on-disk for filtering.
                Installed: tile.IsOnDisk,
                HasUnread: tile.HasUnread,
                FirstReleaseYear: tile.ReleaseYear,
                FacetIds: facetIds,
                GameModes: gameModes);

            // Folded packs are held back rather than dropped: the base tile
            // they belong to may not have been built yet, and the marker it
            // wears is derived from the pack's own figures below.
            if (foldedInto.TryGetValue(resolvedWorkId, out var baseResolvedWorkId))
            {
                folded.Add((baseResolvedWorkId, tile));
            }
            else
            {
                tileByResolvedWorkId[resolvedWorkId] = tile;
                tiles.Add(tile);
            }
        }

        // The marker the fold owes the user. Taking the pack's tile away also
        // takes it off the Never played rail, so the base game says the fact
        // instead: you have this, and something you own for it is untouched.
        foreach (var (baseResolvedWorkId, packTile) in folded)
        {
            if (!tileByResolvedWorkId.TryGetValue(baseResolvedWorkId, out var baseTile))
            {
                continue;
            }

            baseTile.GroupedExpansionCount++;
            if (packTile.PlaytimeMinutes <= 0)
            {
                baseTile.HasUnplayedExpansion = true;
            }
        }

        var selectedOwnershipId = SelectedTile?.OwnershipId;
        _allTiles = tiles;
        _coverage = coverage;

        // Counted over tiles, on the game's bucket. The rail and the grid
        // are the same set now, so a bucket count is the number of tiles
        // the rail would show, not the number of ownership rows behind them.
        foreach (var bucket in Buckets)
        {
            bucket.Count = _allTiles.Count(t => t.Bucket == bucket.Key);
        }

        // Rail's "All games" count.
        AllGames.Count = _allTiles.Count;

        TotalCount = _allTiles.Count;
        TotalCountText = TotalCount.ToString("N0");
        SearchPlaceholder = $"Search {TotalCountText} titles…";

        // Rebuild filter options; selections survive by key across reloads.
        Filters.Rebuild(_allTiles, _facets);

        Lists.ApplySnapshot(loaded.snapshot.Lists, loaded.snapshot.ListItems);

        // Re-derive rail selection marks after list objects are replaced.
        MarkRailSelection();

        _loaded = true;
        ApplyFilter();
        if (selectedOwnershipId is { } selectedId)
        {
            var selected = VisibleTiles.FirstOrDefault(t => t.Covers(selectedId));
            SelectTile(selected);
        }

        // Background install refresh must also reach a modal that is already open.
        // Keep its instance so an in-progress metadata edit or disclosure survives.
        if (Details is { } details && TileForOwnership(details.Tile.OwnershipId) is { } refreshed)
        {
            details.RefreshTileActions(refreshed);
        }

        // Notify listeners that the tile set has been replaced.
        TilesChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// Opens the detail modal for the tile holding one of
    /// <paramref name="releaseIds"/>. The Merges screen asks for this when a
    /// candidate row is clicked, so the entries can be compared before
    /// answering; the modal is the library's and is drawn over every pane.
    /// </summary>
    /// <returns>False when no tile holds any of the releases, in which case nothing opens.</returns>
    public async Task<bool> OpenDetailsForReleasesAsync(IReadOnlyList<long> releaseIds)
    {
        ArgumentNullException.ThrowIfNull(releaseIds);

        var tile = _allTiles.FirstOrDefault(t => releaseIds.Contains(t.ReleaseId))
            ?? _allTiles.FirstOrDefault(t => t.ReleaseIds.Any(releaseIds.Contains));
        if (tile is null)
        {
            return false;
        }

        await OpenDetailsAsync(tile);
        return true;
    }

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

        // Every entry's updates, not just the primary's. The unread badge
        // is the game's bucket, computed from the latest patch anywhere in
        // the group, so a modal that only listed the primary release's
        // events could show a badge with nothing under it — the Steam copy
        // patched, the Epic copy primary.
        var events = new List<Core.Domain.UpdateEvent>();
        foreach (var releaseId in target.ReleaseIds)
        {
            events.AddRange(await _updateEvents.GetByReleaseAsync(releaseId));
        }

        // Newest first; each row knows whether it landed after last play -- the
        // GAME's last play, which is the same date the badge was decided on.
        var updates = events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => UpdateEventViewModel.Create(e, target.LastPlayedUtc))
            .ToList();

        IReadOnlyList<PlaytimeSnapshot> history = _snapshots is null
            ? []
            : await _snapshots.GetByOwnershipAsync(target.OwnershipId);

        var workId = GameWorkIdFor(target);

        IReadOnlyList<WorkRating> ratings = _workRatings is null || workId is null
            ? []
            : await _workRatings.GetForWorkAsync(workId.Value);

        IReadOnlyList<WorkImages> images = _workImages is null || workId is null
            ? []
            : await _workImages.GetForWorkAsync(workId.Value);

        Details = new GameDetailsViewModel(
            target,
            BucketLabelFor(target.Bucket),
            updates,
            DateTime.UtcNow,
            snapshots: history,
            covers: _leases,
            coverage: await BuildCoverageAsync(target),
            expansions: BuildExpansions(target),
            lists: await BuildListsAsync(target),
            patchNotes: _patchNotes,
            igdbMatch: await BuildIgdbMatchAsync(target),
            metadataEditor: BuildMetadataEditor(target),
            hideGame: HideGameCommand,
            ratings: ratings,
            images: images,
            ownerships: await BuildAcquisitionAsync(target),
            refetch: BuildRefetch(workId),
            lightbox: Lightbox,
            journal: await BuildJournalAsync(target),
            addToList: new RelayCommand(() => BeginAddToListFor([target])));
    }

    /// <summary>
    /// Reads every ownership behind a grouped game. A journal note belongs to
    /// the sitting that wrote it, so the rows stay separate and newest is the
    /// only order that makes the history read as a journal.
    /// </summary>
    private async Task<GameJournalViewModel?> BuildJournalAsync(GameTileViewModel target)
    {
        if (_sessions is null)
        {
            return null;
        }

        var entries = new List<SessionJournalEntry>();
        foreach (var ownershipId in target.OwnershipIds.Distinct())
        {
            entries.AddRange(await _sessions.GetJournalEntriesByOwnershipAsync(ownershipId));
        }

        return new GameJournalViewModel(entries, Journal.PromptEnabled, _sessions);
    }

    private async Task<IReadOnlyList<Ownership>> BuildAcquisitionAsync(GameTileViewModel target)
    {
        var rows = new List<Ownership>();
        foreach (var ownershipId in target.OwnershipIds)
        {
            if (await _ownerships.GetAsync(ownershipId) is { } ownership)
            {
                rows.Add(ownership);
            }
        }

        return rows;
    }

    private GameRefetchViewModel? BuildRefetch(long? workId)
    {
        if (_refetch is null || workId is null)
        {
            return null;
        }

        var note = _refetchNote;
        _refetchNote = null;

        return new GameRefetchViewModel(_refetch, workId.Value, AfterRefetchAsync, note);
    }

    private async Task AfterRefetchAsync(string note)
    {
        _refetchNote = note;
        await ReopenDetailsAsync();
    }

    /// <summary>
    /// Builds the left column's IGDB reassignment control, or null when no
    /// assignment service is registered or the tile resolves to no work id.
    /// Uses the same resolved work id the LISTS and EXPANSIONS sections
    /// derive, so all three answer for the game rather than for a store
    /// entry. The live pin is read here, on open, because it decides
    /// whether the Clear control is drawn.
    /// </summary>
    private async Task<GameIgdbMatchViewModel?> BuildIgdbMatchAsync(GameTileViewModel target)
    {
        if (_igdb is null || GameWorkIdFor(target) is not { } workId)
        {
            return null;
        }

        var note = _igdbNote;
        _igdbNote = null;

        return new GameIgdbMatchViewModel(
            _igdb,
            workId,
            target.Title,
            pin: await _igdb.GetPinAsync(workId),
            covers: _leases,
            afterChange: AfterIgdbChangeAsync,
            note: note,
            linkSameGame: _identityLinks is null
                ? null
                : parentWorkId => LinkIgdbClaimAsync(parentWorkId, workId));
    }

    /// <summary>
    /// Builds the right column's per-field metadata editor, or null when no
    /// edit service is registered or the tile resolves to no work id. Uses
    /// the same resolved work id <see cref="BuildIgdbMatchAsync"/> uses,
    /// through <c>GameWorkIdFor</c>, so the editor writes the row the lists,
    /// expansions and IGDB surfaces all answer for. Wires the text-change
    /// callback to <see cref="AfterMetadataTextChangeAsync"/>, which is the
    /// rename path for a name save. Nothing is read here: the editor loads
    /// its own fields on first disclosure, which keeps opening the modal
    /// the same cost it was before TASK-119.
    /// </summary>
    private GameMetadataEditorViewModel? BuildMetadataEditor(GameTileViewModel target)
    {
        if (_metadataEdits is null || GameWorkIdFor(target) is not { } workId)
        {
            return null;
        }

        var note = _metadataNote;
        _metadataNote = null;

        return new GameMetadataEditorViewModel(
            _metadataEdits,
            workId,
            covers: _leases,
            picker: _imagePicker,
            afterArtChange: AfterMetadataArtChangeAsync,
            afterTextChange: (field, value) => AfterMetadataTextChangeAsync(workId, field, value),
            note: note);
    }

    /// <summary>
    /// The text-save counterpart to <see cref="AfterMetadataArtChangeAsync"/>
    /// and pointedly not a reload: a text save must not reload because
    /// reloading would discard the drafts the user has in the other five
    /// rows (§10.10). Acts on <c>name</c> alone and only on a non-blank
    /// value; the other four text fields are not drawn anywhere outside
    /// the modal, which already refreshed its own rows.
    /// </summary>
    private Task AfterMetadataTextChangeAsync(long workId, string field, string? value)
    {
        if (!string.Equals(field, WorkFields.Name, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value))
        {
            return Task.CompletedTask;
        }

        RenameGame(workId, value.Trim());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Renames every tile whose resolved work id is <paramref name="workId"/>
    /// — every tile, not the selected one, because a same-game link group
    /// can put several tiles behind one work. Updates the work cache and
    /// the coverage entries that work owns, so the ALSO COVERS and
    /// EXPANSIONS sections built after this read the new name. Coverage
    /// entries are matched on the work id itself, not the resolved one,
    /// because a coverage row's title is the name its own work row carries.
    /// Then raises the open modal's headline and re-runs
    /// <see cref="ApplyFilter"/>, which re-applies the search cut and the
    /// sort in one pass.
    /// </summary>
    private void RenameGame(long workId, string name)
    {
        var renamed = false;
        foreach (var tile in _allTiles)
        {
            if (GameWorkIdFor(tile) != workId || string.Equals(tile.Title, name, StringComparison.Ordinal))
            {
                continue;
            }

            tile.Rename(name);
            renamed = true;
        }

        if (_workById.TryGetValue(workId, out var work)
            && !string.Equals(work.Name, name, StringComparison.Ordinal))
        {
            _workById = new Dictionary<long, Work>(_workById)
            {
                [workId] = work with { Name = name, NameIsProvisional = false },
            };
        }

        var coverage = new List<CoverageEntry>(_coverage.Count);
        var touched = false;
        foreach (var entry in _coverage)
        {
            if (entry.WorkId == workId && !string.Equals(entry.Title, name, StringComparison.Ordinal))
            {
                coverage.Add(entry with { Title = name });
                touched = true;
                continue;
            }

            coverage.Add(entry);
        }

        if (touched)
        {
            _coverage = coverage;
        }

        if (!renamed)
        {
            return;
        }

        Details?.NotifyTitleChanged();
        ApplyFilter();
    }

    /// <summary>
    /// An art field is the one edit whose result is not inside the modal.
    /// The stored value becomes a <c>winnow://user-art</c> reference, a
    /// cover key the grid computes at load, so only a reload draws the new
    /// art on the tile. Reopens on the same ownership and carries the
    /// confirmation across, exactly as <see cref="AfterIgdbChangeAsync"/>
    /// does. Text edits take <see cref="AfterMetadataTextChangeAsync"/>
    /// instead: reloading after one would discard the drafts the user has
    /// in the other five rows.
    /// </summary>
    private async Task AfterMetadataArtChangeAsync(string note)
    {
        _metadataNote = note;
        await ReopenDetailsAsync();
    }

    /// <summary>
    /// Accepts the details modal's same-game offer. Another work already
    /// holds the IGDB entry the user chose, and <c>works.igdb_id</c> is
    /// UNIQUE, so the two are the same game. The holder is the parent: it
    /// carries the <c>igdb_id</c>, which is the first rung of the Merges
    /// queue's own precedence ladder, and its metadata is the entry the
    /// user was reaching for.
    ///
    /// <para>Builds the same <see cref="IdentityLinkRequest"/>
    /// <c>MergeQueueViewModel.LinkAsync</c> builds — kind <c>same_game</c>,
    /// source <c>user</c> — so it is the existing identity-link path, not a
    /// second mechanism. Nothing is pinned: pinning the child to an id
    /// another row holds is what the UNIQUE constraint refused.</para>
    /// </summary>
    private async Task<bool> LinkIgdbClaimAsync(long parentWorkId, long childWorkId)
    {
        if (_identityLinks is null || parentWorkId == childWorkId)
        {
            return false;
        }

        try
        {
            await _identityLinks.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = parentWorkId,
                ChildWorkIds = [childWorkId],
                Kind = IdentityLinkKinds.SameGame,
                Source = IdentityLinkSources.User,
            });
        }
        catch (IdentityLinkRefusedException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reloads the library and reopens the modal on the same ownership — the
    /// same arrangement <c>SeparateAsync</c> uses. An assignment rewrites the
    /// work's name, year, publisher, summary and cover URL, so every field on
    /// the bound tile is stale. Clearing a pin comes through here too: it
    /// writes no metadata, but a live pin outranks the store capsule in the
    /// cover-key precedence, so dropping the pin gives a Steam-owned game its
    /// capsule back and only a reload draws it.
    /// </summary>
    private async Task AfterIgdbChangeAsync(string note)
    {
        _igdbNote = note;
        await ReopenDetailsAsync();
    }

    /// <summary>
    /// Reloads the library and reopens the detail modal on the ownership it
    /// was already showing. Nothing reopens when that game is no longer in
    /// the library.
    /// </summary>
    private async Task ReopenDetailsAsync()
    {
        var ownershipId = Details?.Tile.OwnershipId;
        await LoadAsync();

        if (ownershipId is not { } id)
        {
            return;
        }

        // Primary match first, then any tile whose entries contain it. A
        // same-game link folds this game into another tile as a non-primary
        // entry, so a primary-only lookup would miss it and close the modal
        // on the one action that is supposed to reopen it.
        var reopened = _allTiles.FirstOrDefault(t => t.OwnershipId == id)
            ?? _allTiles.FirstOrDefault(t => t.OwnershipIds.Contains(id));
        if (reopened is not null)
        {
            await OpenDetailsAsync(reopened);
        }
    }

    /// <summary>
    /// The resolved work id behind this tile, through <see cref="_resolution"/>.
    /// This is the game — not the store entry — and it is what the repository's
    /// membership query resolves against, so a game owned on two stores gets one
    /// answer.
    /// </summary>
    private long? GameWorkIdFor(GameTileViewModel target)
    {
        var entry = _coverage.FirstOrDefault(e => e.OwnershipId == target.OwnershipId);
        return entry is null ? null : _resolution.Resolve(entry.WorkId);
    }

    /// <summary>
    /// Builds the details modal's LISTS section: one row per hand-built list,
    /// ticked when any release in the game's live link group is a member.
    /// The work id comes from the coverage entry the modal already reads,
    /// through <see cref="SameGameResolution.Resolve"/>, exactly as the
    /// EXPANSIONS section derives its own.
    /// </summary>
    private async Task<GameListsViewModel> BuildListsAsync(GameTileViewModel target)
    {
        var byList = new Dictionary<long, List<long>>();
        if (GameWorkIdFor(target) is { } workId)
        {
            foreach (var membership in await Lists.MembershipForGameAsync(workId))
            {
                if (!byList.TryGetValue(membership.ListId, out var releases))
                {
                    releases = [];
                    byList[membership.ListId] = releases;
                }

                releases.Add(membership.ReleaseId);
            }
        }

        var rows = new List<GameListEntryViewModel>();
        foreach (var list in Lists.Lists)
        {
            IReadOnlyList<long> members = byList.TryGetValue(list.Id, out var releases)
                ? releases
                : [];

            rows.Add(new GameListEntryViewModel(
                list, members, (row, wanted) => ToggleListMembershipAsync(target, row, wanted)));
        }

        return new GameListsViewModel(rows);
    }

    /// <summary>
    /// The checkbox toggle. Ticking appends the tile's primary release.
    /// Unticking removes every release the membership rows name, which is
    /// the only way to leave a list you joined from a different store's
    /// copy of the game.
    /// </summary>
    private async Task ToggleListMembershipAsync(
        GameTileViewModel target, GameListEntryViewModel row, bool wanted)
    {
        if (wanted)
        {
            await Lists.AddToListAsync(row.List, [target.ReleaseId]);
            row.RecordMembership([.. row.MemberReleaseIds.Append(target.ReleaseId).Distinct()]);
        }
        else
        {
            var members = row.MemberReleaseIds.Count > 0
                ? row.MemberReleaseIds
                : [.. row.List.ReleaseIds.Intersect(target.ReleaseIds)];

            await Lists.RemoveFromListAsync(row.List, members);
            row.RecordMembership([]);
        }

        ApplyFilter();
    }

    /// <summary>
    /// Builds the EXPANSIONS section and the EXTENDS line, or null when this
    /// game neither has packs nor is one.
    ///
    /// <para>Derived from the rows this load already read, exactly as coverage
    /// is, so a grouping retracted a moment ago stops showing on the very next
    /// read. Every row carries its OWN minutes beside its OWN last-played, and
    /// there is deliberately no total: an expansion's hours are the
    /// expansion's, and adding them to the base game's would produce a figure
    /// no source reported about either game.</para>
    /// </summary>
    private GameExpansionsViewModel? BuildExpansions(GameTileViewModel target)
    {
        var entry = _coverage.FirstOrDefault(e => e.OwnershipId == target.OwnershipId);
        if (entry is null)
        {
            return null;
        }

        // The GAME, not the store entry: expansions hang off the work the
        // library files this game under, which for a linked pair is the
        // primary. A same-game child is never an expansion parent, because
        // depth one refuses it.
        var gameWorkId = _resolution.Resolve(entry.WorkId);

        var rows = new List<ExpansionRowViewModel>();
        foreach (var expansionWorkId in _expansions.ExpansionsOf(gameWorkId))
        {
            if (RowFor(expansionWorkId, expansionWorkId) is { } row)
            {
                rows.Add(row);
            }
        }

        // The other end of the same relation. The row's link child is THIS
        // game, because the link points from the pack at its base.
        ExpansionRowViewModel? extends = null;
        if (_expansions.BaseOf(gameWorkId) is { } baseWorkId)
        {
            extends = RowFor(baseWorkId, gameWorkId);
        }

        return rows.Count == 0 && extends is null
            ? null
            : new GameExpansionsViewModel(rows, extends, UngroupAsync);

        ExpansionRowViewModel? RowFor(long workId, long childWorkId)
        {
            // Every visible entry of that game, its own store entries folded
            // the way its own tile folds them. This is the game's own figure,
            // identical to the one the grid shows for it, and it is never
            // added to the game whose modal is open.
            var entries = new List<CoverageEntry>();
            var stores = new List<string>();
            foreach (var candidate in _coverage)
            {
                if (_resolution.Resolve(candidate.WorkId) != workId)
                {
                    continue;
                }

                entries.Add(candidate);
                if (!stores.Contains(candidate.Store, StringComparer.OrdinalIgnoreCase))
                {
                    stores.Add(candidate.Store);
                }
            }

            if (entries.Count == 0)
            {
                // Owned but filtered out of this load — a hidden non-game, a
                // consolidated demo, an account the user has scoped away. The
                // modal must not report a row the grid does not stand behind.
                return null;
            }

            var played = CoveragePlaytime.Across(entries);

            return new ExpansionRowViewModel(
                workId,
                childWorkId,
                _workById.TryGetValue(workId, out var work) ? work.Name : entries[0].Title,
                [.. stores.Select(StoreNaming.Badge)],
                string.Join(", ", stores.Select(StoreNaming.Label)),
                played.PlaytimeMinutes,
                played.LastPlayedAt);
        }
    }

    /// <summary>
    /// Retracts one expansion link from the details modal
    /// and reloads, reopening the modal on the same entry so the user sees the
    /// result where they asked for it. The same call the coverage section's
    /// Separate makes, because a link is a link: nothing is deleted, and
    /// grouping again is an ordinary act.
    /// </summary>
    private Task UngroupAsync(long childWorkId) => SeparateAsync(childWorkId);

    /// <summary>
    /// Builds the ALSO COVERS section. Derived from the rows this load
    /// already read, so it costs no library-wide query and cannot report an
    /// entry the grid does not show. Achievements are read per release for
    /// the group's releases and stay per release (§6.2).
    /// </summary>
    private async Task<GameCoverageViewModel?> BuildCoverageAsync(GameTileViewModel target)
    {
        var entry = _coverage.FirstOrDefault(e => e.OwnershipId == target.OwnershipId);
        if (entry is null)
        {
            return null;
        }

        var coverage = IdentityCoverage.For(entry.WorkId, _resolution, _coverage);

        var titleByWork = new Dictionary<long, string>();
        foreach (var row in coverage.OwnEntries.Concat(coverage.CoveredEntries))
        {
            if (_workById.TryGetValue(row.WorkId, out var work))
            {
                titleByWork[row.WorkId] = work.Name;
            }
        }

        var summaries = new Dictionary<long, ReleaseAchievementSummary>();
        if (_achievements is not null)
        {
            var releaseIds = coverage.OwnEntries
                .Concat(coverage.CoveredEntries)
                .Select(e => e.ReleaseId)
                .Distinct()
                .ToList();

            foreach (var summary in await _achievements.GetSummariesAsync(releaseIds))
            {
                summaries[summary.ReleaseId] = summary;
            }
        }

        return new GameCoverageViewModel(coverage, titleByWork, summaries, SeparateAsync);
    }

    /// <summary>
    /// Retracts one link from the details modal and reloads. The modal is
    /// reopened on the same ownership so the user sees the result where they
    /// asked for it. Nothing is deleted — the link row is stamped, so
    /// linking again is an ordinary act.
    /// </summary>
    private async Task SeparateAsync(long childWorkId)
    {
        if (_identityLinks is null)
        {
            return;
        }

        await _identityLinks.RetractLinkAsync(childWorkId);
        await ReopenDetailsAsync();
    }

    [RelayCommand]
    private void CloseDetails() => Details = null;

    // ══ Hiding (TASK-87) ════════════════════════════════════════════════════

    /// <summary>
    /// Hides one game and reloads. The grain is the resolved work, so hiding
    /// takes the whole link group and does not pop a demo back into the grid.
    /// The details modal closes because its subject is no longer in the library.
    /// The reload is what makes the grid, the list view, the feed and every rail
    /// count agree, since all four read the one bucket query the exclusion lives
    /// in. A repeat is quiet: the repository answers false when there was
    /// nothing to do.
    /// </summary>
    [RelayCommand]
    private async Task HideGameAsync(GameTileViewModel? tile)
    {
        var target = tile ?? SelectedTile;
        if (_hidden is null || target is null)
        {
            return;
        }

        await _hidden.HideAsync(target.Game.ResolvedWorkId);
        await AfterHidingAsync();
    }

    /// <summary>
    /// The context menu's Hide: acts on every marked tile rather than on the
    /// anchor, exactly as Add to list does. The grid marks one and the list
    /// view marks many, and one control reads whichever is in force. One reload
    /// for the whole set rather than one per tile.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task HideSelectionAsync()
    {
        if (_hidden is null)
        {
            return;
        }

        foreach (var workId in SelectedTiles.Select(t => t.Game.ResolvedWorkId).Distinct())
        {
            await _hidden.HideAsync(workId);
        }

        await AfterHidingAsync();
    }

    /// <summary>
    /// Closes the detail modal, drops the selection — the tiles it named no
    /// longer exist — and reloads the library.
    /// </summary>
    private async Task AfterHidingAsync()
    {
        Details = null;
        SelectedTiles = [];
        SelectedCount = 0;
        await LoadAsync();
    }

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
        BeginAddToListFor(SelectedTiles);
    }

    private void BeginAddToListFor(IReadOnlyList<GameTileViewModel> tiles)
    {
        var picked = tiles.Select(t => t.ReleaseId).ToList();
        if (picked.Count == 0) return;
        var target = tiles.Count == 1 ? tiles[0].Title : $"{tiles.Count:N0} titles";

        Prompt = new ActionPromptViewModel(
            question: $"Add {target} to",
            confirmLabel: "New list",
            confirm: async prompt =>
            {
                await Lists.CreateListAsync(prompt.Text, picked);
                Prompt = null;
                if (Details is { } details)
                    details.Lists = await BuildListsAsync(details.Tile);
            },
            cancel: () => Prompt = null,
            inputWatermark: "New list name",
            choices: [.. Lists.Lists],
            choose: async list =>
            {
                await Lists.AddToListAsync(list, picked);
                Prompt = null;
                if (Details is { } details)
                    details.Lists = await BuildListsAsync(details.Tile);
            });
    }

    [RelayCommand]
    private void BeginCreateList()
    {
        Lists.IsCreateMenuOpen = false;
        Prompt = new ActionPromptViewModel(
            question: "Name this list",
            confirmLabel: "Create list",
            confirm: async prompt =>
            {
                await Lists.CreateListAsync(prompt.Text, []);
                Prompt = null;
            },
            cancel: () => Prompt = null,
            inputWatermark: "List name");
    }

    /// <summary>Prompts the user to name and save the current filter as a live list.</summary>
    [RelayCommand]
    private void BeginSaveLiveList()
    {
        Lists.IsCreateMenuOpen = false;
        Prompt = new ActionPromptViewModel(
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
    }

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

        // Every entry of the game leaves, not just the primary. The tile
        // is the thing the user is looking at; taking one store entry out
        // would leave the game in the list with nothing on screen to say so.
        await Lists.RemoveFromListAsync(list, SelectedTiles.SelectMany(t => t.ReleaseIds));
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

        // Move acts on the row the list actually holds, which on a collapsed
        // tile need not be the primary entry.
        if (SelectedTiles[0].ReleaseInList(PositionsIn(list)) is not { } releaseId)
        {
            return;
        }

        if (await Lists.MoveAsync(list, releaseId, delta))
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
    {
        get
        {
            if (!CanReorderOpenList || Lists.Open is not { } list)
            {
                return -1;
            }

            var at = SelectedTiles[0].PositionIn(PositionsIn(list));
            return at == int.MaxValue ? -1 : at;
        }
    }

    /// <summary>Release id to its stored position in the list, built once per question.</summary>
    private static Dictionary<long, int> PositionsIn(GameListViewModel list)
    {
        var positions = new Dictionary<long, int>(list.ReleaseIds.Count);
        for (var i = 0; i < list.ReleaseIds.Count; i++)
        {
            positions[list.ReleaseIds[i]] = i;
        }

        return positions;
    }

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

    partial void OnIsCurrentScreenChanged(bool value)
    {
        _ = value;
        MarkRailSelection();
    }

    /// <summary>Updates rail selection: one Volt edge for the active location, IsRule for a bucket inside a list.</summary>
    private void MarkRailSelection()
    {
        var inList = Lists.IsListOpen;
        var here = IsCurrentScreen;

        foreach (var bucket in Buckets)
        {
            var current = ReferenceEquals(bucket, SelectedBucket);
            bucket.IsSelected = here && current && !inList;
            bucket.IsRule = here && current && inList;
        }

        AllGames.IsSelected = here && SelectedBucket is null && !inList;
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

    /// <summary>
    /// Titles per store, over all tiles. A tile counts once under every store
    /// it is owned on, which is §11.2's per-tile rule surviving the change of
    /// grain: a game bought on Steam and on Epic is genuinely on both
    /// platforms, so both say so.
    ///
    /// <para>This is the same relation the filter panel's PLATFORM option
    /// counts with nothing else cut — tiles that include the store — so the
    /// Platforms screen and the panel cannot print different numbers for the
    /// same question. The consequence, stated rather than discovered: the
    /// per-store figures add up to more than All Games by exactly the number
    /// of extra store memberships.</para>
    /// </summary>
    public IReadOnlyDictionary<string, int> TitlesByStore()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in _allTiles)
        {
            foreach (var store in tile.Stores)
            {
                counts[store] = counts.GetValueOrDefault(store) + 1;
            }
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

        IEnumerable<GameTileViewModel> query = _allTiles;
        if (SelectedBucket is { } bucket)
        {
            query = query.Where(t => t.Bucket == bucket.Key);
        }

        // Manual list: AND term on membership. Live list: no term (rules are in the panel).
        if (Lists.Open is { IsManual: true } openList)
        {
            var members = openList.ReleaseIds.ToHashSet();
            query = query.Where(t => t.ReleaseIds.Any(members.Contains));
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
        // Membership recounts often leave the displayed games unchanged. Keep
        // the same source so the views retain their scroll position and selection.
        if (!VisibleTiles.SequenceEqual(visible))
        {
            VisibleTiles = visible;
            if (SelectedTile is { } selected && !visible.Contains(selected))
            {
                SelectTile(null);
            }

            SelectedCount = SelectedTile is null ? 0 : 1;
            SelectedTiles = SelectedTile is null ? [] : [SelectedTile];
        }

        var alphabetSections = BuildAlphabetSections(visible);
        if (!AlphabetSections.SequenceEqual(alphabetSections))
        {
            AlphabetSections = alphabetSections;
        }

        EmptyMessage = BuildEmptyMessage(visible.Count, search);
        RefreshListCounts();
        RefreshCutBar(visible.Count);
    }

    private static IReadOnlyList<AlphabetSectionViewModel> BuildAlphabetSections(
        IReadOnlyList<GameTileViewModel> tiles)
    {
        var available = tiles.Select(tile => AlphabetSectionFor(tile.Title)).ToHashSet();
        return [
            new("#", available.Contains("#")),
            .. Enumerable.Range('A', 26)
                .Select(value => ((char)value).ToString())
                .Select(label => new AlphabetSectionViewModel(label, available.Contains(label))),
        ];
    }

    /// <summary>
    /// Maps a title to the fixed #/A-Z spine. Latin letters with diacritics
    /// fold onto their base letter; numbers, symbols and other scripts use #.
    /// </summary>
    internal static string AlphabetSectionFor(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "#";
        }

        foreach (var rune in title.TrimStart().Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = Rune.ToUpperInvariant(rune).Value;
            return upper is >= 'A' and <= 'Z' ? ((char)upper).ToString() : "#";
        }

        return "#";
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
        // A tile is in a list when any of its store entries is. list_items
        // stays per release on purpose (adding a game to a list is an act
        // on the entry the user picked), so de-duplication happens here, on
        // display, and the stored rows are untouched.
        foreach (var list in Lists.Lists)
        {
            var members = list.ReleaseIds.ToHashSet();
            list.Count = _allTiles.Count(t => t.ReleaseIds.Any(members.Contains));
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
        var position = PositionsIn(list);

        // Position of the game is the earliest position any of its store
        // entries holds; a collapsed tile whose list row was recorded against
        // its non-primary entry would otherwise sort to the end.
        return tiles
            .OrderBy(t => t.PositionIn(position))
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
