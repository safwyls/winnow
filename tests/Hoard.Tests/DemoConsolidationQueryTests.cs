using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// Demo consolidation as the library view actually meets it: through
/// <see cref="LibraryQueryRepository.GetOwnershipBucketsAsync"/>, which is where
/// tiles come from. The view needs no knowledge of demos — a consolidated demo
/// simply has no row.
///
/// <para>Every assertion here is also an assertion that nothing was destroyed:
/// the suppressed demo's ownership and play record are re-read from the
/// repositories afterwards and are expected to be exactly as inserted.</para>
/// </summary>
public class DemoConsolidationQueryTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;

    public DemoConsolidationQueryTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Seeded> SeedAsync(
        string title,
        long playtimeMinutes,
        int? year = null,
        bool provisional = false,
        string? appType = null)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = title,
            FirstReleaseYear = year,
            NameIsProvisional = provisional,
            SteamAppType = appType,
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
            Store = "steam",
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = playtimeMinutes,
            LastPlayedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = "steam_local",
            ObservedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        return new Seeded(workId, releaseId, ownershipId);
    }

    /// <summary>
    /// Consolidation only. The non-game filter is left OFF throughout this
    /// class so its rows are the consolidation decision and nothing else —
    /// <see cref="NonGameFilterTests"/> owns the interaction between the two,
    /// and asserts there that consolidation answers identically either way.
    /// </summary>
    private Task<IReadOnlyList<OwnershipBucket>> QueryAsync()
        => new LibraryQueryRepository(_db.Factory).GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = true });

    [Fact]
    public async Task Demo_beside_its_base_game_yields_one_entry_and_no_merged_playtime()
    {
        var full = await SeedAsync("Bastion", playtimeMinutes: 900);
        var demo = await SeedAsync("Bastion Demo", playtimeMinutes: 42);

        var rows = await QueryAsync();

        var row = Assert.Single(rows);
        Assert.Equal(full.OwnershipId, row.OwnershipId);

        // The base game's own minutes, untouched — not 942. §6.2: two appids
        // are two facts, never one sum.
        Assert.Equal(900, row.PlaytimeMinutes);
        Assert.Equal(1, row.ConsolidatedDemoCount);

        // Nothing was destroyed: the demo's ownership and its 42 minutes are
        // still stored and still queryable.
        Assert.NotNull(await _ownerships.GetAsync(demo.OwnershipId));
        var record = await _plays.GetLatestAsync(demo.OwnershipId);
        Assert.NotNull(record);
        Assert.Equal(42, record.PlaytimeMinutes);
    }

    [Fact]
    public async Task A_solitary_demo_is_a_normal_entry()
    {
        var demo = await SeedAsync("Hellpoint Demo", playtimeMinutes: 30);
        await SeedAsync("Portal 2", playtimeMinutes: 600);

        var rows = await QueryAsync();

        Assert.Equal(2, rows.Count);
        var row = Assert.Single(rows, r => r.OwnershipId == demo.OwnershipId);
        Assert.Equal(30, row.PlaytimeMinutes);
        Assert.Equal(LibraryBuckets.NeverPlayed, row.Bucket);
        Assert.Equal(0, row.ConsolidatedDemoCount);
    }

    [Fact]
    public async Task A_real_game_whose_title_contains_demo_is_never_suppressed()
    {
        // "Demonologist" is in the author's real library, and "Demo" is a
        // substring of it. Tokenising before matching is what keeps it visible.
        var real = await SeedAsync("Demonologist", playtimeMinutes: 300);
        var disc = await SeedAsync("Demo Disc: Spectral Mall", playtimeMinutes: 0);
        await SeedAsync("Demon's Souls", playtimeMinutes: 100);

        var rows = await QueryAsync();

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.OwnershipId == real.OwnershipId);
        Assert.Contains(rows, r => r.OwnershipId == disc.OwnershipId);
    }

    [Fact]
    public async Task Portal_Demo_stays_visible_beside_Portal_2()
    {
        var demo = await SeedAsync("Portal Demo", playtimeMinutes: 20);
        await SeedAsync("Portal 2", playtimeMinutes: 600);

        var rows = await QueryAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.OwnershipId == demo.OwnershipId);
        Assert.All(rows, r => Assert.Equal(0, r.ConsolidatedDemoCount));
    }

    [Fact]
    public async Task A_rebuild_edition_does_not_supersede_the_original_demo()
    {
        // §9 pitfall 5: the remaster is a different Release with a different
        // achievement set, so it does not answer for the original's demo.
        var demo = await SeedAsync("Bastion Demo", playtimeMinutes: 42);
        await SeedAsync("Bastion Remastered", playtimeMinutes: 500);

        var rows = await QueryAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.OwnershipId == demo.OwnershipId);
    }

    /// <summary>
    /// The reversibility requirement, and the reason this is a query rather
    /// than a stored flag or a re-parented Work: remove the base game and the
    /// demo is back on the very next read, with nothing to undo.
    /// </summary>
    [Fact]
    public async Task Removing_the_base_game_makes_the_demo_visible_again()
    {
        var full = await SeedAsync("Magicka", playtimeMinutes: 800);
        var demo = await SeedAsync("Magicka Demo", playtimeMinutes: 15);

        Assert.Single(await QueryAsync());

        using (var lease = _db.Factory.Lease())
        {
            lease.Connection.Execute(
                "DELETE FROM ownerships WHERE id = @id;", new { id = full.OwnershipId });
        }

        var rows = await QueryAsync();

        var row = Assert.Single(rows);
        Assert.Equal(demo.OwnershipId, row.OwnershipId);
        Assert.Equal(15, row.PlaytimeMinutes);
        Assert.Equal(0, row.ConsolidatedDemoCount);
    }

    /// <summary>
    /// Nothing is written, so a second sync cannot drift from the first — and
    /// re-observing playtime (which is what a sync does) does not change which
    /// rows are consolidated.
    /// </summary>
    [Fact]
    public async Task Consolidation_is_idempotent_across_repeated_syncs()
    {
        var full = await SeedAsync("Tales of Arise", playtimeMinutes: 1200);
        var demo = await SeedAsync("Tales of Arise Demo", playtimeMinutes: 60);

        var first = await QueryAsync();

        // A later sync: same games, new observations.
        foreach (var ownershipId in new[] { full.OwnershipId, demo.OwnershipId })
        {
            await _plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = 1300,
                LastPlayedAt = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
                Source = "steam_local",
                ObservedAt = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            });
        }

        var second = await QueryAsync();

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Select(r => r.OwnershipId),
            second.Select(r => r.OwnershipId));
        Assert.Equal(1, Assert.Single(second).ConsolidatedDemoCount);
    }

    /// <summary>
    /// A provisional name is minted from an appid ("App 107110"), so it is
    /// evidence about nothing and takes no part on either side — including the
    /// side that would have been hidden.
    /// </summary>
    [Fact]
    public async Task Provisionally_named_rows_are_left_alone()
    {
        await SeedAsync("App 107100", playtimeMinutes: 500, provisional: true);
        await SeedAsync("App 107100 Demo", playtimeMinutes: 5, provisional: true);

        Assert.Equal(2, (await QueryAsync()).Count);
    }

    // ── Betas and playtests, through the same query ──────────────────────────

    /// <summary>
    /// The user's own case: Monster Hunter Wilds is owned and its beta test is
    /// a separate appid with its own tile. One row comes back, and the beta's
    /// minutes stay on the beta.
    /// </summary>
    [Fact]
    public async Task A_beta_beside_its_owned_base_game_yields_one_entry()
    {
        var full = await SeedAsync("Monster Hunter Wilds", playtimeMinutes: 4_000);
        var beta = await SeedAsync("Monster Hunter Wilds Beta test", playtimeMinutes: 180);

        var rows = await QueryAsync();

        var row = Assert.Single(rows);
        Assert.Equal(full.OwnershipId, row.OwnershipId);
        Assert.Equal(4_000, row.PlaytimeMinutes);
        Assert.Equal(1, row.ConsolidatedDemoCount);

        // Nothing destroyed: the beta's own 180 minutes are still stored.
        var record = await _plays.GetLatestAsync(beta.OwnershipId);
        Assert.NotNull(record);
        Assert.Equal(180, record.PlaytimeMinutes);
    }

    /// <summary>
    /// The other half, and the reason nothing here needs a user-facing undo:
    /// the user owns the playtest and NOT the game, so the playtest is the only
    /// evidence of it in the library and stays exactly where it is.
    /// </summary>
    [Fact]
    public async Task A_playtest_with_no_owned_base_stays_visible()
    {
        var playtest = await SeedAsync("BitCraft Online Playtest", playtimeMinutes: 60);
        await SeedAsync("Portal 2", playtimeMinutes: 600);

        var rows = await QueryAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.OwnershipId == playtest.OwnershipId);
    }

    /// <summary>
    /// Valve's own classification, stored by migration 0006 and read here.
    /// A demo Steam simply named after the game has no marker to find.
    /// </summary>
    [Fact]
    public async Task A_typed_demo_is_consolidated_even_without_a_marker_in_the_title()
    {
        var full = await SeedAsync("Enshrouded", playtimeMinutes: 2_000, appType: "Game");
        var demo = await SeedAsync("Enshrouded", playtimeMinutes: 30, appType: "Demo");

        var row = Assert.Single(await QueryAsync());

        Assert.Equal(full.OwnershipId, row.OwnershipId);
        Assert.Equal(2_000, row.PlaytimeMinutes);
        Assert.NotNull(await _ownerships.GetAsync(demo.OwnershipId));
    }

    /// <summary>
    /// The disagreement resolved in favour of the storefront: Valve types demos
    /// <c>Demo</c>, so a <c>Game</c> whose title ends in the word is a real game
    /// and keeps its tile.
    /// </summary>
    [Fact]
    public async Task A_game_typed_row_is_not_suppressed_by_a_demo_token()
    {
        await SeedAsync("Cloudheim", playtimeMinutes: 500, appType: "Game");
        var lookalike = await SeedAsync("Cloudheim Demo", playtimeMinutes: 12, appType: "Game");

        var rows = await QueryAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.OwnershipId == lookalike.OwnershipId);
    }

    /// <summary>
    /// A tool Valve typed as such is not a variant of the game it accompanies,
    /// even when its name would have bound. Consolidation leaves it alone —
    /// hiding non-game entries is a separate, reversible filter
    /// (<see cref="NonGameFilterTests"/>), and it is off here.
    /// </summary>
    [Fact]
    public async Task A_tool_is_never_consolidated_into_the_game_it_accompanies()
    {
        await SeedAsync("Eco", playtimeMinutes: 900, appType: "Game");
        var server = await SeedAsync("Eco Demo", playtimeMinutes: 0, appType: "Tool");

        var rows = await QueryAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.OwnershipId == server.OwnershipId);
    }

    [Fact]
    public async Task The_base_row_keeps_its_own_bucket_and_dates()
    {
        var full = await SeedAsync("Voxelgram", playtimeMinutes: 45);
        await SeedAsync("Voxelgram Demo", playtimeMinutes: 9_000);

        var row = Assert.Single(await QueryAsync());

        Assert.Equal(full.OwnershipId, row.OwnershipId);

        // Bucketed on ITS 45 minutes — under the refund line, so `never_played`.
        // Had the demo's 9,000 leaked in, this would read "retired": a game the
        // user has barely opened, filed under "Played out" by a merge nobody
        // asked for.
        Assert.Equal(LibraryBuckets.NeverPlayed, row.Bucket);
        Assert.Equal(new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc), row.LastPlayedAt);
    }

    private sealed record Seeded(long WorkId, long ReleaseId, long OwnershipId);
}
