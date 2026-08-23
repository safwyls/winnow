using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Hoard.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Resolve;

/// <summary>Counts of what one <see cref="ExternalIdResolver.ResolveAsync"/> pass did.</summary>
public sealed record ResolveResult(
    int MatchedExisting,
    int CreatedReleases,
    int PlayRecordsWritten,
    int SnapshotsWritten);

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
/// </summary>
public sealed class ExternalIdResolver
{
    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IOwnershipRepository _ownerships;
    private readonly IPlayRecordRepository _playRecords;
    private readonly IPlaytimeSnapshotRepository _snapshots;
    private readonly ILogger<ExternalIdResolver> _logger;

    public ExternalIdResolver(
        IWorkRepository works,
        IReleaseRepository releases,
        IOwnershipRepository ownerships,
        IPlayRecordRepository playRecords,
        IPlaytimeSnapshotRepository snapshots,
        ILogger<ExternalIdResolver>? logger = null)
    {
        _works = works;
        _releases = releases;
        _ownerships = ownerships;
        _playRecords = playRecords;
        _snapshots = snapshots;
        _logger = logger ?? NullLogger<ExternalIdResolver>.Instance;
    }

    public async Task<ResolveResult> ResolveAsync(
        IReadOnlyCollection<CandidateOwnership> candidates,
        CancellationToken ct = default)
    {
        var matched = 0;
        var created = 0;
        var playRecordsWritten = 0;
        var snapshotsWritten = 0;

        foreach (var candidate in candidates)
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
            }

            var ownershipId = await UpsertOwnershipAsync(releaseId, candidate, ct);

            // A candidate without playtime data is "no observation", not an
            // observation of zero — never fabricate a play record for it.
            if (candidate.PlaytimeMinutes is { } minutes)
            {
                if (await AppendPlayRecordIfChangedAsync(ownershipId, minutes, candidate, ct))
                {
                    playRecordsWritten++;
                }

                if (await AppendSnapshotIfChangedAsync(ownershipId, minutes, candidate, ct))
                {
                    snapshotsWritten++;
                }
            }
        }

        _logger.LogInformation(
            "Resolved {Count} candidates: {Matched} matched, {Created} created, "
            + "{PlayRecords} play records, {Snapshots} snapshots",
            candidates.Count, matched, created, playRecordsWritten, snapshotsWritten);

        return new ResolveResult(matched, created, playRecordsWritten, snapshotsWritten);
    }

    private async Task<long> CreateWorkAndReleaseAsync(CandidateOwnership candidate, CancellationToken ct)
    {
        // M0: Work and Release are 1:1, both named from the raw candidate
        // title. Enrichment/merging refines this later — never here.
        var workId = await _works.InsertAsync(new Work { Name = candidate.Title }, ct);
        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = candidate.Title,
        }, ct);

        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = candidate.Provider,
            ProviderId = candidate.ProviderId,
        }, ct);

        _logger.LogDebug(
            "Created work/release for {Provider}:{ProviderId} ({Title})",
            candidate.Provider, candidate.ProviderId, candidate.Title);
        return releaseId;
    }

    private async Task<long> UpsertOwnershipAsync(
        long releaseId, CandidateOwnership candidate, CancellationToken ct)
    {
        // M0: one ownership per (release, store). Account attribution is
        // informational; matching on it would mint duplicate ownerships when
        // the winning account changes between syncs.
        var existing = (await _ownerships.GetByReleaseAsync(releaseId, ct))
            .FirstOrDefault(o => string.Equals(o.Store, candidate.Provider, StringComparison.Ordinal));

        if (existing is null)
        {
            return await _ownerships.InsertAsync(new Ownership
            {
                ReleaseId = releaseId,
                Store = candidate.Provider,
                AccountRef = candidate.AccountRef,
                AcquiredAt = candidate.AcquiredAt,
                InstallPath = candidate.InstallPath,
                Installed = candidate.Installed,
            }, ct);
        }

        if (existing.Installed != candidate.Installed
            || !string.Equals(existing.InstallPath, candidate.InstallPath, StringComparison.Ordinal))
        {
            await _ownerships.UpdateInstallStateAsync(
                existing.Id, candidate.InstallPath, candidate.Installed, ct);
        }

        return existing.Id;
    }

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
        var history = await _snapshots.GetByOwnershipAsync(ownershipId, ct);
        var lastSnapshot = history.Count > 0 ? history[^1] : null;
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
