using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// One feed recommendation card: a borrowed library tile, the engine's reason
/// sentence, and "not interested" / "not now" feedback (§6b). After a verdict
/// the action line becomes an undo receipt; a failed write keeps both controls
/// in place.
/// </summary>
public partial class FeedCardViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Optional so a host that never registered the feedback store costs the two
    /// controls rather than the screen — the same rule the tile source follows.
    /// With no service the buttons are simply not offered, which is honest;
    /// offering them and swallowing the click is not.
    /// </summary>
    private readonly IFeedService? _feed;
    private readonly Action<GameTileViewModel>? _addToList;

    private bool _busy;

    /// <summary>Unheld time this receipt has been standing.</summary>
    private TimeSpan _counted;

    public FeedCardViewModel(GameTileViewModel tile, string reason, IFeedService? feed = null,
        Action<GameTileViewModel>? addToList = null)
    {
        Tile = tile;
        Cover = tile.NewCoverPresenter();
        Reason = reason;
        ReasonRuns = ReasonText.Split(reason);
        _feed = feed;
        _addToList = addToList;
    }

    public bool CanAddToList => _addToList is not null;

    [RelayCommand(CanExecute = nameof(CanAddToList))]
    private void AddToList() => _addToList?.Invoke(Tile);

    /// <summary>
    /// The library's own tile. Shared instance, not a copy — see
    /// <see cref="IGameTileSource"/> for why the feed borrows rather than builds.
    /// </summary>
    public GameTileViewModel Tile { get; }

    /// <summary>
    /// This card's own cover state. The tile is borrowed and the wall recycles
    /// it; the art on this card is not the wall's to blank.
    /// </summary>
    public CoverPresenter Cover { get; }

    /// <summary>Drops this card's cover state when the shelf it belongs to is replaced.</summary>
    public void Dispose() => Cover.Dispose();

    /// <summary>
    /// The engine's sentence, verbatim, for the accessible name and for any
    /// caller that wants the text rather than the runs.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// The sentence split into prose and numbers, so the card can set every
    /// number in Plex Mono with tabular figures (§3) without rewriting a word
    /// of it. See <see cref="ReasonText"/>.
    /// </summary>
    public IReadOnlyList<ReasonRun> ReasonRuns { get; }

    /// <summary>
    /// Raised after this card's verdict is stored or taken back. The Feed's
    /// header states how many verdicts are on record, and a count that only
    /// caught up when somebody opened the history would be a number the
    /// interface was wrong about for as long as nobody checked.
    /// </summary>
    internal event EventHandler? VerdictChanged;

    /// <summary>
    /// Which scoring pass built this card. A swap belonging to a pass the feed
    /// has already replaced must not land on the pass that replaced it.
    /// </summary>
    internal long Generation { get; init; }

    internal long SurfacingReleaseId { get; init; }
    internal long FeedbackReleaseId => SurfacingReleaseId > 0 ? SurfacingReleaseId : Tile.ReleaseId;

    /// <summary>
    /// Whether the shelf behind this card is holding something to put in its
    /// place. False means the receipt has nowhere to go, and it keeps the card's
    /// place with no countdown on it — a clock that ran out on nothing would be
    /// stating a replacement the shelf does not have.
    /// </summary>
    [ObservableProperty]
    public partial bool CanReplace { get; set; }

    /// <summary>
    /// How much of the receipt's three seconds has run, 0 to 1. The view sweeps
    /// an arc from it. Determinate on purpose: it states the time left before
    /// the undo goes, which is a fact the reader is entitled to act on, not a
    /// decoration saying that something is happening.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountdownSweep))]
    public partial double CountdownProgress { get; set; }

    /// <summary>The arc's sweep in degrees, so the view binds a number rather than carrying a converter.</summary>
    public double CountdownSweep => CountdownProgress * 360.0;

    /// <summary>Whether the receipt is on a clock. False for a receipt with nowhere to go.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusAnnouncement))]
    public partial bool IsCountingDown { get; set; }

    /// <summary>
    /// Held while the reader is on this card — the pointer is over it, or
    /// something inside it has focus. A three-second window on an undo is a time
    /// limit, and taking it away from somebody who is looking at it is the one
    /// thing this countdown must not do. Every verdict keeps its undo on the
    /// history screen afterwards regardless.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCountdownHeld))]
    public partial bool IsPointerOver { get; set; }

    /// <inheritdoc cref="IsPointerOver"/>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCountdownHeld))]
    public partial bool IsFocusWithin { get; set; }

    /// <summary>Whether the countdown is being held where it is.</summary>
    public bool IsCountdownHeld => IsPointerOver || IsFocusWithin;

    /// <summary>
    /// Whether the two feedback controls are offered at all. False only when
    /// nothing can store the answer.
    /// </summary>
    public bool CanGiveFeedback => _feed is not null;

    /// <summary>
    /// The verdict standing on this card, or null. Kept so <c>Undo</c> revokes
    /// the kind that was actually given rather than guessing at one — the two
    /// are separate rows in storage and revoking the wrong one would silently
    /// do nothing.
    /// </summary>
    public FeedVerdictKind? Verdict { get; private set; }

    /// <summary>
    /// True once a verdict has been stored from this card. The card keeps its
    /// place, its cover and its sentence; what changes is the action line.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions), nameof(StatusAnnouncement))]
    public partial bool IsSetAside { get; set; }

    /// <summary>
    /// The receipt's words. Two different facts and they are worded as two:
    /// a dismissal states what is now true, a snooze states the day it ends.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusAnnouncement))]
    public partial string SetAsideNote { get; set; } = string.Empty;

    /// <summary>
    /// The date a snooze lapses, alone so the view can set it in the data face
    /// (§3) while the words beside it stay prose. Empty for a dismissal, which
    /// has no date to state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetAsideDate), nameof(StatusAnnouncement))]
    public partial string SetAsideDate { get; set; } = string.Empty;

    /// <summary>
    /// Set when a write did not land. Shown above the action line, which keeps
    /// both controls exactly where they were so the user can simply press again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem), nameof(StatusAnnouncement))]
    public partial string? Problem { get; set; }

    public bool HasSetAsideDate => SetAsideDate.Length > 0;

    public bool HasProblem => Problem is not null;

    /// <summary>
    /// What a screen reader is told about this card: the tile's own name, then
    /// the engine's sentence. The sentence is the whole point of a feed card —
    /// <see cref="Reason"/> has always said it was kept "for the accessible
    /// name", and until now nothing acted on that — and it was reachable only
    /// by walking into the card's children.
    /// <para>One string, and it sits on the Button, because the Button is the
    /// Tab stop and the one element here with an automation peer of its own.
    /// The panels and TextBlocks it wraps are pruned from the control view or
    /// answer with their own text, so a name hung on any of them says nothing
    /// or says the wrong thing.</para>
    /// </summary>
    public string AutomationName
    {
        get
        {
            var name = Tile.AutomationName;
            return Reason.Length == 0 ? name : $"{name} {Reason}";
        }
    }

    /// <summary>
    /// Everything about this card that changes after it is drawn, in one
    /// sentence, bound to <c>AutomationProperties.ItemStatus</c>. It carries
    /// the failed write first, then the verdict receipt, and says the undo is
    /// on a clock while the countdown runs. Empty while the card is unanswered,
    /// so an untouched card announces no state at all.
    /// <para>ItemStatus and not the name: <c>ControlAutomationPeer</c> raises a
    /// UIA property-changed event for IsVisible, Bounds, RenderTransform,
    /// VisualParent and ItemStatus, and for nothing else. Changing a Name at
    /// runtime announces nothing, so a reader who had already passed the card
    /// would never learn that the verdict landed, or that it did not.</para>
    /// </summary>
    public string StatusAnnouncement
    {
        get
        {
            if (HasProblem)
            {
                return Problem!;
            }

            if (!IsSetAside)
            {
                return string.Empty;
            }

            var note = HasSetAsideDate ? $"{SetAsideNote} {SetAsideDate}" : SetAsideNote;
            return IsCountingDown
                ? $"{note} Undo before this card is replaced."
                : note;
        }
    }

    /// <summary>The action line's two states, and they are exclusive.</summary>
    public bool ShowActions => !IsSetAside;

    /// <summary>
    /// "Not interested" — the durable verdict. Never expires; holds until the
    /// user takes it back, here or on the history screen.
    /// </summary>
    [RelayCommand]
    private Task NotInterestedAsync(CancellationToken ct)
        => GiveAsync(FeedVerdictKind.NotInterested, ct);

    /// <summary>
    /// "Not now" — the deferral. Lapses by itself after the default snooze, with
    /// no write anywhere when it does.
    /// </summary>
    [RelayCommand]
    private Task NotNowAsync(CancellationToken ct)
        => GiveAsync(FeedVerdictKind.Snoozed, ct);

    /// <summary>
    /// Takes it back. A revocation stamp, never a delete — the history survives,
    /// which is what makes the loop inspectable rather than merely reversible.
    /// </summary>
    [RelayCommand]
    private async Task UndoAsync(CancellationToken ct)
    {
        if (_busy || _feed is null || Verdict is not { } kind)
        {
            return;
        }

        _busy = true;
        try
        {
            Problem = null;

            // False here is not necessarily a failure — a snooze can lapse
            // under the user's finger, and it had already undone itself. Either
            // way the honest thing to draw is the card back as it was.
            await _feed.RevokeVerdictAsync(FeedbackReleaseId, kind, ct);
            Restore();
            VerdictChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The service is contracted not to throw; this is the belt on top of
            // the braces, because a card may not take the screen with it.
            Problem = "Couldn't undo that just now.";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// How long a receipt stays before its replacement takes the slot. Three
    /// seconds is long enough to read one line and reach the undo beside it,
    /// and short enough that answering a run of cards does not turn into
    /// waiting for each one. It is a floor rather than a deadline: the clock is
    /// held for as long as the reader is on the card.
    /// </summary>
    internal static TimeSpan Countdown { get; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Advances the receipt's clock and reports whether it has run out. Driven
    /// by the screen rather than by a timer of its own: several receipts can be
    /// counting at once, and one ticker for the feed is cheaper and easier to
    /// reason about than one per card.
    /// </summary>
    /// <param name="elapsed">Time since the last tick. Ignored while the countdown is held.</param>
    internal bool Tick(TimeSpan elapsed)
    {
        if (!IsCountingDown)
        {
            return false;
        }

        if (IsCountdownHeld)
        {
            return false;
        }

        _counted += elapsed;

        var progress = _counted / Countdown;
        CountdownProgress = Math.Clamp(progress, 0.0, 1.0);

        return progress >= 1.0;
    }

    /// <summary>Puts the receipt on the clock, if the shelf has something to put in its place.</summary>
    private void StartCountdown()
    {
        if (!IsSetAside || !CanReplace || IsCountingDown)
        {
            return;
        }

        _counted = TimeSpan.Zero;
        CountdownProgress = 0.0;
        IsCountingDown = true;
    }

    private void StopCountdown()
    {
        _counted = TimeSpan.Zero;
        CountdownProgress = 0.0;
        IsCountingDown = false;
    }

    /// <summary>
    /// A shelf that had nothing to offer can be handed something by a backfill
    /// while a receipt is standing on it, and then the receipt has somewhere to
    /// go after all.
    /// </summary>
    partial void OnCanReplaceChanged(bool value)
    {
        if (value)
        {
            StartCountdown();
        }
        else
        {
            StopCountdown();
        }
    }

    /// <summary>
    /// Puts the card back to its unanswered state. Called by the undo above and
    /// by the history screen, whose revoke covers the same release — a receipt
    /// still showing "off the feed" for a verdict the user has just taken back
    /// somewhere else would be the two surfaces disagreeing about one row.
    /// </summary>
    internal void Restore()
    {
        Verdict = null;
        IsSetAside = false;
        SetAsideNote = string.Empty;
        SetAsideDate = string.Empty;
        StopCountdown();
    }

    private async Task GiveAsync(FeedVerdictKind kind, CancellationToken ct)
    {
        if (_busy || _feed is null || IsSetAside)
        {
            return;
        }

        _busy = true;
        try
        {
            Problem = null;

            var outcome = await _feed.RecordVerdictAsync(FeedbackReleaseId, kind, ct);
            if (!outcome.Saved)
            {
                // Both controls stay put and the card says why. See the class
                // remarks: a receipt over a write that did not land is the one
                // lie this surface cannot afford.
                Problem = "Couldn't save that — nothing changed.";
                return;
            }

            Verdict = kind;
            SetAsideNote = kind == FeedVerdictKind.Snoozed ? "Back on" : "Off the feed.";
            SetAsideDate = kind == FeedVerdictKind.Snoozed && outcome.ExpiresAt is { } expires
                ? expires.ToLocalTime().ToString("d MMM yyyy")
                : string.Empty;
            IsSetAside = true;
            StartCountdown();
            VerdictChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Problem = "Couldn't save that — nothing changed.";
        }
        finally
        {
            _busy = false;
        }
    }
}
