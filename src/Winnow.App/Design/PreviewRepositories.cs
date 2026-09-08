using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Design;

/// <summary>
/// In-memory repositories over <see cref="PreviewLibrary"/>, for the
/// previewer only (TASK-150). Reads answer from the fabricated set; writes
/// accept and do nothing, because a previewer click must not fail and must
/// not mutate anything — there is nothing to mutate.
///
/// <para>These types are the reason the preview renders real layouts rather
/// than hand-drawn mock rows: the view models under test run their own load
/// paths against them, so a constructor change that breaks the preview breaks
/// a test in <c>tests/Winnow.Ui.Tests</c>, not somebody's afternoon.</para>
/// </summary>
internal sealed class PreviewLibraryQueryRepository : ILibraryQueryRepository
{
    public Task<LibrarySnapshot> GetSnapshotAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Snapshot());

    public Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Snapshot().Buckets);

    public Task<int> CountHiddenByAccountScopeAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> CountHiddenByExplicitFilterAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> CountHiddenByRatingCapAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FacetTarget>>([]);
}

internal sealed class PreviewOwnershipRepository : IOwnershipRepository
{
    public Task<long> InsertAsync(Ownership ownership, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<Ownership?> GetAsync(long id, CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Ownerships.FirstOrDefault(o => o.Id == id));

    public Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long releaseId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Ownership>>(
            [.. PreviewLibrary.Ownerships.Where(o => o.ReleaseId == releaseId)]);

    public Task<long> UpsertAsync(OwnershipUpsert ownership, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Ownerships);

    public Task<bool> FillAcquisitionFactsAsync(OwnershipAcquisitionFill fill, CancellationToken ct = default)
        => Task.FromResult(false);
}

internal sealed class PreviewReleaseRepository : IReleaseRepository
{
    public Task<long> InsertAsync(Release release, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task UpdateNameAsync(long id, string name, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Release?> GetAsync(long id, CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Releases.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Release>>(
            [.. PreviewLibrary.Releases.Where(r => r.WorkId == workId)]);

    public Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalId>>(
            [.. PreviewLibrary.ExternalIds.Where(id => id.ReleaseId == releaseId)]);

    public Task<Release?> FindByExternalIdAsync(
        string provider, string providerId, CancellationToken ct = default)
    {
        var match = PreviewLibrary.ExternalIds.FirstOrDefault(
            id => id.Provider == provider && id.ProviderId == providerId);
        return Task.FromResult(
            match is null ? null : PreviewLibrary.Releases.FirstOrDefault(r => r.Id == match.ReleaseId));
    }

    public Task<IReadOnlyList<ReleaseIdentity>> GetIdentitiesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReleaseIdentity>>([]);
}

internal sealed class PreviewWorkRepository : IWorkRepository
{
    public Task<long> InsertAsync(Work work, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task UpdateNameAsync(long id, string name, bool nameIsProvisional, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Work?> GetAsync(long id, CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Works.FirstOrDefault(w => w.Id == id));

    public Task<Work?> GetByIgdbIdAsync(long igdbId, CancellationToken ct = default)
        => Task.FromResult<Work?>(null);

    public Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Works);

    public Task<IReadOnlyList<ProvisionalNameTarget>> GetProvisionalNameTargetsAsync(
        string provider, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProvisionalNameTarget>>([]);

    public Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EnrichmentTarget>>([]);

    public Task<bool> ApplyEnrichmentAsync(WorkEnrichment enrichment, CancellationToken ct = default)
        => Task.FromResult(false);
}

internal sealed class PreviewUpdateEventRepository : IUpdateEventRepository
{
    public Task<long> InsertAsync(UpdateEvent updateEvent, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<IReadOnlyList<UpdateEvent>> GetByReleaseAsync(long releaseId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UpdateEvent>>(
            [.. PreviewLibrary.UpdateEvents
                .Where(e => e.ReleaseId == releaseId)
                .OrderBy(e => e.OccurredAt)]);
}

internal sealed class PreviewSnapshotRepository : IPlaytimeSnapshotRepository
{
    public Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<long?> TryAppendAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default)
        => Task.FromResult<long?>(null);

    public Task<PlaytimeSnapshot?> GetLatestAsync(long ownershipId, CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Snapshots
            .Where(s => s.OwnershipId == ownershipId)
            .OrderByDescending(s => s.ObservedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefault());

    public Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(
        long ownershipId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PlaytimeSnapshot>>(
            [.. PreviewLibrary.Snapshots
                .Where(s => s.OwnershipId == ownershipId)
                .OrderBy(s => s.ObservedAt)]);
}

internal sealed class PreviewStorefrontRepository : IStorefrontRepository
{
    public Task<IReadOnlyDictionary<string, StorefrontDetails>> ReadAllAsync(CancellationToken ct = default)
        => Task.FromResult(PreviewLibrary.Storefronts);
}

/// <summary>An empty merge queue: the Merges screen previews its empty state honestly.</summary>
internal sealed class PreviewMergeCandidateRepository : IMergeCandidateRepository
{
    public Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MergeCandidate>>([]);

    public Task<int> CountPendingAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<IReadOnlyList<MergeCandidate>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MergeCandidate>>([]);

    public Task<MergeCandidate?> GetAsync(long id, CancellationToken ct = default)
        => Task.FromResult<MergeCandidate?>(null);

    public Task<MergeCandidate?> FindByPairAsync(
        long leftReleaseId, long rightReleaseId, CancellationToken ct = default)
        => Task.FromResult<MergeCandidate?>(null);

    public Task SetStatusAsync(long id, string status, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<bool> UpdatePendingScoreAsync(
        long id, double score, string? signalsJson, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> WithdrawPendingAsync(long id, CancellationToken ct = default)
        => Task.FromResult(false);
}

internal sealed class PreviewIdentityLinkRepository : IIdentityLinkRepository
{
    public Task<IdentityResolution> GetResolutionAsync(CancellationToken ct = default)
        => Task.FromResult(IdentityResolution.Empty);

    public Task<long> LinkAsync(IdentityLinkRequest request, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<bool> RetractActAsync(long actId, string? note = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> RetractLinkAsync(long childWorkId, string? note = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<IdentityLink>> GetHistoryAsync(long? workId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IdentityLink>>([]);

    public Task<IReadOnlyList<IdentityAct>> GetActsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IdentityAct>>([]);
}

internal sealed class PreviewExpansionRefusalRepository : IExpansionRefusalRepository
{
    public Task<IReadOnlyList<ExpansionRefusal>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExpansionRefusal>>([]);

    public Task RefuseAsync(
        IReadOnlyList<ExpansionRefusalRequest> pairs, string? note = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<int> RetractAsync(
        IReadOnlyList<ExpansionRefusalRequest> pairs, CancellationToken ct = default)
        => Task.FromResult(0);
}

internal sealed class PreviewAccountStatsRepository : IAccountStatsRepository
{
    private static readonly AccountStats Stats = new()
    {
        Source = AccountFactSources.Steam,
        TransactionCount = 214,
        LicenseCount = 312,
        GrossProductSpendCents = 412_300,
        GrossProductTransactionCount = 208,
        RefundedProductSpendCents = 6_499,
        RefundedProductTransactionCount = 2,
        SpendByYear =
        [
            new AccountSpendYear(2023, 61, 118_400),
            new AccountSpendYear(2024, 72, 142_900),
            new AccountSpendYear(2025, 73, 144_501),
        ],
        Purchases = new AccountSpendSlice(184, 371_200),
        GiftPurchases = new AccountSpendSlice(6, 14_995),
        InGamePurchases = new AccountSpendSlice(18, 9_606),
        BiggestPurchase = new AccountBiggestPurchase
        {
            Cents = 9_999,
            OccurredAt = PreviewLibrary.DaysAgo(500),
            ItemNames = ["Baldur's Gate 3", "Larian gift bundle"],
            ItemCount = 2,
            CurrencySymbol = "$",
        },
        FirstTransactionAt = PreviewLibrary.DaysAgo(2000),
        LastTransactionAt = PreviewLibrary.DaysAgo(12),
        Currencies = [new AccountCurrencyUse("$", 214)],
        LicenseAcquisitions =
        [
            new AccountLicenseAcquisition("Steam Store", 288),
            new AccountLicenseAcquisition("Gift/Guest Pass", 14),
            new AccountLicenseAcquisition("Complimentary", 10),
        ],
        FirstLicenseAt = PreviewLibrary.DaysAgo(2000),
        LastLicenseAt = PreviewLibrary.DaysAgo(12),
    };

    public Task<AccountStats> GetAsync(string source, CancellationToken ct = default)
        => Task.FromResult(Stats);
}
