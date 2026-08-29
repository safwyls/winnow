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
/// Hard-join resolver (§5.3 step 1): maps <see cref="CandidateOwnership"/>
/// onto Work/Release/Ownership/PlayRecord by exact (provider, provider_id)
/// match. A hit updates ownership and appends play observations; a miss
/// creates a new Work + Release + external id. Collapses duplicate sources
/// via <see cref="CandidateOwnershipMerge"/> before comparing. Idempotent
/// by change detection. Runs atomically in one <see cref="IUnitOfWork"/>.
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

            // Neither minutes nor date means "no observation". A date without
            // minutes IS an observation (appmanifest LastPlayed with no userdata).
            if (candidate.PlaytimeMinutes is not null || candidate.LastPlayedAt is not null)
            {
                var minutes = candidate.PlaytimeMinutes ?? 0;

                if (await AppendPlayRecordIfChangedAsync(ownershipId, minutes, candidate, ct))
                {
                    playRecordsWritten++;
                }

                // Date-only observations have no playtime to plot.
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
        // Only compare against the newest snapshot.
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
