using System.ComponentModel;
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
public partial class GameDetailsViewModel : ObservableObject, IDisposable
{
    /// <summary>Cover at a size worth looking at, on the same 2:3 capsule geometry.</summary>
    public const double CoverWidth = 200;

    public const double CoverHeight = CoverWidth * 1.5;

    /// <summary>Max update marks on the gap rail before it overflows to the list.</summary>
    private const int MaxRailMarks = 14;

    /// <summary>
    /// The modal's own cover, leased. Vivid only: §5.5 draws the art at full
    /// saturation behind the information, so the floor variant would be a
    /// second decode of every cover a user opens and nothing would draw it.
    /// </summary>
    private readonly LeasedCover _cover;

    /// <summary>Update flag service. Null hides the mark-as-read control.</summary>
    private readonly IUpdateFlagService? _flags;

    /// <summary>
    /// Embedded patch-notes reader. Null means the patch-notes button opens the
    /// system browser instead; the view tries the reader first and falls back
    /// silently.
    /// </summary>
    private readonly Core.Reading.IPatchNotesReader? _patchNotes;

    /// <summary>Raw update events for this release, passed to <see cref="IUpdateFlagService.DismissAsync"/>.</summary>
    private readonly IReadOnlyList<UpdateEvent> _events;

    /// <summary>Re-runs the library query after a dismissal changes bucket membership.</summary>
    private readonly Func<Task>? _reloadLibrary;

    private readonly DateTime _nowUtc;

    private readonly IReadOnlyList<PlaytimeSnapshot> _snapshots;

    private bool _busy;

