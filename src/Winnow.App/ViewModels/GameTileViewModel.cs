using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One cover tile. Dormancy renders as the two-layer cross-fade from
/// docs/spikes/avalonia-dormancy-rendering.md: a floor variant (saturation
/// 0.22, brightness 0.60) sits under the vivid art, whose opacity is
/// <see cref="DisplayAlpha"/> — the ramp value normally, 1.0 under the
/// pointer (the view animates the change over 140ms).
/// <para>Real cover art (M1) is exactly that swap and nothing more: when
/// <see cref="VividCover"/> arrives it paints over the procedural
/// <see cref="PlaceholderArt"/> layers, sharing the same
/// <see cref="DisplayAlpha"/> and the same 140ms transition. The placeholder
/// stays underneath as the fallback, so a missing or still-loading cover shows
/// the title on a Surface field (§7) rather than a hole or a spinner.</para>
/// </summary>
public partial class GameTileViewModel : ObservableObject
{
    /// <summary>
    /// The ramp a tile built without one resolves through — dimming on, motion
    /// from the OS. Shared because probing the OS setting once per tile on a
    /// 606-tile wall would be six hundred syscalls for one answer; never
    /// mutated, because the app's own ramp is the one the library owns.
    /// </summary>
    private static readonly DormancyRamp DefaultRamp = new();

    private readonly ICoverCache? _covers;
    private readonly DormancyRamp _ramp;
    private readonly DateTime _nowUtc;
    private bool _coverWanted;

    public GameTileViewModel(
        long ownershipId,
        long releaseId,
        string title,
        string store,
        string bucket,
        long playtimeMinutes,
        DateTime? lastPlayedUtc,
        DateTime nowUtc,
        bool hasUnread = false,
        CoverKey? coverKey = null,
        ICoverCache? covers = null,
        Work? work = null,
        Ownership? ownership = null,
        DormancyRamp? ramp = null,
        string? steamAppId = null,
        string? gogProductId = null,
        EpicLaunchKey? epicLaunchKey = null,
        string? bucketLabel = null)
    {
        CoverKey = coverKey;
        SteamAppId = GameLink.IsSteamAppId(steamAppId) ? steamAppId : null;
        GogProductId = StoreActions.IsGogProductId(gogProductId) ? gogProductId : null;
        EpicLaunchKey = epicLaunchKey;
        _covers = covers;
        _ramp = ramp ?? DefaultRamp;
        _nowUtc = nowUtc;
        OwnershipId = ownershipId;
        ReleaseId = releaseId;
        Title = title;
        Store = store;
        StoreBadge = store.ToUpperInvariant();
        Bucket = bucket;
        // The §7 name, not the query's key. The back of the card is the only
        // place in the grid that says which pile a game is in, and it has to say
        // it in the rail's own words rather than in "stale_but_patched".
        BucketLabel = string.IsNullOrWhiteSpace(bucketLabel) ? bucket : bucketLabel;
        PlaytimeMinutes = playtimeMinutes;
        LastPlayedUtc = lastPlayedUtc;
        HasUnread = hasUnread;

        // Enrichment fills these in behind a library the user is already
        // browsing (§7), so every one of them is legitimately null on a fresh
        // database. Nothing here invents a stand-in — the detail view simply
        // does not render a row it has no fact for.
        ReleaseYear = work?.FirstReleaseYear;
        NameIsProvisional = work?.NameIsProvisional ?? false;
        Summary = string.IsNullOrWhiteSpace(work?.Summary) ? null : work!.Summary;
        Publisher = string.IsNullOrWhiteSpace(work?.Publisher) ? null : work!.Publisher;
        // Three-valued on purpose. A stored Ownership always has a definite
        // answer, so `installed` is a plain bool once a row exists — but no row
        // is not the same statement as "not on disk", it is "nothing looked",
        // and folding the two is the mistake that once cleared this library's
        // whole install state. The button that reads this refuses to be named
        // rather than guess (StoreActions.PrimaryFor).
        Installed = ownership is null ? null : ownership.Installed;
        InstallPath = string.IsNullOrWhiteSpace(ownership?.InstallPath) ? null : ownership!.InstallPath;

        // The one filled affordance, decided once, here, so the tile's back face
        // and the detail panel can never disagree about whether this game is
        // reached by Play, by Install or by neither (§10.3).
        PrimaryAction = StoreActions.PrimaryFor(
            Store, Installed, SteamAppId, GogProductId, EpicLaunchKey);

        PlaytimeText = BuildPlaytimeText(playtimeMinutes);
        IdleText = BuildIdleText(lastPlayedUtc, nowUtc);
        // Three states, not two. A game with minutes on the clock and no
        // last-played stamp is common in Steam's local files, and calling that
        // "Never played" would contradict the playtime sitting next to it.
        HasLastPlayedDate = lastPlayedUtc is not null;
        LastPlayedText = lastPlayedUtc is { } played
            ? UpdateEventViewModel.LocalDateText(played)
            : playtimeMinutes <= 0 ? "Never played" : "Not recorded";
        StatText = BuildStatText(playtimeMinutes, lastPlayedUtc, nowUtc);

        var (start, end) = PlaceholderArt.VividColors(title);
        VividBrush = PlaceholderArt.Gradient(start, end);
        FloorBrush = PlaceholderArt.Gradient(PlaceholderArt.ToFloor(start), PlaceholderArt.ToFloor(end));
        FloorTitleBrush = new ImmutableSolidColorBrush(PlaceholderArt.ToFloor(Colors.White));
    }

