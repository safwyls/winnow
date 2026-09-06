using System.Text;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Fixtures for the stored Web API key — the store behind
/// <see cref="SettingsTableApiKeySource"/>, and the stand-in protectors its
/// tests run under instead of real DPAPI.
/// </summary>
internal static class SteamApiKeyFixtures
{
    /// <summary>
    /// A reversible stand-in for DPAPI, so the store tests assert the <i>shape</i>
    /// of protection (that nothing readable is written and that it round-trips)
    /// without depending on a real Windows user profile. Base64 is not
    /// encryption and is not pretending to be; the test that matters for real
    /// encryption is the DPAPI round-trip below.
    /// </summary>
    public sealed class ReversibleProtector : ISteamApiKeyProtector
    {
        public bool IsAvailable => true;

        public string Name => "test:reversible";

        public string? Protect(string plaintext)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string? protectedBase64)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(protectedBase64 ?? ""));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// What the Web API key store puts on disk, and what it must never: the same
/// binding conditions §4.7's second amendment makes for the session — refuse
/// rather than degrade to plaintext, and migrate a pre-protection install's
/// plaintext row rather than keeping it readable.
/// </summary>
public sealed class SteamApiKeyStoreTests
{
    private const string Key = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public async Task A_key_round_trips_through_the_store()
    {
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamApiKeyStore(settings, new SteamApiKeyFixtures.ReversibleProtector());

        Assert.Equal(SteamApiKeySaveOutcome.Stored, await store.SaveAsync(Key));
        Assert.Equal(Key, await store.GetAsync());
    }

    /// <summary>
    /// The stored row is the protected blob, not the key. What is at rest must be
    /// unreadable to anything that reads the database without this Windows user —
    /// the whole point of closing the plaintext gap.
    /// </summary>
    [Fact]
    public async Task The_stored_row_never_contains_the_key_itself()
    {
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamApiKeyStore(settings, new SteamApiKeyFixtures.ReversibleProtector());

        await store.SaveAsync(Key);

        var stored = await settings.GetAsync(SettingsSteamApiKeyStore.ProtectedSetting, ct: default);
        Assert.DoesNotContain(Key, stored, StringComparison.Ordinal);
        Assert.NotEqual(Key, stored);
    }

