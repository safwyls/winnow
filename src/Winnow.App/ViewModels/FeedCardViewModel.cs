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
public partial class FeedCardViewModel : ObservableObject
{
    /// <summary>
    /// Optional so a host that never registered the feedback store costs the two
    /// controls rather than the screen — the same rule the tile source follows.
    /// With no service the buttons are simply not offered, which is honest;
    /// offering them and swallowing the click is not.
    /// </summary>
    private readonly IFeedService? _feed;

    private bool _busy;

    public FeedCardViewModel(GameTileViewModel tile, string reason, IFeedService? feed = null)
    {
        Tile = tile;
        Reason = reason;
        ReasonRuns = ReasonText.Split(reason);
        _feed = feed;
    }

    /// <summary>
    /// The library's own tile. Shared instance, not a copy — see
    /// <see cref="IGameTileSource"/> for why the feed borrows rather than builds.
    /// </summary>
    public GameTileViewModel Tile { get; }

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
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsSetAside { get; set; }

    /// <summary>
    /// The receipt's words. Two different facts and they are worded as two:
    /// a dismissal states what is now true, a snooze states the day it ends.
    /// </summary>
    [ObservableProperty]
    public partial string SetAsideNote { get; set; } = string.Empty;

    /// <summary>
    /// The date a snooze lapses, alone so the view can set it in the data face
    /// (§3) while the words beside it stay prose. Empty for a dismissal, which
    /// has no date to state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetAsideDate))]
    public partial string SetAsideDate { get; set; } = string.Empty;

    /// <summary>
    /// Set when a write did not land. Shown above the action line, which keeps
    /// both controls exactly where they were so the user can simply press again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasSetAsideDate => SetAsideDate.Length > 0;

    public bool HasProblem => Problem is not null;

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
            await _feed.RevokeVerdictAsync(Tile.ReleaseId, kind, ct);
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

            var outcome = await _feed.RecordVerdictAsync(Tile.ReleaseId, kind, ct);
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