    public long OwnershipId { get; }

    /// <summary>The release this ownership is a license for — the key update events hang off.</summary>
    public long ReleaseId { get; }

    public string Title { get; }

    /// <summary>Store as stored ("steam"); the badge is the uppercased display cut.</summary>
    public string Store { get; }

    public string StoreBadge { get; }

    /// <summary>Derived-bucket key (LibraryBuckets.*), used for rail filtering.</summary>
    public string Bucket { get; }

    public long PlaytimeMinutes { get; }

    public DateTime? LastPlayedUtc { get; }

    /// <summary>Unread-update badge (§5.2) — set from stale-but-patched bucket membership.</summary>
    public bool HasUnread { get; }

    /// <summary>Scrim line: "312h · idle 8mo", or "never opened".</summary>
    public string StatText { get; }

    /// <summary>List-view playtime column: "312h", or an em dash at zero.</summary>
    public string PlaytimeText { get; }

    /// <summary>List-view idle column: "8mo", or an em dash when never played.</summary>
    public string IdleText { get; }

    /// <summary>Detail view: the date itself, local time — "12 Mar 2023", or why there isn't one.</summary>
    public string LastPlayedText { get; }

    /// <summary>True when <see cref="LastPlayedText"/> is a date; false when it is a sentence.</summary>
    public bool HasLastPlayedDate { get; }

    /// <summary>works.first_release_year, or null until enrichment lands it.</summary>
    public int? ReleaseYear { get; }

    public bool HasReleaseYear => ReleaseYear is > 0;

    /// <summary>Plex Mono, tabular, no thousands separator — it is a year, not a count.</summary>
    public string ReleaseYearText => ReleaseYear is > 0 ? ReleaseYear.Value.ToString("D4") : string.Empty;

    /// <summary>works.summary, or null. Never a placeholder sentence.</summary>
    public string? Summary { get; }

    /// <summary>works.publisher (migration 0005), or null until enrichment lands it.</summary>
    public string? Publisher { get; }

    /// <summary>
    /// Genre, theme, store-tag and game-mode ids split by kind, for the filter
    /// panel to count and cut on. Set at load rather than taken in the
    /// constructor because the facet snapshot is one read for the whole library,
    /// not one per tile — and because it is legitimately
    /// <see cref="Filters.TileFacets.None"/> until the backfill has been
    /// through, which the panel handles by not drawing those groups at all.
    /// </summary>
    public Filters.TileFacets Facets { get; set; } = Filters.TileFacets.None;

    /// <summary>
    /// This tile as <see cref="Winnow.Core.Queries.LibraryFilter"/> sees it.
    ///
    /// <para>Built once at load so the filter panel, the grid and every saved
    /// live list ask the same question of the same projection. Two
    /// implementations of "does this row match" is the failure mode the core
    /// filter's remarks name, and it is the one that produces plausible wrong
    /// answers rather than visible breakage.</para>
    /// </summary>
    public Winnow.Core.Queries.FilterableRow Row { get; set; }
        = new(0, 0, string.Empty, string.Empty, string.Empty, false, false, null, [], []);

    /// <summary>The §7 bucket name this tile falls in ("Never played"), for the back face.</summary>
    public string BucketLabel { get; }

    /// <summary>
    /// Whether the store's local files say this is on disk right now — and
    /// <c>null</c> when no source has looked, which is a third answer rather
    /// than a quieter "no". See the constructor.
    /// </summary>
    public bool? Installed { get; }

