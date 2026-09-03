using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Merges screen knows nothing about the detail modal; the shell wires a
/// row's request to the library, which owns the modal and draws it over every
/// pane. This test holds that wiring end to end, because a broken event
/// subscription would compile and render a row whose click does nothing.
/// </summary>
public sealed class MergesDetailsTests
{
    [Fact]
    public async Task A_row_click_opens_the_librarys_detail_modal_for_that_entry()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownerships = new OwnershipRepository(db.Factory);
        var candidates = new MergeCandidateRepository(db.Factory);
        var links = new IdentityLinkRepository(db.Factory);
        var refusals = new ExpansionRefusalRepository(db.Factory);

        var left = await SeedAsync(works, releases, ownerships, "Prey", 2017);
        var right = await SeedAsync(works, releases, ownerships, "Prey", null);
        await candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = left,
            RightReleaseId = right,
            Score = 0.8,
            Status = MergeCandidateStatuses.Pending,
        });

        var library = new LibraryViewModel(
            new LibraryQueryRepository(db.Factory),
            ownerships,
            releases,
            works,
            new UpdateEventRepository(db.Factory),
            covers: null);
        var queue = new MergeQueueViewModel(
            candidates, releases, works, links, ownerships,
            new LibraryExpansionScan(releases, links, refusals),
            refusals,
            new LibraryQueryRepository(db.Factory),
            post: action => action());
        var shell = new MainWindowViewModel(
            library, queue, DetachedStores.Create(), DetachedAppearance.Create(),
            DetachedFeed.Create(), new AccountStatsViewModel(new FakeAccountStatsRepository()));

        await library.LoadCommand.ExecuteAsync(null);
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Sections.Single(s => s.Kind == MergeSectionKind.Editions).Cards);
        var row = card.Rows.Single(r => r.ReleaseIds.Contains(right));
        Assert.False(library.IsDetailsOpen);

        queue.OpenDetailsCommand.Execute(row);
        await WaitForAsync(() => library.IsDetailsOpen);

        Assert.True(library.IsDetailsOpen);
        Assert.Equal(right, library.Details!.Tile.ReleaseId);
        Assert.Same(row, queue.FocusedRow);

        // The modal is the shell's only reader of the request; the queue is
        // still a live screen behind it, header untouched.
        Assert.Equal(0, card.HeaderIndex);
        Assert.NotNull(shell.MergeQueue);
    }

    [Fact]
    public async Task A_row_with_no_tile_opens_nothing_and_says_so()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownerships = new OwnershipRepository(db.Factory);

        var library = new LibraryViewModel(
            new LibraryQueryRepository(db.Factory),
            ownerships,
            releases,
            works,
            new UpdateEventRepository(db.Factory),
            covers: null);
        await library.LoadCommand.ExecuteAsync(null);

        // A release the library has no ownership row for, as the sample
        // seeder's minted merge pairs are.
        var workId = await works.InsertAsync(new Work { Name = "Prey" });
        var releaseId = await releases.InsertAsync(new Release { WorkId = workId, Name = "Prey", Platform = "windows" });

        Assert.False(await library.OpenDetailsForReleasesAsync([releaseId]));
        Assert.False(library.IsDetailsOpen);
    }

    private static async Task<long> SeedAsync(
        WorkRepository works, ReleaseRepository releases, OwnershipRepository ownerships,
        string title, int? year)
    {
        var workId = await works.InsertAsync(new Work { Name = title, FirstReleaseYear = year });
        var releaseId = await releases.InsertAsync(new Release { WorkId = workId, Name = title, Platform = "windows" });
        await ownerships.UpsertAsync(new OwnershipUpsert(releaseId, "steam", null, null, null, null));
        return releaseId;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }
}
