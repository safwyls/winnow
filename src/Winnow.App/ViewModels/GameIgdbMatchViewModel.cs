using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.ViewModels;

/// <summary>
/// The details modal's recourse when fuzzy resolution picked the wrong IGDB
/// entry, or none. Search by title, pick the right one, and pin it so later
/// enrichment passes leave it alone.
///
/// <para>Disclosed from a "Wrong game?" row in the action band's menu
/// (§10.3). The search field and candidate list draw full width in the
/// right column's rest band; Clear alone sits in the left column, under the
/// identity facts. The search is disclosed inline, in the modal's own tree,
/// never a flyout (an adorner layer does not exist inside a popup).</para>
///
/// <para>When the assignment hits a UNIQUE-constraint collision on
/// <c>works.igdb_id</c>, the control turns the refusal into an offer:
/// it names and shows the game that already holds the entry and offers
/// to link the two as the same game. Accepting writes a <c>same_game</c>
/// identity link and pins nothing; declining restores the refusal
/// sentence.</para>
///
/// <para>Optional in the way every seam on this modal is. No service and no
/// work id means no control at all, which is the pre-TASK-89 modal
/// exactly.</para>
/// </summary>
public partial class GameIgdbMatchViewModel : ObservableObject
{
    /// <summary>Candidate thumbnail width, matching the merge queue's candidate-cover geometry.</summary>
    public const double CandidateCoverWidth = 34;

    public const double CandidateCoverHeight = CandidateCoverWidth * 1.5;

    private readonly IIgdbAssignmentService _service;
    private readonly ICoverCache? _covers;

    /// <summary>
    /// Reloads the library and reopens this modal on the same ownership,
    /// carrying the confirmation note across the rebuild so the user sees the
    /// corrected cover and metadata where they asked for them. Null in a test,
    /// where the note lands on this instance instead.
    ///
    /// <para>Both writes on this control run through it. Assigning changes
    /// the metadata and the cover key; clearing changes the cover key back,
    /// because a live IGDB pin outranks the store capsule and dropping the
    /// pin hands a Steam-owned game its capsule again.</para>
    /// </summary>
    private readonly Func<string, Task>? _afterChange;

    /// <summary>
    /// Links this work under the claiming work as the same game, through
    /// the identity-link path the Merges queue uses. Null when no
    /// identity-link repository is registered; a null delegate means the
    /// collision degrades to its bare refusal sentence and the offer is
    /// never shown.
    /// </summary>
    private readonly Func<long, Task<bool>>? _linkSameGame;

    private readonly long _workId;

    private double _coverWidthPixels = CandidateCoverWidth;

    private bool _busy;

    public GameIgdbMatchViewModel(
        IIgdbAssignmentService service,
        long workId,
        string title,
        WorkIgdbPin? pin = null,
        ICoverCache? covers = null,
        Func<string, Task>? afterChange = null,
        string? note = null,
        Func<long, Task<bool>>? linkSameGame = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _workId = workId;
        _covers = covers;
        _afterChange = afterChange;
        _linkSameGame = linkSameGame;

        // The title the library already shows is the search anyone would type.
        Query = title ?? string.Empty;
        IsPinned = pin is not null;
        Note = note;
    }

    /// <summary>Whether the search surface is disclosed.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The title being searched for. Seeded from the game's own title.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Query { get; set; }

    /// <summary>Candidates the last search returned.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCandidates))]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    public partial IReadOnlyList<IgdbCandidateViewModel> Candidates { get; set; } = [];

    /// <summary>True once a search has completed, so an empty list can be distinguished from no search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    public partial bool Searched { get; set; }

    /// <summary>Whether a live pin stands. Draws the Clear control.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPinned))]
    public partial bool IsPinned { get; set; }

    /// <summary>Standing note beside the control: pin confirmation, assignment confirmation, or cleared.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    /// <summary>Status field, in words. Null when nothing is in flight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    [NotifyPropertyChangedFor(nameof(ShowIdMiss))]
    public partial string? Status { get; set; }

    /// <summary>A refusal sentence, drawn in Amber. The controls stay in place for a retry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    [NotifyPropertyChangedFor(nameof(ShowIdMiss))]
    public partial string? Problem { get; set; }

