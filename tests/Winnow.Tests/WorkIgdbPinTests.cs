using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Repository-level tests for <c>work_igdb_pins</c> (migration 0026):
/// pinning rewrites metadata, re-pinning stamps the previous row,
/// clearing returns the work to automatic enrichment, the two refusal
/// cases — work not found, igdb_id already claimed — are enforced, and the
/// bulk read (<c>GetLivePinnedWorkIdsAsync</c>) returns only live pins,
/// excludes cleared pins, and lists a re-pinned work exactly once.
/// </summary>
public sealed class WorkIgdbPinTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly WorkIgdbPinRepository _pins;

    public WorkIgdbPinTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _pins = new WorkIgdbPinRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Pinning_rewrites_the_metadata_and_records_the_mapping()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        var outcome = await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId,
            IgdbId = 103_298,
            Name = "Prey (2017)",
            FirstReleaseYear = 2017,
            Summary = "A hand-picked summary.",
            CoverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1r7f.jpg",
            Publisher = "Bethesda Softworks",
            IgdbGameType = "main_game",
        });

        Assert.Equal(WorkIgdbPinOutcome.Pinned, outcome);

        var pin = await _pins.GetAsync(workId);
        Assert.NotNull(pin);
        Assert.Equal(103_298, pin.IgdbId);

        var work = await _works.GetAsync(workId);
        Assert.NotNull(work);
        Assert.Equal(103_298, work.IgdbId);
        Assert.Equal("Prey (2017)", work.Name);
        Assert.False(work.NameIsProvisional);
        Assert.Equal(2017, work.FirstReleaseYear);
        Assert.Equal("A hand-picked summary.", work.Summary);
        Assert.Equal("Bethesda Softworks", work.Publisher);
        Assert.Equal("main_game", work.IgdbGameType);
    }

    [Fact]
    public async Task Pinning_overwrites_metadata_that_the_wrong_match_had_already_written()
    {
        var workId = await SeedAsync("620", "Prey (2006)", provisional: false, igdbId: 1020);

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId,
            IgdbId = 103_298,
            Name = "Prey (2017)",
            FirstReleaseYear = 2017,
            Summary = null,
        });

        var work = await _works.GetAsync(workId);
        Assert.NotNull(work);
        Assert.Equal(103_298, work.IgdbId);
        Assert.Equal("Prey (2017)", work.Name);
        Assert.Equal(2017, work.FirstReleaseYear);
        Assert.Null(work.Summary);
    }

    [Fact]
    public async Task A_pinned_work_is_no_longer_an_enrichment_target()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        Assert.Contains(await _works.GetEnrichmentTargetsAsync(), t => t.WorkId == workId);

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });

        Assert.DoesNotContain(await _works.GetEnrichmentTargetsAsync(), t => t.WorkId == workId);

        Assert.True(await _pins.ClearAsync(workId));

        Assert.Contains(await _works.GetEnrichmentTargetsAsync(), t => t.WorkId == workId);
    }

    [Fact]
    public async Task An_enrichment_write_against_a_pinned_work_changes_nothing()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId,
            IgdbId = 103_298,
            Name = "Prey (2017)",
            Summary = "A hand-picked summary.",
        });

        var promoted = await _works.ApplyEnrichmentAsync(new WorkEnrichment(
            workId,
            Name: "Prey (2006)",
            IgdbId: 1020,
            FirstReleaseYear: 2006,
            Summary: "The wrong game.",
            CoverUrl: "https://images.example/wrong.jpg",
            Publisher: "3D Realms"));

        Assert.False(promoted);

        var work = await _works.GetAsync(workId);
        Assert.NotNull(work);
        Assert.Equal(103_298, work.IgdbId);
        Assert.Equal("Prey (2017)", work.Name);
        Assert.Equal("A hand-picked summary.", work.Summary);
        Assert.Null(work.FirstReleaseYear);
        Assert.Null(work.CoverUrl);
        Assert.Null(work.Publisher);
    }

    [Fact]
    public async Task Repinning_replaces_the_live_row_and_leaves_exactly_one()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 1020, Name = "Prey (2006)",
        });
        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });

        var pin = await _pins.GetAsync(workId);
        Assert.NotNull(pin);
        Assert.Equal(103_298, pin.IgdbId);

        Assert.Equal(2, await RowCountAsync(workId));
        Assert.Equal(1, await LiveRowCountAsync(workId));
    }

    [Fact]
    public async Task A_pin_is_refused_when_another_work_already_holds_that_igdb_id()
    {
        var taken = await SeedAsync("620", "Prey (2017)", provisional: false, igdbId: 103_298);
        var workId = await SeedAsync("570", "App 570", provisional: true);

        var outcome = await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });

        Assert.Equal(WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork, outcome);
        Assert.Null(await _pins.GetAsync(workId));
        Assert.Equal("App 570", (await _works.GetAsync(workId))!.Name);
        Assert.Equal(103_298, (await _works.GetAsync(taken))!.IgdbId);
    }

    [Fact]
    public async Task A_pin_against_a_work_that_does_not_exist_is_refused()
    {
        var outcome = await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = 9_999, IgdbId = 103_298, Name = "Prey (2017)",
        });

        Assert.Equal(WorkIgdbPinOutcome.WorkNotFound, outcome);
    }

    [Fact]
    public async Task Clearing_a_work_that_was_never_pinned_reports_nothing_cleared()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        Assert.False(await _pins.ClearAsync(workId));
    }

    [Fact]
    public async Task The_bulk_read_lists_only_the_works_that_carry_a_live_pin()
    {
        var pinned = await SeedAsync("620", "App 620", provisional: true);
        var unpinned = await SeedAsync("570", "App 570", provisional: true);

        Assert.Empty(await _pins.GetLivePinnedWorkIdsAsync());

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = pinned, IgdbId = 103_298, Name = "Prey (2017)",
        });

        var live = await _pins.GetLivePinnedWorkIdsAsync();
        Assert.Contains(pinned, live);
        Assert.DoesNotContain(unpinned, live);
    }

    [Fact]
    public async Task The_bulk_read_excludes_a_pin_that_has_been_cleared()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });
        Assert.Contains(workId, await _pins.GetLivePinnedWorkIdsAsync());

        Assert.True(await _pins.ClearAsync(workId));

        Assert.DoesNotContain(workId, await _pins.GetLivePinnedWorkIdsAsync());
    }

    [Fact]
    public async Task The_bulk_read_lists_a_repinned_work_exactly_once()
    {
        var workId = await SeedAsync("620", "App 620", provisional: true);

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 1020, Name = "Prey (2006)",
        });
        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });

        Assert.Equal(2, await RowCountAsync(workId));
        Assert.Equal(1, await LiveRowCountAsync(workId));

        var live = await _pins.GetLivePinnedWorkIdsAsync();
        Assert.Single(live);
        Assert.Contains(workId, live);
    }

    private async Task<long> SeedAsync(
        string appId, string name, bool provisional, long? igdbId = null)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = name,
            NameIsProvisional = provisional,
            IgdbId = igdbId,
        });

        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = appId,
        });

        return workId;
    }

    private async Task<int> RowCountAsync(long workId)
    {
        using var lease = _db.Factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM work_igdb_pins WHERE work_id = @workId;",
            new { workId },
            lease.Transaction);
    }

    private async Task<int> LiveRowCountAsync(long workId)
    {
        using var lease = _db.Factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM work_igdb_pins WHERE work_id = @workId AND cleared_at IS NULL;",
            new { workId },
            lease.Transaction);
    }
}
