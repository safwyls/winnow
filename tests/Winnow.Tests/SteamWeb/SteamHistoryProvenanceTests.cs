using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Enrich.SteamWeb.Storage;
using Xunit;

namespace Winnow.Tests.SteamWeb;

public sealed class SteamHistoryProvenanceTests
{
    private static readonly DateTimeOffset Now = new(2024, 12, 31, 12, 0, 0, TimeSpan.Zero);
    private static SteamId Account => SteamId.FromAccountId(SteamWebFixtures.FixtureAccountId)!.Value;

    private static HttpResponseMessage Answer(RecordedSteamWebRequest request, int _)
        => FakeSteamWebHandler.Json(HttpStatusCode.OK, request.Endpoint == SteamWebTestHost.ClientGetLastPlayedTimes
            ? SteamWebFixtures.LastPlayedTimes() : SteamWebFixtures.YearInReview2024());

    [Fact]
    public async Task Durable_history_cache_survives_restart_only_for_the_credential_that_fetched_it()
    {
        using var db = new TempDatabase();
        var cache = new SqliteSteamWebMetadataCache(db.Factory);
        using (var first = new SteamWebTestHost(Answer, apiKey: "key-a", cache: cache, now: Now))
        {
            await first.History.GetLastPlayedTimesAsync();
            await first.History.GetYearInReviewAsync(Account, 2024);
        }

        using (var changed = new SteamWebTestHost((_, _) => new(HttpStatusCode.Forbidden),
            apiKey: "key-b", cache: cache, now: Now))
        {
            Assert.False((await changed.History.GetLastPlayedTimesAsync()).Answered);
            Assert.False((await changed.History.GetYearInReviewAsync(Account, 2024)).Answered);
            Assert.Equal(2, changed.Handler.Requests.Count);
        }

        using var unchanged = new SteamWebTestHost((_, _) => throw new InvalidOperationException("Cache expected"),
            apiKey: "key-a", cache: cache, now: Now);
        Assert.True((await unchanged.History.GetLastPlayedTimesAsync()).FromCache);
        Assert.True((await unchanged.History.GetYearInReviewAsync(Account, 2024)).FromCache);
        Assert.Empty(unchanged.Handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("replacement-key")]
    public async Task Unscoped_legacy_payloads_cannot_supply_history_or_disclosure(string? key)
    {
        var cache = new InMemorySteamWebMetadataCache();
        await cache.SetAsync(SteamWebApiClient.CacheProvider, "lastplayed", SteamWebFixtures.LastPlayedTimes(), Now.UtcDateTime);
        await cache.SetAsync(SteamWebApiClient.CacheProvider, $"yir:{Account.Value}:2024",
            SteamWebFixtures.YearInReview2024(), Now.UtcDateTime);
        using var host = new SteamWebTestHost((_, _) => new(HttpStatusCode.Forbidden), apiKey: key, cache: cache, now: Now);

        Assert.False((await host.History.GetLastPlayedTimesAsync()).Answered);
        Assert.False((await host.History.GetYearInReviewAsync(Account, 2024)).Answered);
    }

    [Fact]
    public async Task Different_key_and_session_accounts_have_separate_history_cache_scopes()
    {
        using var host = new SteamWebTestHost(Answer, apiKey: "key-a", now: Now);
        var sessionAccount = SteamId.FromAccountId(22222222)!.Value;
        var sessions = host.Resolve<ISteamSessionProvider>();
        await sessions.SaveAsync(SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(Now.AddDays(1), sessionAccount.Value.ToString()), null, Now)!);

        var keyResult = await host.History.GetLastPlayedTimesAsync();
        var sessionResult = await host.History.GetLastPlayedTimesAsync(SteamCredentialPurpose.UserInitiated);

        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.NotEqual(keyResult.CredentialIdentity, sessionResult.CredentialIdentity);
        Assert.Equal(sessionAccount, sessionResult.CredentialIdentity!.Account);
        await sessions.SignOutAsync();
        var afterSignOut = await host.History.GetLastPlayedTimesAsync(SteamCredentialPurpose.UserInitiated);
        Assert.Equal(keyResult.CredentialIdentity, afterSignOut.CredentialIdentity);
        Assert.True(afterSignOut.FromCache);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_delayed_response_cannot_publish_after_its_credential_is_replaced(bool year, bool session)
    {
        var original = session
            ? SteamCredential.TryCreateSessionToken("token-a", "test", Now.AddDays(1), Account)!
            : SteamCredential.TryCreateApiKey("key-a", "test")!;
        var credentials = new MutableCredentials { Current = original };
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler);
        var cache = new InMemorySteamWebMetadataCache();
        var client = new SteamHistoryClient(http, cache, credentials, new SteamWebOptions(),
            new SteamWebTestClock(Now), NullLogger<SteamHistoryClient>.Instance);

        var pending = year
            ? IsAnsweredAsync(client.GetYearInReviewAsync(Account, 2024))
            : IsAnsweredAsync(client.GetLastPlayedTimesAsync());
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        credentials.Current = session
            ? SteamCredential.TryCreateSessionToken("token-b", "test", Now.AddDays(1), SteamId.FromAccountId(22222222))
            : SteamCredential.TryCreateApiKey("key-b", "test");
        handler.Response.SetResult(FakeSteamWebHandler.Json(HttpStatusCode.OK,
            year ? SteamWebFixtures.YearInReview2024() : SteamWebFixtures.LastPlayedTimes()));

        Assert.False(await pending);
        var identity = SteamCredentialIdentity.From(original)!;
        var key = year ? SteamHistoryClient.YearInReviewCacheKey(identity, Account, 2024)
            : SteamHistoryClient.LastPlayedCacheKey(identity);
        Assert.Null(await cache.GetAsync(SteamWebApiClient.CacheProvider, key));
    }

