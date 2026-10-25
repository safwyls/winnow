using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;

namespace Winnow.Electron.Fixtures;

// Source-shaped UI fixtures use the actual matcher to retain its complete evidence payload.
internal sealed class MergeReviewFixture(
    IWorkRepository works,
    IReleaseRepository releases,
    IOwnershipRepository ownerships,
    IMergeCandidateRepository candidates,
    IResolveStateRepository resolveState,
    LibraryChangePublisher publisher)
{
    public async Task<object> SeedPairAsync(MergePairSeed input)
    {
        async Task<long> AddAsync(string store)
        {
            var work = await works.InsertAsync(new Work
                { Name = input.Title, FirstReleaseYear = 2011, Publisher = "Supergiant Games" });
            var release = await releases.InsertAsync(new Release
                { WorkId = work, Name = input.Title, Platform = "windows" });
            await ownerships.UpsertAsync(new OwnershipUpsert(release, store, null, null, null, null));
            return release;
        }
        var left = await AddAsync("steam");
        if (input.MultipleStores)
            await ownerships.UpsertAsync(new OwnershipUpsert(left, "gog", null, null, null, null));
        var right = await AddAsync(input.RightStore);
        MatchSubject Subject(long id) => new()
            { ReleaseId = id, Title = input.Title, ReleaseYear = 2011, Publisher = "Supergiant Games" };
        var score = new SoftMatcher().Score(Subject(left), Subject(right));
        await candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = left, RightReleaseId = right, Score = score.Score,
            SignalsJson = SoftMatchSignalsJson.Serialize(score), Status = MergeCandidateStatuses.Pending,
        });
        if (input.MarkSweepComplete)
            await resolveState.SetLastSoftMatchSweepAsync(DateTimeOffset.UtcNow);
        return new { Left = left, Right = right };
    }

    public Task PublishAsync() => publisher.PublishAsync(default);
}

internal sealed record MergePairSeed(string Title, string RightStore, bool MultipleStores = false,
    bool MarkSweepComplete = false);