    /// <summary>True only when a source looked and found it on disk.</summary>
    public bool IsOnDisk => Installed == true;

    /// <summary>Install directory when installed and known; null otherwise.</summary>
    public string? InstallPath { get; }

    /// <summary>
    /// The Steam appid this release is known by, or null. Validated as digits at
    /// construction (external_ids.provider_id is TEXT), because it is what the
    /// detail view's <c>steam://</c> and store URLs are built from and a URL is
    /// not a place to interpolate an unchecked string.
    /// </summary>
    public string? SteamAppId { get; }

    /// <summary>
    /// The GOG product id this release is known by, or null. Validated as digits
    /// at construction for the same reason the appid is: it is interpolated into
    /// a <c>goggalaxy://</c> target, and external_ids.provider_id is TEXT.
    /// </summary>
    public string? GogProductId { get; }

    /// <summary>
    /// Epic's <c>namespace : catalogItemId : artifactId</c>, or null when we do
    /// not hold all three. <c>external_ids</c> stores only the middle one, so
    /// the other two come from the catalog rows the app itself cached; a title
    /// the cache has not reached yet simply has no Epic launch target, which
    /// renders as no button rather than as a broken one.
    /// </summary>
    public EpicLaunchKey? EpicLaunchKey { get; }

    /// <summary>
    /// <c>Play</c> when it is on disk, <c>Install</c> when it is not, and null
    /// when this app cannot honestly name either — no id for the store, no
    /// verified install route for the store, or no answer at all about the
    /// install state. Never an inert button (§10.3).
    /// </summary>
    public GameLink? PrimaryAction { get; }

    public bool HasPrimaryAction => PrimaryAction is not null;

    /// <summary>The button's face: "Play" or "Install", named for what it does.</summary>
    public string PrimaryActionLabel => PrimaryAction?.Label ?? string.Empty;

    /// <summary>Tooltip for the primary action — which launcher it hands to.</summary>
    public string PrimaryActionHint => PrimaryAction?.Tooltip ?? string.Empty;

    /// <summary>
    /// True when the title is a machine-minted stand-in ("App 8510") rather than
    /// a real name — Steam's local files knew the appid and nothing else. The
    /// detail view says so out loud; a placeholder that looks like a title is
    /// how a user concludes the whole panel is wrong.
    /// </summary>
    public bool NameIsProvisional { get; }

    /// <summary>
    /// Resting vivid-layer opacity from the §5.1 ramp: α = (S − 0.22) / 0.78 —
    /// or 1.0 when the user has turned dimming off. Resolved on read rather than
    /// baked at construction, so flipping the preference repaints the wall
    /// without rebuilding a tile or disturbing the cover cache.
    /// </summary>
    public double DormancyAlpha => _ramp.VividAlphaFor(LastPlayedUtc, _nowUtc);

    /// <summary>Vivid art layer. Placeholder gradient now; display-resolution bitmap later.</summary>
    public IBrush VividBrush { get; }

    /// <summary>Floor variant (sat 0.22 / bright 0.60). Pre-computed bitmap variant later.</summary>
    public IBrush FloorBrush { get; }

    /// <summary>Placeholder-title ink on the floor layer, so the title fades with its art.</summary>
    public IBrush FloorTitleBrush { get; }

    /// <summary>Provider id this tile's art is fetched under; null when we know no id for it.</summary>
    public CoverKey? CoverKey { get; }

    /// <summary>Real vivid cover, decoded at display resolution. Null until it arrives.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(ShowPlaceholder))]
    public partial Bitmap? VividCover { get; set; }

    /// <summary>Real floor variant (sat 0.22 / bright 0.60), pre-computed by the cover cache.</summary>
    [ObservableProperty]
    public partial Bitmap? FloorCover { get; set; }

    public bool HasCover => VividCover is not null;

    /// <summary>Procedural art is the fallback: it paints whenever no cover is loaded.</summary>
    public bool ShowPlaceholder => VividCover is null;

    /// <summary>
    /// Called when the tile is realized. A memory hit applies synchronously so
    /// scrolling back never flashes the placeholder; anything else is handed to
    /// the cover cache and arrives later (§5.1 — art never blocks the UI).
    /// </summary>
    public void RequestCover(double displayWidthPixels)
    {
        _coverWanted = true;
        if (_covers is null || CoverKey is not { } key)
        {
            return;
        }

        if (_covers.TryGet(key, displayWidthPixels, out var cached))
        {
            Apply(cached);
            return;
        }

        _ = LoadCoverAsync(key, displayWidthPixels);
    }

