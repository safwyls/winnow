using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class OwnershipInventoryTests
{
    [Theory]
    [InlineData("steam", "111", OwnershipInventorySources.SteamOwnedGames, true)]
    [InlineData("steam", "222", OwnershipInventorySources.SteamOwnedGames, false)]
    [InlineData("gog", "111", OwnershipInventorySources.SteamOwnedGames, false)]
    [InlineData("steam", "111", "steam_local", false)]
    public async Task Only_the_selected_account_and_suitable_source_can_establish_absence(string store, string account, string source, bool hides)
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        var inventories = new OwnershipInventoryRepository(db.Factory);
        var attempt = await inventories.BeginAttemptAsync(store, account, source);
        var query = new LibraryQueryRepository(db.Factory);
        Assert.Equal(2, (await query.GetOwnershipBucketsAsync(BucketThresholds.Default)).Count);
        Assert.True(await inventories.CompleteAsync(attempt, DateTime.UtcNow, 1));
        Assert.Equal(hides ? 1 : 2, (await query.GetSnapshotAsync(BucketThresholds.Default)).Buckets.Count);
    }

    [Fact]
    public async Task A_failed_or_partial_new_attempt_retires_completeness_and_an_old_completion_cannot_restore_it()
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        var inventories = new OwnershipInventoryRepository(db.Factory);
        var first = await inventories.BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
        await inventories.CompleteAsync(first, DateTime.UtcNow, 1);
        var query = new LibraryQueryRepository(db.Factory);
        Assert.Single(await query.GetOwnershipBucketsAsync(BucketThresholds.Default));
        var second = await inventories.BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
        Assert.True(second.Revision > first.Revision);
        Assert.False(await inventories.CompleteAsync(first, DateTime.UtcNow, 1));
        Assert.Equal(2, (await query.GetOwnershipBucketsAsync(BucketThresholds.Default)).Count);
    }

    [Fact]
    public async Task A_cached_inventory_cannot_infer_absence_for_a_game_discovered_after_its_response()
    {
        using var db = new TempDatabase();
        await SeedAsync(db);
        var inventories = new OwnershipInventoryRepository(db.Factory);
        var attempt = await inventories.BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
        var observedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await inventories.CompleteAsync(attempt, observedAt, 1);
        using (var connection = db.Factory.Open()) connection.Execute("UPDATE ownership_accounts SET first_seen_at='2026-09-02' WHERE ownership_id=2;");
        Assert.Equal(2, (await new LibraryQueryRepository(db.Factory).GetOwnershipBucketsAsync(BucketThresholds.Default)).Count);
        using var check = db.Factory.Open();
        Assert.Equal(observedAt, check.ExecuteScalar<DateTime>("SELECT observed_at FROM account_inventory_observations;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inventory_operations_respect_the_callers_transaction(bool commit)
    {
        using var db = new TempDatabase();
        var inventories = new OwnershipInventoryRepository(db.Factory);
        using (var scope = db.Factory.Begin())
        {
            var attempt = await inventories.BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
            Assert.True(await inventories.CompleteAsync(attempt, DateTime.UtcNow, 0));
            if (commit) scope.Commit();
        }
        using var check = db.Factory.Open();
        Assert.Equal(commit ? 1 : 0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM account_inventory_observations WHERE is_complete=1;"));
    }

    private static async Task SeedAsync(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        await connection.ExecuteAsync("""
            INSERT INTO works(id,name) VALUES(1,'Mine'),(2,'Household');
            INSERT INTO releases(id,work_id,name) VALUES(1,1,'Mine'),(2,2,'Household');
            INSERT INTO ownerships(id,release_id,store) VALUES(1,1,'steam'),(2,2,'steam');
            INSERT INTO ownership_accounts(ownership_id,account_ref,source,first_seen_at,last_seen_at)
                VALUES(1,'111','steam_local','2026-08-01','2026-08-01'),(2,'222','steam_local','2026-08-01','2026-08-01');
            """);
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "111");
    }
}