    /// <summary>True when the query was all digits and the id lookup returned
    /// nothing. Drawn as its own line in <c>TextDim</c> beside the title
    /// results; it is not a failure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIdMiss))]
    public partial bool IdMissed { get; set; }

    /// <summary>
    /// The game that already holds the chosen IGDB entry, shown so the user
    /// can judge whether it really is the same game. Null whenever no offer
    /// stands: before a collision, after the user accepts or declines, and
    /// when the holder could not be resolved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowClaim))]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    [NotifyPropertyChangedFor(nameof(ShowIdMiss))]
    public partial IgdbClaimViewModel? Claim { get; set; }

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasNote => Note is not null;

    public bool HasStatus => Status is not null;

    public bool HasProblem => Problem is not null;

    /// <summary>Gates the Clear control in the left column. The standing note
    /// has its own <c>HasNote</c> gate in the right column.</summary>
    public bool ShowPinned => IsPinned;

    /// <summary>
    /// True when a search ran and found nothing, which is not a problem and
    /// does not read as one. Never shown while something is in flight or
    /// while a refusal is standing.
    /// </summary>
    public bool ShowNoMatches
        => Searched && Candidates.Count == 0 && Status is null && Problem is null && Claim is null;

    /// <summary>
    /// True when the id-miss line should be drawn: the id lookup found
    /// nothing, nothing is in flight and no refusal is standing.
    /// </summary>
    public bool ShowIdMiss => IdMissed && Status is null && Problem is null && Claim is null;

    /// <summary>Whether the same-game offer is standing and should be drawn.</summary>
    public bool ShowClaim => Claim is not null;

    public string ClaimLinkLabel => GameIgdbMatchCopy.ClaimLinkLabel;

    public string ClaimDeclineLabel => GameIgdbMatchCopy.ClaimDeclineLabel;

    public string IdMissText => GameIgdbMatchCopy.IdMissText;

