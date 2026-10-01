using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Core.Queries;
using Winnow.Core.Auth;
using Winnow.Electron.Fixtures;
using Winnow.Ingest.Epic.Web.Auth;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class PluginProvenanceParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_legacy_Epic_prompt_policy_is_callable_without_prompts_or_with_two_unavailable_prompts(bool unavailable)
    {
        await using var host = await Host.StartAsync("settings", unavailable);
        var prompts = host.App.Services.GetServices<IInteractiveAuthPrompt>().ToArray();
        Assert.Equal(unavailable ? 2 : 0, prompts.Length);
        // Electron HTTP uses BuildRequest and explicit completion; this invokes the still-registered
        // legacy prompt policy directly and makes no claim that the HTTP route iterates these prompts.
        var result = await host.App.Services.GetRequiredService<EpicInteractiveSignIn>().SignInAsync();
        Assert.False(result.Succeeded); Assert.Equal(EpicSignInFailure.NoInteractivePrompt, result.Failure);
        Assert.Empty((await host.State()).ProviderRequests);
    }

    [Fact]
    public async Task Grouped_Steam_primary_keeps_the_secondary_Xbox_history_observation_and_no_invented_actions()
    {
        await using var host = await Host.StartAsync("grouped");
        var game = Assert.Single((await host.Library()).Games);
        Assert.Equal(1, game.WorkId); Assert.Equal("Fixture", game.Title);
        Assert.Equal(LibraryBuckets.NeverPlayed, game.Bucket);
        Assert.Equal(0, game.PlaytimeMinutes); Assert.Null(game.LastPlayedAt);
        Assert.Equal(new long[] { 1, 2 }, game.Entries.Select(entry => entry.OwnershipId));
        Assert.Equal(new[] { "steam", "plugin:xbox" }, game.Entries.Select(entry => entry.Store));
        Assert.All(game.Entries, entry => { Assert.Equal(1, entry.WorkId); Assert.False(entry.Installed); Assert.Null(entry.LastPlayedAt); });
        var workspace = await host.Workspace();
        var actions = Assert.Single(workspace.PluginActions);
        Assert.Equal(2, actions.Key); Assert.Equal(PluginProvenanceFixture.SourceLabel, actions.Value.SourceLabel);
        Assert.False(actions.Value.CanPlay); Assert.False(actions.Value.CanOpenStore);
        Assert.Equal(new long[] { 1, 2 }, (await host.Details()).Ownerships.Select(entry => entry.Id));
        Assert.Empty((await host.State()).ProviderRequests);
    }

    [Fact]
    public async Task PlayStation_history_retains_sixty_minutes_yesterday_and_exact_source_without_actions()
    {
        await using var host = await Host.StartAsync("psn");
        var state = await host.State();
        var game = Assert.Single((await host.Library()).Games);
        Assert.Equal("Fixture", game.Title); Assert.Equal(LibraryBuckets.Active, game.Bucket);
        Assert.Equal(60, game.PlaytimeMinutes); Assert.Equal(state.Now.AddDays(-1), game.LastPlayedAt);
        var entry = Assert.Single(game.Entries);
        Assert.Equal("plugin:psn", entry.Store); Assert.Equal(1, entry.OwnershipId); Assert.False(entry.Installed);
        var workspace = await host.Workspace();
        var actions = Assert.Single(workspace.PluginActions).Value;
        Assert.Equal(PluginProvenanceFixture.SourceLabel, actions.SourceLabel);
        Assert.False(actions.CanPlay); Assert.False(actions.CanOpenStore);
        Assert.Single((await host.Details()).Ownerships);
        Assert.Empty((await host.State()).ProviderRequests);
    }

    [Fact]
    public async Task Exact_two_game_filter_population_keeps_one_PlayStation_entry_and_the_original_list_order()
    {
        await using var host = await Host.StartAsync("filter");
        var library = await host.Library();
        Assert.Equal(new[] { "Game 1", "Game 2" }, library.Games.Select(game => game.Title));
        Assert.Equal(new long[] { 2, 1 }, Assert.Single(library.Lists).ReleaseIds);
        var workspace = await host.Workspace();
        var psn = Assert.Single(workspace.Ownerships, entry => entry.Store == "plugin:psn");
        Assert.Equal(2, psn.Id); Assert.Equal(2, psn.ReleaseId);
        Assert.Equal("Game 2", Assert.Single(library.Games, game => game.Entries.Any(entry => entry.OwnershipId == psn.Id)).Title);
        Assert.Equal(new[] { "1", "2" }, workspace.ExternalIds.Where(id => id.Provider == "steam").Select(id => id.ProviderId));
        Assert.Empty((await host.State()).ProviderRequests);
    }

    [Fact]
    public async Task Real_packaged_PSN_settings_mask_save_omit_and_remove_the_exact_source_secret_and_history_options()
    {
        await using var host = await Host.StartAsync("settings");
        var state = await host.State();
        Assert.True(state.Loaded); Assert.True(state.Isolated);
        Assert.DoesNotContain(typeof(BackendApplication).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("Winnow.Plugin.", StringComparison.Ordinal) == true);
        Assert.Equal(new[] { "Winnow.PluginSdk" }, typeof(Winnow.PluginFixture.PresentationFixturePlugin).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).Where(name => name!.StartsWith("Winnow.", StringComparison.Ordinal)));
        var plugin = await host.Plugin();
        Assert.Equal("PlayStation", plugin.Name); Assert.True(plugin.IsLoaded); Assert.True(plugin.Enabled);
        Assert.False(plugin.HasAccount); Assert.Equal("Library sources, Metadata, Artwork", plugin.Capabilities);
        Assert.Equal(new[] { "npsso", "import-history", "include-legacy" }, plugin.Settings.Select(field => field.Key));
        var secret = plugin.Settings[0];
        Assert.True(secret.IsSecret); Assert.True(secret.HasStoredSecret); Assert.Null(secret.Value);
        Assert.Equal("https://ca.account.sony.com/api/v1/ssocookie", secret.SetupUrl);
        Assert.All(plugin.Settings.Skip(1), field => { Assert.True(field.IsBoolean); Assert.Equal("false", field.Value); });
        var values = new Dictionary<string, string> { ["npsso"] = "fixture-session-token", ["import-history"] = "true", ["include-legacy"] = "true" };
        (await host.Http.PutAsJsonAsync("api/v1/connections/plugins/psn/settings", new PluginSettingsValues(values))).EnsureSuccessStatusCode();
        state = await host.State();
        Assert.Equal(values, Assert.Single(state.Writes)); Assert.Equal("fixture-session-token", state.Secret); Assert.Equal(1, state.RefreshRequests);
        Assert.Null((await host.Plugin()).Settings[0].Value);
        values.Remove("npsso");
        (await host.Http.PutAsJsonAsync("api/v1/connections/plugins/psn/settings", new PluginSettingsValues(values))).EnsureSuccessStatusCode();
        state = await host.State();
        Assert.Equal(values, state.Writes[1]); Assert.Equal("fixture-session-token", state.Secret); Assert.True(state.StoredSecret);
        (await host.Http.DeleteAsync("api/v1/connections/plugins/psn/secrets/npsso")).EnsureSuccessStatusCode();
        state = await host.State();
        Assert.Equal(new[] { "npsso" }, state.RemovedSecrets); Assert.False(state.StoredSecret); Assert.Null(state.Secret);
        Assert.Equal(3, state.RefreshRequests); Assert.Equal("true", state.ImportHistory); Assert.Equal("true", state.IncludeLegacy);
        Assert.False((await host.Plugin()).Settings[0].HasStoredSecret); Assert.Empty(state.ProviderRequests);
    }

    [Fact]
    public async Task Fixture_controls_and_plugin_secret_routes_require_the_actual_backend_bearer_token()
    {
        await using var host = await Host.StartAsync("settings");
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/plugin-provenance/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("api/v1/connections/plugins/psn/secrets/npsso")).StatusCode);
        Assert.True((await host.State()).StoredSecret); Assert.Empty((await host.State()).RemovedSecrets);
    }

    private sealed class Host(string directory) : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;
        public HttpClient Http { get; private set; } = null!;
        private WinnowApiClient _api = null!;
        public static async Task<Host> StartAsync(string kind, bool unavailablePrompts = false)
        {
            var host = new Host(Path.Combine(Path.GetTempPath(), "winnow-plugin-provenance-api-" + Guid.NewGuid().ToString("N")));
            try
            {
                host.App = BackendApplication.Build(["--data-dir", host.directory, "--no-sync"], services =>
                {
                    PluginProvenanceFixture.Register(services);
                    if (!unavailablePrompts) return;
                    services.AddSingleton<IInteractiveAuthPrompt>(new UnavailablePrompt("browser"));
                    services.AddSingleton<IInteractiveAuthPrompt>(new UnavailablePrompt("console"));
                });
                await host.App.Services.GetRequiredService<PluginProvenanceFixture>().InitializeAsync(host.directory);
                PluginProvenanceFixture.Map(host.App);
                await host.App.StartAsync();
                var endpoint = await BackendConnection.ReadAsync(host.directory);
                host.Http = new HttpClient { BaseAddress = new(endpoint.Address) };
                host.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
                host._api = WinnowApiClient.Attach(host.directory);
                (await host.Http.PostAsJsonAsync("__fixture/plugin-provenance/seed", new ProvenanceSeed(kind))).EnsureSuccessStatusCode();
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        private readonly string directory = directory;
        public async Task<ProvenanceState> State() => (await Http.GetFromJsonAsync<ProvenanceState>("__fixture/plugin-provenance/state"))!;
        public Task<LibraryResponse> Library() => _api.GetAsync<LibraryResponse>("library");
        public Task<LibraryWorkspaceResponse> Workspace() => _api.GetAsync<LibraryWorkspaceResponse>("library/workspace");
        public Task<GameDetailsResponse> Details() => _api.GetAsync<GameDetailsResponse>("games/1/details");
        public async Task<PluginSettingsSnapshot> Plugin() => Assert.Single(await _api.GetAsync<PluginSettingsSnapshot[]>("connections/plugins"), plugin => plugin.Id == "psn");
        public async ValueTask DisposeAsync()
        {
            _api?.Dispose(); Http?.Dispose();
            if (App is not null) { await App.StopAsync(); await App.DisposeAsync(); }
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { /* The collectible fixture assembly may still be mapped on Windows. */ }
        }
    }

    private sealed class UnavailablePrompt(string name) : IInteractiveAuthPrompt
    {
        public string Name => name;
        public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default) => ValueTask.FromResult(false);
        public Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Unavailable prompts must not capture credentials.");
    }
}
