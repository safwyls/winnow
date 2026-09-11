using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Winnow.Enrich.SteamGridDb;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamGridDbCredentialsTests
{
    [Fact]
    public async Task Saved_key_is_protected_not_returned_by_status_and_removal_restores_configuration()
    {
        using var db = new TempDatabase();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SteamGridDb:ApiKey"] = "config-fixture-key" }).Build();
        var store = new SteamGridDbSettingsStore(db.Factory, new Protector(), config);
        Assert.Equal("config-fixture-key", await store.GetApiKeyAsync());
        Assert.True(await store.SaveAsync("saved-fixture-key"));
        Assert.Equal("saved-fixture-key", await store.GetApiKeyAsync());
        var status = await store.GetStatusAsync();
        Assert.True(status.HasStoredKey);
        Assert.Equal("saved", status.Source);
        Assert.DoesNotContain("saved-fixture-key", status.ToString(), StringComparison.Ordinal);
        using (var lease = db.Factory.Lease())
        {
            var persisted = await lease.Connection.QuerySingleAsync<string>("SELECT value FROM settings WHERE key=@key;",
                new { key = SteamGridDbSettingsStore.SettingsKey });
            Assert.DoesNotContain("saved-fixture-key", persisted, StringComparison.Ordinal);
        }
        await store.RemoveAsync();
        Assert.False((await store.GetStatusAsync()).HasStoredKey);
        Assert.Equal("config-fixture-key", await store.GetApiKeyAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Failed_protection_never_replaces_an_existing_key(bool available, bool refuse)
    {
        using var db = new TempDatabase();
        var protector = new Protector();
        var store = new SteamGridDbSettingsStore(db.Factory, protector, new ConfigurationBuilder().Build());
        Assert.True(await store.SaveAsync("old-fixture-key"));
        protector.IsAvailable = available;
        protector.Refuse = refuse;
        Assert.False(await store.SaveAsync("new-fixture-key"));
        Assert.Equal("old-fixture-key", await store.GetApiKeyAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("key\r\nInjected: value")]
    [InlineData("two words")]
    public async Task Invalid_key_is_not_persisted(string key)
    {
        using var db = new TempDatabase();
        var store = new SteamGridDbSettingsStore(db.Factory, new Protector(), new ConfigurationBuilder().Build());
        Assert.False(await store.SaveAsync(key));
        Assert.False((await store.GetStatusAsync()).HasStoredKey);
    }

    [Fact]
    public void Platform_protector_refuses_plaintext_and_roundtrips_only_where_available()
    {
        var protector = new SteamGridDbSecretProtector();
        Assert.Null(protector.Unprotect("plaintext-fixture"));
        var protectedKey = protector.Protect("fixture-key");
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(protectedKey);
            Assert.NotEqual("fixture-key", protectedKey);
            Assert.Equal("fixture-key", protector.Unprotect(protectedKey));
        }
        else Assert.Null(protectedKey);
    }

    private sealed class Protector : ISteamGridDbSecretProtector
    {
        public bool IsAvailable { get; set; } = true;
        public bool Refuse { get; set; }
        public string? Protect(string plaintext) => Refuse ? null : "fixture:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string? Unprotect(string ciphertext) => ciphertext.StartsWith("fixture:", StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext[8..])) : null;
    }
}
