using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Resolve;

/// <summary>
/// Counts of what one <see cref="ExternalIdResolver.ResolveAsync"/> pass did.
///
/// <para>Counted in OBSERVATIONS, not in candidates: a pass first collapses the
/// candidates addressing one ownership into one observation
/// (<see cref="CandidateOwnershipMerge"/>), so an appid both Steam sources
/// reported contributes 1 here rather than 2.</para>
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
/// The M0 resolver (§5.1, §5.3 step 1): maps <see cref="CandidateOwnership"/>
/// records onto Work/Release/Ownership/PlayRecord via the external-id hard
/// join ONLY — provider + provider_id, exact match. A hit updates ownership
/// install state and appends play observations; a miss creates a fresh
/// Work + Release (1:1 for M0) + external id + ownership.
///
/// <para><b>Never</b> fuzzy/title matching here. Soft matching writes to the
/// merge_candidates queue with a human in the loop, and that is M1's job —
/// fuzzy matching would confidently merge Prey (2006) with Prey (2017) (§5.3).</para>
///
/// <para>Idempotent by change detection, not by observation time: a re-sync
/// with unchanged playtime writes no new play_records or playtime_snapshots
/// even though ObservedAt differs.</para>
///
/// <para><b>One pass, one observation per ownership.</b> Change detection asks
/// whether a candidate differs from the newest STORED record, which is only a
/// meaningful question if the candidates within a pass are a time series. Two
/// sources describing the same appid are not: they are two views of one instant.
/// So the pass opens by collapsing them through
/// <see cref="CandidateOwnershipMerge"/>. Doing it here rather than in the
/// caller is what makes the invariant hold for every caller — this is the only
/// component that knows candidates map many-to-one onto ownerships, because
/// performing that mapping is its job. Left un-collapsed, two sources that
/// disagree by a single minute each "changed" relative to the other and appended
/// a row apiece on every sync, forever.</para>
///
/// <para><b>Provisional names.</b> A candidate may arrive with a null
/// <see cref="CandidateOwnership.Title"/> — a Steam appid known only from
/// localconfig playtime, with no installed manifest to name it. Since
/// <c>works.name</c> is NOT NULL, such a work is created as
/// <c>"App &lt;provider_id&gt;"</c> with <c>name_is_provisional = 1</c>, and the
/// M1 enrichment pass replaces it. The promotion is one-way: when a real title
/// later arrives (the user installs the game) it overwrites the placeholder and
/// clears the flag, but a real title is never overwritten by a placeholder, so
/// a game that gets uninstalled keeps its name.</para>
///
/// <para><b>Atomicity.</b> The whole pass runs inside one
/// <see cref="IUnitOfWork"/>: one connection, one transaction, one commit.
/// A work, its release and its external id are only meaningful together — a
/// crash between the release insert and the external-id insert would leave a
/// work no later sync can find by external id, so the next sync mints a
/// duplicate and the library shows both, forever (§5.3: "get it wrong and the
/// dataset is untrustworthy"). It is also what makes a cold 615-game sync one
/// fsync instead of several thousand.</para>
/// </summary>
public sealed class ExternalIdResolver
{
    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IOwnershipRepository _ownerships;
    private readonly IPlayRecordRepository _playRecords;
    private readonly IPlaytimeSnapshotRepository _snapshots;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ILogger<ExternalIdResolver> _logger;

    public ExternalIdResolver(
        IWorkRepository works,
        IReleaseRepository releases,
        IOwnershipRepository ownerships,
        IPlayRecordRepository playRecords,
        IPlaytimeSnapshotRepository snapshots,
        IUnitOfWorkFactory unitOfWork,
        ILogger<ExternalIdResolver>? logger = null)
    {
        _works = works;
        _releases = releases;
        _ownerships = ownerships;
        _playRecords = playRecords;
        _snapshots = snapshots;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<ExternalIdResolver>.Instance;
    }

