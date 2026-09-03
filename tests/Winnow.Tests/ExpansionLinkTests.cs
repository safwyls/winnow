using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The expansion relation end to end: the scan that
/// proposes it, the answers that record it, and the one claim the whole stage
/// exists to keep: GROUPING AN EXPANSION MOVES NO NUMBER.
///
/// <para>The user's decision of 2026-08-31 is that an expansion counts as a
/// title in the library and its playtime does not roll up. Civilization IV's
/// hours stay Civilization IV's, and an unplayed pack of a played-out base
/// game stays exactly the recommendation the app most wants to make. So the
/// central test here compares every number on screen before and after a
/// grouping and requires all of them to be identical.</para>
/// </summary>
public sealed class ExpansionLinkTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── The claim ───────────────────────────────────────────────────────────

    /// <summary>
    /// Every figure the user can see, before and after. The library count, the
    /// bucket counts, both games' own playtime, the number of tiles on the
    /// grid, and the per-store title counts. A same-game link legitimately
    /// moves several of these (TASK-70.6 collapses a tile); an expansion link
    /// must move none of them.
    /// </summary>
    [Fact]
    public async Task An_expansion_link_moves_no_number_anywhere()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync(
            "Sid Meier's Civilization IV", minutes: 12_000, lastPlayed: Now.AddDays(-900));
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword",
            minutes: 0,
            lastPlayed: null,
            year: 2007);

        var before = await fixture.LoadAsync();
        var beforeSnapshot = Snapshot(before);

        await fixture.GroupAsync(civ.WorkId, bts.WorkId);

        var after = await fixture.LoadAsync();
        Assert.Equal(beforeSnapshot, Snapshot(after));

        // And stated positively as well as by equality, because an assertion
        // that two identical bugs agree is not an assertion about anything.
        Assert.Equal(2, after.VisibleTiles.Count);
        Assert.Equal(2, after.AllGames.Count);

        var pack = after.VisibleTiles.Single(t => t.Title.EndsWith("Sword", StringComparison.Ordinal));
        Assert.Equal(0, pack.PlaytimeMinutes);
        Assert.Equal(LibraryBuckets.NeverPlayed, pack.Bucket);

        var baseGame = after.VisibleTiles.Single(t => t.Title.EndsWith("IV", StringComparison.Ordinal));
        Assert.Equal(12_000, baseGame.PlaytimeMinutes);

        // The read model agrees with the grid: the link is live, and the
        // bucket query still resolves every row to itself, because it filters
        // on same_game and this link is not one.
        var rows = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.All(rows, row => Assert.Equal(row.WorkId, row.ResolvedWorkId));

        var resolution = await fixture.Links.GetResolutionAsync();
        Assert.Equal(civ.WorkId, resolution.Expansions.BaseOf(bts.WorkId));
        Assert.True(resolution.SameGame.IsEmpty);
    }

    /// <summary>
    /// The other half of the same claim, stated where a reader would look for
    /// it: the coverage section, which DOES sum, cannot see an expansion at
    /// all. <c>IdentityCoverage.For</c> takes a <c>SameGameResolution</c>, and
    /// <c>ExpansionGrouping</c> has no resolver to hand it, so the omission is
    /// a fact about the types rather than about this test.
    /// </summary>
    [Fact]
    public async Task An_expansion_never_enters_the_coverage_sum()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync(
            "Sid Meier's Civilization IV", minutes: 12_000, lastPlayed: Now.AddDays(-900));
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword",
            minutes: 4_000,
            lastPlayed: Now.AddDays(-10),
            year: 2007);

        await fixture.GroupAsync(civ.WorkId, bts.WorkId);

        var library = await fixture.LoadAsync();
        var tile = library.VisibleTiles.Single(t => t.Title.EndsWith("IV", StringComparison.Ordinal));
        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var details = library.Details;
        Assert.NotNull(details);

        // ALSO COVERS is not drawn: this game covers nothing.
        Assert.False(details.ShowCoverage);

        // EXPANSIONS is, with the pack's OWN hours on it and no total anywhere.
        Assert.True(details.ShowExpansions);
        var row = Assert.Single(details.Expansions!.Expansions);
        Assert.Equal(4_000, row.PlaytimeMinutes);
        Assert.Equal(bts.WorkId, row.WorkId);

        // The base game's own headline is untouched by the pack's 4,000.
        Assert.Equal(12_000, tile.PlaytimeMinutes);
    }

    /// <summary>
    /// The relation reads from both ends: the pack's own modal names what it
    /// extends, and the two sections are never merged into one list.
    /// </summary>
    [Fact]
    public async Task The_pack_says_what_it_extends_and_the_two_sections_stay_apart()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync(
            "Sid Meier's Civilization IV", minutes: 12_000, lastPlayed: Now.AddDays(-900));
        var civGog = await fixture.SeedAsync(
            "Sid Meier's Civilization IV", minutes: 60, lastPlayed: Now.AddDays(-5), store: "gog");
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword",
            minutes: 0,
            lastPlayed: null,
            year: 2007);

        await fixture.LinkAsync(civ.WorkId, civGog.WorkId);
        await fixture.GroupAsync(civ.WorkId, bts.WorkId);

        var library = await fixture.LoadAsync();

        // The base game shows BOTH sections, and they hold different things:
        // one covered store entry, one expansion.
        var baseTile = library.VisibleTiles.Single(
            t => t.Title.EndsWith("IV", StringComparison.Ordinal));
        await library.OpenDetailsCommand.ExecuteAsync(baseTile);

        Assert.True(library.Details!.ShowCoverage);
        Assert.True(library.Details.ShowExpansions);
        Assert.Single(library.Details.Coverage!.Rows, r => r.IsCovered);
        Assert.Single(library.Details.Expansions!.Expansions);
        Assert.False(library.Details.ShowExtends);

        // The composite is over the two Civilization IV entries only: 12,000
        // plus 60, never the pack's hours.
        Assert.Equal(12_060, baseTile.PlaytimeMinutes);
        Assert.Equal(2, library.Details.Coverage.Rows.Count);

        // The pack's own modal names the base and offers no expansions of its
        // own.
        var packTile = library.VisibleTiles.Single(
            t => t.Title.EndsWith("Sword", StringComparison.Ordinal));
        await library.OpenDetailsCommand.ExecuteAsync(packTile);

        Assert.True(library.Details!.ShowExtends);
        Assert.False(library.Details.ShowExpansions);
        Assert.Equal("Sid Meier's Civilization IV", library.Details.Expansions!.Extends!.Title);

        // The link the row retracts is the PACK's, not the base game's,
        // whichever end the row is drawn at.
        Assert.Equal(bts.WorkId, library.Details.Expansions.Extends!.ChildWorkId);
    }

    // ── The scan ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_scan_proposes_the_base_and_its_packs_and_nothing_else()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync("Sid Meier's Civilization IV", 0, null);
        var warlords = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Warlords", 0, null, year: 2006);
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 0, null, year: 2007);

        // Two unrelated games that share a first token, with nothing else in
        // common. Neither may be proposed.
        await fixture.SeedAsync("Rush", 0, null, year: null, publisher: null);
        await fixture.SeedAsync("Rush Bros", 0, null, year: null, publisher: null);

        var report = await fixture.Scan.ScanAsync();

        var group = Assert.Single(report.Groups);
        Assert.Equal(civ.WorkId, group.Base.WorkId);
        Assert.Equal([warlords.WorkId, bts.WorkId], group.Members.Select(m => m.Work.WorkId).Order());
    }

    /// <summary>
    /// A refusal is a decision, so it is stored, and the question is never
    /// asked again. The proposals themselves are not stored — they are
    /// re-derived on every scan, for the reason §6.1 gives about buckets.
    /// </summary>
    [Fact]
    public async Task A_refused_pair_never_comes_back()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync("Sid Meier's Civilization IV", 0, null);
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 0, null, year: 2007);

        Assert.Single((await fixture.Scan.ScanAsync()).Groups);

        await fixture.Refusals.RefuseAsync([new ExpansionRefusalRequest(civ.WorkId, bts.WorkId)]);
        Assert.Empty((await fixture.Scan.ScanAsync()).Groups);

        // Idempotent: the same answer twice is one row, not a constraint
        // violation.
        await fixture.Refusals.RefuseAsync([new ExpansionRefusalRequest(civ.WorkId, bts.WorkId)]);
        Assert.Single(await fixture.Refusals.GetAllAsync());
    }

    /// <summary>A pair already grouped is an answered question and is not re-asked.</summary>
    [Fact]
    public async Task A_grouped_pair_is_not_proposed_again()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync("Sid Meier's Civilization IV", 0, null);
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 0, null, year: 2007);

        await fixture.GroupAsync(civ.WorkId, bts.WorkId);
        Assert.Empty((await fixture.Scan.ScanAsync()).Groups);
    }

    // ── Retraction ──────────────────────────────────────────────────────────

    /// <summary>
    /// The property the whole rework exists for, at this relation. Group,
    /// ungroup, group again, four times over, and the state after each cycle
    /// is identical to the state after the first — including the proposal
    /// coming back every time it is ungrouped, with no terminal status
    /// anywhere.
    /// </summary>
    [Fact]
    public async Task Group_ungroup_and_group_again_ends_where_grouping_once_ends()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync("Sid Meier's Civilization IV", 0, null);
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 0, null, year: 2007);

        for (var cycle = 0; cycle < 4; cycle++)
        {
            var actId = await fixture.GroupAsync(civ.WorkId, bts.WorkId);

            var grouped = await fixture.Links.GetResolutionAsync();
            Assert.Equal(civ.WorkId, grouped.Expansions.BaseOf(bts.WorkId));
            Assert.Empty((await fixture.Scan.ScanAsync()).Groups);

            Assert.True(await fixture.Links.RetractActAsync(actId));

            var ungrouped = await fixture.Links.GetResolutionAsync();
            Assert.Null(ungrouped.Expansions.BaseOf(bts.WorkId));
            Assert.Single((await fixture.Scan.ScanAsync()).Groups);

            // Retracting twice is a no-op, not an error.
            Assert.False(await fixture.Links.RetractActAsync(actId));
        }
    }

    /// <summary>
    /// The details modal's Ungroup goes through the one-child retraction, so a
    /// group of six can be broken one pack at a time from the place the user
    /// noticed it, leaving the rest standing.
    /// </summary>
    [Fact]
    public async Task Ungrouping_one_pack_leaves_its_siblings_grouped()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync("Sid Meier's Civilization IV", 0, null);
        var warlords = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Warlords", 0, null, year: 2006);
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 0, null, year: 2007);

        await fixture.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = civ.WorkId,
            ChildWorkIds = [warlords.WorkId, bts.WorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
        });

        Assert.True(await fixture.Links.RetractLinkAsync(bts.WorkId));

        var resolution = await fixture.Links.GetResolutionAsync();
        Assert.Null(resolution.Expansions.BaseOf(bts.WorkId));
        Assert.Equal(civ.WorkId, resolution.Expansions.BaseOf(warlords.WorkId));
    }

    // ── The two writes that would have moved a number ───────────────────────

    /// <summary>
    /// The defect this stage found. Depth one re-parents the children of a
    /// work that is becoming a child, and the repository wrote them under the
    /// REQUEST's kind. With only same-game links live that was invisible; the
    /// moment expansions exist it silently converts one into an identity and
    /// folds a playtime. A displaced link now keeps its own kind.
    /// </summary>
    [Fact]
    public async Task Re_parenting_a_base_game_keeps_its_expansions_expansions()
    {
        using var fixture = new ExpansionFixture();
        var steam = await fixture.SeedAsync(
            "Sid Meier's Civilization IV", minutes: 12_000, lastPlayed: Now.AddDays(-900));
        var gog = await fixture.SeedAsync(
            "Sid Meier's Civilization IV", minutes: 60, lastPlayed: Now.AddDays(-5), store: "gog");
        var bts = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword",
            minutes: 4_000,
            lastPlayed: Now.AddDays(-10),
            year: 2007);

        await fixture.GroupAsync(steam.WorkId, bts.WorkId);

        // Now hold the two Civilization IV entries as one game, keeping the
        // GOG row, which makes the Steam row a child and re-parents the pack.
        await fixture.LinkAsync(gog.WorkId, steam.WorkId);

        var resolution = await fixture.Links.GetResolutionAsync();
        Assert.Equal(gog.WorkId, resolution.Expansions.BaseOf(bts.WorkId));
        Assert.False(resolution.SameGame.IsChild(bts.WorkId));

        // And the numbers say the same thing: two tiles, and the pack's 4,000
        // minutes did not join the base game's 12,060.
        var library = await fixture.LoadAsync();
        Assert.Equal(2, library.VisibleTiles.Count);
        Assert.Equal(
            12_060,
            library.VisibleTiles.Single(t => t.Title.EndsWith("IV", StringComparison.Ordinal))
                .PlaytimeMinutes);
        Assert.Equal(
            4_000,
            library.VisibleTiles.Single(t => t.Title.EndsWith("Sword", StringComparison.Ordinal))
                .PlaytimeMinutes);
    }

    /// <summary>
    /// The other side of the same hazard, refused rather than repaired. A pack
    /// that is itself held as one game across two stores cannot be grouped,
    /// because depth one would re-parent its SAME-GAME twin onto the base game
    /// and fold that twin's playtime into it.
    /// </summary>
    [Fact]
    public async Task A_pack_that_is_already_a_parent_is_refused()
    {
        using var fixture = new ExpansionFixture();
        var civ = await fixture.SeedAsync("Sid Meier's Civilization IV", 0, null);
        var btsSteam = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 0, null, year: 2007);
        var btsGog = await fixture.SeedAsync(
            "Sid Meier's Civilization IV: Beyond the Sword", 900, Now.AddDays(-3),
            year: 2007, store: "gog");

        await fixture.LinkAsync(btsSteam.WorkId, btsGog.WorkId);

        var thrown = await Assert.ThrowsAsync<IdentityLinkRefusedException>(
            () => fixture.GroupAsync(civ.WorkId, btsSteam.WorkId));

        Assert.Equal(IdentityLinkRefusal.ExpansionChildIsAlreadyAParent, thrown.Refusal);

        // Nothing was written, so nothing moved.
        var resolution = await fixture.Links.GetResolutionAsync();
        Assert.True(resolution.Expansions.IsEmpty);

        // And the scan does not offer the question either, so the refusal is
        // a backstop rather than the user's experience.
        Assert.Empty((await fixture.Scan.ScanAsync()).Groups);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every number a user can read off the library screen, as one comparable
    /// value. Compared whole rather than field by field so a figure added
    /// later is covered without anyone remembering to add it here.
    /// </summary>
    private static string Snapshot(LibraryViewModel library)
    {
        var parts = new List<string>
        {
            $"tiles={library.VisibleTiles.Count}",
            $"all={library.AllGames.Count}",
            $"total={library.TotalCount}",
        };

        foreach (var bucket in library.Buckets)
        {
            parts.Add($"bucket:{bucket.Key}={bucket.Count}");
        }

        foreach (var (store, count) in library.TitlesByStore().OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            parts.Add($"store:{store}={count}");
        }

        foreach (var tile in library.VisibleTiles.OrderBy(t => t.Title, StringComparer.Ordinal))
        {
            parts.Add($"game:{tile.Title}={tile.PlaytimeMinutes}:{tile.Bucket}:{tile.LastPlayedUtc:O}");
        }

        return string.Join('\n', parts);
    }

    private sealed record SeededEntry(long WorkId, long ReleaseId, long OwnershipId);

    private sealed class ExpansionFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 800_000;

        public ExpansionFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Ownerships = new OwnershipRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
            Queries = new LibraryQueryRepository(_db.Factory);
            Links = new IdentityLinkRepository(_db.Factory);
            Refusals = new ExpansionRefusalRepository(_db.Factory);
            Scan = new LibraryExpansionScan(Releases, Links, Refusals);
        }

        public WorkRepository Works { get; }

        public ReleaseRepository Releases { get; }

        public OwnershipRepository Ownerships { get; }

        public PlayRecordRepository Plays { get; }

        public UpdateEventRepository Updates { get; }

        public LibraryQueryRepository Queries { get; }

        public IIdentityLinkRepository Links { get; }

        public IExpansionRefusalRepository Refusals { get; }

        public LibraryExpansionScan Scan { get; }

        public void Dispose() => _db.Dispose();

        public async Task<LibraryViewModel> LoadAsync()
        {
            var library = new LibraryViewModel(
                Queries, Ownerships, Releases, Works, Updates,
                covers: null,
                identityLinks: Links);

            await library.LoadCommand.ExecuteAsync(null);
            return library;
        }

        public Task<long> GroupAsync(long baseWorkId, long childWorkId)
            => Links.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = baseWorkId,
                ChildWorkIds = [childWorkId],
                Kind = IdentityLinkKinds.ExpansionOf,
            });

        public Task<long> LinkAsync(long parentWorkId, long childWorkId)
            => Links.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = parentWorkId,
                ChildWorkIds = [childWorkId],
                Kind = IdentityLinkKinds.SameGame,
            });

        public async Task<SeededEntry> SeedAsync(
            string title,
            long minutes,
            DateTime? lastPlayed,
            int? year = 2005,
            string? publisher = "2K Games",
            string store = "steam")
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = title,
                FirstReleaseYear = year,
                Publisher = publisher,
            });

            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = title,
                Platform = "windows",
            });

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
    }
}
