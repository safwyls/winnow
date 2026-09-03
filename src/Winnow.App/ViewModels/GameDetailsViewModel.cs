using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// Game detail modal (§5.3). Shows identity, play history, updates, and
/// launch actions. Null fields render as absent rows, not placeholders.
/// </summary>
public partial class GameDetailsViewModel : ObservableObject
{
    /// <summary>Cover at a size worth looking at, on the same 2:3 capsule geometry.</summary>
    public const double CoverWidth = 200;

    public const double CoverHeight = CoverWidth * 1.5;

    /// <summary>Max update marks on the gap rail before it overflows to the list.</summary>
    private const int MaxRailMarks = 14;

    private readonly ICoverCache? _covers;

    /// <summary>Update flag service. Null hides the mark-as-read control.</summary>
    private readonly IUpdateFlagService? _flags;

    /// <summary>Raw update events for this release, passed to <see cref="IUpdateFlagService.DismissAsync"/>.</summary>
    private readonly IReadOnlyList<UpdateEvent> _events;

    /// <summary>Re-runs the library query after a dismissal changes bucket membership.</summary>
    private readonly Func<Task>? _reloadLibrary;

    private readonly DateTime _nowUtc;

    private bool _busy;

    public GameDetailsViewModel(
        GameTileViewModel tile,
        string bucketLabel,
        IReadOnlyList<UpdateEventViewModel> updates,
        DateTime nowUtc,
        IReadOnlyList<PlaytimeSnapshot>? snapshots = null,
        ICoverCache? covers = null,
        IReadOnlyList<UpdateEvent>? updateEvents = null,
        DateTime? acknowledgedThrough = null,
        IUpdateFlagService? updateFlags = null,
        Func<Task>? reloadLibrary = null,
        GameCoverageViewModel? coverage = null,
        GameExpansionsViewModel? expansions = null)
    {
        Coverage = coverage;
        Expansions = expansions;
        Tile = tile;
        BucketLabel = bucketLabel;
        Updates = updates;
        _covers = covers;
        _nowUtc = nowUtc;
        _events = updateEvents ?? [];
        _flags = updateFlags;
        _reloadLibrary = reloadLibrary;

        LastPlayedUtc = tile.LastPlayedUtc is { } played
            ? UpdateEventViewModel.AsUtc(played)
            : null;

        // Unread flag = bucket membership; dismissal standing is separate.
        FlagIsRaised = tile.HasUnread;
        DismissalStands = acknowledgedThrough is not null;

        RecordLine = BuildRecordLine(snapshots ?? [], nowUtc);
        (PrimaryAction, Links) = BuildLinks(tile);

        // Derives acknowledged state, rail marks, and caption.
        ApplyWatermark(acknowledgedThrough);
    }

    /// <summary>The tile this describes — title, store, art and the stat strings all come from it.</summary>
    public GameTileViewModel Tile { get; }

    /// <summary>
    /// The titles this game covers, with the per-store breakdown and the
    /// per-release achievement rows. Null when nothing has taught this modal
    /// about links, which renders as no section rather than an empty one —
    /// the pre-link view exactly.
    /// </summary>
    public GameCoverageViewModel? Coverage { get; }

    /// <summary>
    /// Draws the section only when this game covers another title. A game
    /// that covers nothing shows what it always showed.
    /// </summary>
    public bool ShowCoverage => Coverage is { HasCoverage: true };

    /// <summary>
    /// The expansions grouped under this game, and the base
    /// game it extends if it is itself a pack. A SEPARATE section from
    /// <see cref="Coverage"/> and never merged with it: a covered title is
    /// this same game on another store, an expansion is a different product
    /// with its own hours.
    /// </summary>
    public GameExpansionsViewModel? Expansions { get; }

    /// <summary>Drawn only when this game has packs under it. A game with none shows what it always showed.</summary>
    public bool ShowExpansions => Expansions is { HasExpansions: true };

    /// <summary>Drawn only when this game is itself a pack, so the grouping can be undone from either end.</summary>
    public bool ShowExtends => Expansions is { HasBase: true };

    // ── Band 1: what is this ────────────────────────────────────────────────

    public string Title => Tile.Title;

    /// <summary>True when the title is a raw app id, not a real name.</summary>
    public bool TitleIsProvisional => Tile.NameIsProvisional;

    public string ProvisionalNote => "Name not yet available. Showing the app id until metadata loads.";

    /// <summary>Year with separator, or empty. Plex Mono for the number, Jakarta for the publisher.</summary>
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

