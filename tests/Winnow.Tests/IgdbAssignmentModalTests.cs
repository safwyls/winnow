using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The details modal's wrong-game control against a real migrated database
/// (TASK-89, acceptance criteria 1, 2 and 4). The assignment rewrites the
/// work's metadata, the library reloads, the modal reopens on the same
/// ownership with the corrected game, and the tile's cover key follows the
/// rewritten <c>works.cover_url</c> — no separate cover-refresh mechanism
/// is needed.
///
/// <para>The work is seeded with no Steam appid on purpose.
/// <c>LibraryViewModel</c> prefers a Steam capsule key when a Steam
/// external id exists, so with one the tile's cover would be the Steam key
/// whatever IGDB said, and the thing being proved would not be
/// proved.</para>
/// </summary>
public sealed class IgdbAssignmentModalTests : IDisposable
{
    private static readonly DateTime Observed = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string WrongCover =
        "https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg";

    private const string RightCover =
        "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg";

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;
    private readonly UpdateEventRepository _updates;
    private readonly LibraryQueryRepository _queries;
    private readonly WorkIgdbPinRepository _pins;

    public IgdbAssignmentModalTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
        _updates = new UpdateEventRepository(_db.Factory);
        _queries = new LibraryQueryRepository(_db.Factory);
        _pins = new WorkIgdbPinRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Choosing_a_candidate_rewrites_the_metadata_and_the_cover()
    {
        await SeedAsync();

        var service = new PinningAssignmentService(_pins);
        var library = await LoadAsync(service);

        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal(2006, tile.ReleaseYear);
        Assert.Equal("co1r76", tile.CoverKey?.Id);

        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var match = library.Details?.IgdbMatch;
        Assert.NotNull(match);

        // The field opens on the title the library already shows.
        Assert.Equal("Prey", match.Query);

        await match.SearchCommand.ExecuteAsync(null);
        var candidate = Assert.Single(match.Candidates);
        Assert.Equal("2017 · ", candidate.YearText);
        Assert.Equal("PC (Microsoft Windows), PlayStation 4", candidate.PlatformsText);
        Assert.Equal("co2abc", candidate.CoverKey?.Id);

        await match.AssignCommand.ExecuteAsync(candidate);

        // The modal reopened on the same game, and every fact the wrong entry
        // supplied has been replaced.
        var reopened = library.Details;
        Assert.NotNull(reopened);
        Assert.Equal(tile.OwnershipId, reopened.Tile.OwnershipId);
        Assert.Equal(2017, reopened.Tile.ReleaseYear);
        Assert.Equal("Bethesda Softworks", reopened.Tile.Publisher);
        Assert.Equal("Morgan Yu wakes on Talos I.", reopened.Tile.Summary);

        // The cover key is derived from the rewritten URL, which is why no
        // separate cover refresh is needed.
        Assert.Equal("igdb", reopened.Tile.CoverKey?.Provider);
        Assert.Equal("co2abc", reopened.Tile.CoverKey?.Id);

        // The confirmation survived the reload, and the pin is read back from
        // the database rather than remembered.
        Assert.NotNull(reopened.IgdbMatch);
        Assert.True(reopened.IgdbMatch.IsPinned);
        Assert.True(reopened.IgdbMatch.HasNote);
        Assert.False(reopened.IgdbMatch.HasProblem);
    }

    [Fact]
    public async Task A_second_game_cannot_claim_an_entry_another_game_already_holds()
    {
        var first = await SeedAsync();
        await SeedAsync("Prey (2017)", igdbId: 5678, coverUrl: RightCover, year: 2017);

        var library = await LoadAsync(new PinningAssignmentService(_pins));

        var tile = library.VisibleTiles.Single(t => t.Title == "Prey");
        Assert.Equal(first.OwnershipId, tile.OwnershipId);

        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var match = library.Details!.IgdbMatch!;
        await match.SearchCommand.ExecuteAsync(null);
        await match.AssignCommand.ExecuteAsync(match.Candidates[0]);

        Assert.Equal(
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork),
            match.Problem);
        Assert.False(match.IsPinned);

