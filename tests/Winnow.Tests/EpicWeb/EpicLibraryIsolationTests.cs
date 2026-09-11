using System.Net;
using System.Text;
using Winnow.App.Services;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Xunit;

namespace Winnow.Tests.EpicWeb;

public sealed class EpicLibraryIsolationTests
{
    private const string AccountA = "00000000000000000000000000000001";
    private const string AccountB = "00000000000000000000000000000003";

    [Fact]
    public async Task Switching_accounts_after_restart_cannot_reuse_another_accounts_durable_library()
    {
        using var db = new TempDatabase();
        var cache = new SqliteEpicLibraryCache(db.Factory);
        using (var original = new EpicWebTestHost(EpicWebTestHost.Healthy(), libraryCache: cache))
        {
            await original.SignInAsync();
            Assert.Equal(3, (await original.Client.GetOwnedLibraryAsync()).Items.Count);
        }

        using var replacement = new EpicWebTestHost((request, count) => request.Endpoint switch
        {
            EpicEndpoint.Token => FakeEpicHandler.Json(HttpStatusCode.OK, TokenFor(AccountB)),
            EpicEndpoint.LibraryItems => FakeEpicHandler.Json(HttpStatusCode.OK, "{\"records\":[]}"),
            _ => EpicWebTestHost.Healthy()(request, count),
        }, libraryCache: cache);
        await replacement.SignInAsync();
        var library = await replacement.Client.GetOwnedLibraryAsync();

        Assert.True(library.Succeeded);
        Assert.False(library.FromCache);
        Assert.Equal(AccountB, library.AccountId);
        Assert.Empty(library.Items);
        Assert.NotNull(await cache.GetAsync(EpicAccountClient.LibraryCacheKey(AccountA)));
        Assert.NotNull(await cache.GetAsync(EpicAccountClient.LibraryCacheKey(AccountB)));
    }