    /// <summary>Every store this game is owned on, as chip faces (TASK-70.6).</summary>
    public IReadOnlyList<string> StoreChips => Tile.StoreChips;

    /// <summary>The same stores in words, for the chip row's tooltip.</summary>
    public string StoreNames => Tile.StoreNames;

    /// <summary>The §7 bucket name this game currently falls in ("Never played").</summary>
    public string BucketLabel { get; }

    /// <summary>Install state text. Three-valued: null means unknown and hides the chip.</summary>
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

    /// <summary>Explanation when there is no gap rail (never played vs. no date recorded).</summary>
    public string NoGapText => Tile.PlaytimeMinutes <= 0
        ? "You've never opened this."
        : "Steam has no date for your last session.";

    /// <summary>
    /// Positions (0-1) of unread updates on the gap rail. Observable so a
    /// dismissal updates the rail immediately.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRailMarks))]
    public partial IReadOnlyList<double> RailMarks { get; set; } = [];

    public bool HasRailMarks => RailMarks.Count > 0;

    /// <summary>Gap rail caption: counts updates since last play, distinguishing unread from read.</summary>
    public string GapCaption
    {
        get
        {
            var missed = Updates.Count(u => u.IsUnread);
            if (missed > 0)
            {
                return missed == 1
                    ? "1 update landed while you were away."
                    : $"{missed} updates landed while you were away.";
            }

            // No unread marks: either nothing was recorded, or user marked them read.
            var read = Updates.Count(u => u.IsSinceYouPlayed);
            return read switch
            {
                0 => "No updates recorded in that stretch.",
                1 => "1 update landed while you were away. You've marked it read.",
                _ => $"{read} updates landed while you were away. You've marked them read.",
            };
        }
    }

    /// <summary>Longitudinal playtime record sentence (e.g. "Checked 5 times since Jan — up 3h").</summary>
    public string RecordLine { get; }

    public bool HasRecordLine => RecordLine.Length > 0;

    // ── Band 3: what happened while I was away ──────────────────────────────

    /// <summary>Newest first — the update the user missed most recently is the one they want.</summary>
    public IReadOnlyList<UpdateEventViewModel> Updates { get; }

    public bool HasUpdates => Updates.Count > 0;

    /// <summary>"SINCE YOU PLAYED" when gap updates exist, otherwise "UPDATE HISTORY".</summary>
    public string UpdatesLabel => Updates.Any(u => u.IsSinceYouPlayed)
        ? "SINCE YOU PLAYED"
        : "UPDATE HISTORY";

    // ── Under the list: "I've read this one" ────────────────────────────────

    /// <summary>Whether the unread flag is raised on this release (from bucket membership).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDismissFlag))]
    [NotifyPropertyChangedFor(nameof(ShowRestoreFlag))]
    public partial bool FlagIsRaised { get; set; }

    /// <summary>Whether an acknowledgement is standing. Can be true alongside FlagIsRaised if a newer push outranked the watermark.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRestoreFlag))]
    public partial bool DismissalStands { get; set; }

    /// <summary>Offered while the flag is up: the way to say you have read it.</summary>
    public bool ShowDismissFlag => _flags is not null && FlagIsRaised;

    /// <summary>Offered while the flag is down due to a dismissal (the undo control).</summary>
    public bool ShowRestoreFlag => _flags is not null && !FlagIsRaised && DismissalStands;

    /// <summary>Whether the block is on screen at all. Neither state, no block.</summary>
    public bool ShowFlagControl => ShowDismissFlag || ShowRestoreFlag;

    /// <summary>Label for the dismiss control.</summary>
    public string DismissFlagLabel => "Mark as read";

    /// <summary>Explanatory note under the dismiss control.</summary>
    public string DismissFlagNote => "Removes from Patched. A newer patch puts it back.";

    /// <summary>The way back, named for what it does rather than as "Undo".</summary>
    public string RestoreFlagLabel => "Show it again";

    /// <summary>Explanatory note under the restore control.</summary>
    public string RestoreFlagNote => "Marked read. A newer patch will flag it again.";

    /// <summary>Error message when a flag write fails. Null when no error.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlagProblem))]
    public partial string? FlagProblem { get; set; }

    public bool HasFlagProblem => FlagProblem is not null;

