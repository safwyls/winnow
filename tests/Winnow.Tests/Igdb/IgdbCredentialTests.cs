using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// Credential resolution order: the <c>settings</c> table first — §4.2 keys are
/// user-supplied and stored locally, so that is the product path — then
/// <see cref="IConfiguration"/> for developers.
/// </summary>
public class IgdbCredentialTests
{
    [Fact]
    public async Task Settings_table_wins_over_configuration()
    {
        var provider = Build(
            settings: ("from-settings", "settings-secret"),
            configuration: ("from-config", "config-secret"));

        var credentials = await provider.GetAsync();

        Assert.NotNull(credentials);
        Assert.Equal("settings", credentials.Source);
        Assert.Equal("from-settings", credentials.ClientId);
    }

    [Fact]
    public async Task Configuration_is_used_when_the_settings_table_is_empty()
    {
        var provider = Build(settings: null, configuration: ("from-config", "config-secret"));

        var credentials = await provider.GetAsync();

        Assert.NotNull(credentials);
        Assert.Equal("configuration", credentials.Source);
        Assert.Equal("from-config", credentials.ClientId);
    }

    [Fact]
    public async Task Half_a_pair_is_not_credentials_and_falls_through_to_the_next_source()
    {
        var store = new InMemorySettingsStore();

        // A client id with no secret is a half-finished settings screen, not a
        // configured account.
        await store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "orphan-id");

        var provider = Build(store, ("from-config", "config-secret"));
        var credentials = await provider.GetAsync();

        Assert.NotNull(credentials);
        Assert.Equal("configuration", credentials.Source);
    }

    [Fact]
    public async Task No_source_configured_returns_null_rather_than_throwing()
    {
        var provider = Build(settings: null, configuration: null);

        Assert.Null(await provider.GetAsync());
    }

    [Fact]
    public async Task Whitespace_only_values_count_as_unset()
    {
        var store = new InMemorySettingsStore();
        await store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "   ");
        await store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "\t");

        Assert.Null(await Build(store, null).GetAsync());
    }

    [Fact]
    public async Task Configuration_reads_the_double_underscore_environment_variables()
    {
        const string idVariable = "Igdb__ClientId";
        const string secretVariable = "Igdb__ClientSecret";
        var previousId = Environment.GetEnvironmentVariable(idVariable);
        var previousSecret = Environment.GetEnvironmentVariable(secretVariable);

        try
        {
            Environment.SetEnvironmentVariable(idVariable, "env-client");
            Environment.SetEnvironmentVariable(secretVariable, "env-secret");

            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var source = new ConfigurationCredentialSource(configuration);

            var credentials = await source.TryGetAsync();

            Assert.NotNull(credentials);
            Assert.Equal("env-client", credentials.ClientId);
            Assert.Equal("env-secret", credentials.ClientSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(idVariable, previousId);
            Environment.SetEnvironmentVariable(secretVariable, previousSecret);
        }
    }

    [Fact]
    public async Task A_host_with_no_configuration_at_all_is_tolerated()
        => Assert.Null(await new ConfigurationCredentialSource(configuration: null).TryGetAsync());

    [Fact]
    public async Task Invalidate_makes_the_provider_re_read_its_sources()
    {
        var store = new InMemorySettingsStore();
        var provider = Build(store, null);

        Assert.Null(await provider.GetAsync());

        await store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "late-id");
        await store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "late-secret");

        // Memoised until told otherwise — the settings table is on the hot path.
        Assert.Null(await provider.GetAsync());

        provider.Invalidate();
        Assert.NotNull(await provider.GetAsync());
    }

    private static ChainedIgdbCredentialProvider Build(
        (string Id, string Secret)? settings, (string Id, string Secret)? configuration)
    {
        var store = new InMemorySettingsStore();
        if (settings is { } pair)
        {
            store.SetAsync(SettingsTableCredentialSource.ClientIdKey, pair.Id).GetAwaiter().GetResult();
            store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, pair.Secret).GetAwaiter().GetResult();
        }

        return Build(store, configuration);
    }

    private static ChainedIgdbCredentialProvider Build(
        ISettingsStore store, (string Id, string Secret)? configuration)
    {
        IConfiguration? config = null;
        if (configuration is { } pair)
        {
            config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Igdb:ClientId"] = pair.Id,
                    ["Igdb:ClientSecret"] = pair.Secret,
                })
                .Build();
        }

        return new ChainedIgdbCredentialProvider(
            [new SettingsTableCredentialSource(store), new ConfigurationCredentialSource(config)],
            NullLogger<ChainedIgdbCredentialProvider>.Instance);
    }
}
