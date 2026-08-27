using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.Core.Domain;
using Hoard.Covers;

namespace Hoard.App.ViewModels;

/// <summary>
/// Everything the tile deliberately does not say.
///
/// <para>design-system.md §5.3 caps the hover overlay at four facts — "the tile
/// is a decision surface, not a detail view" — which only works if the detail
/// view exists somewhere. This is that somewhere: a modal over the library,
/// opened by <c>Enter</c> or a double click and dismissed by <c>Escape</c> or a
/// click on the scrim (§8: keyboard reachable, Escape closes).</para>
///
/// <para><b>The panel answers four questions, in the order a person asks
/// them.</b> What is this (art, title, year, publisher, summary). What is my
/// history with it (playtime, and the gap). What happened while I was away
/// (updates). Get me in (launch, store, patch notes). The layout is those four
/// bands and nothing else.</para>
///
/// <para><b>The gap is the signature.</b> Hoard is the only thing that holds
/// <c>play_records.last_played_at</c> and <c>update_events.occurred_at</c> in
/// one place, so it is the only thing that can draw the stretch between your
/// last session and now with the patches you missed marked on it. §1 calls that
/// join the product; nothing in the UI had ever shown it. It is deliberately
/// NOT a playtime chart: <c>playtime_snapshots</c> holds one reading per game
/// on a fresh install, and a line through one point is a decoration pretending
/// to be evidence. What the snapshots honestly support is a sentence — how many
/// times Hoard has checked, since when, and what it has seen move — and that is
/// what <see cref="RecordLine"/> is.</para>
///
/// <para><b>Nothing here is invented.</b> Year, publisher and summary arrive
/// from IGDB enrichment behind a library the user is already browsing (§7), so
/// each is legitimately null for a long while after first run. A null fact
/// renders as no row at all rather than as "Unknown" — a placeholder that turns
/// into real data later is a lie with a timer on it. Facts the schema has but
/// this data source never fills (acquired_at, license_type, price, edition,
/// platform, achievements) are absent from the design rather than bound and
/// hidden: a row that can never appear is dead weight impersonating a
/// feature.</para>
/// </summary>
public partial class GameDetailsViewModel : ObservableObject
{
    /// <summary>Cover at a size worth looking at, on the same 2:3 capsule geometry.</summary>
    public const double CoverWidth = 200;

    public const double CoverHeight = CoverWidth * 1.5;

    /// <summary>
    /// The most marks the gap rail will draw. Past this the rail stops being a
    /// picture of a gap and becomes a smear; the list below is the exhaustive
    /// record, and it stays exhaustive.
    /// </summary>
    private const int MaxRailMarks = 14;

    private readonly ICoverCache? _covers;

    public GameDetailsViewModel(
        GameTileViewModel tile,
        string bucketLabel,
        IReadOnlyList<UpdateEventViewModel> updates,
        DateTime nowUtc,
        IReadOnlyList<PlaytimeSnapshot>? snapshots = null,
        ICoverCache? covers = null)
    {
        Tile = tile;
        BucketLabel = bucketLabel;
        Updates = updates;
        _covers = covers;

        LastPlayedUtc = tile.LastPlayedUtc is { } played
            ? UpdateEventViewModel.AsUtc(played)
            : null;

        RailMarks = BuildRailMarks(updates, LastPlayedUtc, nowUtc);
        RecordLine = BuildRecordLine(snapshots ?? [], nowUtc);
        (PrimaryAction, Links) = BuildLinks(tile);
    }

    /// <summary>The tile this describes — title, store, art and the stat strings all come from it.</summary>
    public GameTileViewModel Tile { get; }

    // ── Band 1: what is this ────────────────────────────────────────────────

    public string Title => Tile.Title;

    /// <summary>
    /// "App 8510" is an appid wearing a title's clothes. Saying so is the
    /// difference between a panel that is honestly waiting for metadata and one
    /// that looks broken.
    /// </summary>
    public bool TitleIsProvisional => Tile.NameIsProvisional;

    public string ProvisionalNote => "Steam's local files gave an id and no name. Hoard shows the id until enrichment lands one.";