    /// <summary>Marks the current update as read.</summary>
    [RelayCommand]
    private async Task DismissFlagAsync(CancellationToken ct)
    {
        if (_busy || _flags is null || !FlagIsRaised)
        {
            return;
        }

        _busy = true;
        try
        {
            FlagProblem = null;

            var outcome = await _flags.DismissAsync(Tile.ReleaseId, _events, ct);
            if (!outcome.Saved)
            {
                // Both refusals leave the badge in place.
                FlagProblem = outcome.Result == UpdateFlagResult.NothingToDo
                    ? "There's no patch here to mark read."
                    : "Couldn't save that — nothing changed.";
                return;
            }

            ApplyWatermark(outcome.AcknowledgedThrough);
            FlagIsRaised = false;
            DismissalStands = true;

            await ReloadLibraryAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Belt-and-braces; service should not throw but must not take the window down.
            FlagProblem = "Couldn't save that — nothing changed.";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Revokes the standing dismissal (stamp, not delete).</summary>
    [RelayCommand]
    private async Task RestoreFlagAsync(CancellationToken ct)
    {
        if (_busy || _flags is null || !DismissalStands)
        {
            return;
        }

        _busy = true;
        try
        {
            FlagProblem = null;

            var outcome = await _flags.RestoreAsync(Tile.ReleaseId, ct);
            if (outcome.Result == UpdateFlagResult.NotStored)
            {
                FlagProblem = "Couldn't undo that just now.";
                return;
            }

            // Both Stored and NothingToDo mean no acknowledgement stands.
            ApplyWatermark(null);
            DismissalStands = false;
            FlagIsRaised = true;

            await ReloadLibraryAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            FlagProblem = "Couldn't undo that just now.";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Applies the watermark to rows and re-derives rail marks and caption.</summary>
    private void ApplyWatermark(DateTime? acknowledgedThrough)
    {
        // The service extends the watermark to cover correlated announcements.
        var readThrough = _flags is null
            ? acknowledgedThrough
            : _flags.ReadThrough(Tile.ReleaseId, _events, acknowledgedThrough);

        foreach (var update in Updates)
        {
            update.IsAcknowledged = readThrough is { } through && update.OccurredAtUtc <= through;
        }

        RailMarks = BuildRailMarks(Updates, LastPlayedUtc, _nowUtc);
        OnPropertyChanged(nameof(GapCaption));
    }

    /// <summary>Reloads the library after a flag change so bucket counts update.</summary>
    private Task ReloadLibraryAsync() => _reloadLibrary?.Invoke() ?? Task.CompletedTask;

    // ── Band 4: get me in ───────────────────────────────────────────────────

    /// <summary>Play or Install link, from the tile. Null when no honest action is available.</summary>
    public GameLink? PrimaryAction { get; }

    public bool HasPrimaryAction => PrimaryAction is not null;

    /// <summary>Store page and patch-notes hub. Empty when we hold no appid.</summary>
    public IReadOnlyList<GameLink> Links { get; }

    public bool HasLinks => Links.Count > 0;

    /// <summary>Install directory path for "open folder", or null if not on disk.</summary>
    public string? OpenableFolder => Tile.IsOnDisk ? Tile.InstallPath : null;

    public bool HasOpenableFolder => OpenableFolder is not null;

    public string? SteamAppId => Tile.SteamAppId;

    public bool HasSteamAppId => SteamAppId is not null;

    // ── Body ────────────────────────────────────────────────────────────────

    public string? Summary => Tile.Summary;

    public bool HasSummary => Summary is not null;

    /// <summary>Placeholder when no summary is available yet.</summary>
    public string EmptyBodyText => "No description yet. Metadata fills in automatically.";

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

    /// <summary>Requests the cover at full saturation for the given display width.</summary>
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

    /// <summary>Computes 0-1 positions for unread updates within the gap.</summary>
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
            .Where(u => u.IsUnread)
            .OrderBy(u => u.OccurredAtUtc)
            .Select(u => Math.Clamp((u.OccurredAtUtc - played).TotalSeconds / span, 0.0, 1.0))
            .Take(MaxRailMarks)
            .ToArray();
    }

    /// <summary>Builds the playtime record sentence from snapshots.</summary>
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
            return $"Checked once, on {since}.";
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

    /// <summary>Builds the primary action and store links from the tile's store ids.</summary>
    private static (GameLink? Primary, IReadOnlyList<GameLink> Links) BuildLinks(GameTileViewModel tile)
        => (tile.PrimaryAction, StoreActions.LinksFor(tile.Store, tile.SteamAppId, tile.GogProductId));
}