    /// <summary>
    /// Returns the IGDB id when <paramref name="query"/> is composed
    /// entirely of digits and parses to a positive <see cref="long"/>.
    /// Anything else — including a title that merely starts with
    /// digits — returns null and is treated as a title search.
    /// </summary>
    public static long? IgdbIdIn(string? query)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (character is < '0' or > '9')
            {
                return null;
            }
        }

        return long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && id > 0
            ? id
            : null;
    }

    /// <summary>
    /// The menu row's label, constant regardless of whether the section is
    /// open. The row names an action ("Wrong game?"), not a state; a toggle
    /// label that flipped to "Close" said nothing about what it closed.
    /// </summary>
    public string OpenLabel => GameIgdbMatchCopy.OpenLabel;

    public string OpenTooltip => GameIgdbMatchCopy.OpenTooltip;

    public string SectionHeading => GameIgdbMatchCopy.SectionHeading;

    public string CloseTooltip => GameIgdbMatchCopy.CloseTooltip;

    public string CloseAutomationName => GameIgdbMatchCopy.CloseAutomationName;

    public string FieldWatermark => GameIgdbMatchCopy.FieldWatermark;

    public string FieldLabel => GameIgdbMatchCopy.FieldLabel;

    public string SearchLabel => GameIgdbMatchCopy.SearchLabel;

    public string ClearLabel => GameIgdbMatchCopy.ClearLabel;

    public string ClearTooltip => GameIgdbMatchCopy.ClearTooltip;

    public string NoMatchesText => GameIgdbMatchCopy.NoMatchesText;

    /// <summary>
    /// Sets the display resolution for candidate thumbnails from the view's
    /// own render scaling. Same arrangement the merge queue uses.
    /// </summary>
    public void SetCoverScaling(double scaling)
    {
        if (scaling <= 0)
        {
            return;
        }

        _coverWidthPixels = CandidateCoverWidth * scaling;
        RequestCovers();
    }

    /// <summary>
    /// One-way open. When already open the command returns early; the view
    /// turns that no-op into a <c>BringIntoView</c> that scrolls the section
    /// back into the rest band's viewport. The refusal and claim are cleared
    /// only on a real opening — a refusal belongs to the attempt that caused
    /// it, and a standing same-game offer must survive a navigational
    /// re-entry so the user can finish answering it.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;

        // A refusal belongs to the attempt that caused it, not to the next one.
        Problem = null;
        Claim = null;
    }

    /// <summary>
    /// Folds the section. This is the only user route that sets
    /// <see cref="IsOpen"/> false — the close button in the section's header
    /// row drives it. The two other <c>IsOpen = false</c> sites (a landed
    /// assignment, a landed same-game link) rebuild the modal entirely.
    /// </summary>
    [RelayCommand]
    private void Close() => IsOpen = false;

    private bool CanSearch => !string.IsNullOrWhiteSpace(Query);

    /// <summary>
    /// Runs the title search. A failed search and a search that matched
    /// nothing produce the same empty list, because they are the same
    /// answer to the user.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync(CancellationToken ct)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            Problem = null;
            Claim = null;
            Note = null;
            IdMissed = false;
            Status = GameIgdbMatchCopy.SearchingStatus;

            var query = Query.Trim();

            // Numeric titles are real (2064, 1979 Revolution, 428), so an
            // all-digit query runs the id lookup AND the title search
            // rather than routing digits to the id path alone.
            var igdbId = IgdbIdIn(query);
            IgdbCandidateViewModel? idMatch = null;
            if (igdbId is { } id
                && await _service.GetCandidateByIdAsync(id, ct) is { } hit)
            {
                idMatch = new IgdbCandidateViewModel(hit, _covers, isIdMatch: true);
            }

            var results = await _service.SearchAsync(query, ct);
            var titles = results
                .Where(r => idMatch is null || r.IgdbId != idMatch.IgdbId)
                .Select(r => new IgdbCandidateViewModel(r, _covers));

            Candidates = idMatch is null ? [.. titles] : [idMatch, .. titles];
            IdMissed = igdbId is not null && idMatch is null;
            Searched = true;
            RequestCovers();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            _busy = false;
        }
    }

    /// <summary>
    /// Pins the chosen entry. On success the library reloads and the modal
    /// reopens on the corrected game. Every refusal keeps the candidate
    /// list on screen under its own sentence.
    /// </summary>
    [RelayCommand]
    private async Task AssignAsync(IgdbCandidateViewModel? candidate, CancellationToken ct)
    {
        if (_busy || candidate is null)
        {
            return;
        }

        _busy = true;
        try
        {
            Problem = null;
            Claim = null;
            Note = null;
            Status = GameIgdbMatchCopy.AssigningStatus;

            var outcome = await _service.AssignAsync(_workId, candidate.IgdbId, ct);
            Status = null;

            if (outcome != IgdbAssignmentOutcome.Assigned)
            {
                if (outcome == IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork
                    && await OfferToLinkAsync(candidate.IgdbId, ct))
                {
                    return;
                }

                Problem = GameIgdbMatchCopy.ProblemFor(outcome);
                return;
            }

            IsPinned = true;
            IsOpen = false;

            var note = GameIgdbMatchCopy.AssignedNote(candidate.Name);
            if (_afterChange is null)
            {
                Note = note;
                return;
            }

            await _afterChange(note);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            _busy = false;
        }
    }

    /// <summary>
    /// Turns the collision refusal into an offer. <c>works.igdb_id</c> is
    /// UNIQUE, so two works claiming one entry are the same game; naming
    /// and showing the holder is what lets the user judge it. Returns
    /// false — and the caller falls back to the bare refusal sentence —
    /// when no link path is wired, when nothing holds the entry any more,
    /// or when the holder resolves to this same work.
    /// </summary>
    private async Task<bool> OfferToLinkAsync(long igdbId, CancellationToken ct)
    {
        if (_linkSameGame is null)
        {
            return false;
        }

        var holder = await _service.FindClaimingGameAsync(igdbId, ct);
        if (holder is null || holder.WorkId == _workId)
        {
            return false;
        }

        Claim = new IgdbClaimViewModel(holder, _covers);
        Claim.RequestCover(_coverWidthPixels);
        return true;
    }

    /// <summary>
    /// Accepts the offer: links this work under the claiming work as the
    /// same game, through the identity-link path the Merges queue uses.
    /// Nothing is pinned — <c>works.igdb_id</c> is UNIQUE, so pinning the
    /// child to an id another row holds is what the constraint refused, and
    /// the link is the whole answer. On success the library reloads and
    /// the modal reopens on the game the two now are.
    /// </summary>
    [RelayCommand]
    private async Task LinkClaimAsync(CancellationToken ct)
    {
        if (_busy || Claim is not { } claim || _linkSameGame is null)
        {
            return;
        }

        _busy = true;
        try
        {
            Problem = null;
            Status = GameIgdbMatchCopy.LinkingStatus;

            var linked = await _linkSameGame(claim.WorkId);
            Status = null;

            if (!linked)
            {
                Problem = GameIgdbMatchCopy.LinkFailedText;
                return;
            }

            Claim = null;
            IsOpen = false;

            var note = GameIgdbMatchCopy.LinkedNote(claim.Name);
            if (_afterChange is null)
            {
                Note = note;
                return;
            }

            await _afterChange(note);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            _busy = false;
        }
    }

    /// <summary>
    /// Declines the offer. Writes nothing — no pin, no link — and restores
    /// the refusal sentence so the user still knows why the assignment did
    /// not land. The candidate list stays for another try.
    /// </summary>
    [RelayCommand]
    private void DeclineClaim()
    {
        if (Claim is null)
        {
            return;
        }

        Claim = null;
        Problem = GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork);
    }

    /// <summary>
    /// Returns the work to automatic resolution, and reloads. Clearing writes
    /// no metadata, but it changes what the tile draws: a live pin outranks
    /// the store capsule, so dropping the pin hands a Steam-owned game its
    /// capsule back and the grid must be rebuilt to show it. The confirmation
    /// rides the reload the same way an assignment's does.
    /// </summary>
    [RelayCommand]
    private async Task ClearAsync(CancellationToken ct)
    {
        if (_busy || !IsPinned)
        {
            return;
        }

        _busy = true;
        try
        {
            Problem = null;

            if (!await _service.ClearAsync(_workId, ct))
            {
                Problem = GameIgdbMatchCopy.ClearFailedText;
                return;
            }

            IsPinned = false;

            var note = GameIgdbMatchCopy.ClearedNote;
            if (_afterChange is null)
            {
                Note = note;
                return;
            }

            await _afterChange(note);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            _busy = false;
        }
    }

    private void RequestCovers()
    {
        foreach (var candidate in Candidates)
        {
            candidate.RequestCover(_coverWidthPixels);
        }

        Claim?.RequestCover(_coverWidthPixels);
    }
}