    /// <summary>
    /// Year and publisher share a line but not a typeface: the year is a number
    /// and sets in Plex Mono, the publisher is a name and sets in Jakarta (§3).
    /// The separator belongs to the year run so it disappears with it.
    /// </summary>
    public string IdentityYearText => HasReleaseYear
        ? HasPublisher ? $"{ReleaseYearText} · " : ReleaseYearText
        : string.Empty;

    public bool HasIdentityLine => HasReleaseYear || HasPublisher;

    public bool HasReleaseYear => Tile.ReleaseYear is > 0;

    /// <summary>Plex Mono, tabular, no thousands separator — it is a year, not a count.</summary>
    public string ReleaseYearText => Tile.ReleaseYear?.ToString("D4") ?? string.Empty;

    /// <summary>works.publisher (migration 0005), or null until enrichment lands it.</summary>
    public string? Publisher => Tile.Publisher;

    public bool HasPublisher => Publisher is not null;

    public string StoreBadge => Tile.StoreBadge;

    /// <summary>The §7 bucket name this game currently falls in ("Never played").</summary>
    public string BucketLabel { get; }

    /// <summary>
    /// Install state as a fact, not a verdict — and only when there is one.
    ///
    /// <para><see cref="GameTileViewModel.Installed"/> is three-valued: null
    /// means no source looked, which is neither "Installed" nor "Not installed".
    /// The chip is absent in that case rather than guessing, on §10.5's rule
    /// that a null fact renders as no row at all — a placeholder that turns into
    /// real data later is a lie with a timer on it.</para>
    /// </summary>
    public string InstallText => Tile.Installed == true ? "Installed" : "Not installed";

    /// <summary>False when nothing has looked at this game's install state.</summary>
    public bool HasInstallState => Tile.Installed is not null;

    public string? InstallPath => Tile.InstallPath;

    public bool HasInstallPath => InstallPath is not null;

    // ── Band 2: my history with it ──────────────────────────────────────────

    /// <summary>Total on the clock — the one number big enough to read from across the room.</summary>
    public string PlaytimeText => Tile.PlaytimeText;

    public DateTime? LastPlayedUtc { get; }

    /// <summary>The gap rail only draws when there is a gap to draw.</summary>
    public bool HasGap => LastPlayedUtc is not null;

    public bool LacksGap => !HasGap;

    /// <summary>Rail's left cap: the day you stopped.</summary>
    public string LastPlayedText => Tile.LastPlayedText;

    /// <summary>Rail's headline: how long that has been. "2y 8mo".</summary>
    public string IdleText => Tile.IdleText;

    /// <summary>
    /// Why there is no rail, said plainly. Two different facts, and §7 will not
    /// let them collapse into one: a game you never opened, and a game Steam
    /// recorded minutes for without recording a date.
    /// </summary>
    public string NoGapText => Tile.PlaytimeMinutes <= 0
        ? "You've never opened this."
        : "Steam has no date for your last session.";

    /// <summary>
    /// Positions, 0–1, of the updates that landed inside the gap. Empty is a
    /// legitimate and common answer, and the rail says so in words.
    /// </summary>
    public IReadOnlyList<double> RailMarks { get; }

    public bool HasRailMarks => RailMarks.Count > 0;

    /// <summary>
    /// The rail's caption. Counts only what landed after the last session —
    /// which is the definition the unread badge and the "Patched since" bucket
    /// both use (§5.2).
    ///
    /// <para>The zero case says "recorded", not "nothing shipped". Update
    /// polling is staggered across days (§4.5), so an empty rail can mean the
    /// game had a quiet decade or that its turn has not come round yet — and
    /// the interface may only claim the one of those it can actually
    /// support.</para>
    /// </summary>
    public string GapCaption
    {
        get
        {
            var missed = Updates.Count(u => u.IsSinceYouPlayed);
            return missed switch
            {
                0 => "No updates recorded in that stretch.",
                1 => "1 update landed while you were away.",
                _ => $"{missed} updates landed while you were away.",
            };
        }
    }

