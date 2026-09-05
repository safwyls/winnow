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
/// <para>Sits in the left column of the modal, under the cover art and the
/// install path, where the identity facts about the game live. At rest it is
/// one quiet line; the search is disclosed inline, in the modal's own tree,
/// never a flyout (an adorner layer does not exist inside a popup).</para>
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
    /// carrying the confirmation note across the rebuild so the user sees
    /// the corrected cover and metadata where they asked for them. Null in
    /// a test, and then the note lands on this instance instead.
    /// </summary>
    private readonly Func<string, Task>? _afterAssign;

    private readonly long _workId;

    private double _coverWidthPixels = CandidateCoverWidth;

    private bool _busy;

    public GameIgdbMatchViewModel(
        IIgdbAssignmentService service,
        long workId,
        string title,
        WorkIgdbPin? pin = null,
        ICoverCache? covers = null,
        Func<string, Task>? afterAssign = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _workId = workId;
        _covers = covers;
        _afterAssign = afterAssign;

        // The title the library already shows is the search anyone would type.
        Query = title ?? string.Empty;
        IsPinned = pin is not null;
        Note = note;
    }

    /// <summary>Whether the search surface is disclosed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
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
    public partial string? Status { get; set; }

    /// <summary>A refusal sentence, drawn in Amber. The controls stay in place for a retry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    public partial string? Problem { get; set; }

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasNote => Note is not null;

    public bool HasStatus => Status is not null;

    public bool HasProblem => Problem is not null;

    /// <summary>The pin note and the Clear control travel together.</summary>
    public bool ShowPinned => IsPinned;

    /// <summary>
    /// True when a search ran and found nothing, which is not a problem and
    /// does not read as one. Never shown while something is in flight or
    /// while a refusal is standing.
    /// </summary>
    public bool ShowNoMatches
        => Searched && Candidates.Count == 0 && Status is null && Problem is null;

    /// <summary>The disclosure control's label, which changes with the open state.</summary>
    public string ToggleLabel => IsOpen ? GameIgdbMatchCopy.CloseLabel : GameIgdbMatchCopy.OpenLabel;

    public string OpenTooltip => GameIgdbMatchCopy.OpenTooltip;

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

    /// <summary>Discloses or folds the search surface.</summary>
    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;

        // A refusal belongs to the attempt that caused it, not to the next one.
        if (IsOpen)
        {
            Problem = null;
        }
    }

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
            Note = null;
            Status = GameIgdbMatchCopy.SearchingStatus;

            var results = await _service.SearchAsync(Query.Trim(), ct);

            Candidates = [.. results.Select(r => new IgdbCandidateViewModel(r, _covers))];
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
            Note = null;
            Status = GameIgdbMatchCopy.AssigningStatus;

            var outcome = await _service.AssignAsync(_workId, candidate.IgdbId, ct);
            Status = null;

            if (outcome != IgdbAssignmentOutcome.Assigned)
            {
                Problem = GameIgdbMatchCopy.ProblemFor(outcome);
                return;
            }

            IsPinned = true;
            IsOpen = false;

            var note = GameIgdbMatchCopy.AssignedNote(candidate.Name);
            if (_afterAssign is null)
            {
                Note = note;
                return;
            }

            await _afterAssign(note);
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
    /// Returns the work to automatic resolution. No reload: clearing writes
    /// no metadata, so nothing on screen has changed except this control.
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
            Note = GameIgdbMatchCopy.ClearedNote;
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
    }
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
public partial class IgdbCandidateViewModel : ObservableObject
{
    private readonly ICoverCache? _covers;
    private readonly IgdbCandidate _candidate;

    public IgdbCandidateViewModel(IgdbCandidate candidate, ICoverCache? covers = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        _candidate = candidate;
        _covers = covers;

        CoverKey = IgdbImageUrl.ImageId(candidate.CoverUrl) is { } imageId
            ? Winnow.Covers.CoverKey.Igdb(imageId)
            : null;
    }

    public long IgdbId => _candidate.IgdbId;

    public string Name => _candidate.Name;

    /// <summary>Null when IGDB named no cover, or named one whose URL does not carry an image id.</summary>
    public CoverKey? CoverKey { get; }

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

    /// <summary>An entry with neither a year nor a platform draws no detail line.</summary>
    public bool HasDetailLine => HasYear || HasPlatforms;

    public string AssignLabel => GameIgdbMatchCopy.AssignLabel;

    public string AssignAutomationName => GameIgdbMatchCopy.AssignAutomationName(Name);

    /// <summary>The candidate's cover art at row resolution. Null until it arrives.</summary>
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
