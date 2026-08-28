using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;

namespace Hoard.Recommend.Tests;

/// <summary>Ids of one seeded game, at every layer a test might assert on.</summary>
public sealed record SeededGame(long WorkId, long ReleaseId, long OwnershipId);

/// <summary>
/// A migrated temp database, the real Hoard.Data repositories over it, and the
/// engine wired to them — plus seeding helpers shaped like the facts the
/// tests reason about ("a bounced game from 2019", "a patch last month").
///
/// <para>Every date is expressed relative to <see cref="AsOf"/>, a fixed
/// instant, so a test written today asserts the same feed in five years.</para>
/// </summary>
public sealed class RecommendHarness : IDisposable
{
    /// <summary>The tests' "now". Arbitrary but fixed; every seeded date hangs off it.</summary>
    public static readonly DateTime AsOf = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();

    public RecommendHarness()
    {
        Works = new WorkRepository(_db.Factory);
        Releases = new ReleaseRepository(_db.Factory);
        Ownerships = new OwnershipRepository(_db.Factory);
        PlayRecords = new PlayRecordRepository(_db.Factory);
        Snapshots = new PlaytimeSnapshotRepository(_db.Factory);
        Sessions = new SessionRepository(_db.Factory);
        UpdateEvents = new UpdateEventRepository(_db.Factory);
        Facets = new FacetRepository(_db.Factory);
        Feedback = new FeedFeedbackRepository(_db.Factory);

        Engine = new RecommendationEngine(
            new LibraryQueryRepository(_db.Factory),
            Releases,
            Ownerships,
            Snapshots,
            Sessions,
            UpdateEvents,
            Facets);
    }

    public WorkRepository Works { get; }
    public ReleaseRepository Releases { get; }
    public OwnershipRepository Ownerships { get; }
    public PlayRecordRepository PlayRecords { get; }
    public PlaytimeSnapshotRepository Snapshots { get; }
    public SessionRepository Sessions { get; }
    public UpdateEventRepository UpdateEvents { get; }
    public FacetRepository Facets { get; }

    /// <summary>The feedback loop's storage — verdicts, surfacings, endorsements (migration 0011).</summary>
    public FeedFeedbackRepository Feedback { get; }

    public RecommendationEngine Engine { get; }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// A request with everything pinned: the fixed clock and a fixed shuffle
    /// seed, so every assertion about order is about the model, not the day
    /// the suite happened to run.
    /// </summary>
    public static RecommendationRequest Request(int maxResults = 20) => new()
    {
        AsOfUtc = AsOf,
        MaxResults = maxResults,
        ShuffleSeed = 1,
    };

    /// <summary>
    /// Seeds one game through all four layers plus its latest play record and
    /// — matching the measured cold-start shape, where 955 of 960 ownerships
    /// hold exactly one reading — a single snapshot alongside it.
    /// </summary>
    public async Task<SeededGame> SeedGameAsync(
        string name,
        long minutes = 0,
        DateTime? lastPlayed = null,
        string store = "steam",
        bool installed = false,
        bool provisionalName = false)
    {
        var workId = await Works.InsertAsync(new Work
        {
            Name = name,
            NameIsProvisional = provisionalName,
            FirstReleaseYear = 2020,
        });
        var releaseId = await Releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = name,
            Platform = "windows",
        });
        var ownershipId = await Ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
            Installed = installed,
        });

        await PlayRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            LastPlayedAt = lastPlayed,
            Source = "steam_local",
            ObservedAt = AsOf.AddDays(-1),
        });
        await Snapshots.InsertAsync(new PlaytimeSnapshot
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            ObservedAt = AsOf.AddDays(-1),
        });

        return new SeededGame(workId, releaseId, ownershipId);
    }

    /// <summary>
    /// A second ownership of an existing game on another store — the
    /// bought-it-twice case, which in production exists only after the user
    /// confirms a cross-store merge.
    /// </summary>
    public async Task<long> SeedSecondStoreAsync(SeededGame game, string store)
    {
        var ownershipId = await Ownerships.InsertAsync(new Ownership
        {
            ReleaseId = game.ReleaseId,
            Store = store,
        });
        await PlayRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 0,
            LastPlayedAt = null,
            Source = "gog_local",
            ObservedAt = AsOf.AddDays(-1),
        });
        return ownershipId;
    }

    /// <summary>
    /// A "major update" as §4.5 defines one: a build push AND an announcement
    /// within the correlation window. Seeding only one of the pair is exactly
    /// how a test proves the stale bucket does NOT light up.
    /// </summary>
    public async Task SeedMajorUpdateAsync(SeededGame game, DateTime occurredAt, string title)
    {
        await UpdateEvents.InsertAsync(new UpdateEvent
        {
            ReleaseId = game.ReleaseId,
            Kind = UpdateEventKinds.BuildPush,
            BuildId = "100",
            OccurredAt = occurredAt,
        });
        await UpdateEvents.InsertAsync(new UpdateEvent
        {
            ReleaseId = game.ReleaseId,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = occurredAt.AddHours(1),
            Title = title,
        });
    }

    /// <summary>Appends a snapshot reading — a later, higher one is a "rise", i.e. a play episode.</summary>
    public Task SeedSnapshotAsync(SeededGame game, long minutes, DateTime observedAt)
        => Snapshots.InsertAsync(new PlaytimeSnapshot
        {
            OwnershipId = game.OwnershipId,
            PlaytimeMinutes = minutes,
            ObservedAt = observedAt,
        });

    public Task SeedSessionAsync(
        SeededGame game, DateTime startedAt, int durationMinutes = 30, string? attributedBy = null)
        => Sessions.InsertAsync(new Session
        {
            OwnershipId = game.OwnershipId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(durationMinutes),
            DurationSeconds = durationMinutes * 60L,
            DetectionMethod = DetectionMethods.ProcessWatch,
            AttributedBy = attributedBy,
        });

    /// <summary>Attaches a single genre to the work, minting the facet row as the backfill would.</summary>
    public Task SeedGenreAsync(SeededGame game, string genre)
        => Facets.SetWorkFacetsAsync(game.WorkId, [new FacetAssignment(FacetKinds.Genre, genre)]);

    /// <summary>Attaches several genres at once — the diversity-cap tests need multi-genre games.</summary>
    public Task SeedGenresAsync(SeededGame game, params string[] genres)
        => Facets.SetWorkFacetsAsync(
            game.WorkId,
            genres.Select(g => new FacetAssignment(FacetKinds.Genre, g)).ToList());

    /// <summary>
    /// Attaches game-mode facets to the release, the way the Steam category
    /// sync writes them. Slugs from <see cref="GameModes"/>.
    /// </summary>
    public Task SeedModesAsync(SeededGame game, params string[] modeSlugs)
        => Facets.SetReleaseFacetsAsync(
            game.ReleaseId,
            modeSlugs.Select(GameModes.Assignment).ToList());
}