/// <summary>
/// Cover machinery shared by a candidate row and the claiming-game row.
/// Both draw a 34x51 thumbnail from the IGDB image path, requested at
/// the display-scaled width the view will draw them at. Lifted from
/// <see cref="IgdbCandidateViewModel"/> when the claiming-game row
/// needed the same art loading without the candidate-specific facts.
/// </summary>
public abstract partial class IgdbCoverRowViewModel : ObservableObject
{
    private readonly ICoverCache? _covers;

    protected IgdbCoverRowViewModel(string? coverUrl, ICoverCache? covers)
    {
        _covers = covers;

        CoverKey = IgdbImageUrl.ImageId(coverUrl) is { } imageId
            ? Winnow.Covers.CoverKey.Igdb(imageId)
            : null;
    }

    /// <summary>Null when IGDB named no cover, or named one whose URL does not carry an image id.</summary>
    public CoverKey? CoverKey { get; }

    /// <summary>The row's cover art at row resolution. Null until it arrives.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Cover { get; set; }

    /// <summary>A row with no art draws a placeholder field, never a hole.</summary>
    public bool ShowPlaceholder => Cover is null;

    /// <summary>Asks the cache for the art at the width it will be drawn at, off-thread.</summary>
    public void RequestCover(double displayWidthPixels)
    {
        if (_covers is null || CoverKey is not { } key || Cover is not null || displayWidthPixels <= 0)
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
}

/// <summary>
/// The game that already holds the chosen IGDB entry. Drawn in the
/// candidate row's own idiom — 34x51 cover, name and year in Plex —
/// because the user is judging whether it is the same game, the same
/// judgement a candidate row is drawn for.
/// </summary>
public sealed partial class IgdbClaimViewModel : IgdbCoverRowViewModel
{
    private readonly IgdbClaimingGame _holder;

