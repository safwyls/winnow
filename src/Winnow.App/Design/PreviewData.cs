using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Resolve;

namespace Winnow.App.Design;

/// <summary>
/// Design-time view models for the Avalonia previewer in Rider and Visual
/// Studio (TASK-150). A view assigns the matching property as its DataContext
/// when <see cref="Avalonia.Controls.Design.IsDesignMode"/> is true; nothing
/// on the runtime path ever touches this type, and every instance is built
/// from <see cref="PreviewLibrary"/>'s fabricated records — no database, no
/// filesystem, no network.
///
/// <para>Properties are built lazily on first access so opening one view in
/// the previewer pays for that view's graph only. <see cref="Library"/> is
/// shared: <see cref="Feed"/>, <see cref="Stores"/> and <see cref="Shell"/>
/// all read the same tiles, exactly as the real composition root wires
/// them.</para>
///
/// <para>The library's tiles do not exist until its load command runs — the
/// shell's <c>OnOpened</c> drives that in the previewer as it does at
/// runtime, and the views that preview standalone call
/// <see cref="LoadShellAsync"/> themselves.</para>
/// </summary>
internal static class PreviewData
{
    private static LibraryViewModel? _library;
    private static MainWindowViewModel? _shell;
    private static FeedViewModel? _feed;
    private static StoresViewModel? _stores;
    private static AppearanceViewModel? _appearance;
    private static MergeQueueViewModel? _mergeQueue;
    private static AccountStatsViewModel? _accountStats;
    private static LibrarySettingsViewModel? _librarySettings;
    private static GameTileViewModel? _tile;
    private static GameDetailsViewModel? _gameDetails;
    private static FeedCardViewModel? _feedCard;
    private static bool _shellLoadStarted;

    /// <summary>The library over the fabricated set. Loads on its own command, as at runtime.</summary>
    public static LibraryViewModel Library => _library ??= new LibraryViewModel(
        new PreviewLibraryQueryRepository(),
        new PreviewOwnershipRepository(),
        new PreviewReleaseRepository(),
        new PreviewWorkRepository(),
        new PreviewUpdateEventRepository(),
        snapshots: new PreviewSnapshotRepository(),
        storefrontCache: new PreviewStorefrontRepository());

    /// <summary>The whole window: rail, wall, feed and settings, all over the fabricated library.</summary>
    public static MainWindowViewModel Shell => _shell ??= new MainWindowViewModel(
        Library,
        MergeQueue,
        Stores,
        Appearance,
        Feed,
        AccountStats,
        LibrarySettings);

    /// <summary>The landing screen's feed, resolved against <see cref="Library"/>'s tiles.</summary>
    public static FeedViewModel Feed => _feed ??= new FeedViewModel(new PreviewFeedService(), Library);

    /// <summary>SETTINGS › PLATFORMS in its not-connected state.</summary>
    public static StoresViewModel Stores => _stores ??= new StoresViewModel(
        new PreviewStoreConnections(), counts: Library);

    /// <summary>SETTINGS › APPEARANCE on the default theme.</summary>
    public static AppearanceViewModel Appearance => _appearance ??= new AppearanceViewModel(new ThemeService());

    /// <summary>The Merges screen with an empty queue — the honest state of a resolved library.</summary>
    public static MergeQueueViewModel MergeQueue => _mergeQueue ??= BuildMergeQueue();

    /// <summary>The STATS screen over fabricated account figures.</summary>
    public static AccountStatsViewModel AccountStats =>
        _accountStats ??= new AccountStatsViewModel(new PreviewAccountStatsRepository());

    /// <summary>SETTINGS › LIBRARY with no services, its plainest state.</summary>
    public static LibrarySettingsViewModel LibrarySettings =>
        _librarySettings ??= new LibrarySettingsViewModel();

    /// <summary>The command bar's filter panel, bound to <see cref="Library"/>.</summary>
    public static ViewModels.Filters.FilterPanelViewModel Filters => Library.Filters;

    /// <summary>One tile on its own — Hollow Knight, installed on Steam, with an unread patch.</summary>
    public static GameTileViewModel Tile => _tile ??= BuildTile();

