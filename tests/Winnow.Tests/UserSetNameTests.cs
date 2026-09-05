using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-124: a user reported that a name set in the details modal's metadata
/// editor never reached the library. The name does reach every surface, and
/// did before the fix — the shadowing the report described does not exist.
/// These tests pin that fact so it is not re-diagnosed.
///
/// <para>The real defect was that nothing refreshed the title in a running
/// session; the fix is in the App layer. This class also pins the other
/// half: the one query that prefers the storefront title — demo
/// consolidation's — must keep preferring it after a rename.</para>
/// </summary>
public sealed class UserSetNameTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The user-set name appears on the grid tile after a load.
    /// </summary>
    [Fact]
    public async Task A_user_set_name_reaches_the_grid_tile()
    {
        using var fixture = new Fixture();
        var seeded = await fixture.SeedAsync(
            workName: "Automatic Name", releaseName: "Storefront Title");
        await fixture.NameAsync(seeded.WorkId, "My Own Name");

        var library = fixture.CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Equal("My Own Name", Assert.Single(library.VisibleTiles).Title);
    }

    /// <summary>
    /// The user-set name appears as the details modal headline, and the
    /// provisional badge is cleared because the user chose this name.
    /// </summary>
    [Fact]
    public async Task A_user_set_name_reaches_the_details_modal_headline()
    {
        using var fixture = new Fixture();
        var seeded = await fixture.SeedAsync(
            workName: "Automatic Name", releaseName: "Storefront Title");
        await fixture.NameAsync(seeded.WorkId, "My Own Name");

        var library = fixture.CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.VisibleTiles));

        var details = library.Details;
        Assert.NotNull(details);
        Assert.Equal("My Own Name", details.Title);

        // The editor cleared name_is_provisional, so the modal must not badge a
        // name the user chose as a placeholder.
        Assert.False(details.TitleIsProvisional);
    }

    /// <summary>
    /// The user-set name governs both the title sort and the search filter.
    /// The old storefront title is not a second name the game answers to.
    /// </summary>
    [Fact]
    public async Task A_user_set_name_reaches_search_and_the_title_sort()
    {
        using var fixture = new Fixture();
        var seeded = await fixture.SeedAsync(
            workName: "Automatic Name", releaseName: "Storefront Title");
        await fixture.SeedAsync(workName: "Almanac", releaseName: "Almanac");
        await fixture.NameAsync(seeded.WorkId, "Zenith");

        var library = fixture.CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        library.Sort = LibrarySort.NameAscending;
        Assert.Equal(["Almanac", "Zenith"], library.VisibleTiles.Select(t => t.Title));

        library.SearchText = "Zeni";
        Assert.Equal("Zenith", Assert.Single(library.VisibleTiles).Title);

        // The storefront title the user replaced is not a second name the game
        // still answers to.
        library.SearchText = "Storefront";
        Assert.Empty(library.VisibleTiles);
    }

    /// <summary>
    /// The user-set name appears in the Merges queue card rows.
    /// </summary>
    [Fact]
    public async Task A_user_set_name_reaches_the_merges_queue()
    {
        using var fixture = new Fixture();
        var left = await fixture.SeedAsync(workName: "Prey", releaseName: "Prey");
        var right = await fixture.SeedAsync(workName: "Prey", releaseName: "Prey", store: "gog");
        await fixture.QueuePairAsync(left, right);
        await fixture.NameAsync(left.WorkId, "Prey (2017)");

        var queue = fixture.CreateMergeQueue();
        await queue.LoadCommand.ExecuteAsync(null);

        var titles = queue.Sections
            .SelectMany(section => section.Cards)
            .SelectMany(card => card.Rows)
            .Select(row => row.Title)
            .ToList();

        Assert.Contains("Prey (2017)", titles);
    }

    /// <summary>
    /// A game the user has never named keeps the automatic title unchanged.
    /// </summary>
    [Fact]
    public async Task A_game_the_user_has_not_named_keeps_the_title_it_had()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync(workName: "Automatic Name", releaseName: "Storefront Title");

        var library = fixture.CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Automatic Name", Assert.Single(library.VisibleTiles).Title);
    }

    /// <summary>
    /// Demo consolidation still matches on the storefront title after a user
    /// rename. The bucket query's Title column prefers releases.name, so a
    /// rename cannot unfold a demo.
    /// </summary>
    [Fact]
    public async Task Demo_consolidation_still_reads_the_storefront_title_after_a_rename()
    {
        using var fixture = new Fixture();
        var full = await fixture.SeedAsync(workName: "Bastion", releaseName: "Bastion");
        await fixture.SeedAsync(workName: "Bastion Demo", releaseName: "Bastion Demo");

        var before = await fixture.Queries.GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = true });
        Assert.Single(before);

        await fixture.NameAsync(full.WorkId, "Bastion, the one I like");

        var after = await fixture.Queries.GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = true });

        Assert.Equal(before.Single().ReleaseId, Assert.Single(after).ReleaseId);
    }

    /// <summary>
    /// Resetting the name to automatic marks it provisional again but leaves
    /// the user's text on screen: <c>works.name</c> is NOT NULL, so a reset
    /// cannot empty it and instead marks it provisional for the next automatic
    /// pass to promote over.
    /// </summary>
    [Fact]
    public async Task Handing_the_name_back_to_automatic_marks_it_provisional_again()
    {
        using var fixture = new Fixture();
        var seeded = await fixture.SeedAsync(
            workName: "Automatic Name", releaseName: "Storefront Title");
        await fixture.NameAsync(seeded.WorkId, "My Own Name");
        await fixture.Fields.ResetFieldAsync(seeded.WorkId, WorkFields.Name);

        var library = fixture.CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        var tile = Assert.Single(library.VisibleTiles);

        Assert.Equal("My Own Name", tile.Title);
        Assert.True(tile.NameIsProvisional);
        Assert.Empty(await fixture.Fields.GetSourcesAsync(seeded.WorkId));
    }

    private sealed record Seeded(long WorkId, long ReleaseId, string Title);

    private sealed class Fixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 700000;

        public Fixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Ownerships = new OwnershipRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
            Queries = new LibraryQueryRepository(_db.Factory);
            Fields = new WorkFieldSourceRepository(_db.Factory);
            Candidates = new MergeCandidateRepository(_db.Factory);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IOwnershipRepository Ownerships { get; }

        public IPlayRecordRepository Plays { get; }

        public IUpdateEventRepository Updates { get; }

        public ILibraryQueryRepository Queries { get; }

        public IWorkFieldSourceRepository Fields { get; }

        public IMergeCandidateRepository Candidates { get; }

        public LibraryViewModel CreateLibrary()
            => new(Queries, Ownerships, Releases, Works, Updates);

        public MergeQueueViewModel CreateMergeQueue()
        {
            var links = new IdentityLinkRepository(_db.Factory);
            var refusals = new ExpansionRefusalRepository(_db.Factory);
            return new MergeQueueViewModel(
                Candidates,
                Releases,
                Works,
                links,
                Ownerships,
                new LibraryExpansionScan(Releases, links, refusals),
                refusals,
                Queries);
        }

        /// <summary>The editor's own write path, not a direct UPDATE.</summary>
        public async Task NameAsync(long workId, string name)
            => Assert.Equal(
                WorkFieldEditOutcome.Applied,
                await Fields.SetFieldAsync(workId, WorkFields.Name, name));

        public async Task<Seeded> SeedAsync(
            string workName, string releaseName, string store = "steam")
        {
            var workId = await Works.InsertAsync(new Work { Name = workName });

            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = releaseName,
                Platform = "windows",
            });

            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = store == "gog" ? ExternalIdProviders.Gog : ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
            });

            var ownershipId = await Ownerships.InsertAsync(new Ownership
            {
                ReleaseId = releaseId,
                Store = store,
            });

            await Plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = 0,
                LastPlayedAt = null,
                Source = "steam_localconfig",
                ObservedAt = Now,
            });

            return new Seeded(workId, releaseId, releaseName);
        }

        /// <summary>
        /// Scores with the real matcher and stores the real payload, so the
        /// queue decodes what the resolver would have written.
        /// </summary>
        public async Task QueuePairAsync(Seeded left, Seeded right)
        {
            var score = new SoftMatcher().Score(Subject(left), Subject(right));
            Assert.True(score.ShouldQueue);

            await Candidates.InsertAsync(new MergeCandidate
            {
                LeftReleaseId = left.ReleaseId,
                RightReleaseId = right.ReleaseId,
                Score = score.Score,
                SignalsJson = SoftMatchSignalsJson.Serialize(score),
                Status = MergeCandidateStatuses.Pending,
            });
        }

        private static MatchSubject Subject(Seeded seeded)
            => new() { ReleaseId = seeded.ReleaseId, Title = seeded.Title };

        public void Dispose() => _db.Dispose();
    }
}