    [Fact]
    public async Task Cached_Replay_can_support_history_but_cannot_recreate_a_missing_confirmation()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var fail = false;
        using var host = new SteamWebTestHost((request, count) => fail
            ? new(HttpStatusCode.Forbidden) : Answer(request, count),
            apiKey: "key-a", cache: new SqliteSteamWebMetadataCache(db.Factory), now: Now);
        await host.History.GetYearInReviewAsync(Account, 2024);
        await host.History.GetLastPlayedTimesAsync();
        fail = true;
        await SeedAsync(db);

        await Backfill(db, settings, host).BackfillAsync();

        Assert.Null(await settings.GetAsync(SteamOwnedAccount.RefSettingKey));
        Assert.False((await new AccountVisibilityService(settings, new LibraryQueryRepository(db.Factory)).GetAsync()).AccountConfirmed);
    }

    [Fact]
    public async Task A_key_change_between_Replay_and_anchor_writes_no_history_or_confirmation()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        SteamWebTestHost? running = null;
        using var host = new SteamWebTestHost((request, count) =>
        {
            if (request.Endpoint == SteamWebTestHost.ClientGetLastPlayedTimes)
            {
                running!.Resolve<ISteamApiKeyStore>().SaveAsync("key-b").GetAwaiter().GetResult();
                running.Resolve<ISteamCredentialProvider>().Invalidate();
            }
            return Answer(request, count);
        }, apiKey: "key-a", now: Now);
        running = host;
        var ownershipId = await SeedAsync(db);

        var result = await Backfill(db, settings, host).BackfillAsync();

        Assert.False(result.WroteAnything);
        Assert.Empty(await new PlaytimeSnapshotRepository(db.Factory).GetByOwnershipAsync(ownershipId));
        Assert.Null(await settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    [Fact]
    public async Task Confirmation_rejects_evidence_from_a_replaced_key_without_recording_its_account()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var writer = new SteamAccountConfirmation(settings, new FakeSteamApiKeyProvider("key-b"), unitOfWork: db.Factory);

        Assert.False(await writer.ConfirmAsync(Account, new FakeSteamApiKeyProvider("key-a").Identity!));
        Assert.Null(await settings.GetAsync(SteamOwnedAccount.RefSettingKey));
        Assert.Null(await settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey));
    }

    private static SteamPlaytimeBackfillService Backfill(TempDatabase db, SettingsRepository settings, SteamWebTestHost host)
        => new(host.History, new ReleaseRepository(db.Factory), new OwnershipRepository(db.Factory),
            new OwnershipAccountRepository(db.Factory), new PlayRecordRepository(db.Factory),
            new PlaytimeSnapshotRepository(db.Factory), settings, db.Factory, new LibrarySyncGate(),
            new SteamPlaytimeBackfillOptions { FirstYear = 2024 }, host.Clock,
            host.Resolve<ISteamApiKeyProvider>(), NullLogger<SteamPlaytimeBackfillService>.Instance);

    private static async Task<long> SeedAsync(TempDatabase db)
    {
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Test game" });
        var releases = new ReleaseRepository(db.Factory);
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Test game" });
        await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "1203620" });
        return await new OwnershipRepository(db.Factory).InsertAsync(
            new Ownership { ReleaseId = release, Store = "steam", AccountRef = Account.AccountRef });
    }

    private static async Task<bool> IsAnsweredAsync(Task<SteamYearInReview> task) => (await task).Answered;
    private static async Task<bool> IsAnsweredAsync(Task<SteamLastPlayedTimes> task) => (await task).Answered;

    private sealed class MutableCredentials : ISteamCredentialProvider
    {
        public SteamCredential? Current { get; set; }
        public ValueTask<SteamCredential?> GetAsync(SteamCredentialPurpose purpose, CancellationToken ct = default)
            => ValueTask.FromResult(Current);
        public ValueTask<SteamCredentialInventory> GetInventoryAsync(CancellationToken ct = default)
            => ValueTask.FromResult(SteamCredentialInventory.Empty);
        public void Invalidate() { }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            return Response.Task.WaitAsync(ct);
        }
    }
}