    /// <summary>
    /// The details modal for Stardew Valley: chosen because it exercises the
    /// most of the modal at once — an install path and Play action, the
    /// Stale-but-patched gap rail, since-you-played updates, the GOG
    /// patch-notes expander, an acquisition row and the playtime record line.
    /// </summary>
    public static GameDetailsViewModel GameDetails => _gameDetails ??= BuildGameDetails();

    /// <summary>One feed card on its own, with the tile it draws.</summary>
    public static FeedCardViewModel FeedCard =>
        _feedCard ??= new FeedCardViewModel(Tile, "Patched 10 days ago, six weeks after you last played");

    /// <summary>
    /// Runs the loads the shell drives on open, once. Standalone previews of
    /// the feed, the filter panel and the command bar call this so their data
    /// source has tiles to draw; the window itself does not need it
    /// (<c>OnOpened</c> fires in the previewer), but a second run is a
    /// reload, which is safe.
    /// </summary>
    public static async Task LoadShellAsync()
    {
        if (_shellLoadStarted)
        {
            return;
        }

        _shellLoadStarted = true;
        await Library.LoadCommand.ExecuteAsync(null);
        await Feed.LoadCommand.ExecuteAsync(null);
    }

    private static MergeQueueViewModel BuildMergeQueue()
    {
        var releases = new PreviewReleaseRepository();
        var links = new PreviewIdentityLinkRepository();
        var refusals = new PreviewExpansionRefusalRepository();
        return new MergeQueueViewModel(
            new PreviewMergeCandidateRepository(),
            releases,
            new PreviewWorkRepository(),
            links,
            new PreviewOwnershipRepository(),
            new LibraryExpansionScan(releases, links, refusals),
            refusals,
            new PreviewLibraryQueryRepository());
    }

    private static GameTileViewModel BuildTile()
    {
        var now = DateTime.UtcNow;
        var snapshot = PreviewLibrary.Snapshot();
        var row = snapshot.Buckets.Single(b => b.OwnershipId == PreviewLibrary.HollowKnightOwnership);
        var ownership = PreviewLibrary.Ownerships.Single(o => o.Id == row.OwnershipId);
        var work = PreviewLibrary.Works.Single(w => w.Id == PreviewLibrary.HollowKnightWork);

        var entries = new[]
        {
            TileEntry.For(
                row.OwnershipId,
                row.ReleaseId,
                row.WorkId,
                ownership.Store,
                row.PlaytimeMinutes,
                row.LastPlayedAt,
                ownership: ownership,
                steamAppId: "367520"),
        };

        return new GameTileViewModel(entries, row.Game, work.Name, now, work: work);
    }

    private static GameDetailsViewModel BuildGameDetails()
    {
        var now = DateTime.UtcNow;
        var snapshot = PreviewLibrary.Snapshot();
        var row = snapshot.Buckets.Single(b => b.OwnershipId == PreviewLibrary.StardewOwnership);
        var ownership = PreviewLibrary.Ownerships.Single(o => o.Id == row.OwnershipId);
        var work = PreviewLibrary.Works.Single(w => w.Id == PreviewLibrary.StardewWork);

        var entries = new[]
        {
            TileEntry.For(
                row.OwnershipId,
                row.ReleaseId,
                row.WorkId,
                ownership.Store,
                row.PlaytimeMinutes,
                row.LastPlayedAt,
                ownership: ownership,
                gogProductId: "1453375253",
                storefront: PreviewLibrary.StardewStorefront),
        };

        var tile = new GameTileViewModel(entries, row.Game, work.Name, now, work: work);
        var events = PreviewLibrary.UpdateEvents
            .Where(e => e.ReleaseId == row.ReleaseId)
            .ToList();
        var updates = events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => UpdateEventViewModel.Create(e, tile.LastPlayedUtc))
            .ToList();

        return new GameDetailsViewModel(
            tile,
            "Patched",
            updates,
            now,
            snapshots: [.. PreviewLibrary.Snapshots.Where(s => s.OwnershipId == row.OwnershipId)],
            updateEvents: events,
            ownerships: [ownership]);
    }
}
