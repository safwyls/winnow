using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The details modal's wrong-game control against a real migrated database
/// (TASK-89, acceptance criteria 1, 2 and 4; TASK-106, the cover-precedence
/// fix). The assignment rewrites the work's metadata, the library reloads,
/// and the modal reopens on the same ownership with the corrected game.
///
/// <para>Cover precedence is the heart of the TASK-106 tests. A live IGDB
/// pin outranks the Steam portrait capsule, so a Steam-owned game draws the
/// pinned art after an assignment and gets its capsule back after a clear.
/// The GOG-seeded test exercises rule 3 — the image-id path that has no
/// Steam appid to compete with — and the Steam-seeded tests exercise the
/// rule that a pin wins over store precedence.</para>
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

        // Rule 3: this work has no Steam appid, so the cover key is the image
        // id in the rewritten cover_url — the Epic/GOG path, not the pin path.
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

    /// <summary>
    /// TASK-106: the defect the user reported. A Steam-owned game's tile kept
    /// the original capsule after an assignment, because store precedence
    /// chose <c>CoverKey.Steam(appid)</c> before anything read the
    /// <c>cover_url</c> the pin had just rewritten. A live pin now outranks
    /// the capsule, and clearing the pin hands it back.
    ///
    /// <para>The three keys this test walks — steam:3900, igdb:co2abc,
    /// steam:3900 again — also show that nothing has to be evicted from the
    /// cover cache. A pin moves the tile to a key naming a different artwork
    /// asset; the old key is simply no longer asked for.</para>
    /// </summary>
    [Fact]
    public async Task A_steam_owned_game_takes_the_pinned_art_and_gives_the_capsule_back()
    {
        var seeded = await SeedAsync(steamAppId: "3900");

        var library = await LoadAsync(new PinningAssignmentService(_pins));

        var before = Assert.Single(library.VisibleTiles);
        Assert.Equal("steam", before.CoverKey?.Provider);
        Assert.Equal("3900", before.CoverKey?.Id);

        await library.OpenDetailsCommand.ExecuteAsync(before);

        var match = library.Details!.IgdbMatch!;
        await match.SearchCommand.ExecuteAsync(null);
        await match.AssignCommand.ExecuteAsync(match.Candidates[0]);

        // The pin outranks the capsule, so the tile the user is looking at
        // draws the art of the game they said this is.
        var pinned = library.Details!.Tile;
        Assert.Equal(before.OwnershipId, pinned.OwnershipId);
        Assert.Equal("igdb", pinned.CoverKey?.Provider);
        Assert.Equal("co2abc", pinned.CoverKey?.Id);
        Assert.NotEqual(before.CoverKey, pinned.CoverKey);
        Assert.Equal(pinned.CoverKey, Assert.Single(library.VisibleTiles).CoverKey);

        // The Steam appid is still the release's own — the pin changed which
        // art the tile asks for, not what the game is owned as.
        Assert.Equal("3900", pinned.SteamAppId);

        await library.Details!.IgdbMatch!.ClearCommand.ExecuteAsync(null);

        Assert.Null(await _pins.GetAsync(seeded.WorkId));

        var cleared = library.Details!.Tile;
        Assert.Equal(before.OwnershipId, cleared.OwnershipId);
        Assert.Equal("steam", cleared.CoverKey?.Provider);
        Assert.Equal("3900", cleared.CoverKey?.Id);
        Assert.Equal(before.CoverKey, cleared.CoverKey);
        Assert.False(library.Details!.IgdbMatch!.IsPinned);
    }

    /// <summary>
    /// A pinned entry that IGDB gave no cover falls through to the store
    /// capsule rather than to the placeholder. The user is no worse off than
    /// before the pin, and a placeholder tells them less than the wrong art.
    /// </summary>
    [Fact]
    public async Task A_pinned_entry_with_no_cover_leaves_the_capsule_in_place()
    {
        await SeedAsync(steamAppId: "3900");

        var library = await LoadAsync(new PinningAssignmentService(_pins, coverUrl: null));
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles.Single());

        var match = library.Details!.IgdbMatch!;
        await match.SearchCommand.ExecuteAsync(null);
        await match.AssignCommand.ExecuteAsync(match.Candidates[0]);

        var tile = library.Details!.Tile;
        Assert.Equal(2017, tile.ReleaseYear);
        Assert.Equal("steam", tile.CoverKey?.Provider);
        Assert.Equal("3900", tile.CoverKey?.Id);
    }

    private async Task<SeededGame> SeedAsync(
        string title = "Prey",
        long? igdbId = 1234,
        string? coverUrl = WrongCover,
        int year = 2006,
        string? steamAppId = null)
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

        var store = steamAppId is null ? "gog" : "steam";

        if (steamAppId is not null)
        {
            await _releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = steamAppId,
            });
        }

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 120,
            LastPlayedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Source = store,
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

        /// <summary>
        /// The cover URL the chosen entry carries. Null stands for an IGDB
        /// entry with no cover at all — the one case where a pin cannot
        /// outrank the store capsule.
        /// </summary>
        private readonly string? _coverUrl;

        public PinningAssignmentService(IWorkIgdbPinRepository pins, string? coverUrl = RightCover)
        {
            _pins = pins;
            _coverUrl = coverUrl;
        }

        public Task<IReadOnlyList<IgdbCandidate>> SearchAsync(
            string title, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IgdbCandidate>>(
            [
                new IgdbCandidate(
                    5678,
                    "Prey",
                    _coverUrl,
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
                    CoverUrl = _coverUrl,
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

        public Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
            => _pins.GetLivePinnedWorkIdsAsync(ct);
    }
}
