using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.Services;

namespace Hoard.App.ViewModels;

/// <summary>
/// One card on one shelf: a game the library already knows how to draw, the
/// sentence that says why it is in front of you, and the two things you are
/// allowed to say back.
///
/// <para><b>The reason is on the FRONT, in full, always.</b> Not a tooltip, not
/// trimmed to "Patched", not paraphrased into a genre label — the sentence is
/// the product, and a wall of covers with the reasons hidden is the storefront
/// feed Hoard is trying to beat. The card is therefore sized to the sentence
/// (measured on the real library: median 115 characters, 90th percentile 155,
/// longest 256) rather than the sentence trimmed to a card.</para>
///
/// <para><b>And that is why the card does not flip.</b> The library's tiles turn
/// over because a cover has nowhere to put four facts; this card is already
/// showing the one fact that matters, and turning it over would hide the reason
/// to reveal a weaker restatement of it — the bucket name, the playtime and the
/// last-played date are all clauses of the sentence on the front. Both actions
/// the back face carried are on this face instead: the primary Play/Install and
/// the route to the detail modal.</para>
///
/// <para><b>Two feedback controls, never one (§6b).</b> "Not interested" is a
/// verdict and "not now" is a deferral; the storage keeps them apart because
/// collapsing them loses the difference forever, and a single dismiss control
/// that guessed would be the place the loss happened. They sit in the action
/// line the card already had, so they cost the sentence no height at all — see
/// the view for the geometry, which is the whole of how they stay
/// secondary.</para>
///
/// <para><b>Undo is where the click was, and it stays.</b> Acting does not make
/// the card vanish: the action line becomes a one-line receipt with an
/// <c>Undo</c> in the same place the pressed control stood, and it holds until
/// the feed is next computed. No timer, because a recoverable act with a
/// countdown on it is an act somebody loses a race with — and an
/// irreversible-feeling dismiss is how a feedback affordance stops being used at
/// all. Past that, the history screen is the second route back, which is why it
/// exists.</para>
///
/// <para><b>A write that did not land never shows a receipt.</b> The card says
/// it could not save it and leaves both controls where they were. Claiming a
/// dismissal the database does not hold would put the game back on the feed
/// tomorrow in front of a user who believes they already answered for it.</para>
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