    /// <summary>
    /// A host that cannot encrypt refuses the save outright. No protected row, no
    /// plaintext row, and no partial state a later read could mistake for a key.
    /// </summary>
    [Fact]
    public async Task A_host_that_cannot_encrypt_refuses_to_store()
    {
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamApiKeyStore(settings, new UnavailableSteamApiKeyProtector());

        Assert.Equal(SteamApiKeySaveOutcome.Refused, await store.SaveAsync(Key));
        Assert.Null(await settings.GetAsync(SettingsSteamApiKeyStore.ProtectedSetting, ct: default));
        Assert.Null(await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct: default));
        Assert.Null(await store.GetAsync());
    }

    /// <summary>
    /// The upgrade path: an install from before protected storage has its key in
    /// the plaintext row. The first read migrates it — protected row written,
    /// plaintext row left empty — and serves the key, because the value was
    /// already at rest and moving it removes plaintext rather than adding it.
    /// </summary>
    [Fact]
    public async Task A_plaintext_row_from_an_earlier_version_is_migrated_on_first_read()
    {
        var settings = new InMemorySettingsRepository();
        await settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, Key);
        var store = new SettingsSteamApiKeyStore(settings, new SteamApiKeyFixtures.ReversibleProtector());

        Assert.Equal(Key, await store.GetAsync());

        // Migrated: protected row written, plaintext row emptied.
        var protectedValue = await settings.GetAsync(SettingsSteamApiKeyStore.ProtectedSetting, ct: default);
        Assert.DoesNotContain(Key, protectedValue, StringComparison.Ordinal);
        Assert.Equal(
            string.Empty, await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct: default));

        // And it still reads the second time, from the protected row alone.
        Assert.Equal(Key, await store.GetAsync());
    }

    /// <summary>
    /// Refusing is not license to destroy. On a host that cannot encrypt, the
    /// plaintext row an earlier version wrote is left exactly where it was —
    /// unusable through Winnow, but not deleted by a build the user never asked
    /// to clean up after. The row keeps paying out to nobody.
    /// </summary>
    [Fact]
    public async Task A_host_that_cannot_encrypt_leaves_a_plaintext_row_untouched()
    {
        var settings = new InMemorySettingsRepository();
        await settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, Key);
        var store = new SettingsSteamApiKeyStore(settings, new UnavailableSteamApiKeyProtector());

        Assert.Null(await store.GetAsync());
        Assert.Equal(Key, await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct: default));
        Assert.Null(await settings.GetAsync(SettingsSteamApiKeyStore.ProtectedSetting, ct: default));
    }

    /// <summary>
    /// A protected row that cannot be read here (a different Windows user, a
    /// profile restored onto another machine, a truncated write) reads as no
    /// key, not as an error — and does not fall through to the plaintext row.
    /// </summary>
    [Fact]
    public async Task An_unreadable_protected_row_is_no_key_rather_than_an_error()
    {
        var settings = new InMemorySettingsRepository();
        await settings.SetAsync(SettingsSteamApiKeyStore.ProtectedSetting, "not base64 at all !!");
        var store = new SettingsSteamApiKeyStore(
            settings,
            new SteamApiKeyFixtures.ReversibleProtector());

        Assert.Null(await store.GetAsync());

        // No fall-through: the plaintext row is still unset, so a blob this host
        // cannot read is not an invitation to look somewhere else.
        Assert.Null(await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct: default));
    }

    [Fact]
    public async Task Clearing_empties_both_rows()
    {
        var settings = new InMemorySettingsRepository();
        await settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, Key);
        var store = new SettingsSteamApiKeyStore(settings, new SteamApiKeyFixtures.ReversibleProtector());

        await store.SaveAsync(Key);
        await store.ClearAsync();

        Assert.Null(await store.GetAsync());
        Assert.Equal(
            string.Empty, await settings.GetAsync(SettingsSteamApiKeyStore.ProtectedSetting, ct: default));
        Assert.Equal(
            string.Empty, await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct: default));
    }

    /// <summary>
    /// Saving whitespace is clearing, not a stored run of spaces — the seam the
    /// panel relies on when the field is emptied by the user.
    /// </summary>
    [Fact]
    public async Task Saving_whitespace_clears_rather_than_storing_it()
    {
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamApiKeyStore(settings, new SteamApiKeyFixtures.ReversibleProtector());

        Assert.Equal(SteamApiKeySaveOutcome.Stored, await store.SaveAsync(Key));
        Assert.Equal(SteamApiKeySaveOutcome.Stored, await store.SaveAsync("   "));

        Assert.Null(await store.GetAsync());
    }

    /// <summary>
    /// The entropy is distinct on purpose. The session is the account and the
    /// key reads its library; neither must open the other's ciphertext, so one
    /// module's blob is never a valid answer to the other's question.
    /// </summary>
    [Fact]
    public void The_session_protector_cannot_read_a_key_blob_and_vice_versa()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var keyProtector = new DpapiSteamApiKeyProtector();
        var sessionProtector = new DpapiSteamSecretProtector();

        var keyBlob = keyProtector.Protect("an-api-key")!;
        var sessionBlob = sessionProtector.Protect("a-refresh-token")!;

        Assert.Null(sessionProtector.Unprotect(keyBlob));
        Assert.Null(keyProtector.Unprotect(sessionBlob));
        Assert.Equal("an-api-key", keyProtector.Unprotect(keyBlob));
    }

    [Fact]
    public void Dpapi_round_trips_a_key_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiSteamApiKeyProtector();

        var cipher = protector.Protect(Key);

        Assert.NotNull(cipher);
        Assert.DoesNotContain(Key, cipher, StringComparison.Ordinal);
        Assert.Equal(Key, protector.Unprotect(cipher));

        // Garbage in, null out, never an exception.
        Assert.Null(protector.Unprotect("not base64 at all !!"));
        Assert.Null(protector.Unprotect(Convert.ToBase64String([1, 2, 3, 4])));
        Assert.Null(protector.Unprotect(string.Empty));
        Assert.Null(protector.Unprotect(null));
    }

    /// <summary>
    /// The registration the host composition relies on: DPAPI on Windows, a
    /// refusal anywhere else, and the source reading through the store so the
    /// key the panel saves is the key the chain resolves.
    /// </summary>
    [Fact]
    public async Task The_store_is_composed_with_a_protector_and_the_source_reads_through_it()
    {
        var settings = new InMemorySettingsRepository();
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsRepository>(settings);
        services.AddSteamWebApi();

        using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<ISteamApiKeyProtector>();
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DpapiSteamApiKeyProtector>(protector);
            Assert.True(protector.IsAvailable);
        }
        else
        {
            Assert.IsType<UnavailableSteamApiKeyProtector>(protector);
            Assert.False(protector.IsAvailable);
        }

        // The source is constructed by the container off the store, which is what
        // keeps "what the panel saved" and "what the chain resolves" one fact.
        // Several sources are registered, so this picks the settings-table one
        // out of the chain by name rather than relying on resolution order.
        var sources = provider.GetRequiredService<IEnumerable<ISteamApiKeySource>>();
        var settingsSource = Assert.Single(
            sources, s => string.Equals(s.Name, SettingsTableApiKeySource.SourceName, StringComparison.Ordinal));
        Assert.Null(await settingsSource.TryGetAsync());
    }
}
