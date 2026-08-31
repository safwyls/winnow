using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Resolve;

/// <summary>
/// Counts of what one <see cref="ExternalIdResolver.ResolveAsync"/> pass did,
/// counted in observations (after <see cref="CandidateOwnershipMerge"/>).
/// </summary>
/// <param name="MatchedExisting">Observations that hit an existing release by external id.</param>
/// <param name="CreatedReleases">Observations that minted a new Work + Release.</param>
/// <param name="PlayRecordsWritten">Play observations appended (unchanged playtime writes none).</param>
/// <param name="SnapshotsWritten">Playtime snapshots appended.</param>
/// <param name="NamesPromoted">
/// Works whose provisional placeholder name was replaced by a real title this pass.
/// </param>
public sealed record ResolveResult(
    int MatchedExisting,
    int CreatedReleases,
    int PlayRecordsWritten,
    int SnapshotsWritten,
    int NamesPromoted = 0);

/// <summary>
/// Whether a pass's playtime figures are the whole truth or a floor that
/// another source may already have counted past. Controls whether a figure
/// lower than the newest stored observation is recorded or clamped.
/// </summary>
public enum PlaytimeView
{
    /// <summary>
    /// The pass sees the complete playtime for every ownership it reports.
    /// A lower figure than the stored one is a genuine correction and is
    /// recorded as a new observation.
    /// </summary>
    Complete,

    /// <summary>
    /// Every figure is a floor from a cumulative counter another source may
    /// have counted further. A figure below the newest stored one by more
    /// than <see cref="ExternalIdResolver.PlaytimeToleranceMinutes"/> is
    /// raised to it rather than appended. A figure within the band is kept
    /// at its own lower value, the err-low decision applied to cross-source
    /// disagreement.
    /// </summary>
    LowerBound,
}

