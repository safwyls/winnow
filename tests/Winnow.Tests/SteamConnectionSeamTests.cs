using Winnow.App.Services;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Tests.SteamWeb;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-55 S5, the seam half: <see cref="StoreConnections"/> answering "what
/// Steam credentials exist" off the credential inventory, and owning the one
/// write path for an in-app Web API key.
///
/// <para>The S1 note recorded the gap these close: everything that answered "is
/// Steam configured" read the key chain, so a keyless user who had signed in was
/// told they had no credential at all.</para>
/// </summary>
public class SteamConnectionSeamTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The state that used to read as "not configured". A live session is a
    /// usable credential, and the whole keyless sign-in path depends on every
    /// caller agreeing about that.
    /// </summary>
    [Fact]
    public async Task A_signed_in_keyless_user_reads_as_configured()
    {
        var credentials = new FakeCredentialProvider
        {
            Inventory = new SteamCredentialInventory(
                HasApiKey: false,
                ApiKeySource: null,
                HasSession: true,
                SessionSource: "session",
                SessionExpiresAt: Now.AddHours(24),
                SessionUsable: true,
                SessionAccount: SteamId.FromSteamId64(76561198000000001UL)),
        };

        var connections = new StoreConnections(steamCredentials: credentials);

        Assert.True(await connections.IsSteamWebApiConfiguredAsync());

        var steam = await connections.GetSteamConnectionAsync();
        Assert.False(steam.HasApiKey);
        Assert.True(steam.HasSession);
        Assert.True(steam.HasUsableCredential);
        Assert.Equal("76561198000000001", steam.SessionAccount);
        Assert.Equal(Now.AddHours(24), steam.SessionExpiresAt);
    }

    /// <summary>
    /// A session that has died is still ON THE BOOKS — that is what lets the
    /// screen say "signed in and expired" rather than "never connected" — but it
    /// is not a credential anything can send.
    /// </summary>
    [Fact]
    public async Task An_expired_session_is_present_but_not_usable()
    {
        var credentials = new FakeCredentialProvider
        {
            Inventory = SteamCredentialInventory.Empty with
            {
                HasSession = true,
                SessionUsable = false,
                SessionExpiresAt = Now.AddHours(-1),
            },
        };

        var connections = new StoreConnections(steamCredentials: credentials);

        var steam = await connections.GetSteamConnectionAsync();
        Assert.True(steam.HasSession);
        Assert.True(steam.HasAnyCredential);
        Assert.False(steam.HasUsableCredential);
        Assert.False(await connections.IsSteamWebApiConfiguredAsync());
    }

    [Fact]
    public async Task A_key_from_the_settings_table_is_the_one_this_screen_owns()
    {
        var connections = new StoreConnections(
            steamCredentials: new FakeCredentialProvider
            {
                Inventory = SteamCredentialInventory.Empty with
                {
                    HasApiKey = true,
                    ApiKeySource = SettingsTableApiKeySource.SourceName,
                },
            });

        Assert.True((await connections.GetSteamConnectionAsync()).ApiKeyIsAppManaged);
    }

    /// <summary>
    /// A key from <c>Steam__ApiKey</c> or <c>appsettings.local.json</c> is in
    /// force and is not this screen's to delete, which is exactly the distinction
    /// the Clear button's enabled state hangs on.
    /// </summary>
    [Fact]
    public async Task A_key_from_configuration_is_not()
    {
        var connections = new StoreConnections(
            steamCredentials: new FakeCredentialProvider
            {
                Inventory = SteamCredentialInventory.Empty with
                {
                    HasApiKey = true,
                    ApiKeySource = ConfigurationApiKeySource.SourceName,
                },
            });

        var steam = await connections.GetSteamConnectionAsync();
        Assert.True(steam.HasApiKey);
        Assert.False(steam.ApiKeyIsAppManaged);
    }

    [Fact]
    public async Task A_host_with_no_steam_module_answers_nothing_rather_than_throwing()
    {
        var connections = new StoreConnections();

        Assert.Equal(SteamConnection.None, await connections.GetSteamConnectionAsync());
        Assert.False(await connections.IsSteamWebApiConfiguredAsync());

        // And the write path is a no-op rather than a crash.
        await connections.SaveSteamApiKeyAsync("ABC");
        await connections.ClearSteamApiKeyAsync();
    }

    // ══ The in-app key write path ═══════════════════════════════════════════

    /// <summary>
    /// The requirement behind the in-app field: a key that needs a restart to
    /// take effect is a field that reads as broken. The chain memoises, so the
    /// write is only half done until the memo is dropped.
    /// </summary>
    [Fact]
    public async Task Saving_a_key_writes_it_and_invalidates_the_credential_provider()
    {
        var settings = new InMemorySettingsRepository();
        var credentials = new FakeCredentialProvider();
        var confirmation = new RecordingConfirmation();

        var connections = new StoreConnections(
            steamCredentials: credentials, settings: settings, confirmation: confirmation);

        await connections.SaveSteamApiKeyAsync("  0123456789ABCDEF  ");

        Assert.Equal(
            "0123456789ABCDEF",
            await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting));
        Assert.Equal(1, credentials.Invalidations);

        // A confirmation earned by the key that was just replaced no longer names
        // a credential in force, and an account filter still trusting it would
        // hide the wrong library.
        Assert.Equal(1, confirmation.Reconciliations);
    }

    [Fact]
    public async Task Clearing_a_key_empties_the_row_and_invalidates()
    {
        var settings = new InMemorySettingsRepository();
        var credentials = new FakeCredentialProvider();
        var confirmation = new RecordingConfirmation();

        var connections = new StoreConnections(
            steamCredentials: credentials, settings: settings, confirmation: confirmation);

        await connections.SaveSteamApiKeyAsync("0123456789ABCDEF");
        await connections.ClearSteamApiKeyAsync();

        // The empty string IS the cleared state: the settings contract has no
        // delete, and a blank value already counts as unset to every reader.
        Assert.Equal(string.Empty, await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting));
        Assert.Null(SteamApiKey.TryCreate(
            await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting), "settings"));

        Assert.Equal(2, credentials.Invalidations);
        Assert.Equal(2, confirmation.Reconciliations);
    }

    /// <summary>A blank save is a clear, not a stored run of spaces.</summary>
    [Fact]
    public async Task Saving_a_blank_field_clears_rather_than_storing_whitespace()
    {
        var settings = new InMemorySettingsRepository();
        var connections = new StoreConnections(
            steamCredentials: new FakeCredentialProvider(), settings: settings);

        await connections.SaveSteamApiKeyAsync("   ");

        Assert.Equal(string.Empty, await settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting));
    }

    // ══ Doubles ═════════════════════════════════════════════════════════════

    /// <summary>The inventory, scripted, plus a count of how often it was told to forget what it knew.</summary>
    private sealed class FakeCredentialProvider : ISteamCredentialProvider
    {
        public SteamCredentialInventory Inventory { get; set; } = SteamCredentialInventory.Empty;

        public int Invalidations { get; private set; }

        public ValueTask<SteamCredential?> GetAsync(
            SteamCredentialPurpose purpose, CancellationToken ct = default)
            => ValueTask.FromResult<SteamCredential?>(null);

        public ValueTask<SteamCredentialInventory> GetInventoryAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Inventory);

        public void Invalidate() => Invalidations++;
    }

    private sealed class RecordingConfirmation : ISteamAccountConfirmation
    {
        public int Reconciliations { get; private set; }

        public Task<bool> ConfirmAsync(
            SteamId steamId, SteamAccountConfirmationSource source, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> ReconcileAsync(CancellationToken ct = default)
        {
            Reconciliations++;
            return Task.FromResult(false);
        }

        public Task<string?> GetConfirmedAccountRefAsync(CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> GetRecordedFingerprintAsync(CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<bool> IsInForceAsync(string fingerprint, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