    public async Task<ResolveResult> ResolveAsync(
        IReadOnlyCollection<CandidateOwnership> candidates,
        CancellationToken ct = default)
    {
        // Two views of one ownership become one observation before anything is
        // compared against the database. See the type docs.
        var observations = CandidateOwnershipMerge.Coalesce(candidates);

        var matched = 0;
        var created = 0;
        var playRecordsWritten = 0;
        var snapshotsWritten = 0;
        var namesPromoted = 0;

        // Repositories enlist in this scope automatically; nothing below changes
        // shape. Dispose without Commit — a throw, a cancellation, a crash —
        // rolls the entire pass back, leaving no half-built entity behind.
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

            // A candidate carrying neither minutes nor a date is "no
            // observation", not an observation of zero — never fabricate a play
            // record for it. A date without minutes IS an observation, though:
            // an appmanifest LastPlayed on a machine whose userdata is
            // unreadable is the only evidence of play that machine has, and
            // discarding it read the entire library as never_played.
            if (candidate.PlaytimeMinutes is not null || candidate.LastPlayedAt is not null)
            {
                var minutes = candidate.PlaytimeMinutes ?? 0;

                if (await AppendPlayRecordIfChangedAsync(ownershipId, minutes, candidate, ct))
                {
                    playRecordsWritten++;
                }

                // Snapshots are a playtime series; a date-only observation has
                // no point to plot, so it appends nothing.
                if (candidate.PlaytimeMinutes is not null
                    && await AppendSnapshotIfChangedAsync(ownershipId, minutes, candidate, ct))
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

    /// <summary>
    /// The placeholder name for a title-less candidate. Deliberately stable and
    /// derivable from the provider id so a re-sync produces the same string.
    /// </summary>
    private static string ProvisionalName(CandidateOwnership candidate)
        => $"App {candidate.ProviderId}";

    private async Task<long> CreateWorkAndReleaseAsync(CandidateOwnership candidate, CancellationToken ct)
    {
        // M0: Work and Release are 1:1, both named from the raw candidate
        // title. Enrichment/merging refines this later — never here.
        // A missing title means the source has no name for this app (played but
        // uninstalled on Steam); works.name is NOT NULL, so mint a flagged
        // placeholder rather than inventing a title. Whitespace-only counts as
        // missing: an appmanifest with `"name" ""` would otherwise create a
        // blank work that is not flagged provisional, so promotion can never
        // repair it and the library shows a nameless tile forever.
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

    /// <summary>
    /// Promotes a placeholder name to a real title when a later sync supplies
    /// one. One-way by construction: a candidate with no title cannot demote an
    /// existing name, and a work already holding a real title is left alone
    /// (renaming on every sync would fight both enrichment and the user).
    /// </summary>
    private async Task<bool> PromoteProvisionalNameAsync(
        Release release, CandidateOwnership candidate, CancellationToken ct)
    {
        // Whitespace-only is "no title", not a title: it must neither promote a
        // placeholder nor overwrite a real name with blanks.
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

        // Releases have no flag of their own; in M0 they are 1:1 with the work,
        // so the placeholder they were created with is promoted alongside it.
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
        // M0: one ownership per (release, store), now enforced by a UNIQUE index
        // (migration 0003) instead of a read-then-insert. Account attribution is
        // not part of the key — matching on it would mint duplicate ownerships
        // when the winning account changes between syncs — but it IS refreshed
        // on every pass, so it always names the account whose minutes and
        // last-played the newest play record carries.
        //
        // Installed passes through as the three-valued answer the source gave.
        // The resolver deliberately does not decide it: flattening null to false
        // here, or ranking sources by name, would put "which source ran last"
        // back in charge of what the library says is on disk. The write rule
        // lives in the upsert, where "no opinion" can leave the columns alone.
        => _ownerships.UpsertAsync(new OwnershipUpsert(
            ReleaseId: releaseId,
            Store: candidate.Provider,
            AccountRef: candidate.AccountRef,
            AcquiredAt: candidate.AcquiredAt,
            InstallPath: candidate.InstallPath,
            Installed: candidate.Installed), ct);

    private async Task<bool> AppendPlayRecordIfChangedAsync(
        long ownershipId, long minutes, CandidateOwnership candidate, CancellationToken ct)
    {
        var latest = await _playRecords.GetLatestAsync(ownershipId, ct);
        if (latest is not null
            && latest.PlaytimeMinutes == minutes
            && latest.LastPlayedAt == candidate.LastPlayedAt)
        {
            return false;
        }

        await _playRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            LastPlayedAt = candidate.LastPlayedAt,
            Source = candidate.Source,
            ObservedAt = candidate.ObservedAt,
        }, ct);
        return true;
    }

    private async Task<bool> AppendSnapshotIfChangedAsync(
        long ownershipId, long minutes, CandidateOwnership candidate, CancellationToken ct)
    {
        // Only the newest snapshot decides whether this one is a change. Reading
        // the whole series to look at its last element is an N+1 over a table
        // that grows for the lifetime of the library.
        var lastSnapshot = await _snapshots.GetLatestAsync(ownershipId, ct);
        if (lastSnapshot is not null && lastSnapshot.PlaytimeMinutes == minutes)
        {
            return false;
        }

        await _snapshots.InsertAsync(new PlaytimeSnapshot
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            ObservedAt = candidate.ObservedAt,
        }, ct);
        return true;
    }
}