    [Fact]
    public async Task Sign_out_prevents_a_warm_cache_from_emitting_candidates()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();
        var candidates = await host.Client.GetOwnershipCandidatesAsync();
        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.Equal(AccountA, candidate.AccountRef));

        await host.Client.SignOutAsync();
        var calls = host.Handler.Requests.Count;
        Assert.Empty(await host.Client.GetOwnershipCandidatesAsync());
        Assert.Equal(calls, host.Handler.Requests.Count);
    }

    [Fact]
    public async Task Offline_refresh_of_an_expired_token_can_use_only_the_same_accounts_stale_payload()
    {
        var offline = false;
        using var host = new EpicWebTestHost((request, count) => offline
            ? FakeEpicHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
            : EpicWebTestHost.Healthy()(request, count), configure: options => options.MaxRetryAttempts = 1);
        await host.SignInAsync();
        var original = await host.Client.GetOwnedLibraryAsync();
        host.Clock.Advance(TimeSpan.FromHours(9));
        offline = true;

        var stale = await host.Client.GetOwnedLibraryAsync();

        Assert.True(stale.Succeeded);
        Assert.True(stale.FromCache);
        Assert.Equal(AccountA, stale.AccountId);
        Assert.Equal(original.ObservedAt, stale.ObservedAt);
        Assert.All(await host.Client.GetOwnershipCandidatesAsync(), candidate => Assert.Equal(AccountA, candidate.AccountRef));
    }

    [Fact]
    public async Task Legacy_global_payload_and_misfiled_account_payload_are_not_evidence_for_a_new_account()
    {
        var cache = new InMemoryEpicLibraryCache();
        using (var original = new EpicWebTestHost(EpicWebTestHost.Healthy(), libraryCache: cache))
        {
            await original.SignInAsync();
            await original.Client.GetOwnedLibraryAsync();
        }
        var entry = (await cache.GetAsync(EpicAccountClient.LibraryCacheKey(AccountA)))!.Value;
        await cache.SetAsync("epic:library", "[]", entry.FetchedAt);
        await cache.SetAsync(EpicAccountClient.LibraryCacheKey(AccountB), entry.PayloadJson, entry.FetchedAt);
        using var host = new EpicWebTestHost((request, _) => request.Endpoint == EpicEndpoint.Token
            ? FakeEpicHandler.Json(HttpStatusCode.OK, TokenFor(AccountB))
            : FakeEpicHandler.Json(HttpStatusCode.Forbidden, "{}"), libraryCache: cache);
        await host.SignInAsync();

        Assert.False((await host.Client.GetOwnedLibraryAsync()).Succeeded);
        Assert.Empty(await host.Client.GetOwnershipCandidatesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delayed_page_is_discarded_after_sign_out_even_if_the_same_account_signs_back_in(bool sameAccount)
    {
        using var page = new DelayedJson(EpicFixturesWeb.LibraryPage2());
        var account = AccountA;
        var cache = new InMemoryEpicLibraryCache();
        using var host = new EpicWebTestHost((request, count) => request.Endpoint switch
        {
            EpicEndpoint.Token => FakeEpicHandler.Json(HttpStatusCode.OK, TokenFor(account)),
            EpicEndpoint.LibraryItems when request.Query("cursor") is not null => new(HttpStatusCode.OK) { Content = page },
            _ => EpicWebTestHost.Healthy()(request, count),
        }, libraryCache: cache);
        await host.SignInAsync();
        var before = await host.Tokens.GetIdentityAsync();
        var pending = host.Client.GetOwnershipCandidatesAsync();
        await page.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await host.Client.SignOutAsync();
        account = sameAccount ? AccountA : AccountB;
        await host.SignInAsync();
        Assert.NotEqual(before, await host.Tokens.GetIdentityAsync());
        page.Release.TrySetResult();

        Assert.Empty(await pending);
        Assert.Null(await cache.GetAsync(EpicAccountClient.LibraryCacheKey(AccountA)));
        Assert.Equal(0, host.Handler.CountFor(EpicEndpoint.Playtime));
    }

    [Fact]
    public async Task A_delayed_cache_lookup_cannot_publish_after_an_account_switch()
    {
        var cache = new DelayedCache();
        var account = AccountA;
        using var host = new EpicWebTestHost((request, count) => request.Endpoint == EpicEndpoint.Token
            ? FakeEpicHandler.Json(HttpStatusCode.OK, TokenFor(account))
            : EpicWebTestHost.Healthy()(request, count), libraryCache: cache);
        await host.SignInAsync();
        await host.Client.GetOwnedLibraryAsync();
        cache.Delay = true;
        var pending = host.Client.GetOwnedLibraryAsync();
        await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await host.Client.SignOutAsync();
        account = AccountB;
        await host.SignInAsync();
        cache.Release.TrySetResult();

        Assert.False((await pending).Succeeded);
        Assert.Equal(2, host.Handler.CountFor(EpicEndpoint.LibraryItems));
    }

    [Fact]
    public async Task Normal_token_renewal_preserves_the_operation_generation()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();
        var identity = await host.Tokens.GetIdentityAsync();
        var previous = await host.Tokens.GetAsync();

        Assert.NotNull(await host.Tokens.RefreshAsync(previous));
        Assert.Equal(identity, await host.Tokens.GetIdentityAsync());
        Assert.True((await host.Client.GetOwnedLibraryAsync()).Succeeded);
    }

    private static string TokenFor(string account)
        => EpicFixturesWeb.Token().Replace(AccountA, account, StringComparison.Ordinal);

    private sealed class DelayedCache : IEpicLibraryCache
    {
        private readonly InMemoryEpicLibraryCache _inner = new();
        public bool Delay { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<EpicCacheEntry?> GetAsync(string key, CancellationToken ct = default)
        {
            var result = await _inner.GetAsync(key, ct);
            if (Delay)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
        public Task SetAsync(string key, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
            => _inner.SetAsync(key, payloadJson, fetchedAt, ct);
    }

    private sealed class DelayedJson(string json) : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Started.TrySetResult();
            await Release.Task;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
