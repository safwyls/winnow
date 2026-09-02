using System.Diagnostics;
using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The stage where links stop being inert. Everything before this was
/// additive and invisible; this is where a link changes what the user sees,
/// so what these tests hold is the boundary between resolving and not
/// resolving. A link is still purely additive underneath — an unresolved
/// surface shows two entries for one game, which is exactly what the app
/// showed before any of this existed, and never doubled, missing or
/// corrupted data.
/// </summary>
public sealed class IdentityReadModelTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    // ── Nothing linked: nothing moves ───────────────────────────────────────

    /// <summary>
    /// AC #3, and the claim the stage is safe on. The rows the bucket query
    /// returns are compared field by field, not merely counted.
    /// </summary>
    [Fact]
    public async Task Linking_moves_no_count_and_no_bucket()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var before = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        var bucketsBefore = library.Buckets.ToDictionary(b => b.Key, b => b.Count);
        var allBefore = library.AllGames.Count;
        var storesBefore = library.TitlesByStore();

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        await library.LoadCommand.ExecuteAsync(null);
        var after = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);

        // Same rows, same buckets, same playtime, same order. Only the two work
        // columns differ, and only on the child.
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].OwnershipId, after[i].OwnershipId);
            Assert.Equal(before[i].ReleaseId, after[i].ReleaseId);
            Assert.Equal(before[i].Bucket, after[i].Bucket);
            Assert.Equal(before[i].PlaytimeMinutes, after[i].PlaytimeMinutes);
            Assert.Equal(before[i].LastPlayedAt, after[i].LastPlayedAt);
            Assert.Equal(before[i].WorkId, after[i].WorkId);
        }

        Assert.Equal(bucketsBefore, library.Buckets.ToDictionary(b => b.Key, b => b.Count));
        Assert.Equal(allBefore, library.AllGames.Count);
        Assert.Equal(storesBefore, library.TitlesByStore());
        Assert.Equal(3, library.AllGames.Count);
    }

    /// <summary>
    /// With nothing linked, every row resolves to itself. This is what makes
    /// the byte-identity claim above structural rather than lucky.
    /// </summary>
    [Fact]
    public async Task With_nothing_linked_every_work_resolves_to_itself()
    {
        using var fixture = new ReadModelFixture();
        await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);

        var rows = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);

        Assert.All(rows, row => Assert.Equal(row.WorkId, row.ResolvedWorkId));
        Assert.All(rows, row => Assert.False(row.IsLinkedChild));
    }

    /// <summary>
    /// One game where there were two. The grid grain does not change until
    /// TASK-70.6, so this is the fact underneath rather than the tile count:
    /// the rows the whole read model is keyed on now resolve to one identity
    /// where they resolved to two.
    /// </summary>
    [Fact]
    public async Task A_linked_group_resolves_to_one_game_where_it_resolved_to_two()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");

        var before = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(2, before.Select(r => r.ResolvedWorkId).Distinct().Count());

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var after = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(2, after.Count);
        Assert.Single(after.Select(r => r.ResolvedWorkId).Distinct());
        Assert.Equal(steam.WorkId, after.Select(r => r.ResolvedWorkId).Distinct().Single());

        // Both rows still exist, and the child still knows which work it is.
        Assert.Contains(after, r => r.WorkId == epic.WorkId && r.IsLinkedChild);
        Assert.Contains(after, r => r.WorkId == steam.WorkId && !r.IsLinkedChild);
    }

    // ── The visible half of the fix ─────────────────────────────────────────

    /// <summary>
    /// AC #2. Two tiles remain, because the grid is still one tile per
    /// ownership until TASK-70.6, but both read as one game.
    /// </summary>
    [Fact]
    public async Task Both_store_entries_of_a_linked_game_take_the_primary_title_and_cover()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey (2017)", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        // Before: two names, two covers.
        Assert.Equal(["Prey", "Prey (2017)"], library.VisibleTiles.Select(t => t.Title).Order());
        var epicTileBefore = library.VisibleTiles.Single(t => t.Store == "epic");
        var steamTileBefore = library.VisibleTiles.Single(t => t.Store == "steam");
        Assert.NotEqual(steamTileBefore.CoverKey, epicTileBefore.CoverKey);

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, library.VisibleTiles.Count);
        Assert.All(library.VisibleTiles, tile => Assert.Equal("Prey", tile.Title));

        var epicTile = library.VisibleTiles.Single(t => t.Store == "epic");
        var steamTile = library.VisibleTiles.Single(t => t.Store == "steam");
        Assert.Equal(steamTile.CoverKey, epicTile.CoverKey);

        // Each entry keeps its own playtime: the grid grain has not changed.
        Assert.Equal(300, steamTile.PlaytimeMinutes);
        Assert.Equal(90, epicTile.PlaytimeMinutes);
    }

    // ── Coverage on the details modal ───────────────────────────────────────

    /// <summary>
    /// AC #5 and the user's fourth complaint. Each covered title carries its
    /// own store, its own playtime and its own last-played.
    /// </summary>
    [Fact]
    public async Task The_modal_lists_the_titles_this_game_covers_with_their_own_figures()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey Deluxe", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var tile = library.VisibleTiles.Single(t => t.Store == "steam");
        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var coverage = library.Details!.Coverage!;
        Assert.True(library.Details.ShowCoverage);
        Assert.True(coverage.HasCoverage);

        var covered = Assert.Single(coverage.Rows, r => r.IsCovered);
        Assert.Equal("Prey Deluxe", covered.Title);
        Assert.Equal("EPIC", covered.StoreBadge);
        Assert.Equal("1h", covered.PlaytimeText);

        var own = Assert.Single(coverage.Rows, r => !r.IsCovered);
        Assert.Equal("Prey", own.Title);
        Assert.Equal("STEAM", own.StoreBadge);
        Assert.Equal("5h", own.PlaytimeText);

        // The per-store breakdown is on screen beside the composite, which is
        // what lets the user check the sum.
        Assert.Equal(2, coverage.Rows.Count);
    }

    /// <summary>
    /// The user's decision of 2026-08-31, and the F10 hazard. The sum is
    /// Winnow's own composite and carries its own coherent date. The
    /// higher-playtime entry here is the one with the OLDER date, so a
    /// composite that borrowed a date from a store would show the wrong one.
    /// </summary>
    [Fact]
    public async Task The_summed_playtime_never_pairs_with_a_foreign_last_played()
    {
        using var fixture = new ReadModelFixture();
        var older = Now.AddDays(-400);
        var newer = Now.AddDays(-3);

        var steam = await fixture.SeedAsync("Prey", minutes: 3_000, lastPlayed: older);
        var epic = await fixture.SeedAsync("Prey", minutes: 60, lastPlayed: newer, store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var entries = await fixture.CoverageEntriesAsync();
        var coverage = IdentityCoverage.For(steam.WorkId, await fixture.ResolutionAsync(), entries);

        Assert.Equal(3_060, coverage.Total.PlaytimeMinutes);
        Assert.True(coverage.Total.IsComposite);

        // The latest date across the SAME entries the minutes were summed over —
        // not the date belonging to the store that contributed most of them.
        Assert.Equal(newer, coverage.Total.LastPlayedAt);
        Assert.NotEqual(older, coverage.Total.LastPlayedAt);

        // And every per-entry row still holds its own pair, uncrossed.
        var steamEntry = Assert.Single(coverage.OwnEntries);
        Assert.Equal(3_000, steamEntry.PlaytimeMinutes);
        Assert.Equal(older, steamEntry.LastPlayedAt);

        var epicEntry = Assert.Single(coverage.CoveredEntries);
        Assert.Equal(60, epicEntry.PlaytimeMinutes);
        Assert.Equal(newer, epicEntry.LastPlayedAt);
    }

    /// <summary>
    /// §6.2 rendered literally for the first time. Two releases, two rows,
    /// two percentages, and nothing that averages them.
    /// </summary>
    [Fact]
    public async Task The_modal_shows_per_release_achievement_rows_and_never_a_blended_percentage()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        // 100% on one platform and 30% on another: two facts, not one average.
        await fixture.SeedAchievementsAsync(steam.ReleaseId, total: 10, unlocked: 10);
        await fixture.SeedAchievementsAsync(epic.ReleaseId, total: 10, unlocked: 3);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(
            library.VisibleTiles.Single(t => t.Store == "steam"));

        var rows = library.Details!.Coverage!.Rows;
        Assert.Equal(2, rows.Count);

        var steamRow = rows.Single(r => r.StoreBadge == "STEAM").Achievements!;
        var epicRow = rows.Single(r => r.StoreBadge == "EPIC").Achievements!;

        Assert.Equal("10/10", steamRow.CountText);
        Assert.Equal("100%", steamRow.PercentText);
        Assert.Equal("3/10", epicRow.CountText);
        Assert.Equal("30%", epicRow.PercentText);

        // The blend §6.2 forbids would be 65%. Nothing on this screen says it.
        Assert.DoesNotContain("65", steamRow.PercentText, StringComparison.Ordinal);
        Assert.DoesNotContain("65", epicRow.PercentText, StringComparison.Ordinal);
        Assert.NotEqual(steamRow.PercentText, epicRow.PercentText);
    }

    /// <summary>
    /// A release with no achievements renders no row rather than zero of
    /// zero.
    /// </summary>
    [Fact]
    public async Task A_release_with_no_achievements_carries_no_achievement_row()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        await fixture.SeedAchievementsAsync(steam.ReleaseId, total: 4, unlocked: 1);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(
            library.VisibleTiles.Single(t => t.Store == "steam"));

        var rows = library.Details!.Coverage!.Rows;
        Assert.True(rows.Single(r => r.StoreBadge == "STEAM").HasAchievements);
        Assert.False(rows.Single(r => r.StoreBadge == "EPIC").HasAchievements);
    }

    /// <summary>
    /// A game that covers nothing shows what it always showed.
    /// </summary>
    [Fact]
    public async Task A_game_that_covers_nothing_draws_no_coverage_section()
    {
        using var fixture = new ReadModelFixture();
        await fixture.SeedAsync("Dishonored", minutes: 40, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        Assert.False(library.Details!.ShowCoverage);
        Assert.False(library.Details.Coverage!.HasCoverage);
    }

    // ── Separate this ───────────────────────────────────────────────────────

    /// <summary>AC #6. One link goes; the rest of the act stands.</summary>
    [Fact]
    public async Task Separate_retracts_one_link_and_leaves_the_rest_of_the_act()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey Epic", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        var gog = await fixture.SeedAsync("Prey GOG", minutes: 20, lastPlayed: Now.AddDays(-50), store: "gog");

        await fixture.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steam.WorkId,
            ChildWorkIds = [epic.WorkId, gog.WorkId],
        });

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(
            library.VisibleTiles.Single(t => t.Store == "steam"));

        var coverage = library.Details!.Coverage!;
        Assert.Equal(2, coverage.Rows.Count(r => r.IsCovered));

        var epicRow = coverage.Rows.Single(r => r.StoreBadge == "EPIC");
        await coverage.SeparateCommand.ExecuteAsync(epicRow);

        // The Epic title is its own game again; the GOG one is still covered.
        var resolution = await fixture.ResolutionAsync();
        Assert.Equal(epic.WorkId, resolution.Resolve(epic.WorkId));
        Assert.Equal(steam.WorkId, resolution.Resolve(gog.WorkId));

        // And the modal, reopened on the same entry, says so.
        var after = library.Details!.Coverage!;
        var stillCovered = Assert.Single(after.Rows, r => r.IsCovered);
        Assert.Equal("GOG", stillCovered.StoreBadge);

        // Nothing was deleted: the separated title is back in the library under
        // its own name.
        Assert.Contains(library.VisibleTiles, t => t.Title == "Prey Epic");
    }

    /// <summary>
    /// AC #5 of TASK-70 at this stage's grain: separate and link again, any
    /// number of times, with no terminal state.
    /// </summary>
    [Fact]
    public async Task Separate_and_link_again_is_repeatable()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey Epic", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");

        for (var round = 0; round < 4; round++)
        {
            await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
            Assert.Equal(steam.WorkId, (await fixture.ResolutionAsync()).Resolve(epic.WorkId));

            Assert.True(await fixture.Links.RetractLinkAsync(epic.WorkId));
            Assert.Equal(epic.WorkId, (await fixture.ResolutionAsync()).Resolve(epic.WorkId));

            // Retracting again is a no-op, not a refusal.
            Assert.False(await fixture.Links.RetractLinkAsync(epic.WorkId));
        }
    }

    // ── Do not resolve ──────────────────────────────────────────────────────

    /// <summary>
    /// The reason the enrichment targets are on the other list. A linked
    /// child keeps being enriched, and its own igdb_id is what fills the
    /// group.
    /// </summary>
    [Fact]
    public async Task Enrichment_still_targets_a_linked_child()
    {
        using var fixture = new ReadModelFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var works = await fixture.Works.GetAllAsync();
        Assert.Contains(works, w => w.Id == epic.WorkId);

        var targets = await fixture.Works.GetEnrichmentTargetsAsync();
        Assert.Contains(targets, t => t.WorkId == epic.WorkId);

        var facetTargets = await fixture.Queries.GetFacetTargetsAsync();
        Assert.Contains(facetTargets, t => t.WorkId == epic.WorkId);
        Assert.Contains(facetTargets, t => t.ReleaseId == epic.ReleaseId);
    }

    /// <summary>
    /// The user's decision that expansions are titles whose playtime does
    /// not roll up, held by a test before the stage that creates them
    /// exists. The kind filter in the bucket query is what enforces it.
    /// </summary>
    [Fact]
    public async Task An_expansion_link_moves_nothing()
    {
        using var fixture = new ReadModelFixture();
        var civ = await fixture.SeedAsync("Civilization IV", minutes: 12_000, lastPlayed: Now.AddDays(-900));
        var bts = await fixture.SeedAsync("Beyond the Sword", minutes: 0, lastPlayed: null);

        var before = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);

        await fixture.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = civ.WorkId,
            ChildWorkIds = [bts.WorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
        });

        var after = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);

        Assert.Equal(before.Count, after.Count);
        Assert.All(after, row => Assert.Equal(row.WorkId, row.ResolvedWorkId));

        // The expansion is still its own title, still never opened, still the
        // best recommendation the app can make about a played-out parent.
        var expansion = after.Single(r => r.ReleaseId == bts.ReleaseId);
        Assert.Equal(LibraryBuckets.NeverPlayed, expansion.Bucket);
        Assert.Equal(0, expansion.PlaytimeMinutes);

        // And it cannot reach the coverage sum either: the same-game resolver
        // does not know about it.
        var coverage = IdentityCoverage.For(
            civ.WorkId, await fixture.ResolutionAsync(), await fixture.CoverageEntriesAsync());
        Assert.False(coverage.HasCoverage);
        Assert.Equal(12_000, coverage.Total.PlaytimeMinutes);
    }

    // ── Load ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The design flagged the resolved join as something to MEASURE rather
    /// than assume, on the grounds that a materially slower load would
    /// change the recommendation. Seeds a library larger than the author's
    /// measured one, links a fifth of it, and requires the resolved query to
    /// stay well inside a budget a cartesian blow-up or an accidental
    /// per-row read could not. The ceiling is deliberately loose — this test
    /// catches a shape change, not milliseconds.
    /// </summary>
    [Fact]
    public async Task The_resolved_join_stays_cheap_on_a_realistic_library()
    {
        using var fixture = new ReadModelFixture();

        var works = new List<SeededEntry>(1_200);
        for (var i = 0; i < 1_000; i++)
        {
            works.Add(await fixture.SeedAsync(
                $"Game {i:D4}",
                minutes: i % 400,
                lastPlayed: i % 3 == 0 ? null : Now.AddDays(-(i % 900)),
                store: i % 5 == 0 ? "epic" : "steam"));
        }

        // Warm first, measure second: the first query of a session pays for the
        // connection, the page cache and Dapper's mapper, none of which the join
        // is being measured for.
        await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);

        var unlinked = Stopwatch.StartNew();
        var before = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        unlinked.Stop();

        // 200 links: a fifth of the library is a cross-store duplicate, which is
        // far past anything the author's own library holds.
        for (var i = 0; i + 1 < 400; i += 2)
        {
            await fixture.LinkAsync(parent: works[i].WorkId, child: works[i + 1].WorkId);
        }

        await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);

        var linked = Stopwatch.StartNew();
        var after = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        linked.Stop();

        // The join multiplies nothing: same rows in, same rows out.
        Assert.Equal(1_000, before.Count);
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(200, after.Count(r => r.IsLinkedChild));

        Assert.True(
            linked.ElapsedMilliseconds < 2_000,
            $"The resolved bucket query took {linked.ElapsedMilliseconds}ms over 1,000 games with "
            + $"200 links (unresolved: {unlinked.ElapsedMilliseconds}ms).");
    }

    // ── Fixture ─────────────────────────────────────────────────────────────

    private sealed record SeededEntry(long WorkId, long ReleaseId, long OwnershipId);

    private sealed class ReadModelFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 500_000;

        public ReadModelFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Ownerships = new OwnershipRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
            Queries = new LibraryQueryRepository(_db.Factory);
            Links = new IdentityLinkRepository(_db.Factory);
            Achievements = new AchievementQueryRepository(_db.Factory);
        }

        public WorkRepository Works { get; }

        public ReleaseRepository Releases { get; }

        public OwnershipRepository Ownerships { get; }

        public PlayRecordRepository Plays { get; }

        public UpdateEventRepository Updates { get; }

        public LibraryQueryRepository Queries { get; }

        public IIdentityLinkRepository Links { get; }

        public IAchievementQueryRepository Achievements { get; }

        public void Dispose() => _db.Dispose();

        public LibraryViewModel CreateViewModel()
            => new(
                Queries, Ownerships, Releases, Works, Updates,
                covers: null,
                identityLinks: Links,
                achievements: Achievements);

        public Task LinkAsync(long parent, long child)
            => Links.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = parent,
                ChildWorkIds = [child],
            });

        public async Task<SameGameResolution> ResolutionAsync()
            => (await Links.GetResolutionAsync()).SameGame;

        /// <summary>
        /// The entries the library would show, in the shape coverage is
        /// derived from.
        /// </summary>
        public async Task<IReadOnlyList<CoverageEntry>> CoverageEntriesAsync()
        {
            var rows = await Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
            var ownerships = (await Ownerships.GetAllAsync()).ToDictionary(o => o.Id);
            var works = (await Works.GetAllAsync()).ToDictionary(w => w.Id);

            return rows
                .Select(row => new CoverageEntry
                {
                    OwnershipId = row.OwnershipId,
                    ReleaseId = row.ReleaseId,
                    WorkId = row.WorkId,
                    Title = works[row.WorkId].Name,
                    Store = ownerships[row.OwnershipId].Store,
                    PlaytimeMinutes = row.PlaytimeMinutes,
                    LastPlayedAt = row.LastPlayedAt,
                })
                .ToList();
        }

        public async Task<SeededEntry> SeedAsync(
            string title, long minutes, DateTime? lastPlayed, string store = "steam")
        {
            var workId = await Works.InsertAsync(new Work { Name = title, FirstReleaseYear = 2017 });
            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = title,
                Platform = "windows",
            });

            // A Steam appid per release, so each has its own cover key and the
            // borrowed one is visibly a different key rather than two nulls.
            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
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
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_localconfig",
                ObservedAt = Now,
            });

            return new SeededEntry(workId, releaseId, ownershipId);
        }

        /// <summary>
        /// Nothing ingests achievements yet, so the rows are written straight
        /// into the §6.2 tables the reader reads.
        /// </summary>
        public async Task SeedAchievementsAsync(long releaseId, int total, int unlocked)
        {
            using var lease = _db.Factory.Lease();
            for (var i = 0; i < total; i++)
            {
                var key = $"ach_{releaseId}_{i}";
                await Dapper.SqlMapper.ExecuteAsync(lease.Connection, """
                    INSERT INTO achievements (release_id, provider_key, name, description, hidden)
                    VALUES (@releaseId, @key, @name, NULL, 0);
                    """, new { releaseId, key, name = $"Achievement {i}" }, lease.Transaction);

                if (i < unlocked)
                {
                    await Dapper.SqlMapper.ExecuteAsync(lease.Connection, """
                        INSERT INTO achievement_unlocks (release_id, provider_key, unlocked_at)
                        VALUES (@releaseId, @key, @at);
                        """,
                        new { releaseId, key, at = Now.AddDays(-10) },
                        lease.Transaction);
                }
            }
        }
    }
}
