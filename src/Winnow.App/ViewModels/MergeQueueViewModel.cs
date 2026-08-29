using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.ViewModels;

/// <summary>
/// Merge confirm queue: shows all pending soft matches, strongest first, for
/// the user to confirm or reject. Both answers are terminal — a decided pair
/// is never re-asked.
/// </summary>
public partial class MergeQueueViewModel : ObservableObject
{
    /// <summary>Cover geometry from §6: "two covers side by side at 200×300".</summary>
    public const double CoverWidth = 200;

    /// <summary>2:3 portrait, matching the capsule geometry the grid uses.</summary>
    public const double CoverHeight = CoverWidth * 1.5;

    private readonly IMergeCandidateRepository _candidates;
    private readonly IReleaseRepository _releases;
    private readonly IWorkRepository _works;
    private readonly ICoverCache? _covers;
    private readonly IResolveStateRepository? _resolveState;

    /// <summary>Display resolution the view last asked for; 0 until it attaches.</summary>
    private double _coverWidthPixels;

    private bool _loaded;

    public MergeQueueViewModel(
        IMergeCandidateRepository candidates,
        IReleaseRepository releases,
        IWorkRepository works,
        ICoverCache? covers = null,
        IResolveStateRepository? resolveState = null)
    {
        _candidates = candidates;
        _releases = releases;
        _works = works;
        _covers = covers;
        _resolveState = resolveState;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PendingCount), nameof(PendingCountText), nameof(HasPending),
        nameof(ShowEmpty), nameof(RowOpacity))]
    public partial IReadOnlyList<MergeCandidateViewModel> Candidates { get; set; } = [];

    [ObservableProperty]
    public partial MergeCandidateViewModel? SelectedCandidate { get; set; }

    public int PendingCount => Candidates.Count;

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string PendingCountText => PendingCount.ToString("N0");

    /// <summary>True when there are pending candidates to review.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>Dims to 40% when empty so the rail row stays visible but recedes.</summary>
    public double RowOpacity => HasPending ? 1.0 : 0.4;

    public bool ShowEmpty => _loaded && PendingCount == 0;

    /// <summary>True once a soft-match sweep has completed. False when unknown or unregistered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial bool HasCompletedSweep { get; set; }

    /// <summary>Empty-state message: distinguishes "sweep found nothing" from "sweep hasn't run yet".</summary>
    public string EmptyMessage => HasCompletedSweep
        ? "Nothing to review. No ambiguous pairs found."
        : "Nothing to review yet. Still comparing your library for duplicates.";

    /// <summary>Standing explanation under the screen title.</summary>
    public string IntroMessage =>
        "These pairs might be the same game. Nothing merges until you decide.";

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // Must be read before candidates so the empty state knows if the matcher has run.
        HasCompletedSweep = _resolveState is not null
            && await _resolveState.GetLastSoftMatchSweepAsync(ct) is not null;

        // Sort here so review order is owned by this screen, not just the SQL query.
        var pending = await _candidates.GetPendingAsync(ct);

        var releaseIds = new HashSet<long>();
        foreach (var candidate in pending)
        {
            releaseIds.Add(candidate.LeftReleaseId);
            releaseIds.Add(candidate.RightReleaseId);
        }

        var (titles, coverKeys) = await DescribeReleasesAsync(releaseIds, ct);

        var cards = new List<MergeCandidateViewModel>(pending.Count);
        foreach (var candidate in pending)
        {
            cards.Add(MergeCandidateViewModel.Create(candidate, titles, coverKeys, _covers));
        }

        cards.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Id.CompareTo(b.Id);
        });

        _loaded = true;
        Candidates = cards;
        Select(cards.Count > 0 ? cards[0] : null);
        RequestCovers(_coverWidthPixels);
    }

    /// <summary>Writes <c>confirmed</c> and removes the pair from the queue.</summary>
    [RelayCommand]
    private Task SameGameAsync(MergeCandidateViewModel? candidate)
        => DecideAsync(candidate, MergeCandidateStatuses.Confirmed);

    /// <summary>Writes <c>rejected</c> (permanent) and removes the pair from the queue.</summary>
    [RelayCommand]
    private Task DifferentGamesAsync(MergeCandidateViewModel? candidate)
        => DecideAsync(candidate, MergeCandidateStatuses.Rejected);

    /// <summary>Selection, shared by pointer and keyboard.</summary>
    public void Select(MergeCandidateViewModel? candidate)
    {
        if (ReferenceEquals(SelectedCandidate, candidate))
        {
            return;
        }

        if (SelectedCandidate is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedCandidate = candidate;
        if (candidate is not null)
        {
            candidate.IsSelected = true;
        }
    }

    /// <summary>
    /// Keyboard navigation (§8): moves selection by <paramref name="delta"/>
    /// cards. Returns the new index, or -1 when the queue is empty.
    /// </summary>
    public int MoveSelection(int delta)
    {
        if (Candidates.Count == 0)
        {
            return -1;
        }

        var current = IndexOf(SelectedCandidate);
        var next = current < 0
            ? 0
            : Math.Clamp(current + delta, 0, Candidates.Count - 1);
        Select(Candidates[next]);
        return next;
    }

    /// <summary>Sets the display resolution for cover decoding.</summary>
    public void RequestCovers(double displayWidthPixels)
    {
        if (displayWidthPixels <= 0)
        {
            return;
        }

        _coverWidthPixels = displayWidthPixels;
        foreach (var candidate in Candidates)
        {
            candidate.RequestCovers(displayWidthPixels);
        }
    }

    private async Task DecideAsync(MergeCandidateViewModel? candidate, string status)
    {
        if (candidate is null || candidate.IsDecided)
        {
            return;
        }

        // Latch before await to prevent double-writes from rapid clicks.
        candidate.IsDecided = true;

        // TODO(data-layer M1): `confirmed` records intent only — releases are
        // NOT merged yet. Merge execution (repointing ownerships, events,
        // external_ids, collapsing works, tombstoning losers) belongs in a
        // separate commit with its own integrity review.
        await _candidates.SetStatusAsync(candidate.Id, status);

        var remaining = new List<MergeCandidateViewModel>(Candidates.Count);
        foreach (var existing in Candidates)
        {
            if (!ReferenceEquals(existing, candidate))
            {
                remaining.Add(existing);
            }
        }

        var index = IndexOf(candidate);
        Candidates = remaining;

        // Select the pair that slid into the answered one's place.
        if (remaining.Count == 0)
        {
            Select(null);
        }
        else
        {
            Select(remaining[Math.Clamp(index, 0, remaining.Count - 1)]);
        }
    }

    private async Task<(Dictionary<long, string> Titles, Dictionary<long, CoverKey> CoverKeys)>
        DescribeReleasesAsync(IEnumerable<long> releaseIds, CancellationToken ct)
    {
        var titles = new Dictionary<long, string>();
        var coverKeys = new Dictionary<long, CoverKey>();

        foreach (var releaseId in releaseIds)
        {
            var release = await _releases.GetAsync(releaseId, ct);
            if (release is null)
            {
                continue;
            }

            // Prefer work name (human title) over release name (edition).
            var work = await _works.GetAsync(release.WorkId, ct);
            titles[releaseId] = work?.Name ?? release.Name;

            var externalIds = await _releases.GetExternalIdsAsync(releaseId, ct);
            var steam = externalIds.FirstOrDefault(x => x.Provider == ExternalIdProviders.Steam);
            if (steam is not null)
            {
                coverKeys[releaseId] = CoverKey.Steam(steam.ProviderId);
            }
            else if (IgdbImageUrl.ImageId(work?.CoverUrl) is { Length: > 0 } imageId)
            {
                // IGDB fallback for the side without a Steam appid (common in cross-store pairs).
                coverKeys[releaseId] = CoverKey.Igdb(imageId);
            }
        }

        return (titles, coverKeys);
    }

    private int IndexOf(MergeCandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return -1;
        }

        for (var i = 0; i < Candidates.Count; i++)
        {
            if (ReferenceEquals(Candidates[i], candidate))
            {
                return i;
            }
        }

        return -1;
    }
}
