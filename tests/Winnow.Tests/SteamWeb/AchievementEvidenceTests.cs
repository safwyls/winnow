using System.Net;
using System.Text;
using Dapper;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Tests.SteamWeb;

public sealed class AchievementEvidenceTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SteamId Account = SteamId.FromAccountId(12345)!.Value;
    private static readonly SteamApiKey Key = SteamApiKey.TryCreate("fixture-only-api-key", "fixture")!;
    private const string Schema = """{"game":{"gameName":"Fixture","availableGameStats":{"achievements":[{"name":"A","displayName":"First","hidden":0}]}}}""";
    private const string Globals = """{"achievementpercentages":{"achievements":[{"name":"A","percent":12.5}]}}""";

    private static string Progress(int unlocked) => System.Text.Json.JsonSerializer.Serialize(new
    {
        playerstats = new { steamID = Account.ToString(), success = true,
            achievements = new[] { new { apiname = "A", achieved = unlocked, unlocktime = 0 } } },
    });

    private static async Task<AchievementFetch> Fetch(string schema = Schema, string? progress = null, string globals = Globals)
    {
        using var handler = new FakeSteamWebHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.Endpoint.EndsWith("GetSchemaForGame", StringComparison.Ordinal) ? schema
                : request.Endpoint.EndsWith("GetPlayerAchievements", StringComparison.Ordinal) ? progress ?? Progress(0) : globals,
                Encoding.UTF8, "application/json"),
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://api.steampowered.com/") };
        return await new SteamAchievementClient(http, new SteamWebTestClock(Now)).FetchAsync(Account, 100, Key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Known_schema_distinguishes_zero_unlocks_and_records_global_percentages(int unlocked)
    {
        var result = await Fetch(progress: Progress(unlocked));
        Assert.Single(result.Schema!);
        Assert.Equal(unlocked, result.Unlocks!.Count);
        Assert.Equal(12.5, result.GlobalPercentages!["A"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"game\":{}}")]
    [InlineData("{\"game\":{\"gameName\":\"Fixture\",\"availableGameStats\":null}}")]
    [InlineData("not json")]
    public async Task Unanswered_or_malformed_schema_is_not_confirmed_absence(string schema)
        => Assert.Null((await Fetch(schema)).Schema);

    [Fact]
    public async Task Named_game_with_explicit_empty_schema_has_no_achievements()
    {
        var result = await Fetch("""{"game":{"gameName":"Fixture","availableGameStats":{"achievements":[]}}}""");
        Assert.Empty(result.Schema!);
        Assert.Null(result.Unlocks);
    }

    [Theory]
    [InlineData("{\"playerstats\":{\"success\":false,\"error\":\"private\"}}")]
    [InlineData("{\"playerstats\":{\"success\":true,\"steamID\":\"wrong-account\",\"achievements\":[]}}")]
    [InlineData("{\"playerstats\":{\"success\":true}}")]
    public async Task Private_wrong_account_or_incomplete_progress_is_unavailable(string progress)
    {
        var result = await Fetch(progress: progress);
        Assert.Single(result.Schema!);
        Assert.Null(result.Unlocks);
        Assert.NotNull(result.GlobalPercentages);
    }

    [Fact]
    public async Task Missing_global_answer_does_not_discard_valid_player_progress()
    {
        var result = await Fetch(progress: Progress(1), globals: "{}");
        Assert.Single(result.Unlocks!);
        Assert.Null(result.GlobalPercentages);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2)]
    public async Task Production_pipeline_soft_fails_auth_and_transient_errors_without_logging_identity_or_keys(HttpStatusCode status, int requests)
    {
        using var host = new SteamWebTestHost(responder: (_, _) => FakeSteamWebHandler.Json(status, "{}"),
            configure: options => options.MaxRetryAttempts = 1);
        var result = await host.Resolve<ISteamAchievementClient>().FetchAsync(Account, 100, Key);
        Assert.Null(result.Schema);
        Assert.Equal(requests, host.Handler.Requests.Count);
        Assert.DoesNotContain(Key.Value, host.Logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(Account.ToString(), host.Logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_pipeline_enforces_response_ceiling()
    {
        using var host = new SteamWebTestHost(responder: (_, _) => FakeSteamWebHandler.Json(HttpStatusCode.OK, new string('x', 4096)),
            configure: options => { options.MaxResponseBytes = 1024; options.MaxRetryAttempts = 1; });
        Assert.Null((await host.Resolve<ISteamAchievementClient>().FetchAsync(Account, 100, Key)).Schema);
    }

    [Fact]
    public async Task No_schema_and_global_failures_preserve_their_distinct_observation_times()
    {
        using var db = new TempDatabase();
        Seed(db);
        var repository = new AchievementRepository(db.Factory);
        var first = await Fetch(progress: Progress(1));
        await repository.SaveAsync(1, Account.AccountRef, first);
        await repository.SaveAsync(1, Account.AccountRef, first with { AttemptedAt = Now.AddHours(1), GlobalPercentages = null });
        var summary = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now.AddHours(1)));
        Assert.Equal(Now.AddHours(1), summary.ObservedAt);
        Assert.Equal(Now, summary.GlobalObservedAt);
        Assert.Equal(100, summary.PercentComplete);
        await repository.SaveAsync(1, Account.AccountRef, new AchievementFetch { AttemptedAt = Now.AddHours(2), Schema = [] });
        summary = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now.AddHours(2)));
        Assert.Equal(AchievementAvailability.NoSchema, summary.Availability);
        Assert.Equal(0, summary.Total);
        Assert.False(summary.HasKnownProgress);
        Assert.Null(summary.PercentComplete);
    }

    [Fact]
    public async Task Changed_schema_without_progress_does_not_combine_new_total_with_old_unlocks()
    {
        using var db = new TempDatabase();
        Seed(db);
        var repository = new AchievementRepository(db.Factory);
        var first = await Fetch(progress: Progress(1));
        await repository.SaveAsync(1, Account.AccountRef, first);
        var changed = first with { AttemptedAt = Now.AddHours(1),
            Schema = [.. first.Schema!, new("B", "Second", null, false)], Unlocks = null, GlobalPercentages = null };
        await repository.SaveAsync(1, Account.AccountRef, changed);
        var retained = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now.AddHours(1)));
        Assert.Equal(1, retained.Total);
        Assert.Equal(100, retained.PercentComplete);
        Assert.True(retained.IsStale);
        await repository.SaveAsync(1, "67890", changed with { Unlocks = new Dictionary<string, DateTime?>() });
        var otherSchema = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now.AddHours(1)));
        Assert.Equal(2, otherSchema.Total);
        Assert.Null(otherSchema.PercentComplete);
        Assert.False(otherSchema.HasKnownProgress);
    }

    [Fact]
    public async Task Older_other_account_response_preserves_newer_shared_schema_and_globals_but_can_record_matching_progress()
    {
        using var db = new TempDatabase();
        Seed(db);
        var repository = new AchievementRepository(db.Factory);
        var first = await Fetch(progress: Progress(1));
        await repository.SaveAsync(1, Account.AccountRef, first with { AttemptedAt = Now.AddHours(1),
            Schema = [new("A", "New name", null, false)], GlobalPercentages = new Dictionary<string, double> { ["A"] = 25 } });
        await repository.SaveAsync(1, "67890", first);
        using var connection = db.Factory.Open();
        Assert.Equal("New name", connection.ExecuteScalar<string>("SELECT name FROM achievements;"));
        Assert.Equal(25, connection.ExecuteScalar<double>("SELECT global_pct FROM achievements;"));
        var b = Assert.Single(await repository.GetForAccountAsync([1], "67890", Now.AddHours(1)));
        Assert.Equal(100, b.PercentComplete);
        Assert.Null(b.GlobalObservedAt);
        await repository.SaveAsync(1, "98765", first with { Schema = [new("B", "Old incompatible", null, false)],
            Unlocks = new Dictionary<string, DateTime?>(), GlobalPercentages = null });
        var a = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now.AddHours(1)));
        Assert.Equal(100, a.PercentComplete);
        Assert.False(a.IsStale);
        Assert.Equal("A", connection.ExecuteScalar<string>("SELECT provider_key FROM achievements;"));
        Assert.Null(Assert.Single(await repository.GetForAccountAsync([1], "98765", Now.AddHours(1))).PercentComplete);
    }

    [Fact]
    public async Task Account_switch_failure_and_repeat_preserve_separate_known_and_unknown_states()
    {
        using var db = new TempDatabase();
        Seed(db);
        var repository = new AchievementRepository(db.Factory);
        var known = await Fetch(progress: Progress(1));
        await repository.SaveAsync(1, Account.AccountRef, known);
        await repository.SaveAsync(1, Account.AccountRef, known);
        var a = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now));
        Assert.Equal(100, a.PercentComplete);
        Assert.False(a.IsStale);
        var other = Assert.Single(await repository.GetForAccountAsync([1], "67890", Now));
        Assert.Equal(AchievementAvailability.Unknown, other.Availability);
        Assert.False(other.HasKnownProgress);
        Assert.Null(other.PercentComplete);
        await repository.SaveAsync(1, "67890", known with { Unlocks = new Dictionary<string, DateTime?>() });
        other = Assert.Single(await repository.GetForAccountAsync([1], "67890", Now));
        Assert.True(other.HasKnownProgress);
        Assert.Equal(0, other.PercentComplete);

        await repository.SaveAsync(1, Account.AccountRef, new AchievementFetch { AttemptedAt = Now.AddHours(1) });
        a = Assert.Single(await repository.GetForAccountAsync([1], Account.AccountRef, Now.AddHours(1)));
        Assert.Equal(AchievementAvailability.Unavailable, a.Availability);
        Assert.Equal(100, a.PercentComplete);
        Assert.Equal(Now, a.ObservedAt);
        Assert.Equal(Now, a.GlobalObservedAt);
        Assert.True(a.IsStale);
        using var connection = db.Factory.Open();
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM account_achievement_unlocks;"));
        Assert.Equal(12.5, connection.ExecuteScalar<double>("SELECT global_pct FROM achievements;"));
    }

    [Fact]
    public async Task Legacy_unlocks_never_supply_a_named_accounts_progress_and_current_account_controls_query()
    {
        using var db = new TempDatabase();
        Seed(db);
        using (var connection = db.Factory.Open()) connection.Execute("""
            INSERT INTO achievements(release_id,provider_key,name) VALUES(1,'A','Old');
            INSERT INTO achievement_unlocks(release_id,provider_key) VALUES(1,'A');
            """);
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, Account.AccountRef);
        var query = new AchievementQueryRepository(db.Factory);
        Assert.Null(Assert.Single(await query.GetSummariesAsync([1])).PercentComplete);
        await new AchievementRepository(db.Factory).SaveAsync(1, Account.AccountRef, await Fetch(progress: Progress(1)));
        Assert.Equal(100, Assert.Single(await query.GetSummariesAsync([1])).PercentComplete);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "67890");
        Assert.Null(Assert.Single(await query.GetSummariesAsync([1])).PercentComplete);
    }

    [Fact]
    public async Task Sync_requires_confirmed_key_account_positive_membership_and_respects_refresh_ttl()
    {
        using var db = new TempDatabase();
        Seed(db);
        var repository = new AchievementRepository(db.Factory);
        var settings = new SettingsRepository(db.Factory);
        var client = new StubClient(await Fetch());
        var sync = new SteamAchievementSyncService(repository, settings, new KeyProvider(), client, new SteamWebTestClock(Now));
        Assert.Equal(0, await sync.SyncAsync());
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, Account.AccountRef);
        await settings.SetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, "different-key");
        Assert.Equal(0, await sync.SyncAsync());
        await settings.SetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, SteamCredentialFingerprint.OfApiKey(Key.Value));
        Assert.Equal(1, await sync.SyncAsync());
        Assert.Equal(0, await sync.SyncAsync());
        Assert.Equal(1, client.Calls);
        Assert.Empty(await repository.GetDueSteamAsync("67890", Now.AddDays(2), 20));
    }

    private static void Seed(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        connection.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'Fixture','Fixture');
            INSERT INTO releases(id,work_id,name) VALUES(1,1,'Fixture');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0);
            INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','100');
            INSERT INTO ownership_accounts(ownership_id,account_ref,source,first_seen_at,last_seen_at)
            VALUES(1,@account,'fixture',@at,@at);
            """, new { account = Account.AccountRef, at = Now });
    }

    private sealed class KeyProvider : ISteamApiKeyProvider
    {
        public ValueTask<SteamApiKey?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult<SteamApiKey?>(Key);
        public void Invalidate() { }
    }

    private sealed class StubClient(AchievementFetch result) : ISteamAchievementClient
    {
        public int Calls { get; private set; }
        public Task<AchievementFetch> FetchAsync(SteamId account, uint appId, SteamApiKey key, CancellationToken ct = default)
        { Calls++; return Task.FromResult(result); }
    }
}