    /// <summary>
    /// Called when the tile is recycled out of the visual tree. Dropping the
    /// references is what makes the cache's memory bound real: off-screen tiles
    /// keep nothing alive, so the LRU is the only owner of decoded pixels.
    /// </summary>
    public void ReleaseCover()
    {
        _coverWanted = false;
        VividCover = null;
        FloorCover = null;
    }

    private async Task LoadCoverAsync(CoverKey key, double displayWidthPixels)
    {
        var art = await _covers!.GetAsync(key, displayWidthPixels).ConfigureAwait(false);
        if (art is null)
        {
            return;
        }

        // Covers appear as they arrive; the tile is already on screen showing
        // its placeholder, so this is a repaint, not a load gate.
        Dispatcher.UIThread.Post(() =>
        {
            if (_coverWanted)
            {
                Apply(art);
            }
        });
    }

    private void Apply(CoverArt art)
    {
        FloorCover = art.Floor;
        VividCover = art.Vivid;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAlpha))]
    public partial bool IsPointerOver { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Whether the card is flipped to show its back face. Lives on the VM (not
    /// the container) because the cover wall virtualizes — container state
    /// doesn't survive recycling. Only one card is flipped at a time.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFlipped { get; set; }

    /// <summary>Add to list, wired by the library. Null in tests.</summary>
    /// <summary>Play/Install command, wired by the library for session tracking.</summary>
    public System.Windows.Input.ICommand? PrimaryActionCommand { get; set; }

    public System.Windows.Input.ICommand? AddToListCommand { get; set; }

    /// <summary>
    /// The detail modal for this game. The back face carries it because the flip
    /// took the gesture the grid used to open it with, and §10 calls that modal
    /// the answer to §5.3's four-fact cap — a surface that must not become
    /// unreachable.
    /// </summary>
    public System.Windows.Input.ICommand? OpenDetailsCommand { get; set; }

    /// <summary>
    /// Hover restores full saturation (140ms transition lives in the view). With
    /// dimming off this is 1.0 in both states, so the restore is a no-op without
    /// anything having to special-case it.
    /// </summary>
    public double DisplayAlpha => IsPointerOver ? 1.0 : DormancyAlpha;

    /// <summary>
    /// §8: reduced motion snaps the hover restore instead of fading it. The view
    /// reads this as a style class and drops the cross-fade's transitions.
    /// </summary>
    public bool SnapDormancy => _ramp.ReducedMotion;

    /// <summary>
    /// The ramp's state changed under a tile that is already built. Re-reading
    /// the two derived values is the whole of it — the art layers, the cover
    /// cache and the decoded bitmaps are all untouched, which is why the toggle
    /// costs a repaint rather than a reload.
    /// </summary>
    public void RefreshDormancy()
    {
        OnPropertyChanged(nameof(DormancyAlpha));
        OnPropertyChanged(nameof(DisplayAlpha));
        OnPropertyChanged(nameof(SnapDormancy));
    }

    private static string BuildStatText(long playtimeMinutes, DateTime? lastPlayedUtc, DateTime nowUtc)
    {
        if (playtimeMinutes <= 0)
        {
            return "never opened";
        }

        var playtime = BuildPlaytimeText(playtimeMinutes);

        return lastPlayedUtc is null
            ? playtime
            : $"{playtime} · idle {IdleSpanText(nowUtc - lastPlayedUtc.Value)}";
    }

    /// <summary>
    /// An em dash rather than "0h" at zero playtime: the list's job is to be
    /// scannable, and a column of zeroes reads as data when it is an absence.
    /// </summary>
    private static string BuildPlaytimeText(long playtimeMinutes)
        => playtimeMinutes <= 0
            ? "—"
            : playtimeMinutes < 60
                ? $"{playtimeMinutes}m"
                : $"{playtimeMinutes / 60}h";

    private static string BuildIdleText(DateTime? lastPlayedUtc, DateTime nowUtc)
        => lastPlayedUtc is { } played ? IdleSpanText(nowUtc - played) : "—";

    private static string IdleSpanText(TimeSpan idle)
    {
        var days = Math.Max(0, idle.TotalDays);
        if (days < 30)
        {
            return $"{Math.Max(1, (int)days)}d";
        }

        var months = (int)(days / 30.4375);
        if (months < 12)
        {
            return $"{months}mo";
        }

        var years = months / 12;
        var rest = months % 12;
        return rest == 0 ? $"{years}y" : $"{years}y {rest}mo";
    }
}