    public IgdbClaimViewModel(IgdbClaimingGame holder, ICoverCache? covers = null)
        : base(holder?.CoverUrl, covers)
    {
        ArgumentNullException.ThrowIfNull(holder);

        _holder = holder;
    }

    /// <summary>The work that holds the entry. This is the PARENT of the link the offer writes.</summary>
    public long WorkId => _holder.WorkId;

    public string Name => _holder.Title;

    public bool HasYear => _holder.FirstReleaseYear is > 0;

    /// <summary>The year alone, in Plex Mono. No platforms follow it, so it carries no separator.</summary>
    public string YearText => HasYear
        ? _holder.FirstReleaseYear!.Value.ToString("D4", CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>The offer's headline, naming this game. A question, not a
    /// failure sentence: the border is <c>Line</c>, not <c>Amber</c>.</summary>
    public string Headline => GameIgdbMatchCopy.ClaimHeadline(Name);

    public string LinkAutomationName => GameIgdbMatchCopy.ClaimLinkAutomationName(Name);
}

/// <summary>
/// One candidate row: cover, name, year and platforms. Those four facts are
/// what separates Prey (2006) from Prey (2017), which is the failure this
/// control exists to fix.
///
/// <para>The thumbnail rides the existing image path and adds no new one.
/// IGDB's cover URL carries the asset's image id, and an image-id cover
/// key is one the registered IGDB cover source already answers without
/// credentials.</para>
/// </summary>
public partial class IgdbCandidateViewModel : IgdbCoverRowViewModel
{
    private readonly IgdbCandidate _candidate;

    public IgdbCandidateViewModel(
        IgdbCandidate candidate, ICoverCache? covers = null, bool isIdMatch = false)
        : base(candidate?.CoverUrl, covers)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        _candidate = candidate;
        IsIdMatch = isIdMatch;
    }

    public long IgdbId => _candidate.IgdbId;

    public string Name => _candidate.Name;

    /// <summary>
    /// True when this row was returned by the id lookup rather than the
    /// title search. It is marked with a chip and placed first in the
    /// candidate list.
    /// </summary>
    public bool IsIdMatch { get; }

    public string IdMatchLabel => GameIgdbMatchCopy.IdMatchLabel;

    /// <summary>
    /// The year as a number rather than as display text. The hand-added game
    /// form reads this to fill the year field from a chosen candidate;
    /// <see cref="YearText"/> carries a separator for the detail line and is
    /// not suitable for round-tripping.
    /// </summary>
    public int? FirstReleaseYear => _candidate.FirstReleaseYear;

    public bool HasYear => _candidate.FirstReleaseYear is > 0;

    /// <summary>
    /// Year and separator, split from the platforms so the year is in Plex
    /// Mono and the platform names in Jakarta, matching the modal's own
    /// identity line.
    /// </summary>
    public string YearText => HasYear
        ? HasPlatforms ? $"{_candidate.FirstReleaseYear:D4} · " : $"{_candidate.FirstReleaseYear:D4}"
        : string.Empty;

    public bool HasPlatforms => _candidate.Platforms.Count > 0;

    public string PlatformsText => string.Join(", ", _candidate.Platforms);

    /// <summary>The full platform list, surfaced as a tooltip because the
    /// platforms text trims inside the star column. Null when there are no
    /// platforms, so an entry with none opens no empty tooltip.</summary>
    public string? PlatformsTooltip => HasPlatforms ? PlatformsText : null;

    /// <summary>An entry with neither a year nor a platform draws no detail line.</summary>
    public bool HasDetailLine => HasYear || HasPlatforms;

    public string AssignLabel => GameIgdbMatchCopy.AssignLabel;

    public string AssignAutomationName => GameIgdbMatchCopy.AssignAutomationName(Name);
}
