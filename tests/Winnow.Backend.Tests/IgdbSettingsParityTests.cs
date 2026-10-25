using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class IgdbSettingsParityTests
{
    private static readonly string[] TokenKeys = [TwitchTokenProvider.TokenBlobKey,
        TwitchTokenProvider.TokenClientIdKey, TwitchTokenProvider.TokenValueKey, TwitchTokenProvider.TokenExpiresAtKey];

    [Fact]
    public async Task Accepted_save_protects_the_secret_clears_every_token_and_notifies_the_existing_refresh_source()
    {
        await using var host = await Host.Start();
        foreach (var key in TokenKeys) await host.Raw.SetAsync(key, "old-token");
        await host.Raw.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "legacy-secret");
        Assert.Equal(IgdbSettingsSaveResult.Saved, await host.Save(" client-id ", " entered-secret "));
        Assert.Equal(1, host.Changes);
        Assert.Equal("client-id", await host.Raw.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Null(await host.Raw.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
        var protectedValue = await host.Raw.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey);
        Assert.NotNull(protectedValue);
        Assert.DoesNotContain("entered-secret", protectedValue);
        foreach (var key in TokenKeys) Assert.Null(await host.Raw.GetAsync(key));
        var pair = await new SettingsTableCredentialSource(host.Raw, host.Protector).TryGetAsync();
        Assert.Equal("client-id", pair?.ClientId);
        Assert.Equal("entered-secret", pair?.ClientSecret);
        Assert.Equal(new IgdbSettingsSnapshot("client-id", true, true, false), await host.Snapshot());
        Assert.DoesNotContain("entered-secret", (await host.Api.GetAsync<JsonElement>("connections/igdb")).GetRawText());
    }

    [Theory]
    [InlineData("", "entered-secret")]
    [InlineData("client-id", "   ")]
    public async Task Missing_fields_do_not_write_or_notify(string id, string secret)
    {
        await using var host = await Host.Start();
        Assert.Equal(IgdbSettingsSaveResult.MissingFields, await host.Save(id, secret));
        Assert.Null(await host.Raw.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Null(await host.Raw.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Equal(0, host.Changes);
    }

    [Fact]
    public async Task Protection_refusal_preserves_the_previous_identity_and_cached_tokens()
    {
        await using var host = await Host.Start();
        await host.Raw.SetAsync(SettingsTableCredentialSource.ClientIdKey, "previous-id");
        await host.Raw.SetAsync(TwitchTokenProvider.TokenBlobKey, "previous-token");
        host.Protector.IsAvailable = false;
        Assert.Equal(IgdbSettingsSaveResult.ProtectionUnavailable, await host.Save("client-id", "entered-secret"));
        Assert.Equal("previous-id", await host.Raw.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Equal("previous-token", await host.Raw.GetAsync(TwitchTokenProvider.TokenBlobKey));
        Assert.Null(await host.Raw.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Equal(0, host.Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_clears_saved_legacy_and_token_rows_and_reports_configuration_fallback(bool configured)
    {
        await using var host = await Host.Start(configured);
        var rows = await host.SeedPrevious();
        Assert.Equal(configured, await host.Remove());
        foreach (var key in rows.Keys) Assert.Null(await host.Raw.GetAsync(key));
        Assert.Equal(new IgdbSettingsSnapshot("", false, false, configured), await host.Snapshot());
        Assert.Equal(1, host.Changes);
    }

    [Fact]
    public async Task Configuration_credentials_are_reported_without_copying_their_identity_or_secret_to_the_frontend()
    {
        await using var host = await Host.Start(configured: true);
        Assert.Equal(new IgdbSettingsSnapshot("", false, false, true), await host.Snapshot());
        var json = (await host.Api.GetAsync<JsonElement>("connections/igdb")).GetRawText();
        Assert.DoesNotContain("external-id", json);
        Assert.DoesNotContain("external-secret", json);
    }

    [Fact]
    public async Task Reading_legacy_credentials_uses_the_runtime_migration_without_exposing_the_secret()
    {
        await using var host = await Host.Start();
        await host.Raw.SetAsync(SettingsTableCredentialSource.ClientIdKey, "client-id");
        await host.Raw.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "entered-secret");
        Assert.Equal(new IgdbSettingsSnapshot("client-id", true, true, false), await host.Snapshot());
        Assert.Equal("", await host.Raw.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
        Assert.Equal("entered-secret", host.Protector.Unprotect(await host.Raw.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey)));
        Assert.DoesNotContain("entered-secret", (await host.Api.GetAsync<JsonElement>("connections/igdb")).GetRawText());
        Assert.Equal(0, host.Changes);
    }

    [Fact]
    public async Task An_unreadable_protected_value_remains_saved_but_is_never_returned()
    {
        await using var host = await Host.Start();
        await host.Raw.SetAsync(SettingsTableCredentialSource.ClientIdKey, "client-id");
        await host.Raw.SetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, "unreadable-fixture-secret");
        Assert.Equal(new IgdbSettingsSnapshot("client-id", true, false, false), await host.Snapshot());
        Assert.DoesNotContain("unreadable-fixture-secret", (await host.Api.GetAsync<JsonElement>("connections/igdb")).GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_mutations_roll_back_every_row_and_return_no_exception_details(bool remove)
    {
        await using var host = await Host.Start();
        var rows = await host.SeedPrevious();
        host.Faults.FailTokenRemoval = true;
        var error = remove
            ? await Assert.ThrowsAsync<BackendApiException>(() => host.Remove())
            : await Assert.ThrowsAsync<BackendApiException>(() => host.Save("new-id", "entered-secret"));
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.DoesNotContain("entered-secret", error.ResponseBody);
        Assert.DoesNotContain("fixture storage exception", error.ResponseBody);
        foreach (var (key, value) in rows) Assert.Equal(value, await host.Raw.GetAsync(key));
        Assert.Equal(0, host.Changes);
    }

    [Fact]
    public async Task A_failed_read_is_sanitized_and_can_be_retried()
    {
        await using var host = await Host.Start();
        host.Faults.FailRead = true;
        var error = await Assert.ThrowsAsync<BackendApiException>(() => host.Snapshot());
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.DoesNotContain("entered-secret", error.ResponseBody);
        host.Faults.FailRead = false;
        Assert.Equal(new IgdbSettingsSnapshot("", false, false, false), await host.Snapshot());
    }

    private sealed class Protector : IIgdbSecretProtector
    {
        private readonly Dictionary<string, string> _values = [];
        public bool IsAvailable { get; set; } = true;
        public string Name => "opaque-test-protector";
        public string? Protect(string plaintext)
        {
            if (!IsAvailable) return null;
            var value = Guid.NewGuid().ToString("N");
            _values[value] = plaintext;
            return value;
        }
        public string? Unprotect(string? value) => value is null ? null : _values.GetValueOrDefault(value);
    }

    private sealed class FaultingStore(ISettingsStore inner) : ISettingsStore
    {
        public bool FailRead { get; set; }
        public bool FailTokenRemoval { get; set; }
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => FailRead
            ? throw new InvalidOperationException("entered-secret fixture storage exception") : inner.GetAsync(key, ct);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => inner.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => FailTokenRemoval && key == TwitchTokenProvider.TokenClientIdKey
            ? throw new InvalidOperationException("entered-secret fixture storage exception") : inner.RemoveAsync(key, ct);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-igdb-settings-tests", Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public Protector Protector { get; } = new();
        public SqliteSettingsStore Raw { get; private set; } = null!;
        public FaultingStore Faults { get; private set; } = null!;
        public int Changes { get; private set; }
        public static async Task<Host> Start(bool configured = false)
        {
            var host = new Host();
            Directory.CreateDirectory(host._directory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(configured
                ? new Dictionary<string, string?> { ["Igdb:ClientId"] = "external-id", ["Igdb:ClientSecret"] = "external-secret" }
                : []).Build();
            try
            {
                host._app = BackendApplication.Build(["--data-dir", host._directory, "--no-sync"], services =>
                {
                    services.AddSingleton<IIgdbSecretProtector>(host.Protector);
                    services.AddSingleton<ISettingsStore>(sp =>
                    {
                        host.Raw = new SqliteSettingsStore(sp.GetRequiredService<ISqliteConnectionFactory>());
                        return host.Faults = new FaultingStore(host.Raw);
                    });
                    services.AddSingleton(sp => new IgdbSettingsService(sp.GetRequiredService<ISettingsStore>(), host.Protector,
                        sp.GetRequiredService<IUnitOfWorkFactory>(), configuration, sp.GetRequiredService<IIgdbCredentialUpdater>()));
                    services.AddSingleton<IIgdbSettingsService>(sp => sp.GetRequiredService<IgdbSettingsService>());
                });
                await host._app.StartAsync();
                host.Api = WinnowApiClient.Attach(host._directory);
                host._app.Services.GetRequiredService<IgdbSettingsService>().CredentialsChanged += () => host.Changes++;
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        public Task<IgdbSettingsSnapshot> Snapshot() => Api.GetAsync<IgdbSettingsSnapshot>("connections/igdb");
        public Task<IgdbSettingsSaveResult> Save(string id, string secret) => Api.SendAsync<SaveIgdbCredentials, IgdbSettingsSaveResult>(HttpMethod.Put, "connections/igdb", new(id, secret));
        public Task<bool> Remove() => Api.SendAsync<object?, bool>(HttpMethod.Delete, "connections/igdb", null);
        public async Task<Dictionary<string, string>> SeedPrevious()
        {
            var rows = TokenKeys.ToDictionary(key => key, _ => "previous-token");
            rows[SettingsTableCredentialSource.ClientIdKey] = "previous-id";
            rows[SettingsTableCredentialSource.ClientSecretProtectedKey] = Protector.Protect("previous-secret")!;
            rows[SettingsTableCredentialSource.ClientSecretKey] = "previous-legacy";
            foreach (var (key, value) in rows) await Raw.SetAsync(key, value);
            return rows;
        }
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
