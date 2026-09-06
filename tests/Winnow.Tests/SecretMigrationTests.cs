using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Tests.SteamWeb;
using Xunit;

namespace Winnow.Tests;

public sealed class SecretMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_migrates_keys_and_recovers_interrupted_cleanup(bool interrupted)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var igdbSettings = new SqliteSettingsStore(db.Factory);
        var steamProtector = new DpapiSteamApiKeyProtector();
        var igdbProtector = new DpapiIgdbSecretProtector();
        var epicProtector = new DpapiEpicSecretProtector();
        const string secret = "synthetic-secret-for-migration";
        await settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, secret);
        await settings.SetAsync(SettingsTableCredentialSource.ClientIdKey, "test-client");
        await settings.SetAsync(SettingsTableCredentialSource.ClientSecretKey, secret);
        await settings.SetAsync(SettingsTableEpicCredentialSource.ClientIdSetting, "test-client");
        await settings.SetAsync(SettingsTableEpicCredentialSource.ClientSecretSetting, secret);

        if (interrupted)
        {
            await settings.SetAsync(SettingsSteamApiKeyStore.ProtectedSetting, steamProtector.Protect(secret)!);
            await settings.SetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, igdbProtector.Protect(secret)!);
            await settings.SetAsync(SettingsTableEpicCredentialSource.ProtectedClientSecretSetting, epicProtector.Protect(secret)!);
        }

        Assert.Equal(secret, await new SettingsSteamApiKeyStore(settings, steamProtector).GetAsync());
        Assert.Equal(secret, (await new SettingsTableCredentialSource(igdbSettings, igdbProtector).TryGetAsync())!.ClientSecret);
        // Exercise the production Epic composition, not just a directly constructed source.
        using var provider = new ServiceCollection()
            .AddSingleton<Winnow.Core.Repositories.ISettingsRepository>(settings)
            .AddEpicWebApi().BuildServiceProvider();
        Assert.Equal(secret, (await provider.GetRequiredService<IEpicCredentialProvider>().GetAsync())!.ClientSecret);

        foreach (var key in new[] { SettingsTableApiKeySource.ApiKeySetting,
                     SettingsTableCredentialSource.ClientSecretKey, SettingsTableEpicCredentialSource.ClientSecretSetting })
        {
            Assert.Equal(string.Empty, await settings.GetAsync(key));
        }

        foreach (var key in new[] { SettingsSteamApiKeyStore.ProtectedSetting,
                     SettingsTableCredentialSource.ClientSecretProtectedKey, SettingsTableEpicCredentialSource.ProtectedClientSecretSetting })
        {
            var value = await settings.GetAsync(key);
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.DoesNotContain(secret, value, StringComparison.Ordinal);
        }

        // Recreate readers to prove the encrypted values survive a restart.
        Assert.Equal(secret, await new SettingsSteamApiKeyStore(settings, steamProtector).GetAsync());
        Assert.Equal(secret, (await new SettingsTableCredentialSource(igdbSettings, igdbProtector).TryGetAsync())!.ClientSecret);
        Assert.Equal(secret, (await new SettingsTableEpicCredentialSource(settings, epicProtector).TryGetAsync())!.ClientSecret);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Epic_refuses_unavailable_or_corrupt_protection_without_using_legacy_secret(bool corrupt)
    {
        var settings = new InMemorySettingsRepository();
        await settings.SetAsync(SettingsTableEpicCredentialSource.ClientIdSetting, "test-client");
        await settings.SetAsync(SettingsTableEpicCredentialSource.ClientSecretSetting, "synthetic-secret");
        IEpicSecretProtector protector = corrupt
            ? new EpicWeb.EpicSecretsTests.ReversibleTestProtector()
            : new UnavailableEpicSecretProtector();
        if (corrupt)
        {
            await settings.SetAsync(SettingsTableEpicCredentialSource.ProtectedClientSecretSetting, "invalid-base64!");
        }

        Assert.Null(await new SettingsTableEpicCredentialSource(settings, protector).TryGetAsync());
        Assert.Equal("synthetic-secret", await settings.GetAsync(SettingsTableEpicCredentialSource.ClientSecretSetting));
    }
}
