using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>Production HTTP key changes with real protected storage, selection and identity reconciliation.</summary>
public sealed class SteamKeyParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_successful_key_changes_request_background_ownership_refresh_after_reconciliation(bool refuse)
    {
        await using var host = await Host.Start(!refuse);
        var value = await host.Save("first-fixture-key");
        Assert.Equal(refuse ? SteamApiKeySaveOutcome.Refused : SteamApiKeySaveOutcome.Stored, value);
        Assert.Equal(refuse ? 0 : 1, host.Refreshes);
        Assert.Equal(refuse ? 0 : 1, host.Credentials.Invalidations);
        Assert.Equal(refuse ? 0 : 1, host.Confirmation.Reconciliations);
        if (!refuse)
        {
            Assert.True(await host.Confirmation.ConfirmAsync(SteamId.FromSteamId64(76561198000000001UL)!.Value, SteamAccountConfirmationSource.WebApiKey));
            Assert.True((await host.Visibility()).AccountConfirmed);
            host.AssertUnconfirmedAtRefresh = true;
            await host.Save(null);
            Assert.Equal(2, host.Refreshes);
            Assert.Equal(2, host.Confirmation.Reconciliations);
            Assert.False((await host.Visibility()).AccountConfirmed);
        }
    }

    [Fact]
    public async Task Saving_a_key_writes_it_and_invalidates_the_credential_provider()
    {
        await using var host = await Host.Start();
        Assert.Null(await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended));
        Assert.Equal(SteamApiKeySaveOutcome.Stored, await host.Save("  0123456789ABCDEF  "));
        Assert.Equal("0123456789ABCDEF", await host.Service<ISteamApiKeyStore>().GetAsync());
        Assert.Equal("0123456789ABCDEF", (await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended))?.Value);
        Assert.Equal(1, host.Credentials.Invalidations);
        Assert.Equal(1, host.Confirmation.Reconciliations);
        Assert.Equal(string.Empty, await host.Stored(SettingsTableApiKeySource.ApiKeySetting));
        Assert.DoesNotContain("0123456789ABCDEF", (await host.Stored(SettingsSteamApiKeyStore.ProtectedSetting))!);
        var steam = (await host.Snapshot()).Steam;
        Assert.True(steam.HasApiKey);
        Assert.True(steam.ApiKeyIsAppManaged);
        Assert.True(steam.HasUsableCredential);
    }

    [Fact]
    public async Task A_host_that_cannot_encrypt_refuses_rather_than_writing_plaintext()
    {
        await using var host = await Host.Start(canEncrypt: false);
        Assert.Equal(SteamApiKeySaveOutcome.Refused, await host.Save("never-plaintext-key"));
        Assert.Null(await host.Stored(SettingsTableApiKeySource.ApiKeySetting));
        Assert.Null(await host.Stored(SettingsSteamApiKeyStore.ProtectedSetting));
        Assert.Null(await host.Service<ISteamApiKeyStore>().GetAsync());
        Assert.Equal(0, host.Credentials.Invalidations);
        Assert.Equal(0, host.Confirmation.Reconciliations);
        Assert.Equal(0, host.Refreshes);
        Assert.False((await host.Snapshot()).Steam.HasApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Clearing_or_saving_a_blank_field_empties_both_rows_and_invalidates(string? empty)
    {
        await using var host = await Host.Start();
        await host.Save("fixture-key");
        Assert.NotNull(await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended));
        Assert.Equal(SteamApiKeySaveOutcome.Stored, await host.Save(empty));
        Assert.Null(await host.Service<ISteamApiKeyStore>().GetAsync());
        Assert.Null(await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended));
        Assert.Equal(string.Empty, await host.Stored(SettingsTableApiKeySource.ApiKeySetting));
        Assert.Equal(string.Empty, await host.Stored(SettingsSteamApiKeyStore.ProtectedSetting));
        Assert.Equal(2, host.Credentials.Invalidations);
        Assert.Equal(2, host.Confirmation.Reconciliations);
        Assert.False((await host.Snapshot()).Steam.HasUsableCredential);
    }

    [Fact]
    public async Task A_configuration_key_is_not_owned_and_saved_settings_take_precedence_until_cleared()
    {
        await using var host = await Host.Start(configuredKey: "configuration-fixture-key");
        var steam = (await host.Snapshot()).Steam;
        Assert.True(steam.HasApiKey);
        Assert.False(steam.ApiKeyIsAppManaged);
        Assert.Equal("configuration-fixture-key", (await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended))?.Value);
        await host.Save("settings-fixture-key");
        Assert.True((await host.Snapshot()).Steam.ApiKeyIsAppManaged);
        Assert.Equal("settings-fixture-key", (await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended))?.Value);
        await host.Save(null);
        Assert.True((await host.Snapshot()).Steam.HasApiKey);
        Assert.False((await host.Snapshot()).Steam.ApiKeyIsAppManaged);
        Assert.Equal("configuration-fixture-key", (await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended))?.Value);
    }

    [Fact]
    public async Task Replacing_key_clears_previous_identity_before_refresh_and_disables_the_account_scope()
    {
        await using var host = await Host.Start();
        await host.Save("account-a-key");
        Assert.True(await host.Confirmation.ConfirmAsync(SteamId.FromSteamId64(76561198000000001UL)!.Value, SteamAccountConfirmationSource.WebApiKey));
        Assert.True((await host.Visibility()).AccountConfirmed);
        host.AssertUnconfirmedAtRefresh = true;
        await host.Save("account-b-key");
        Assert.False((await host.Visibility()).AccountConfirmed);
        Assert.True((await host.Snapshot()).Steam.HasApiKey);
        Assert.Equal("account-b-key", (await host.Credentials.GetCurrentAsync(SteamCredentialPurpose.Unattended))?.Value);
        Assert.Equal(2, host.Refreshes);
    }

    private sealed class Credentials(SteamCredentialProvider inner) : ISteamCredentialProvider
    {
        public int Invalidations { get; private set; }
        public ValueTask<SteamCredential?> GetAsync(SteamCredentialPurpose purpose, CancellationToken ct = default) => inner.GetAsync(purpose, ct);
        public ValueTask<SteamCredential?> GetCurrentAsync(SteamCredentialPurpose purpose, CancellationToken ct = default) => inner.GetCurrentAsync(purpose, ct);
        public ValueTask<SteamCredentialInventory> GetInventoryAsync(CancellationToken ct = default) => inner.GetInventoryAsync(ct);
        public ValueTask<SteamCredential?> RenewAfterUnauthorizedAsync(SteamCredential rejected, CancellationToken ct = default) => inner.RenewAfterUnauthorizedAsync(rejected, ct);
        public void Invalidate() { Invalidations++; inner.Invalidate(); }
    }

    private sealed class Confirmation(SteamAccountConfirmation inner) : ISteamAccountConfirmation
    {
        public int Reconciliations { get; private set; }
        public async Task<bool> ReconcileAsync(CancellationToken ct = default) { var result = await inner.ReconcileAsync(ct); Reconciliations++; return result; }
        public Task<bool> ConfirmAsync(SteamId account, SteamAccountConfirmationSource source, CancellationToken ct = default) => inner.ConfirmAsync(account, source, ct);
        public Task<bool> ConfirmAsync(SteamId account, SteamCredentialIdentity identity, CancellationToken ct = default) => inner.ConfirmAsync(account, identity, ct);
        public Task<string?> GetConfirmedAccountRefAsync(CancellationToken ct = default) => inner.GetConfirmedAccountRefAsync(ct);
        public Task<string?> GetRecordedFingerprintAsync(CancellationToken ct = default) => inner.GetRecordedFingerprintAsync(ct);
        public Task<bool> IsInForceAsync(string fingerprint, CancellationToken ct = default) => inner.IsInForceAsync(fingerprint, ct);
    }

    private sealed class Protector(bool available) : ISteamApiKeyProtector
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public bool IsAvailable => available;
        public string Name => "test AES-GCM";
        public string? Protect(string plaintext)
        {
            if (!IsAvailable) return null;
            var nonce = RandomNumberGenerator.GetBytes(12);
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var tag = new byte[16];
            var encrypted = new byte[bytes.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, bytes, encrypted, tag);
            return Convert.ToBase64String([.. nonce, .. tag, .. encrypted]);
        }
        public string? Unprotect(string? value)
        {
            if (!IsAvailable || value is null) return null;
            var bytes = Convert.FromBase64String(value);
            var plaintext = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-steam-key-tests", Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        private WinnowApiClient _api = null!;
        public Credentials Credentials => Service<Credentials>();
        public Confirmation Confirmation => Service<Confirmation>();
        public int Refreshes { get; private set; }
        public bool AssertUnconfirmedAtRefresh { get; set; }
        public static async Task<Host> Start(bool canEncrypt = true, string? configuredKey = null)
        {
            var host = new Host();
            Directory.CreateDirectory(host._directory);
            try
            {
                host._app = BackendApplication.Build(["--data-dir", host._directory, "--no-sync"], services =>
                {
                    services.AddSingleton<ISteamApiKeyProtector>(new Protector(canEncrypt));
                    // Use the real sources without reading the developer's optional configuration.
                    services.RemoveAll<ISteamApiKeySource>();
                    services.AddSingleton<ISteamApiKeySource, SettingsTableApiKeySource>();
                    services.AddSingleton<ISteamApiKeySource>(new ConfigurationApiKeySource(new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Steam:ApiKey"] = configuredKey }).Build()));
                    services.AddSingleton<SteamCredentialProvider>();
                    services.AddSingleton<Credentials>();
                    services.AddSingleton<ISteamCredentialProvider>(sp => sp.GetRequiredService<Credentials>());
                    services.AddSingleton<SteamAccountConfirmation>();
                    services.AddSingleton<Confirmation>();
                    services.AddSingleton<ISteamAccountConfirmation>(sp => sp.GetRequiredService<Confirmation>());
                });
                await host._app.StartAsync();
                host._api = WinnowApiClient.Attach(host._directory);
                host.Service<OwnershipRefreshRequests>().Requested += () =>
                {
                    host.Refreshes++;
                    Assert.Equal(host.Refreshes, host.Confirmation.Reconciliations);
                    if (host.AssertUnconfirmedAtRefresh)
                        Assert.Null(host.Confirmation.GetConfirmedAccountRefAsync().GetAwaiter().GetResult());
                };
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        public T Service<T>() where T : notnull => _app.Services.GetRequiredService<T>();
        public Task<string?> Stored(string name) => Service<ISettingsRepository>().GetAsync(name);
        public Task<SteamApiKeySaveOutcome> Save(string? value) => _api.SendAsync<SaveSteamApiKey, SteamApiKeySaveOutcome>(HttpMethod.Put, "connections/stores/steam/key", new(value));
        public Task<StoreConnectionSnapshot> Snapshot() => _api.GetAsync<StoreConnectionSnapshot>("connections/stores");
        public Task<AccountVisibilityState> Visibility() => _api.GetAsync<AccountVisibilityState>("connections/account-visibility");
        public async ValueTask DisposeAsync()
        {
            _api?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