/// <summary>
/// Hard-join resolver (§5.3 step 1): maps <see cref="CandidateOwnership"/>
/// onto Work/Release/Ownership/PlayRecord by exact (provider, provider_id)
/// match. A hit updates ownership and appends play observations; a miss
/// creates a new Work + Release + external id. Collapses duplicate sources
/// via <see cref="CandidateOwnershipMerge"/> before comparing. Idempotent
/// by change detection. Runs atomically in one <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class ExternalIdResolver
{
    /// <summary>
    /// Maximum disagreement, in minutes, between two sources' playtime
    /// figures that is absorbed as cross-source noise rather than recorded as
    /// play. Verified on the live database: <c>localconfig.vdf</c> reports
    /// 280 minutes for Portal (appid 400) while <c>GetOwnedGames</c> reports
    /// 279; the same one-minute gap appears on Arma 2 (3 vs 2) and Arma 2
    /// Operation Arrowhead (154 vs 153). Without the band, separate passes
    /// for each source fabricated a rise and a fall on every cycle, producing
    /// nine phantom one-minute rises across ownerships 6, 46 and 47 (verified
    /// 2026-08-29). A move of one minute or less is disagreement and is
    /// absorbed; two minutes or more is play and is recorded. Drift cannot
    /// accumulate because the band is measured against the stored figure,
    /// which does not advance while absorbing.
    /// </summary>
    /// <remarks>
    /// The value itself lives in <see cref="PlaytimeTolerance.Minutes"/> so the
    /// per-account membership rows can enforce the same band. Two literals would
    /// let the filtered library report a minute more than the unfiltered one.
    /// </remarks>
    public const long PlaytimeToleranceMinutes = PlaytimeTolerance.Minutes;

    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IOwnershipRepository _ownerships;
    private readonly IPlayRecordRepository _playRecords;
    private readonly IPlaytimeSnapshotRepository _snapshots;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IOwnershipAccountRepository _ownershipAccounts;
    private readonly ILogger<ExternalIdResolver> _logger;

    public ExternalIdResolver(
        IWorkRepository works,
        IReleaseRepository releases,
        IOwnershipRepository ownerships,
        IPlayRecordRepository playRecords,
        IPlaytimeSnapshotRepository snapshots,
        IUnitOfWorkFactory unitOfWork,
        IOwnershipAccountRepository ownershipAccounts,
        ILogger<ExternalIdResolver>? logger = null)
    {
        _works = works;
        _releases = releases;
        _ownerships = ownerships;
        _playRecords = playRecords;
        _snapshots = snapshots;
        _unitOfWork = unitOfWork;
        _ownershipAccounts = ownershipAccounts;
        _logger = logger ?? NullLogger<ExternalIdResolver>.Instance;
    }

    /// <param name="candidates">The pass's candidates, before merging.</param>
    /// <param name="ct">Cancellation; a cancelled pass commits nothing.</param>
    /// <param name="playtime">
    /// What the pass's playtime figures are worth against what is already
    /// stored. Both sync jobs pass <see cref="PlaytimeView.LowerBound"/>: every
    /// store exposes a cumulative counter and no single source sees all of it,
    /// so a figure that would reduce an ownership's newest observation is a
    /// blind spot in this pass rather than time the user un-played.
    /// </param>
    public async Task<ResolveResult> ResolveAsync(
        IReadOnlyCollection<CandidateOwnership> candidates,
        CancellationToken ct = default,
        PlaytimeView playtime = PlaytimeView.Complete)
    {
        // Collapse duplicate sources into one observation per ownership.
        var observations = CandidateOwnershipMerge.Coalesce(candidates);

        var matched = 0;
        var created = 0;
        var playRecordsWritten = 0;
        var snapshotsWritten = 0;
        var namesPromoted = 0;

        // Dispose without Commit rolls the entire pass back.
        using var scope = _unitOfWork.Begin();

        foreach (var candidate in observations)
        {
            ct.ThrowIfCancellationRequested();

            // §5.3 step 1: hard join by (provider, provider_id). Exact or nothing.
            var release = await _releases.FindByExternalIdAsync(
                candidate.Provider, candidate.ProviderId, ct);

            long releaseId;
            if (release is null)
            {
                releaseId = await CreateWorkAndReleaseAsync(candidate, ct);
                created++;
            }
            else
            {
                releaseId = release.Id;
                matched++;

                if (await PromoteProvisionalNameAsync(release, candidate, ct))
                {
                    namesPromoted++;
                }
            }

            var ownershipId = await UpsertOwnershipAsync(releaseId, candidate, ct);

            // The un-collapsed form of the same observation, written in this
            // same unit of work so a membership row can never name an ownership
            // the pass then rolled back. `ownerships.account_ref` above still
            // holds one account — the play tuple's winner — because the
            // household figures beside it are that account's; these rows are the
            // per-account facts the visibility filter is decided from, and they
            // are additive, so a pass that names fewer accounts than the last one
            // narrows nothing.
            await UpsertAccountsAsync(ownershipId, candidate, ct);

            // Neither minutes nor date means "no observation". A date without
            // minutes IS an observation (appmanifest LastPlayed with no userdata).
            if (candidate.PlaytimeMinutes is not null || candidate.LastPlayedAt is not null)
            {
                // Read once and passed down: the two appenders below need the
                // same two rows the clamp needs.
                var latestRecord = await _playRecords.GetLatestAsync(ownershipId, ct);

                // A date-only observation has no snapshot to append, so under
                // Complete this row is not worth a query — only the clamp needs
                // it unconditionally.
                var latestSnapshot =
                    candidate.PlaytimeMinutes is not null || playtime is PlaytimeView.LowerBound
                        ? await _snapshots.GetLatestAsync(ownershipId, ct)
                        : null;

                var minutes = candidate.PlaytimeMinutes ?? 0;
                var lastPlayedAt = candidate.LastPlayedAt;
                var source = candidate.Source;

                // Complete means the pass sees the whole truth, so any
                // difference is a genuine correction. The tolerance band
                // applies only under LowerBound, where two partial-view
                // passes can report figures that differ by a minute without
                // either being wrong.
                var tolerance = playtime is PlaytimeView.LowerBound ? PlaytimeToleranceMinutes : 0;

                if (playtime is PlaytimeView.LowerBound)
                {
                    var floor = Math.Max(
                        latestRecord?.PlaytimeMinutes ?? 0, latestSnapshot?.PlaytimeMinutes ?? 0);

                    // A gap exceeding the tolerance is a genuine blind spot
                    // in this pass; the figure is raised to the stored floor.
                    // Inside the band the source keeps its own lower reading
                    // under its own source label: when two sources disagree
                    // at sub-minute magnitude the resolved value takes the
                    // lower figure (the err-low decision of 2026-08-29).
                    if (floor - minutes > tolerance)
                    {
                        // The minutes are no longer this source's unaided report;
                        // the row must not claim they are.
                        minutes = floor;
                        source = PlayRecordSources.Carried(source);
                    }

                    lastPlayedAt = Later(lastPlayedAt, latestRecord?.LastPlayedAt);
                }

                if (await AppendPlayRecordIfChangedAsync(
                        latestRecord, ownershipId, minutes, lastPlayedAt, source, tolerance,
                        candidate, ct))
                {
                    playRecordsWritten++;
                }

                // Date-only observations have no playtime to plot.
                if (candidate.PlaytimeMinutes is not null
                    && await AppendSnapshotIfChangedAsync(
                        latestSnapshot, ownershipId, minutes, tolerance, candidate, ct))
                {
                    snapshotsWritten++;
                }
            }
        }

        scope.Commit();

        _logger.LogInformation(
            "Resolved {Count} candidates ({Observations} after merging duplicate sources): "
            + "{Matched} matched, {Created} created, "
            + "{PlayRecords} play records, {Snapshots} snapshots, {Promoted} names promoted",
            candidates.Count, observations.Count, matched, created,
            playRecordsWritten, snapshotsWritten, namesPromoted);

        return new ResolveResult(matched, created, playRecordsWritten, snapshotsWritten, namesPromoted);
    }

    /// <summary>Placeholder name for a title-less candidate.</summary>
    private static string ProvisionalName(CandidateOwnership candidate)
        => $"App {candidate.ProviderId}";

    private async Task<long> CreateWorkAndReleaseAsync(CandidateOwnership candidate, CancellationToken ct)
    {
        // Work and Release are 1:1 in M0. Missing/blank title gets a flagged
        // placeholder so promotion can repair it later.
        var provisional = string.IsNullOrWhiteSpace(candidate.Title);
        var name = provisional ? ProvisionalName(candidate) : candidate.Title!;

        var workId = await _works.InsertAsync(
            new Work { Name = name, NameIsProvisional = provisional }, ct);
        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = name,
        }, ct);

        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = candidate.Provider,
            ProviderId = candidate.ProviderId,
        }, ct);

        _logger.LogDebug(
            "Created work/release for {Provider}:{ProviderId} ({Name}, provisional: {Provisional})",
            candidate.Provider, candidate.ProviderId, name, provisional);
        return releaseId;
    }

    /// <summary>One-way promotion of a placeholder name to a real title.</summary>
    private async Task<bool> PromoteProvisionalNameAsync(
        Release release, CandidateOwnership candidate, CancellationToken ct)
    {
        // Whitespace-only is "no title".
        if (string.IsNullOrWhiteSpace(candidate.Title))
        {
            return false;
        }

        var title = candidate.Title!;

        var work = await _works.GetAsync(release.WorkId, ct);
        if (work is null || !work.NameIsProvisional)
        {
            return false;
        }

        await _works.UpdateNameAsync(work.Id, title, nameIsProvisional: false, ct);

        // Also promote the release name (1:1 with work in M0).
        if (!string.Equals(release.Name, title, StringComparison.Ordinal))
        {
            await _releases.UpdateNameAsync(release.Id, title, ct);
        }

        _logger.LogInformation(
            "Promoted provisional name {Old} to {New} for {Provider}:{ProviderId}",
            work.Name, title, candidate.Provider, candidate.ProviderId);
        return true;
    }

    private Task<long> UpsertOwnershipAsync(
        long releaseId, CandidateOwnership candidate, CancellationToken ct)
        // One ownership per (release, store), enforced by UNIQUE index.
        // Installed passes through as the three-valued answer the source gave.
        => _ownerships.UpsertAsync(new OwnershipUpsert(
            ReleaseId: releaseId,
            Store: candidate.Provider,
            AccountRef: candidate.AccountRef,
            AcquiredAt: candidate.AcquiredAt,
            InstallPath: candidate.InstallPath,
            Installed: candidate.Installed), ct);

    /// <summary>
    /// Records what each account this candidate named holds, one row per
    /// account.
    ///
    /// <para>Falls back to the candidate's own <c>AccountRef</c> when the source
    /// enumerated no accounts but did name one. That covers every reader written
    /// before the per-account list existed and the ordinary single-account
    /// machine, where "the account that won" and "the only account" are the same
    /// answer — and it means the membership table stays complete for sources
    /// that will never learn to enumerate.</para>
    ///
    /// <para>A candidate that names nobody writes nothing, and that silence is
    /// meaningful: with no row at all the filter has no evidence and leaves the
    /// game visible, which is the right answer for Epic (no account concept in
    /// its local files) and for GOG's machine-wide install registry.</para>
    /// </summary>
    private async Task UpsertAccountsAsync(
        long ownershipId, CandidateOwnership candidate, CancellationToken ct)
    {
        var accounts = candidate.Accounts;

        if (accounts.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(candidate.AccountRef))
            {
                return;
            }

            // The single-account form of the same fact. Its figures are the
            // candidate's own, which by construction ARE this account's: the
            // play tuple came from whoever this reference names.
            accounts =
            [
                new CandidateAccount(
                    candidate.AccountRef!, candidate.PlaytimeMinutes, candidate.LastPlayedAt),
            ];
        }

        foreach (var account in accounts)
        {
            await _ownershipAccounts.UpsertAsync(
                new OwnershipAccountUpsert(
                    OwnershipId: ownershipId,
                    AccountRef: account.AccountRef,
                    PlaytimeMinutes: account.PlaytimeMinutes,
                    LastPlayedAt: account.LastPlayedAt,
                    Source: candidate.Source,
                    ObservedAt: candidate.ObservedAt),
                ct);
        }
    }

    /// <summary>Later of two dates; null is "no answer", never "earlier".</summary>
    private static DateTime? Later(DateTime? first, DateTime? second)
        => first is null ? second
            : second is null ? first
            : first.Value >= second.Value ? first
            : second;

    private async Task<bool> AppendPlayRecordIfChangedAsync(
        PlayRecord? latest,
        long ownershipId,
        long minutes,
        DateTime? lastPlayedAt,
        string source,
        long tolerance,
        CandidateOwnership candidate,
        CancellationToken ct)
    {
        // Short-circuit for the common case: a sync tick that learned nothing
        // new. This only compares against the newest row, so it cannot catch an
        // out-of-order replay; idempotency for those belongs to TryAppendAsync
        // and the identity index behind it.
        //
        // The tolerance widens the comparison so a figure within one minute
        // of the stored row is not a change. Without this, alternating
        // passes from sources that disagree by a minute would each see the
        // other's figure as new and append a row on every cycle.
        if (latest is not null
            && Math.Abs(latest.PlaytimeMinutes - minutes) <= tolerance
            && latest.LastPlayedAt == lastPlayedAt)
        {
            return false;
        }

        return await _playRecords.TryAppendAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            LastPlayedAt = lastPlayedAt,
            Source = source,
            ObservedAt = candidate.ObservedAt,
        }, ct) is not null;
    }

    private async Task<bool> AppendSnapshotIfChangedAsync(
        PlaytimeSnapshot? lastSnapshot, long ownershipId, long minutes, long tolerance,
        CandidateOwnership candidate, CancellationToken ct)
    {
        // Same short-circuit as AppendPlayRecordIfChangedAsync, tolerance
        // included. A figure within the band writes no snapshot row, so the
        // playtime_snapshots series never falls inside the band; only
        // play_records can carry a one-minute-lower figure, and only when
        // the last-played date moved and forces a row anyway.
        if (lastSnapshot is not null
            && Math.Abs(lastSnapshot.PlaytimeMinutes - minutes) <= tolerance)
        {
            return false;
        }

        return await _snapshots.TryAppendAsync(new PlaytimeSnapshot
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            ObservedAt = candidate.ObservedAt,
        }, ct) is not null;
    }
}
