using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Migration 0023's <c>hidden_games</c> table: a persisted, reversible exclusion
/// recorded at the work grain, applied in exactly one place (the derived-bucket
/// query) so the grid, the list view, the feed and every rail count inherit it
/// together.
///
/// <para>The hiding clause tests the row's own work, its live <c>same_game</c>
/// parent and its <c>variant_of</c> parent, so hiding a game takes its whole link
/// group and its demo with it. Unhiding stamps the row rather than deleting it,
/// so re-hiding is a fresh insert and the history is the table.</para>
///
/// <para>Ingest safety is structural: no ingest path writes <c>hidden_games</c>,
/// the resolver joins only on an exact <c>(provider, provider_id)</c>, and no
/// runtime path deletes a <c>works</c> row, so re-ingesting a hidden game cannot
/// unhide it.</para>
/// </summary>
public sealed class HiddenGameTests : IDisposable
{
    private static readonly BucketThresholds Thresholds = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3);

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly HiddenGameRepository _hidden;
    private readonly LibraryQueryRepository _library;

    public HiddenGameTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _hidden = new HiddenGameRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Hiding_and_unhiding_round_trips()
    {
        var (workId, _, _) = await SeedGameAsync("Iron Lung");

        Assert.False(await _hidden.IsHiddenAsync(workId));
        Assert.True(await _hidden.HideAsync(workId));
        Assert.True(await _hidden.IsHiddenAsync(workId));

        Assert.True(await _hidden.UnhideAsync(workId));
        Assert.False(await _hidden.IsHiddenAsync(workId));
    }

    [Fact]
    public async Task Hiding_twice_is_one_hidden_game_and_not_a_constraint_violation()
    {
        var (workId, _, _) = await SeedGameAsync("Iron Lung");

        Assert.True(await _hidden.HideAsync(workId));
        Assert.False(await _hidden.HideAsync(workId));

        Assert.Equal([workId], await _hidden.GetHiddenWorkIdsAsync());
    }

    [Fact]
    public async Task Unhiding_stamps_the_row_rather_than_deleting_it()
    {
        var (workId, _, _) = await SeedGameAsync("Iron Lung");
        await _hidden.HideAsync(workId);
        await _hidden.UnhideAsync(workId);

        using var lease = _db.Factory.Lease();
        using var command = lease.Connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM hidden_games WHERE work_id = @w AND unhidden_at IS NOT NULL;";
        command.Parameters.AddWithValue("@w", workId);

        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task Rehiding_after_an_unhide_works()
    {
        var (workId, _, _) = await SeedGameAsync("Iron Lung");

        await _hidden.HideAsync(workId);
        await _hidden.UnhideAsync(workId);
        Assert.True(await _hidden.HideAsync(workId));
        Assert.True(await _hidden.IsHiddenAsync(workId));
    }

    [Fact]
    public async Task A_hidden_game_leaves_the_bucket_query_and_its_counts()
    {
        var (hiddenWorkId, _, _) = await SeedGameAsync("Iron Lung");
        var (keptWorkId, _, _) = await SeedGameAsync("Outer Wilds");

        var before = await _library.GetOwnershipBucketsAsync(Thresholds);
        Assert.Equal(2, before.Count);

        await _hidden.HideAsync(hiddenWorkId);

        var after = await _library.GetOwnershipBucketsAsync(Thresholds);
        Assert.Equal([keptWorkId], after.Select(r => r.ResolvedWorkId).Distinct());
    }

    [Fact]
    public async Task Hiding_a_linked_game_by_its_resolved_work_hides_every_store_entry()
    {
        var (parentWorkId, _, _) = await SeedGameAsync("Civilization IV", store: "steam");
        var (childWorkId, _, _) = await SeedGameAsync("Civilization IV", store: "gog");

        var links = new IdentityLinkRepository(_db.Factory);
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parentWorkId,
            ChildWorkIds = [childWorkId],
        });

        Assert.Equal(2, (await _library.GetOwnershipBucketsAsync(Thresholds)).Count);

        await _hidden.HideAsync(parentWorkId);

        Assert.Empty(await _library.GetOwnershipBucketsAsync(Thresholds));
    }

    [Fact]
    public async Task Hiding_a_game_does_not_pop_its_demo_into_the_grid()
    {
        var (parentWorkId, _, _) = await SeedGameAsync("Tunic");

        // Named "Ferric Trial" rather than "Tunic Demo" so that title-based demo
        // consolidation cannot be what suppresses it. The test is about the
        // variant_of link and the hidden-games clause; a title that looked like a
        // demo would let it pass for the wrong reason.
        var (demoWorkId, _, _) = await SeedGameAsync("Ferric Trial");

        var links = new IdentityLinkRepository(_db.Factory);
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parentWorkId,
            ChildWorkIds = [demoWorkId],
            Kind = IdentityLinkKinds.VariantOf,
        });

        var withParent = await _library.GetOwnershipBucketsAsync(Thresholds);
        Assert.Equal([parentWorkId], withParent.Select(r => r.ResolvedWorkId).Distinct());

        await _hidden.HideAsync(parentWorkId);

        Assert.Empty(await _library.GetOwnershipBucketsAsync(Thresholds));
    }

    [Fact]
    public async Task A_later_ingest_of_the_same_ownership_does_not_unhide_it()
    {
        var resolver = new ExternalIdResolver(
            _works, _releases, _ownerships,
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

        var observed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await resolver.ResolveAsync([Candidate("440", "Team Fortress 2", observed)], CancellationToken.None);

        var workId = (await _works.GetAllAsync()).Single().Id;
        await _hidden.HideAsync(workId);

        await resolver.ResolveAsync(
            [Candidate("440", "Team Fortress 2", observed.AddDays(1))], CancellationToken.None);

        Assert.True(await _hidden.IsHiddenAsync(workId));
        Assert.Empty(await _library.GetOwnershipBucketsAsync(Thresholds));
    }

    [Fact]
    public async Task Hidden_games_are_enumerable_for_the_unhide_screen()
    {
        var (workId, _, _) = await SeedGameAsync("Iron Lung");
        await SeedGameAsync("Outer Wilds");
        await _hidden.HideAsync(workId);

        var listed = await _hidden.GetHiddenGamesAsync();

        var only = Assert.Single(listed);
        Assert.Equal(workId, only.WorkId);
        Assert.Equal("Iron Lung", only.Title);
        Assert.Equal(1, only.StoreEntryCount);
    }

    [Fact]
    public async Task Deleting_the_work_takes_its_hidden_row_with_it()
    {
        var (workId, _, _) = await SeedGameAsync("Iron Lung");
        await _hidden.HideAsync(workId);

        using (var lease = _db.Factory.Lease())
        {
            using var command = lease.Connection.CreateCommand();
            command.CommandText = "DELETE FROM works WHERE id = @w;";
            command.Parameters.AddWithValue("@w", workId);
            command.ExecuteNonQuery();
        }

        Assert.Empty(await _hidden.GetHiddenWorkIdsAsync());
    }

    private static CandidateOwnership Candidate(string appId, string title, DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: null,
            Installed: false,
            PlaytimeMinutes: 0,
            LastPlayedAt: null,
            AcquiredAt: null,
            Source: "steam_local",
            ObservedAt: observedAt);

    private async Task<(long WorkId, long ReleaseId, long OwnershipId)> SeedGameAsync(
        string name, string store = "steam")
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
        });

        return (workId, releaseId, ownershipId);
    }
}