    /// <summary>
    /// What Hoard's own longitudinal record actually amounts to for this game.
    ///
    /// <para>This is §1's "playtime history that storefronts discard", stated at
    /// the resolution the data supports rather than drawn as a chart it does
    /// not. On a fresh install it is one reading and this line says so; after a
    /// month of the snapshot scheduler it is the only place a user can see that
    /// a number moved.</para>
    /// </summary>
    public string RecordLine { get; }

    public bool HasRecordLine => RecordLine.Length > 0;

    // ── Band 3: what happened while I was away ──────────────────────────────

    /// <summary>Newest first — the update the user missed most recently is the one they want.</summary>
    public IReadOnlyList<UpdateEventViewModel> Updates { get; }

    public bool HasUpdates => Updates.Count > 0;

    /// <summary>
    /// §7: a label labels. "Since you played" is a claim about the rows under
    /// it, so it is only used when the rows under it are that — otherwise the
    /// section is what it is, a history.
    /// </summary>
    public string UpdatesLabel => Updates.Any(u => u.IsSinceYouPlayed)
        ? "SINCE YOU PLAYED"
        : "UPDATE HISTORY";

    // ── Band 4: get me in ───────────────────────────────────────────────────

    /// <summary>
    /// The one filled affordance — <c>Play</c> when the game is on disk,
    /// <c>Install</c> when it is not, through whichever launcher owns it.
    ///
    /// <para>Named for what it does, so it cannot lie: a button reading "Play"
    /// on an uninstalled 60GB game promises something the next sixty minutes
    /// will not deliver. Null when this app cannot name one honestly — no id for
    /// the store, no verified install route for the store, or no answer at all
    /// about the install state — in which case the panel simply has no primary
    /// action, never an inert button.</para>
    ///
    /// <para>It is <see cref="GameTileViewModel.PrimaryAction"/> itself, not a
    /// second computation of the same thing. The back of the cover tile offers
    /// the same button, and two implementations of "which one is this" is how
    /// one surface ends up saying Play while the other says Install.</para>
    /// </summary>
    public GameLink? PrimaryAction { get; }

    public bool HasPrimaryAction => PrimaryAction is not null;

    /// <summary>Store page and patch-notes hub. Empty when we hold no appid.</summary>
    public IReadOnlyList<GameLink> Links { get; }

    public bool HasLinks => Links.Count > 0;

    /// <summary>
    /// The install directory, for the shell's "open folder". Handed to
    /// <c>ILauncher.LaunchDirectoryInfoAsync</c> as a path, never as a
    /// <c>file:</c> URI — <see cref="GameLink"/> refuses that scheme on purpose,
    /// and this is the one local target the design actually wants.
    /// </summary>
    public string? OpenableFolder => Tile.IsOnDisk ? Tile.InstallPath : null;

    public bool HasOpenableFolder => OpenableFolder is not null;

    public string? SteamAppId => Tile.SteamAppId;

    public bool HasSteamAppId => SteamAppId is not null;

    // ── Body ────────────────────────────────────────────────────────────────

    public string? Summary => Tile.Summary;

    public bool HasSummary => Summary is not null;

    /// <summary>
    /// §7: an empty screen is an invitation, not a shrug. A game with no
    /// summary and no update history is the normal state of a library Hoard has
    /// only just read, and the panel says which of those two things is true
    /// rather than showing a gap.
    /// </summary>
    public string EmptyBodyText => "No description yet. Hoard fills the year, publisher and summary in from IGDB as it works through your library.";

    public bool ShowEmptyBody => !HasSummary;

    /// <summary>§8: reduced motion snaps state instead of fading it.</summary>
    public bool ReducedMotion => Tile.SnapDormancy;

    // ── Cover ───────────────────────────────────────────────────────────────

    /// <summary>Real cover at detail resolution; null until it arrives (or forever, with no art).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Cover { get; set; }

    /// <summary>Procedural art is the fallback here too — never a hole, never a spinner (§7).</summary>
    public bool ShowPlaceholder => Cover is null;

    /// <summary>The tile's own placeholder gradient, so the modal looks like the tile it came from.</summary>
    public IBrush PlaceholderBrush => Tile.VividBrush;

