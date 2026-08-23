using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Covers;

namespace Hoard.App.ViewModels;

/// <summary>
/// The M0 library view: rail buckets, command bar, cover grid. Reads the
/// database only (§5 — the UI never calls ingest or enrichment); buckets come
/// from the derived-bucket query, tile metadata from an in-memory join over
/// the repositories. Search and bucket filtering are in-memory for M0.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    /// <summary>Rail stub for the unrunnable bucket — no data source in M0, always zero.</summary>
    public const string WontRunKey = "wont_run";

    private readonly ILibraryQueryRepository _libraryQueries;
    private readonly IOwnershipRepository _ownerships;
    private readonly IReleaseRepository _releases;
    private readonly IWorkRepository _works;

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
        ICoverCache? covers = null)
    {
        _libraryQueries = libraryQueries;
        _ownerships = ownerships;
        _releases = releases;
        _works = works;
        _covers = covers;

        // §7 copy, exactly. Order matches the mock rail.
        Buckets =
        [
            new BucketViewModel(LibraryBuckets.StaleButPatched, "Patched since", showsFlarePip: true),
            new BucketViewModel(LibraryBuckets.NeverTouched, "Never opened"),
            new BucketViewModel(LibraryBuckets.Bounced, "Bounced off"),
            new BucketViewModel(LibraryBuckets.Retired, "Played out"),
            new BucketViewModel(WontRunKey, "Won't run"),
        ];
    }

    public IReadOnlyList<BucketViewModel> Buckets { get; }

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
    [NotifyPropertyChangedFor(nameof(ShowGrid), nameof(ShowListStub))]
    public partial bool IsGridView { get; set; } = true;

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial string TotalCountText { get; set; } = "0";

    [ObservableProperty]
    public partial string SearchPlaceholder { get; set; } = "Search…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowGrid), nameof(ShowListStub))]
    public partial string? EmptyMessage { get; set; }

    [ObservableProperty]
    public partial GameTileViewModel? SelectedTile { get; set; }

    public bool ShowEmpty => EmptyMessage is not null;

    public bool ShowGrid => EmptyMessage is null && IsGridView;

    public bool ShowListStub => EmptyMessage is null && !IsGridView;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var bucketRows = await _libraryQueries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        var ownerships = await _ownerships.GetAllAsync();
        var works = await _works.GetAllAsync();

        // release id → owning work name, and release id → the provider id its
        // cover art is fetched under. Small library, per-work fetch is fine for M0.
        var titleByRelease = new Dictionary<long, string>();
        var coverKeyByRelease = new Dictionary<long, CoverKey>();
        foreach (var work in works)
        {
            foreach (var release in await _releases.GetByWorkAsync(work.Id))
            {
                titleByRelease[release.Id] = work.Name;

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

        var storeByOwnership = ownerships.ToDictionary(o => o.Id, o => o.Store);
        var now = DateTime.UtcNow;

        var tiles = new List<GameTileViewModel>(bucketRows.Count);
        foreach (var row in bucketRows)
        {
            tiles.Add(new GameTileViewModel(
                ownershipId: row.OwnershipId,
                title: titleByRelease.GetValueOrDefault(row.ReleaseId, $"Release {row.ReleaseId}"),
                store: storeByOwnership.GetValueOrDefault(row.OwnershipId, "?"),
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
                covers: _covers));
        }

        // Default sort matches the command-bar stub: dormant longest first
        // (never-played counts as maximally dormant).
        _allTiles = tiles
            .OrderBy(t => t.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var bucket in Buckets)
        {
            bucket.Count = bucketRows.Count(r => r.Bucket == bucket.Key);
        }

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

    /// <summary>Rail click: selects a bucket, or clears the filter when it was already selected.</summary>
    [RelayCommand]
    private void SelectBucket(BucketViewModel? bucket)
        => SelectedBucket = ReferenceEquals(SelectedBucket, bucket) ? null : bucket;

    public void SelectTile(GameTileViewModel? tile)
    {
        if (ReferenceEquals(SelectedTile, tile))
        {
            return;
        }

        if (SelectedTile is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedTile = tile;
        if (tile is not null)
        {
            tile.IsSelected = true;
        }
    }

    /// <summary>
    /// Keyboard grid navigation: moves selection by <paramref name="delta"/>
    /// visible tiles (±1 = left/right, ±columns = up/down). Returns the new
    /// selected index, or -1 when the grid is empty.
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

    partial void OnSelectedBucketChanged(BucketViewModel? value)
    {
        foreach (var bucket in Buckets)
        {
            bucket.IsSelected = ReferenceEquals(bucket, value);
        }

        ApplyFilter();
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

        var visible = query.ToList();
        VisibleTiles = visible;

        if (SelectedTile is { } selected && !visible.Contains(selected))
        {
            SelectTile(null);
        }

        EmptyMessage = BuildEmptyMessage(visible.Count, search);
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
            LibraryBuckets.NeverTouched =>
                "You've opened everything you own. Genuinely rare.",
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
