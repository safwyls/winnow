using Microsoft.Extensions.Configuration;
using Winnow.App.Services;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

public sealed class IgdbRuntimeSettingsTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly IgdbFixtures.ReversibleProtector _protector = new();
    private SqliteSettingsStore Store => new(_db.Factory);
    public void Dispose() => _db.Dispose();

    private IgdbSettingsService Service(IgdbTestHost host, ISettingsStore? store = null,
        IConfiguration? configuration = null) => new(store ?? Store, _protector, _db.Factory,
            configuration, host.Resolve<IIgdbCredentialUpdater>());

    [Fact]
    public async Task Saving_activates_cached_absence_and_rotating_same_client_replaces_token()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), clientId: null,
            clientSecret: null, settings: Store, protector: _protector);
        var service = Service(host);
        var changes = 0;
        service.CredentialsChanged += () => changes++;
        Assert.Same(host.TokenProvider, host.Resolve<IIgdbCredentialUpdater>());
        Assert.Null(await host.TokenProvider.GetAsync());

        Assert.Equal(IgdbSettingsSaveResult.Saved, await service.SaveAsync("same-client", "first-secret"));
        var first = await host.TokenProvider.GetAsync();
        Assert.NotNull(first);
        Assert.Equal("same-client", first.ClientId);

        await service.SaveAsync("same-client", "second-secret");
        var second = await host.TokenProvider.GetAsync();
        Assert.NotNull(second);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(2, changes);
        Assert.Equal(2, host.Handler.CountFor("token"));
        Assert.Contains("client_secret=second-secret", host.Handler.Requests.Last().Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_immediately_uses_configuration_fallback_or_disables_auth(bool fallback)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(fallback
            ? new Dictionary<string, string?> { ["Igdb:ClientId"] = "external-client", ["Igdb:ClientSecret"] = "external-secret" }
            : []).Build();
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: Store,
            protector: _protector, configuration: config);
        var service = Service(host, configuration: config);
        var oldToken = await host.TokenProvider.GetAsync();
        Assert.NotNull(oldToken);
        Assert.Equal(fallback, await service.RemoveAsync());
        var current = await host.TokenProvider.RefreshAsync(oldToken);
        if (fallback)
        {
            Assert.Equal("external-client", current?.ClientId);
            Assert.Contains("client_secret=external-secret", host.Handler.Requests.Last().Body);
        }
        else
        {
            Assert.Null(current);
            Assert.Null(await Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
            Assert.Equal(1, host.Handler.CountFor("token"));
        }
    }

    [Fact]
    public async Task Failed_transaction_keeps_runtime_credentials_token_and_notifications_unchanged()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: Store, protector: _protector);
        var token = await host.TokenProvider.GetAsync();
        var blob = await Store.GetAsync(TwitchTokenProvider.TokenBlobKey);
        var service = Service(host, new FailingStore(Store));
        var changes = 0;
        service.CredentialsChanged += () => changes++;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync("new-client", "new-secret"));
        Assert.Equal(token, await host.TokenProvider.GetAsync());
        Assert.Equal(blob, await Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
        Assert.Equal("test-client", (await host.Resolve<IIgdbCredentialProvider>().GetAsync())?.ClientId);
        Assert.Equal(0, changes);
        Assert.Equal(1, host.Handler.CountFor("token"));
    }

    [Fact]
    public async Task Removal_waits_for_inflight_token_persistence_then_clears_it()
    {
        var store = new PausedTokenStore(Store);
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: store, protector: _protector);
        var updater = new ObservedUpdater(host.Resolve<IIgdbCredentialUpdater>());
        var service = new IgdbSettingsService(store, _protector, _db.Factory, credentialUpdater: updater);
        var mint = host.TokenProvider.GetAsync();
        await store.TokenWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var remove = service.RemoveAsync();
        await updater.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(remove.IsCompleted);
        store.AllowTokenWrite.SetResult();
        await mint;
        await remove;

        Assert.Null(await host.TokenProvider.GetAsync());
        Assert.Null(await Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Equal(1, host.Handler.CountFor("token"));
    }

    private sealed class FailingStore(ISettingsStore inner) : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => inner.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => key == TwitchTokenProvider.TokenBlobKey
            ? throw new InvalidOperationException("Injected failure") : inner.RemoveAsync(key, ct);
    }

    private sealed class ObservedUpdater(IIgdbCredentialUpdater inner) : IIgdbCredentialUpdater
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task UpdateCredentialsAsync(Func<Task> update, CancellationToken ct = default)
        {
            var pending = inner.UpdateCredentialsAsync(update, ct);
            Attempted.SetResult();
            return pending;
        }
    }

    private sealed class PausedTokenStore(ISettingsStore inner) : ISettingsStore
    {
        public TaskCompletionSource TokenWriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowTokenWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => inner.RemoveAsync(key, ct);
        public async Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            if (key == TwitchTokenProvider.TokenBlobKey)
            {
                TokenWriteStarted.TrySetResult();
                await AllowTokenWrite.Task.WaitAsync(ct);
            }
            await inner.SetAsync(key, value, ct);
        }
    }
}