    /// <summary>
    /// Full saturation, always. The dormancy ramp is a scanning aid for the
    /// grid (§5.1); once the user has chosen a game, fading its art tells them
    /// something they just acted on.
    /// </summary>
    public void RequestCover(double displayWidthPixels)
    {
        if (_covers is null || Tile.CoverKey is not { } key)
        {
            return;
        }

        if (_covers.TryGet(key, displayWidthPixels, out var cached))
        {
            Cover = cached.Vivid;
            return;
        }

        _ = LoadCoverAsync(key, displayWidthPixels);
    }

    private async Task LoadCoverAsync(CoverKey key, double displayWidthPixels)
    {
        var art = await _covers!.GetAsync(key, displayWidthPixels).ConfigureAwait(false);
        if (art is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => Cover = art.Vivid);
    }

    // ── Construction helpers ────────────────────────────────────────────────

    /// <summary>
    /// Where each missed update sits between "you stopped" and "now", as a
    /// fraction. Updates from before the last session are not marks — they are
    /// not in the gap — but they stay in the list below, because the list is
    /// the record and the rail is the argument.
    /// </summary>
    private static IReadOnlyList<double> BuildRailMarks(
        IReadOnlyList<UpdateEventViewModel> updates,
        DateTime? lastPlayedUtc,
        DateTime nowUtc)
    {
        if (lastPlayedUtc is not { } played)
        {
            return [];
        }

        var span = (nowUtc - played).TotalSeconds;
        if (span <= 0)
        {
            return [];
        }

        return updates
            .Where(u => u.IsSinceYouPlayed)
            .OrderBy(u => u.OccurredAtUtc)
            .Select(u => Math.Clamp((u.OccurredAtUtc - played).TotalSeconds / span, 0.0, 1.0))
            .Take(MaxRailMarks)
            .ToArray();
    }

    /// <summary>
    /// §1's longitudinal history, stated as a sentence.
    ///
    /// <para>"Checked" rather than "sampled" or "snapshotted" (§7: name things
    /// by what people recognise). The delta is between the first and last
    /// reading Hoard holds, which is exactly the claim it can defend — not
    /// total playtime, which Steam supplies, but the part Hoard watched
    /// happen.</para>
    /// </summary>
    private static string BuildRecordLine(IReadOnlyList<PlaytimeSnapshot> snapshots, DateTime nowUtc)
    {
        if (snapshots.Count == 0)
        {
            return string.Empty;
        }

        var ordered = snapshots.OrderBy(s => UpdateEventViewModel.AsUtc(s.ObservedAt)).ToArray();
        var since = UpdateEventViewModel.LocalDateText(ordered[0].ObservedAt);

        if (ordered.Length == 1)
        {
            return $"Checked once, on {since}. Hoard keeps every reading from here.";
        }

        var gained = ordered[^1].PlaytimeMinutes - ordered[0].PlaytimeMinutes;
        var counted = $"Checked {ordered.Length:N0} times since {since}";

        return gained > 0
            ? $"{counted} — up {SpanText(gained)}."
            : $"{counted} — no change.";
    }

    /// <summary>Minutes as the app writes durations: "45m", "3h", "3h 20m".</summary>
    private static string SpanText(long minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes}m";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    /// <summary>
    /// The outbound affordances, all of them derived from an id this database
    /// holds and none of them invented.
    ///
    /// <para><b>This used to return early without a Steam appid</b>, which meant
    /// every Epic and GOG row in the library — 113 of them on the author's
    /// machine — had no primary action and no links whatsoever, on a panel whose
    /// third band is called "get me in". Both halves now come from
    /// <see cref="StoreActions"/>, which knows one thing per store and says so
    /// with the evidence attached; a store it cannot reach still returns
    /// nothing, but it returns nothing per store rather than for everything that
    /// is not Steam.</para>
    /// </summary>
    private static (GameLink? Primary, IReadOnlyList<GameLink> Links) BuildLinks(GameTileViewModel tile)
        => (tile.PrimaryAction, StoreActions.LinksFor(tile.Store, tile.SteamAppId, tile.GogProductId));
}
