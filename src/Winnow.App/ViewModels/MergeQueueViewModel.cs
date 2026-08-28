using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.ViewModels;

/// <summary>
/// The merge confirm queue (§5.3 step 3, §6): every pending soft match at once,
/// strongest first, each one showing why the matcher thinks two records might
/// be the same game.
///
/// <para><b>Why this screen exists.</b> Nothing auto-merges on a fuzzy score —
/// §5.3's one non-negotiable — so clearing this queue is the only path from a
/// soft match to a merge. It is the human in the loop, and it is batched
/// deliberately: "present all pending candidates at once, not one modal at a
/// time". A sequence of modals trains people to click through without reading,
/// which converts a precision-first matcher back into an auto-merger with extra
/// steps.</para>
///
/// <para><b>Both answers are terminal.</b> `Same game` writes
/// <c>confirmed</c>, `Different games` writes <c>rejected</c>, and the resolver
/// relies on both never returning to <c>pending</c> — a pair the user has
/// answered is never asked about again, however many times the library is
/// re-scanned.</para>
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

    /// <summary>
    /// The rail shows the count only while there is something pending: a
    /// permanent zero next to a permanent row is noise, and the row itself
    /// already says the screen exists.
    /// </summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>
    /// The rail row dims to 40% instead of disappearing when there is nothing
    /// pending, the same rule the buckets follow (§6) — so the rail never
    /// reflows and the screen stays reachable to read its empty state.
    /// </summary>
    public double RowOpacity => HasPending ? 1.0 : 0.4;

    public bool ShowEmpty => _loaded && PendingCount == 0;

    /// <summary>
    /// True when a soft-match sweep has finished at least once on this
    /// database, so an empty queue is a finding rather than an absence.
    ///
    /// <para>Read from <c>settings</c> on load. False when the state repository
    /// is not registered at all: "we cannot show that the comparison has run"
    /// and "it has not run" must produce the same copy, because the one thing
    /// the screen must never do is claim a clean library on the strength of a
    /// query it did not make.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial bool HasCompletedSweep { get; set; }

    /// <summary>
    /// §7: empty states are directions, not moods. Two different empty states,
    /// because zero pending rows has two different causes and only one of them
    /// says anything about the user's library.
    ///
    /// <para><b>Swept.</b> The comparison ran and found nothing ambiguous. That
    /// is a fact about the library and the copy may state it.</para>
    ///
    /// <para><b>Not swept.</b> Nothing has compared anything yet — the sweep
    /// runs in the background behind the first scan. Saying "nothing to review"
    /// here would be describing an unwired feature as a clean bill of health,
    /// and the user would have no way to tell the difference. So this one
    /// describes what is about to happen instead: a direction, not a
    /// verdict.</para>
    /// </summary>
    public string EmptyMessage => HasCompletedSweep
        ? "Nothing to review. Winnow compared every record in your library and found no two it "
          + "couldn't tell apart. Anything ambiguous lands here, and nothing merges until you say so."
        : "Nothing to review yet. Winnow hasn't finished comparing your library for records that "
          + "might be the same game — that runs in the background after a scan. Anything it can't "
          + "call lands here, and nothing merges until you say so.";

    /// <summary>Standing explanation under the screen title.</summary>
    public string IntroMessage =>
        "These look like they might be the same game. Nothing has been merged — "
        + "a pair only changes when you answer it.";

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // Read before the candidates: the empty state has to know whether the
        // matcher has ever run, and a null repository (or a read that fails)
        // must leave this false — never optimistically true.
        HasCompletedSweep = _resolveState is not null
            && await _resolveState.GetLastSoftMatchSweepAsync(ct) is not null;

        // Already score-descending from the repository; sorted again here so the
        // review order is a property of this screen rather than of one SQL
        // ORDER BY clause, and so a hand-inserted row cannot jump the queue.
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

    /// <summary>
    /// `Same game`. Writes <c>confirmed</c> — the user's intent — and drops the
    /// pair out of the queue.
    /// </summary>
    [RelayCommand]
    private Task SameGameAsync(MergeCandidateViewModel? candidate)
        => DecideAsync(candidate, MergeCandidateStatuses.Confirmed);

    /// <summary>
    /// `Different games`. Writes <c>rejected</c>, which is permanent: the
    /// resolver checks for an existing row in any status before queueing, so
    /// this pair is never asked about again. Re-asking a question the user has
    /// already answered is how a confirmation queue teaches people to stop
    /// reading it.
    /// </summary>
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

    /// <summary>
    /// Called by the view once it knows its render scaling. Covers decode at
    /// display resolution, never at source resolution (§5.4).
    /// </summary>
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

        // Latched before the await: the card stays on screen until the write
        // returns, and a second click (or a held-down key) must not write a
        // second status onto a row the user answered once.
        candidate.IsDecided = true;

        // TODO(data-layer M1 — merge execution): `confirmed` records INTENT
        // only. The two releases are still separate rows after this write, and
        // nothing has moved between them. Executing the merge is a data-layer
        // change with real integrity consequences and belongs in its own
        // reviewed commit; it must, in one IUnitOfWork:
        //   1. pick the surviving release deterministically (lower id), so a
        //      re-run of the same decision is idempotent;
        //   2. repoint ownerships.release_id at the survivor, respecting
        //      UNIQUE(release_id, store, account_ref) from migration 0003 —
        //      two ownerships of the same game on the same store collapse to
        //      one, and their play_records / playtime_snapshots must be merged
        //      rather than dropped (highest playtime and latest last_played_at
        //      win; silently losing playtime is the failure §5.3 says destroys
        //      trust in every number the app shows);
        //   3. repoint update_events, external_ids and list_items;
        //   4. collapse the losing Work when the merged release was its only
        //      one — WITHOUT collapsing Releases into Works, which is the §5.3
        //      four-layer rule and §9 pitfall 5;
        //   5. resolve the loser's fate: merge_candidates has
        //      ON DELETE CASCADE on both release columns, so deleting the
        //      losing release would silently erase every `rejected` row that
        //      mentions it — and the resolver depends on those rows to keep a
        //      pair the user already refused out of the queue forever. The
        //      losing release must be tombstoned, or the candidate rows
        //      repointed, before any delete.
        // Until that lands, `confirmed` is a durable record of the user's
        // answer and nothing else; the queue is still correct, the library is
        // simply not yet merged.
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

        // Keep the cursor where the user's eye is: the pair that slid up into
        // the answered one's place, or the new last card.
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

            // The work's name is the human title; the release name is the
            // edition. Prefer the work, fall back to the release row.
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
                // Same fallback the library grid makes, and it matters more
                // here: half of every cross-store pair in this queue is the
                // side WITHOUT a Steam appid, so without this the merge UI
                // showed one cover and one placeholder for two rows that are
                // the same game.
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