    public GameDetailsViewModel(
        GameTileViewModel tile,
        string bucketLabel,
        IReadOnlyList<UpdateEventViewModel> updates,
        DateTime nowUtc,
        IReadOnlyList<PlaytimeSnapshot>? snapshots = null,
        ICoverLeases? covers = null,
        IReadOnlyList<UpdateEvent>? updateEvents = null,
        DateTime? acknowledgedThrough = null,
        IUpdateFlagService? updateFlags = null,
        Func<Task>? reloadLibrary = null,
        GameCoverageViewModel? coverage = null,
        GameExpansionsViewModel? expansions = null,
        Lists.GameListsViewModel? lists = null,
        Core.Reading.IPatchNotesReader? patchNotes = null,
        GameIgdbMatchViewModel? igdbMatch = null,
        GameMetadataEditorViewModel? metadataEditor = null,
        System.Windows.Input.ICommand? hideGame = null,
        IReadOnlyList<WorkRating>? ratings = null,
        IReadOnlyList<WorkImages>? images = null,
        IReadOnlyList<Ownership>? ownerships = null,
        GameRefetchViewModel? refetch = null,
        ScreenshotLightboxViewModel? lightbox = null,
        GameJournalViewModel? journal = null,
        System.Windows.Input.ICommand? addToList = null,
        IReadOnlyList<Session>? sessions = null)
    {
        Reception = GameReceptionViewModel.From(ratings);
        Screenshots = GameScreenshotsViewModel.From(images, covers, lightbox);
        Acquisition = GameAcquisitionViewModel.From(ownerships);
        Refetch = refetch;
        Journal = journal;
        HideCommand = hideGame;
        AddToListCommand = addToList;
        _patchNotes = patchNotes;
        IgdbMatch = igdbMatch;
        MetadataEditor = metadataEditor;
        Lists = lists;
        Coverage = coverage;
        Expansions = expansions;
        Tile = tile;
        _cover = new LeasedCover(covers, tile.CoverKey, CoverLayers.Vivid, art => Cover = art?.Vivid);
        BucketLabel = bucketLabel;
        Updates = updates;
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

        _snapshots = snapshots ?? [];
        Tracker = new ActivityTrackerViewModel(_snapshots, sessions ?? [],
            ownerships?.FirstOrDefault(ownership => ownership.Id == tile.OwnershipId)?.AcquiredAt,
            tile.Primary.LastPlayedAt, tile.Primary.PlaytimeMinutes, nowUtc,
            tile.Entries.Count > 1 ? $"{tile.StoreBadge} copy" : string.Empty);
        RecordLine = BuildRecordLine(_snapshots, nowUtc);
        (PrimaryAction, Links, NoWayInSentence) = BuildLinks(tile);
        GogPatchNotes = tile.PlayableEntry.Store == "gog" ? tile.PlayableEntry.Storefront?.PatchNotes : null;

        // Derives acknowledged state, rail marks, and caption.
        ApplyWatermark(acknowledgedThrough);
        if (IgdbMatch is not null) IgdbMatch.PropertyChanged += OnToolPropertyChanged;
        if (MetadataEditor is not null) MetadataEditor.PropertyChanged += OnToolPropertyChanged;
    }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    public bool IsMetadataFocused => MetadataEditor is { IsOpen: true };
    public bool IsMatchFocused => IgdbMatch is { IsOpen: true };
    public bool IsFocusedView => IsMetadataFocused || IsMatchFocused;
    public bool ShowDetailsTabs => !IsFocusedView;

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, IgdbMatch) && e.PropertyName == nameof(GameIgdbMatchViewModel.ShowPinned))
            OnPropertyChanged(nameof(HasTechnicalFacts));
        // Only an explicit open/close changes navigation. Background metadata
        // notifications must not reopen a tool or disturb the selected tab.
        if (e.PropertyName != nameof(GameMetadataEditorViewModel.IsOpen)) return;
        if (ReferenceEquals(sender, MetadataEditor) && IsMetadataFocused)
            IgdbMatch?.CloseCommand.Execute(null);
        else if (ReferenceEquals(sender, IgdbMatch) && IsMatchFocused)
            MetadataEditor?.CloseCommand.Execute(null);
        OnPropertyChanged(nameof(IsMetadataFocused));
        OnPropertyChanged(nameof(IsMatchFocused));
        OnPropertyChanged(nameof(IsFocusedView));
        OnPropertyChanged(nameof(ShowDetailsTabs));
    }

    [RelayCommand]
    private void BackToDetails()
    {
        MetadataEditor?.CloseCommand.Execute(null);
        IgdbMatch?.CloseCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowUpdates() => SelectedTabIndex = 2;

    public string OverviewHistoryText => HasGap
        ? $"Last played {LastPlayedText} · {IdleText} ago"
        : NoGapText;

    public int UnreadUpdateCount => Updates.Count(update => update.IsUnread);
    public bool HasUnreadUpdates => UnreadUpdateCount > 0;
    public string UpdatesShortcutText => GameDetailsCopy.UpdatesSincePlayed(UnreadUpdateCount);
    public string UpdatesTabAutomationName => HasUnreadUpdates
        ? $"{GameDetailsCopy.UpdatesTab}: {UpdatesShortcutText}"
        : GameDetailsCopy.UpdatesTab;
    public bool HasNoUpdates => !HasUpdates && !HasGogPatchNotes;

    public bool ShowCopyBreakdown => Coverage is { Rows.Count: > 0 };
    public bool ShowOwnCopies => !ShowCopyBreakdown;
    public IReadOnlyList<DetailsCopyRow> OwnCopies => Tile.Entries.Select(entry => new DetailsCopyRow(
        entry.StoreName,
        entry.Installed is null ? null : entry.Installed.Value ? "Installed" : "Not installed",
        GameTileViewModel.BuildPlaytimeText(entry.PlaytimeMinutes),
        entry.LastPlayedAt is { } played ? UpdateEventViewModel.LocalDateText(played) : null)).ToArray();

    public bool HasTechnicalFacts => HasSteamAppId || HasInstallPath || IgdbMatch is { ShowPinned: true };

    /// <summary>The tile this describes — title, store, art and the stat strings all come from it.</summary>
    public GameTileViewModel Tile { get; private set; }

    /// <summary>Refreshes launcher actions without replacing editors or their unsaved drafts.</summary>
    internal void RefreshTileActions(GameTileViewModel tile)
    {
        // A changed identity group needs the normal explicit reopen path.
        if (!Tile.OwnershipIds.ToHashSet().SetEquals(tile.OwnershipIds)) return;
        Tile = tile;
        (PrimaryAction, Links, NoWayInSentence) = BuildLinks(tile);
        GogPatchNotes = tile.PlayableEntry.Store == "gog" ? tile.PlayableEntry.Storefront?.PatchNotes : null;
        // Install labels, paths, accessibility copy and command parameters all derive from Tile.
        OnPropertyChanged(string.Empty);
    }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLists))]
    public partial Lists.GameListsViewModel? Lists { get; set; }

    public bool ShowLists => Lists is not null;

    /// <summary>
    /// The focused IGDB matching tool. Null when no assignment service is
    /// registered or the game has no work id.
    /// </summary>
    public GameIgdbMatchViewModel? IgdbMatch { get; }

    public bool ShowIgdbMatch => IgdbMatch is not null;

    /// <summary>
    /// The focused per-field metadata editor. Its instance stays alive when
    /// returning to a tab so unsaved field drafts survive navigation.
    /// </summary>
    public GameMetadataEditorViewModel? MetadataEditor { get; }

    /// <summary>
    /// Whether the optional editor is available; navigation is controlled by IsOpen.
    /// </summary>
    public bool ShowMetadataEditor => MetadataEditor is not null;

    // ── Identity ────────────────────────────────────────────────

    public string Title => Tile.Title;

    /// <summary>
    /// Raises change notifications for <see cref="Title"/> and
    /// <see cref="TitleIsProvisional"/>, which are computed off the tile
    /// and do not follow from its own notifications. The library calls
    /// this after renaming the tiles so the headline follows without the
    /// modal being rebuilt — rebuilding would close the editor and take
    /// the user's unsaved drafts with it.
    /// </summary>
    internal void NotifyTitleChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(TitleIsProvisional));
    }

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

    /// <summary>Overview's reception line: up to three attributed figures, never blended.</summary>
    public GameReceptionViewModel? Reception { get; }

    /// <summary>Drawn only when at least one source contributed a figure.</summary>
    public bool ShowReception => Reception is { HasFigures: true };

    public string StoreBadge => Tile.StoreBadge;

    /// <summary>Every store this game is owned on, as chip faces (TASK-70.6).</summary>
    public IReadOnlyList<string> StoreChips => Tile.StoreChips;

    /// <summary>The same stores in words, for the chip row's tooltip.</summary>
    public string StoreNames => Tile.StoreNames;

    /// <summary>The §7 bucket name this game currently falls in ("Never played").</summary>
    public string BucketLabel { get; }

    public bool HasLifecycle => Tile.HasLifecycle;

    public string? LifecycleText => Tile.LifecycleText;

    /// <summary>Install state text. Three-valued: null means unknown and hides the chip.</summary>
    public string InstallText => Tile.Installed == true ? "Installed" : "Not installed";

    /// <summary>False when nothing has looked at this game's install state.</summary>
    public bool HasInstallState => Tile.Installed is not null;

    public string? InstallPath => Tile.InstallPath;

    public bool HasInstallPath => InstallPath is not null;

    // ── Your history ──────────────────────────────────────────

    /// <summary>Total on the clock — the one number big enough to read from across the room.</summary>
    public string PlaytimeText => Tile.PlaytimeText;

    public ActivityTrackerViewModel Tracker { get; }

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

    /// <summary>The lifetime axis series, recomputed on each watermark change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAxis))]
    [NotifyPropertyChangedFor(nameof(ShowGapRail))]
    [NotifyPropertyChangedFor(nameof(ShowAxisUnmeasured))]
    [NotifyPropertyChangedFor(nameof(AxisUnmeasuredNote))]
    [NotifyPropertyChangedFor(nameof(AxisStartText))]
    public partial PlayAxisSeries Axis { get; set; } = PlayAxisSeries.None;

    /// <summary>True when the axis has enough data to draw. False falls back to the gap rail.</summary>
    public bool ShowAxis => HasGap && Axis.CanDraw;

    /// <summary>True when the gap rail should be drawn instead of the axis.</summary>
    public bool ShowGapRail => HasGap && !Axis.CanDraw;

    /// <summary>Accessible name for the lifetime axis control.</summary>
    public string AxisAutomationName => PlayAxisCopy.AxisAutomationName;

    /// <summary>Tooltip for the lifetime axis control.</summary>
    public string AxisTooltip => PlayAxisCopy.AxisTooltip;

    /// <summary>Sentence stating whose hours these are — the user's own, never a player population.</summary>
    public string AxisOwnHoursNote => PlayAxisCopy.OwnHoursNote;

    /// <summary>True when unmeasured play exists before the coverage boundary.</summary>
    public bool ShowAxisUnmeasured => Axis.HasUnmeasured;

    /// <summary>Sentence describing the unmeasured zone: an amount and the date it covers through.</summary>
    public string AxisUnmeasuredNote => Axis.HasUnmeasured
        ? PlayAxisCopy.UnmeasuredNote(
            SpanText(Axis.UnmeasuredMinutes),
            Axis.CoverageStartUtc.ToLocalTime().ToString("MMM yyyy"))
        : string.Empty;

    /// <summary>Left-edge label: the release year.</summary>
    public string AxisStartText => Axis.CanDraw
        ? Axis.AxisStartUtc.Year.ToString("D4")
        : string.Empty;

    /// <summary>Sentence restating in words what the axis draws: last session, idle time, missed updates.</summary>
    public string AxisLastSessionLine => PlayAxisCopy.LastSessionLine(
        LastPlayedText,
        IdleText,
        Updates.Count(u => u.IsSinceYouPlayed));

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

    // ── Updates ──────────────────────────────

    /// <summary>Newest first — the update the user missed most recently is the one they want.</summary>
    public IReadOnlyList<UpdateEventViewModel> Updates { get; }

    public bool HasUpdates => Updates.Count > 0;

    /// <summary>
    /// The update list's heading, constant whether or not anything landed since
    /// the last session. SINCE YOU PLAYED is the history rail's label; one modal
    /// was saying the same words about two different things, so the list took a
    /// name of its own.
    /// </summary>
    public string UpdatesLabel => GameDetailsCopy.UpdatesHeading;

    /// <summary>True when at least one update carries a readable link.</summary>
    public bool HasNotesPage => Updates.Any(u => u.HasLink);

    /// <summary>True when the game has updates but none of them carries a link.</summary>
    public bool ShowNoNotesNote => HasUpdates && !HasNotesPage;

    /// <summary>Shown under the update list when no update carries a readable link.</summary>
    public string NoNotesText => "No patch notes page available for these updates.";

    /// <summary>
    /// Tries to open <paramref name="link"/> in the embedded patch-notes panel.
    /// Returns false when there is no reader or the policy refuses the URL, and
    /// the view then falls back to the system browser.
    /// </summary>
    public bool TryReadNotes(GameLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (_patchNotes is not { IsAvailable: true } reader
            || !Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return reader.Open(uri, Title) == Core.Reading.PatchNotesOutcome.Opened;
    }

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

        Tracker.RefreshUpdates(Updates);

        RailMarks = BuildRailMarks(Updates, LastPlayedUtc, _nowUtc);

        Axis = PlayAxisSeries.Build(
            _snapshots,
            Tile.ReleaseYear,
            LastPlayedUtc,
            [.. Updates.Where(u => u.IsUnread).Select(u => u.OccurredAtUtc)],
            _nowUtc);

        OnPropertyChanged(nameof(GapCaption));
        OnPropertyChanged(nameof(AxisLastSessionLine));
        OnPropertyChanged(nameof(UnreadUpdateCount));
        OnPropertyChanged(nameof(HasUnreadUpdates));
        OnPropertyChanged(nameof(UpdatesShortcutText));
        OnPropertyChanged(nameof(UpdatesTabAutomationName));
    }

    /// <summary>Reloads the library after a flag change so bucket counts update.</summary>
    private Task ReloadLibraryAsync() => _reloadLibrary?.Invoke() ?? Task.CompletedTask;

    // ── Actions ───────────────────────────────────────────────────

    /// <summary>Play or Install link, from the tile. Null when no honest action is available.</summary>
    public GameLink? PrimaryAction { get; private set; }

    public bool HasPrimaryAction => PrimaryAction is not null;

    public GameLink? ManagementAction => StoreActions.ManagementFor(
        Tile.PlayableEntry.Store, Tile.Installed, Tile.SteamAppId, Tile.PlayableEntry.GogProductId);

    public bool HasManagementAction => ManagementAction is not null;

    /// <summary>Store page and patch-notes hub. Empty when we hold no appid.</summary>
    public IReadOnlyList<GameLink> Links { get; private set; }

    public bool HasLinks => Links.Count > 0;

    public string? GogPatchNotes { get; private set; }
    public bool HasGogPatchNotes => !string.IsNullOrWhiteSpace(GogPatchNotes);

    /// <summary>
    /// Explains why no primary action or outbound link is available for this copy.
    /// </summary>
    public string? NoWayInSentence { get; private set; }

    /// <summary>True when <see cref="NoWayInSentence"/> is non-null and the line should be drawn.</summary>
    public bool HasNoWayInSentence => NoWayInSentence is not null;

    /// <summary>Install directory path for "open folder", or null if not on disk.</summary>
    public string? OpenableFolder => Tile.IsOnDisk ? Tile.InstallPath : null;

    public bool HasOpenableFolder => OpenableFolder is not null;

    public string? SteamAppId => Tile.SteamAppId;

    public bool HasSteamAppId => SteamAppId is not null;

    /// <summary>Face of the More trigger — always the same word, because the menu owns its open state.</summary>
    public string MoreActionsLabel => GameActionBandCopy.OpenLabel;

    /// <summary>Tooltip on the More trigger.</summary>
    public string MoreActionsTooltip => GameActionBandCopy.OpenTooltip;

    /// <summary>
    /// The library's hide command, handed in at construction. The row that
    /// carries it sits inside a popup, which has no Window above it for a
    /// <c>$parent[Window]</c> binding to find. Null leaves the row undrawn
    /// rather than inert (§10.3).
    /// </summary>
    public System.Windows.Input.ICommand? HideCommand { get; }

    public System.Windows.Input.ICommand? AddToListCommand { get; }

    public bool ShowAddToList => AddToListCommand is not null;

    /// <summary>The Hide row draws only when the library handed over its command.</summary>
    public bool ShowHide => HideCommand is not null;

    public bool HasMoreActions => HasLinks || HasManagementAction || HasOpenableFolder
        || ShowRefetch || ShowIgdbMatch || ShowMetadataEditor || ShowHide;

    /// <summary>Hide label — always singular, because this modal shows one game.</summary>
    public string HideLabel => LibrarySettingsCopy.HideDetailsButton;

    /// <summary>Tooltip on Hide, shared with the library's context menu.</summary>
    public string HideTooltip => LibrarySettingsCopy.HideTooltip;

    /// <summary>Accessible group name for the header.</summary>
    public string IdentityGroupName => GameDetailsCopy.IdentityGroupName;

    /// <summary>Accessible group name for the Activity history.</summary>
    public string HistoryGroupName => GameDetailsCopy.HistoryGroupName;

    /// <summary>Accessible group name for the header actions.</summary>
    public string ActionsGroupName => GameDetailsCopy.ActionsGroupName;

    /// <summary>Accessible name for the modal's close button.</summary>
    public string CloseAutomationName => GameDetailsCopy.CloseAutomationName;

    /// <summary>Value label for the ACQUIRED block in Library.</summary>
    public string AcquiredLabel => GameDetailsCopy.AcquiredLabel;

    /// <summary>Section heading for ABOUT in Overview.</summary>
    public string AboutHeading => GameDetailsCopy.AboutHeading;

    /// <summary>Accessible name for the launch button, e.g. "Install Empyrion: Galactic Survival".</summary>
    public string LaunchAutomationName => PrimaryAction is { } action
        ? GameDetailsCopy.LaunchAutomationName(action.Label, Title)
        : string.Empty;

    // ── Body ────────────────────────────────────────────────────────────────

    /// <summary>The screenshot strip inside ABOUT. Null when no screenshots exist.</summary>
    public GameScreenshotsViewModel? Screenshots { get; }

    /// <summary>Drawn only when screenshots exist.</summary>
    public bool ShowScreenshots => Screenshots is { HasShots: true };

    /// <summary>The ACQUIRED block in Library. Null when neither date nor licence exists.</summary>
    public GameAcquisitionViewModel? Acquisition { get; }

    /// <summary>Drawn only when an acquisition fact exists.</summary>
    public bool ShowAcquisition => Acquisition is not null;

    /// <summary>The More-menu refetch row and its status field. Null when no refetch service is registered.</summary>
    public GameRefetchViewModel? Refetch { get; }

    /// <summary>Drawn only when a refetch service is available.</summary>
    public bool ShowRefetch => Refetch is not null;

    /// <summary>Saved post-session notes for this game, or null when no session store is available.</summary>
    public GameJournalViewModel? Journal { get; }

    public bool ShowJournal => Journal is not null;

    public string? Summary => Tile.Summary;

    public bool HasSummary => Summary is not null;

    public const int SummaryPreviewLength = 360;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyPropertyChangedFor(nameof(SummaryDisclosureLabel))]
    public partial bool IsSummaryExpanded { get; set; }

    public bool CanExpandSummary => Summary is { Length: > SummaryPreviewLength };

    public string? SummaryText
    {
        get
        {
            if (Summary is not { } summary || IsSummaryExpanded || !CanExpandSummary) return Summary;
            var end = summary.LastIndexOf(' ', SummaryPreviewLength);
            return summary[..(end > 0 ? end : SummaryPreviewLength)].TrimEnd() + "…";
        }
    }

    public string SummaryDisclosureLabel => IsSummaryExpanded ? GameDetailsCopy.ReadLess : GameDetailsCopy.ReadMore;

    [RelayCommand]
    private void ToggleSummary() => IsSummaryExpanded = !IsSummaryExpanded;

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
    public void RequestCover(double displayWidthPixels) => _cover.Request(displayWidthPixels);

    /// <summary>
    /// The modal is closed and this view model is dropped. Releases every lease
    /// the modal and its sections hold, which is what lets the decoded-memory
    /// cache free art the user is no longer looking at. The library disposes
    /// the outgoing instance when <c>Details</c> changes.
    /// </summary>
    public void Dispose()
    {
        if (IgdbMatch is not null) IgdbMatch.PropertyChanged -= OnToolPropertyChanged;
        if (MetadataEditor is not null) MetadataEditor.PropertyChanged -= OnToolPropertyChanged;
        _cover.Dispose();
        Screenshots?.Dispose();
        IgdbMatch?.Dispose();
        MetadataEditor?.Dispose();
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
    internal static string SpanText(long minutes)
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
    /// Builds the primary action, store links and no-way-in sentence from
    /// the tile's store ids. Returns three values: the primary action (null
    /// when none is honest), the outbound links (empty when no id is held),
    /// and the sentence explaining why there is no way in (null when the
    /// band does have a primary action or at least one link).
    /// </summary>
    private static (GameLink? Primary, IReadOnlyList<GameLink> Links, string? NoWayIn) BuildLinks(
        GameTileViewModel tile)
    {
        var primary = tile.PrimaryAction;
        var links = StoreActions.LinksFor(tile.Store, tile.SteamAppId, tile.GogProductId, tile.PlayableEntry.Storefront);

        var sentence = primary is null && links.Count == 0
            ? GameActionBandCopy.NoWayInSentence(tile.NoWayIn)
            : null;

        return (primary, links, sentence);
    }
}

public sealed record DetailsCopyRow(string Store, string? InstallState, string Playtime, string? LastPlayed)
{
    public bool HasInstallState => InstallState is not null;
    public bool HasLastPlayed => LastPlayed is not null;
}
