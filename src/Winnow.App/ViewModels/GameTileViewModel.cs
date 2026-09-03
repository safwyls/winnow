using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One cover tile. Dormancy renders as the two-layer cross-fade from
/// docs/spikes/avalonia-dormancy-rendering.md: a floor variant (saturation
/// 0.22, brightness 0.60) sits under the vivid art, whose opacity is
/// <see cref="DisplayAlpha"/> — the ramp value normally, 1.0 under the
/// pointer (the view animates the change over 140ms).
/// <para>The decoded bitmaps live on the requesting surface's own
/// <see cref="CoverPresenter"/> rather than here, because one tile is shown
/// by the wall and by a feed card at the same time and a recycled wall
/// container must not blank a visible card. The procedural placeholder stays
/// underneath as the fallback, so a missing or still-loading cover shows the
/// title on a Surface field (§7) rather than a hole or a spinner.</para>
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

    private readonly DormancyRamp _ramp;
    private readonly DateTime _nowUtc;

    public GameTileViewModel(
        IReadOnlyList<TileEntry> entries,
        GameGrouping game,
        string title,
        DateTime nowUtc,
        CoverKey? coverKey = null,
        ICoverLeases? covers = null,
        Work? work = null,
        DormancyRamp? ramp = null,
        string? bucketLabel = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (entries is null || entries.Count == 0)
        {
            throw new ArgumentException("A tile is at least one store entry.", nameof(entries));
        }

        Entries = entries;
        Game = game;
        CoverKey = coverKey;
        Leases = covers;
        _ramp = ramp ?? DefaultRamp;
        _nowUtc = nowUtc;

        // The primary entry. Everything stored per release hangs off it: list
        // membership, playtime snapshots, the journal prompt, the feed's
        // lookup, the details modal's own row. The caller orders the entries
        // with the primary work's own first, so this is the entry belonging
        // to the title the user chose to keep.
        Primary = entries[0];
        OwnershipId = Primary.OwnershipId;
        ReleaseId = Primary.ReleaseId;

        // Play launches the copy that is on disk, whichever store sold it,
        // falling back to the primary's own route. A tile that offered Play
        // for a Steam copy while the Epic copy is the installed one would
        // name an action it cannot perform (§10.3).
        PlayableEntry = entries.FirstOrDefault(static e => e.IsOnDisk) ?? Primary;
        Installed = PlayableEntry.Installed;
        InstallPath = PlayableEntry.InstallPath;
        SteamAppId = PlayableEntry.SteamAppId;
        GogProductId = PlayableEntry.GogProductId;
        EpicLaunchKey = PlayableEntry.EpicLaunchKey;
        PrimaryAction = PlayableEntry.PrimaryAction;

        // One chip per store, in the order the entries arrive, and never
        // twice for one store: two Steam accounts owning one game is two
        // entries and one chip, because the chip answers "where can I get
        // at this", not "how many licences do I hold".
        var stores = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            if (!stores.Contains(entry.Store, StringComparer.OrdinalIgnoreCase))
            {
                stores.Add(entry.Store);
            }
        }

        Stores = stores;
        StoreChips = [.. stores.Select(StoreNaming.Badge)];
        StoreInitials = [.. stores.Select(StoreNaming.Initial)];
        StoreNames = string.Join(", ", stores.Select(StoreNaming.Label));

        Title = title;
        Store = Primary.Store;
        StoreBadge = StoreNaming.Badge(Primary.Store);

        // The bucket of the game, never a member's. The grouping computed it
        // from the summed minutes and the group's own last-played, so a game
        // with sixty minutes on each of two stores is Bounced here even
        // though neither entry is.
        Bucket = game.Bucket;
        BucketLabel = string.IsNullOrWhiteSpace(bucketLabel) ? game.Bucket : bucketLabel;

        // Headline figures come off the grouping and are never recomputed
        // here. The grouping got them from CoveragePlaytime.Across, which
        // derives the sum and the date from the same entries in one pass,
        // so the tile cannot show a total from two stores beside a date
        // belonging to one of them (F10) and cannot disagree with the
        // TOTAL row on the details modal.
        PlaytimeMinutes = game.PlaytimeMinutes;
        LastPlayedUtc = game.LastPlayedAt;

        // The unread badge and the "Patched since" bucket count the same fact
        // (§5.2), so the tile derives one from the other rather than being told
        // both and risking disagreement.
        HasUnread = game.Bucket == LibraryBuckets.StaleButPatched;

        // Enrichment fills these in behind a library the user is already
        // browsing (§7), so every one of them is legitimately null on a fresh
        // database. Nothing here invents a stand-in — the detail view simply
        // does not render a row it has no fact for.
        //
        // `work` is the primary work on a collapsed tile. Title, cover,
        // year, publisher and summary come from it and the members never
        // vote, because the primary is the one thing about the group the
        // user decided (the KEEP choice on the Same Game card). A tile that
        // took a majority or a first-seen value would show something nobody
        // chose.
        ReleaseYear = work?.FirstReleaseYear;
        NameIsProvisional = work?.NameIsProvisional ?? false;
        Summary = string.IsNullOrWhiteSpace(work?.Summary) ? null : work!.Summary;
        Publisher = string.IsNullOrWhiteSpace(work?.Publisher) ? null : work!.Publisher;

        PlaytimeText = BuildPlaytimeText(PlaytimeMinutes);
        IdleText = BuildIdleText(LastPlayedUtc, nowUtc);
        // Three states, not two. A game with minutes on the clock and no
        // last-played stamp is common in Steam's local files, and calling that
        // "Never played" would contradict the playtime sitting next to it.
        HasLastPlayedDate = LastPlayedUtc is not null;
        LastPlayedText = LastPlayedUtc is { } played
            ? UpdateEventViewModel.LocalDateText(played)
            : PlaytimeMinutes <= 0 ? "Never played" : "Not recorded";
        StatText = BuildStatText(PlaytimeMinutes, LastPlayedUtc, nowUtc);

        var (start, end) = PlaceholderArt.VividColors(title);
        VividBrush = PlaceholderArt.Gradient(start, end);
        FloorBrush = PlaceholderArt.Gradient(PlaceholderArt.ToFloor(start), PlaceholderArt.ToFloor(end));
        FloorTitleBrush = new ImmutableSolidColorBrush(PlaceholderArt.ToFloor(Colors.White));
    }

    /// <summary>
    /// Every visible store entry of this game, primary first. One on the
    /// overwhelming majority of tiles.
    /// </summary>
    public IReadOnlyList<TileEntry> Entries { get; }

    /// <summary>The game this tile is, as the library read model folded it.</summary>
    public GameGrouping Game { get; }

    /// <summary>The entry everything stored per release hangs off.</summary>
    public TileEntry Primary { get; }

    /// <summary>The entry <see cref="PrimaryAction"/> acts on — the installed copy, else the primary.</summary>
    public TileEntry PlayableEntry { get; }

    /// <summary>Stores this game is owned on, as stored, in entry order and without repeats.</summary>
    public IReadOnlyList<string> Stores { get; }

    /// <summary>The chip faces: one uppercased store name each.</summary>
    public IReadOnlyList<string> StoreChips { get; }

    /// <summary>The compact resting marks: one letter each. See <see cref="StoreNaming.Initial"/>.</summary>
    public IReadOnlyList<string> StoreInitials { get; }

    /// <summary>The stores in words, comma separated — for tooltips and automation.</summary>
    public string StoreNames { get; }

    /// <summary>True when this game is owned on more than one store, which is the only tile the resting chips are drawn on.</summary>
    public bool IsMultiStore => Stores.Count > 1;

    /// <summary>
    /// What a screen reader is told. A collapsed tile names its stores in
    /// words, because the resting mark is initials and §8 requires anything
    /// the grid encodes to be available as text. A single-store tile is just
    /// the title.
    /// </summary>
    public string AutomationName => IsMultiStore
        ? $"{Title}. Owned on {StoreNames}."
        : Title;

    /// <summary>Every ownership this tile stands for.</summary>
    public IEnumerable<long> OwnershipIds => Entries.Select(static e => e.OwnershipId);

    /// <summary>Every release this tile stands for — what list membership is tested against.</summary>
    public IEnumerable<long> ReleaseIds => Entries.Select(static e => e.ReleaseId);

    /// <summary>True when this tile stands for the given ownership.</summary>
    public bool Covers(long ownershipId)
    {
        foreach (var entry in Entries)
        {
            if (entry.OwnershipId == ownershipId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The earliest-positioned release of this game that <paramref name="positions"/>
    /// actually holds, or null when it holds none. list_items rows are per release,
    /// so the entry a user added to a list may not be this tile's primary.
    /// Reordering, list-order sort and the move buttons all act on this row;
    /// keying them on the primary alone sorted a collapsed tile to the end of a
    /// list it was visibly in and left it unmovable.
    /// </summary>
    public long? ReleaseInList(IReadOnlyDictionary<long, int> positions)
    {
        long? found = null;
        var best = int.MaxValue;

        foreach (var entry in Entries)
        {
            if (positions.TryGetValue(entry.ReleaseId, out var at) && at < best)
            {
                best = at;
                found = entry.ReleaseId;
            }
        }

        return found;
    }

    /// <summary>Where this game sits in a list, or <see cref="int.MaxValue"/> when it is not in one.</summary>
    public int PositionIn(IReadOnlyDictionary<long, int> positions)
    {
        var best = int.MaxValue;
        foreach (var entry in Entries)
        {
            if (positions.TryGetValue(entry.ReleaseId, out var at) && at < best)
            {
                best = at;
            }
        }

        return best;
    }

    /// <summary>True when this tile stands for the given release.</summary>
    public bool CoversRelease(long releaseId)
    {
        foreach (var entry in Entries)
        {
            if (entry.ReleaseId == releaseId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The PRIMARY ownership. A collapsed tile has others; see <see cref="Entries"/>.</summary>
    public long OwnershipId { get; }

    /// <summary>The PRIMARY release. Update events are read for every entry; see LibraryViewModel.</summary>
    public long ReleaseId { get; }

    public string Title { get; }

    /// <summary>The PRIMARY entry's store as stored ("steam"). <see cref="Stores"/> is the whole set.</summary>
    public string Store { get; }

    /// <summary>The PRIMARY entry's chip face. Surfaces that can show them all bind <see cref="StoreChips"/>.</summary>
    public string StoreBadge { get; }

    /// <summary>Derived-bucket key (LibraryBuckets.*), used for rail filtering.</summary>
    public string Bucket { get; }

    /// <summary>The GAME's minutes: summed across its store entries.</summary>
    public long PlaytimeMinutes { get; }

    /// <summary>The GAME's last-played: the latest across the same entries the minutes were summed over.</summary>
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
        = new(0, 0, string.Empty, [], string.Empty, false, false, null, [], []);

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

    /// <summary>
    /// Where any surface showing this game acquires its art. The tile holds the
    /// identity and the lease source; the loaded bitmaps live on each surface's
    /// own <see cref="CoverPresenter"/>, because one tile is on the wall and in
    /// the feed at the same time and a recycled wall container must not blank a
    /// visible card.
    /// </summary>
    internal ICoverLeases? Leases { get; }

    /// <summary>
    /// A new cover presenter for this game, already targeted at its key and
    /// lease source. Owned by the caller and released independently of every
    /// other surface's presenter.
    /// </summary>
    internal CoverPresenter NewCoverPresenter()
    {
        var presenter = new CoverPresenter();
        presenter.Target(CoverKey, Leases);
        return presenter;
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
    public static string BuildPlaytimeText(long playtimeMinutes)
        => playtimeMinutes <= 0
            ? "—"
            : playtimeMinutes < 60
                ? $"{playtimeMinutes}m"
                : $"{playtimeMinutes / 60}h";

    private static string BuildIdleText(DateTime? lastPlayedUtc, DateTime nowUtc)
        => lastPlayedUtc is { } played ? IdleSpanText(nowUtc - played) : "—";

    public static string IdleSpanText(TimeSpan idle)
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
