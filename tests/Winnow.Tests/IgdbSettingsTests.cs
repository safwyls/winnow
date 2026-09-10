using Microsoft.Extensions.Configuration;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests;

public sealed class IgdbSettingsTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private SqliteSettingsStore Store => new(_db.Factory);
    private static readonly string[] TokenKeys = [TwitchTokenProvider.TokenBlobKey,
        TwitchTokenProvider.TokenClientIdKey, TwitchTokenProvider.TokenValueKey, TwitchTokenProvider.TokenExpiresAtKey];
    public void Dispose() => _db.Dispose();

    private IgdbSettingsViewModel Create(ISettingsStore? store = null, IIgdbSecretProtector? protector = null,
        IConfiguration? configuration = null, IUriDispatcher? uris = null)
        => new(new IgdbSettingsService(store ?? Store, protector ?? new TestProtector(), _db.Factory, configuration), uris);

    [Fact]
    public async Task Save_protects_secret_clears_tokens_and_is_read_by_existing_source()
    {
        foreach (var key in TokenKeys) await Store.SetAsync(key, "old-token");
        await Store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "legacy-secret");
        var vm = Create();
        vm.ClientId = " client-id ";
        vm.ClientSecret = " entered-secret ";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.HasSavedCredentials);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.ClientSecret);
        Assert.Contains("Restart Winnow", vm.Status);
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
        Assert.Equal("protected-value", await Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        foreach (var key in TokenKeys) Assert.Null(await Store.GetAsync(key));
        var pair = await new SettingsTableCredentialSource(Store, new TestProtector()).TryGetAsync();
        Assert.Equal("client-id", pair?.ClientId);
        Assert.Equal("entered-secret", pair?.ClientSecret);
        var loaded = Create();
        await loaded.LoadAsync();
        Assert.True(loaded.HasSavedCredentials);
        Assert.Equal("client-id", loaded.ClientId);
        Assert.Empty(loaded.ClientSecret);
    }

    [Theory]
    [InlineData("", "entered-secret")]
    [InlineData("client-id", "  ")]
    public async Task Missing_field_does_not_write(string id, string secret)
    {
        var vm = Create();
        vm.ClientId = id;
        vm.ClientSecret = secret;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Enter both", vm.Status);
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientIdKey));
    }

    [Fact]
    public async Task Protection_refusal_preserves_previous_values()
    {
        await Store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "previous-id");
        await Store.SetAsync(TwitchTokenProvider.TokenBlobKey, "previous-token");
        var vm = Create(protector: new UnavailableIgdbSecretProtector());
        vm.ClientId = "client-id";
        vm.ClientSecret = "entered-secret";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("nothing was saved", vm.Status);
        Assert.Equal("previous-id", await Store.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Equal("previous-token", await Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remove_clears_saved_pair_legacy_and_tokens_and_explains_fallback(bool configured)
    {
        var config = Configuration(configured);
        await Store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "client-id");
        await Store.SetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, "protected-value");
        await Store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "legacy-secret");
        foreach (var key in TokenKeys) await Store.SetAsync(key, "old-token");
        var vm = Create(configuration: config);
        await vm.LoadAsync();
        await vm.RemoveCommand.ExecuteAsync(null);
        Assert.False(vm.HasSavedCredentials);
        Assert.Empty(vm.ClientId);
        Assert.Empty(vm.ClientSecret);
        Assert.Contains("Restart Winnow", vm.Status);
        Assert.Equal(configured, vm.Status.Contains("environment variables"));
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Null(await Store.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
        foreach (var key in TokenKeys) Assert.Null(await Store.GetAsync(key));
    }

    [Fact]
    public async Task Configuration_load_does_not_copy_external_credentials_into_fields()
    {
        var vm = Create(configuration: Configuration(true));
        await vm.LoadAsync();
        Assert.Empty(vm.ClientId);
        Assert.Empty(vm.ClientSecret);
        Assert.False(vm.HasSavedCredentials);
        Assert.Contains("environment variables or configuration", vm.Status);
    }

    [Fact]
    public async Task Load_migrates_legacy_credentials_using_existing_source_without_exposing_secret()
    {
        await Store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "client-id");
        await Store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "entered-secret");
        var vm = Create();
        await vm.LoadAsync();
        Assert.True(vm.HasSavedCredentials);
        Assert.Equal("client-id", vm.ClientId);
        Assert.Empty(vm.ClientSecret);
        Assert.Contains("Credentials are saved", vm.Status);
        Assert.Equal("protected-value", await Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Equal(string.Empty, await Store.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
    }

    [Fact]
    public async Task Unreadable_protected_secret_is_reported_without_showing_it()
    {
        await Store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "client-id");
        await Store.SetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, "unreadable");
        var vm = Create();
        await vm.LoadAsync();
        Assert.True(vm.HasSavedCredentials);
        Assert.Empty(vm.ClientSecret);
        Assert.Contains("re-entered", vm.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_mutations_roll_back_all_rows_and_never_show_exception_details(bool remove)
    {
        await Store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "previous-id");
        await Store.SetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, "previous-protected");
        await Store.SetAsync(TwitchTokenProvider.TokenBlobKey, "previous-token");
        var vm = Create(store: new FailingStore(Store));
        vm.ClientId = "client-id";
        vm.ClientSecret = "entered-secret";
        if (remove) await vm.RemoveCommand.ExecuteAsync(null);
        else await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Could not", vm.Status);
        Assert.DoesNotContain("entered-secret", vm.Status);
        Assert.False(vm.IsBusy);
        Assert.Equal("previous-id", await Store.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Equal("previous-protected", await Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Equal("previous-token", await Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
    }

    [Fact]
    public async Task Read_failure_is_actionable_and_does_not_escape_command()
    {
        var vm = Create(store: new FailingStore(Store, failRead: true));
        await vm.LoadAsync();
        Assert.Contains("Could not read", vm.Status);
        Assert.DoesNotContain("entered-secret", vm.Status);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Setup_opens_Twitch_applications_and_handles_refusal()
    {
        var dispatcher = new TestDispatcher();
        var vm = Create(uris: dispatcher);
        await vm.OpenSetupCommand.ExecuteAsync(null);
        Assert.Equal("https://dev.twitch.tv/console/apps", dispatcher.Opened?.AbsoluteUri);
        Assert.Contains("Could not open", vm.Status);
    }

    private static IConfiguration Configuration(bool configured) => new ConfigurationBuilder()
        .AddInMemoryCollection(configured ? new Dictionary<string, string?>
        { ["Igdb:ClientId"] = "external-id", ["Igdb:ClientSecret"] = "external-secret" } : []).Build();

    private sealed class TestProtector : IIgdbSecretProtector
    {
        public bool IsAvailable => true;
        public string Name => "test";
        public string? Protect(string plaintext) => "protected-value";
        public string? Unprotect(string? value) => value == "protected-value" ? "entered-secret" : null;
    }

    private sealed class TestDispatcher : IUriDispatcher
    {
        public Uri? Opened { get; private set; }
        public Task<bool> OpenAsync(Uri uri) { Opened = uri; return Task.FromResult(false); }
    }

    private sealed class FailingStore(ISettingsStore inner, bool failRead = false) : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => failRead ? throw new InvalidOperationException("entered-secret") : inner.GetAsync(key, ct);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => inner.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default)
            => key == TwitchTokenProvider.TokenClientIdKey
                ? throw new InvalidOperationException("entered-secret") : inner.RemoveAsync(key, ct);
    }
}