        // Nothing was written: the wrong metadata is still the wrong metadata.
        Assert.Equal(2006, library.Details!.Tile.ReleaseYear);
    }

    [Fact]
    public async Task The_assignment_can_be_cleared_and_the_pin_goes_with_it()
    {
        var seeded = await SeedAsync();

        var library = await LoadAsync(new PinningAssignmentService(_pins));
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles.Single());

        var match = library.Details!.IgdbMatch!;
        await match.SearchCommand.ExecuteAsync(null);
        await match.AssignCommand.ExecuteAsync(match.Candidates[0]);

        Assert.NotNull(await _pins.GetAsync(seeded.WorkId));

        var after = library.Details!.IgdbMatch!;
        Assert.True(after.ShowPinned);

        await after.ClearCommand.ExecuteAsync(null);

        Assert.False(after.IsPinned);
        Assert.False(after.ShowPinned);
        Assert.False(after.HasProblem);
        Assert.Null(await _pins.GetAsync(seeded.WorkId));
    }

    /// <summary>
    /// With no assignment service registered the modal is exactly the modal
    /// it was before TASK-89 — the degradation every optional seam on this
    /// view model takes.
    /// </summary>
    [Fact]
    public async Task No_service_means_no_control()
    {
        await SeedAsync();

        var library = new LibraryViewModel(_queries, _ownerships, _releases, _works, _updates);
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles.Single());

        Assert.NotNull(library.Details);
        Assert.Null(library.Details.IgdbMatch);
        Assert.False(library.Details.ShowIgdbMatch);
    }

    private async Task<LibraryViewModel> LoadAsync(IIgdbAssignmentService service)
    {
        var library = new LibraryViewModel(
            _queries, _ownerships, _releases, _works, _updates, igdb: service);

        await library.LoadCommand.ExecuteAsync(null);
        return library;
    }

    private async Task<SeededGame> SeedAsync(
        string title = "Prey",
        long? igdbId = 1234,
        string? coverUrl = WrongCover,
        int year = 2006)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = title,
            IgdbId = igdbId,
            FirstReleaseYear = year,
            Publisher = "2K Games",
            Summary = "A Cherokee garage mechanic is abducted.",
            CoverUrl = coverUrl,
        });

        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = title,
            Platform = "windows",
        });

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "gog",
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 120,
            LastPlayedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Source = "gog",
            ObservedAt = Observed,
        });

        return new SeededGame(workId, releaseId, ownershipId);
    }

    private sealed record SeededGame(long WorkId, long ReleaseId, long OwnershipId);

    /// <summary>
    /// Stands in for the IGDB half only. The search answers from a canned
    /// candidate; the assignment performs the real pin write through
    /// <see cref="WorkIgdbPinRepository"/>, so everything below the view
    /// model is the shipped code, including the UNIQUE-constraint refusal.
    /// </summary>
    private sealed class PinningAssignmentService : IIgdbAssignmentService
    {
        private readonly IWorkIgdbPinRepository _pins;

        public PinningAssignmentService(IWorkIgdbPinRepository pins) => _pins = pins;

        public Task<IReadOnlyList<IgdbCandidate>> SearchAsync(
            string title, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IgdbCandidate>>(
            [
                new IgdbCandidate(
                    5678,
                    "Prey",
                    RightCover,
                    2017,
                    ["PC (Microsoft Windows)", "PlayStation 4"]),
            ]);

        public async Task<IgdbAssignmentOutcome> AssignAsync(
            long workId, long igdbId, CancellationToken ct = default)
        {
            var outcome = await _pins.PinAsync(
                new WorkIgdbPinAssignment
                {
                    WorkId = workId,
                    IgdbId = igdbId,
                    Name = "Prey",
                    FirstReleaseYear = 2017,
                    Summary = "Morgan Yu wakes on Talos I.",
                    CoverUrl = RightCover,
                    Publisher = "Bethesda Softworks",
                },
                ct);

            return outcome switch
            {
                WorkIgdbPinOutcome.Pinned =>
                    IgdbAssignmentOutcome.Assigned,
                WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork =>
                    IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
                _ => IgdbAssignmentOutcome.WorkNotFound,
            };
        }

        public Task<bool> ClearAsync(long workId, CancellationToken ct = default)
            => _pins.ClearAsync(workId, ct);

        public Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
            => _pins.GetAsync(workId, ct);
    }
}
