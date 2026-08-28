using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Key resolution order: the <c>settings</c> table first — §4.2 keys are
/// user-supplied and stored locally, so that is the product path — then
/// <see cref="IConfiguration"/> for developers.
/// </summary>
public class SteamWebCredentialTests
{
    [Fact]
    public async Task Settings_table_wins_over_configuration()
    {
        var provider = Build(settings: "from-settings", configuration: "from-config");

        var key = await provider.GetAsync();

        Assert.NotNull(key);
        Assert.Equal("settings", key.Source);
        Assert.Equal("from-settings", key.Value);
    }

    [Fact]
    public async Task Configuration_is_used_when_the_settings_table_is_empty()
    {
        var provider = Build(settings: null, configuration: "from-config");

        var key = await provider.GetAsync();

        Assert.NotNull(key);
        Assert.Equal("configuration", key.Source);
        Assert.Equal("from-config", key.Value);
    }

    [Fact]
    public async Task No_source_configured_returns_null_rather_than_throwing()
        => Assert.Null(await Build(settings: null, configuration: null).GetAsync());

    [Fact]
    public async Task Whitespace_only_values_count_as_unset()
        => Assert.Null(await Build(settings: "   ", configuration: null).GetAsync());

    [Fact]
    public async Task Surrounding_whitespace_is_trimmed_off_a_pasted_key()
    {
        var key = await Build(settings: "  ABCDEF0123456789  ", configuration: null).GetAsync();

        Assert.NotNull(key);
        Assert.Equal("ABCDEF0123456789", key.Value);
    }

    [Fact]
    public async Task Configuration_reads_the_double_underscore_environment_variable()
    {
        const string variable = "Steam__ApiKey";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            Environment.SetEnvironmentVariable(variable, "env-key");

            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var key = await new ConfigurationApiKeySource(configuration).TryGetAsync();

            Assert.NotNull(key);
            Assert.Equal("env-key", key.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public async Task A_host_with_no_configuration_at_all_is_tolerated()
        => Assert.Null(await new ConfigurationApiKeySource(configuration: null).TryGetAsync());

    [Fact]
    public async Task Invalidate_makes_the_provider_re_read_its_sources()
    {
        var settings = new InMemorySettingsRepository();
        var provider = BuildOver(settings, configuration: null);

        Assert.Null(await provider.GetAsync());

        await settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, "late-key");

        // Memoised until told otherwise — the settings table is on the hot path.
        Assert.Null(await provider.GetAsync());

        provider.Invalidate();
        Assert.NotNull(await provider.GetAsync());
    }

    [Fact]
    public void ToString_redacts_the_key()
    {
        var key = SteamApiKey.TryCreate("SUPERSECRETKEYVALUE", "settings");

        Assert.NotNull(key);

        // The compiler-generated record ToString would print Value. This is the
        // guard against the first person who interpolates one into a log line.
        var rendered = $"{key}";
        Assert.DoesNotContain("SUPERSECRETKEYVALUE", rendered, StringComparison.Ordinal);
        Assert.Contains("redacted", rendered, StringComparison.Ordinal);
        Assert.Contains("settings", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolving_a_key_logs_the_source_and_never_the_value()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));

        var settings = new InMemorySettingsRepository();
        await settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, "SUPERSECRETKEYVALUE");

        var provider = new ChainedSteamApiKeyProvider(
            [new SettingsTableApiKeySource(settings)],
            factory.CreateLogger<ChainedSteamApiKeyProvider>());

        Assert.NotNull(await provider.GetAsync());

        Assert.DoesNotContain("SUPERSECRETKEYVALUE", logs.AllText, StringComparison.Ordinal);
        Assert.Contains("settings", logs.AllText, StringComparison.Ordinal);
    }

    private static ChainedSteamApiKeyProvider Build(string? settings, string? configuration)
    {
        var store = new InMemorySettingsRepository();
        if (settings is not null)
        {
            store.SetAsync(SettingsTableApiKeySource.ApiKeySetting, settings).GetAwaiter().GetResult();
        }

        return BuildOver(store, configuration);
    }

    private static ChainedSteamApiKeyProvider BuildOver(ISettingsRepository settings, string? configuration)
    {
        IConfiguration? config = null;
        if (configuration is not null)
        {
            config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Steam:ApiKey"] = configuration })
                .Build();
        }

        return new ChainedSteamApiKeyProvider(
            [new SettingsTableApiKeySource(settings), new ConfigurationApiKeySource(config)],
            NullLogger<ChainedSteamApiKeyProvider>.Instance);
    }
}
