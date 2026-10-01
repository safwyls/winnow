using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class OwnershipSnapshotParityTests
{
    [Fact]
    public async Task Confirmed_11111_enables_the_account_choice_without_inventory_or_an_implicit_default_write()
    {
        await using var host = await Host.StartAsync("account-empty");
        Assert.Equal(new AccountVisibilityState(false, false, 0, 0), await host.Account());
        Assert.Null((await host.State()).AccountScope);
        Assert.Equal(0, (await host.State()).CompleteInventories);
        await host.Control("confirm");
        Assert.Equal(new AccountVisibilityState(true, false, 0, 0), await host.Account());
        Assert.Equal("11111", (await host.State()).ConfirmedAccount);
        Assert.Null((await host.State()).AccountScope);
        Assert.Equal(0, (await host.State()).CompleteInventories);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1234)]
    public async Task One_explicit_account_write_persists_the_original_own_scope_and_reopens_with_the_exact_hidden_count(int hidden)
    {
        await using var host = await Host.StartAsync("account", hidden);
        Assert.False((await host.Account()).AccountConfirmed);
        Assert.Null((await host.State()).AccountScope);
        Assert.Equal(hidden + 1, (await host.Library()).Games.Count);
        await host.Control("confirm");
        Assert.Equal(new AccountVisibilityState(true, false, hidden, 2), await host.Account());
        Assert.Null((await host.State()).AccountScope);
        var hub = host.App.Services.GetRequiredService<BackendEventHub>();
        using var events = hub.Subscribe($"{hub.Epoch}:{hub.Sequence}");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await host.Http.PutAsJsonAsync("api/v1/connections/account-visibility", new SetAccountVisibility(true));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var change = await events.Reader.ReadAsync(deadline.Token);
        Assert.Equal("library.changed", change.Kind);
        Assert.Equal("account-visibility", change.Resource);
        Assert.False(events.Reader.TryRead(out _));
        var state = await host.State();
        Assert.Equal(AccountScope.Own, state.AccountScope);
        Assert.Single(state.Requests, request => request == "PUT /api/v1/connections/account-visibility");
        Assert.Equal(1, state.CompleteInventories);
        Assert.Equal(hidden + 1, state.OwnershipCount);
        Assert.Equal("My game", Assert.Single((await host.Library()).Games).Title);
        await host.RestartAsync();
        Assert.Equal(new AccountVisibilityState(true, true, hidden, 2), await host.Account());
        Assert.Equal(AccountScope.Own, (await host.State()).AccountScope);
        Assert.Equal(1L, Assert.Single((await host.Library()).Games).WorkId);
        Assert.DoesNotContain((await host.State()).Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owned_storefront_sync_warms_the_exact_two_provider_responses_and_all_HTTP_projections_remain_read_only()
    {
        await using var host = await Host.StartAsync("owned-sync");
        Assert.Empty((await host.State()).ProviderRequests);
        await host.Control("sync");
        var state = await host.State();
        Assert.Equal(3, state.WorkCount);
        Assert.Equal(2, state.OwnershipCount);
        Assert.Equal(new[] { Winnow.Enrich.Stores.StorefrontClient.EpicMappingUrl, "https://api.gog.com/products/1207658871?expand=changelog" }, state.ProviderRequests);
        Assert.DoesNotContain(state.ProviderRequests, url => url.Contains("/99?", StringComparison.Ordinal));
        var library = await host.Library();
        Assert.Equal(new[] { "Owned Epic", "Owned GOG" }, library.Games.Select(game => game.Title).Order());
        var workspace = await host.Workspace();
        Assert.Equal("fn", workspace.EpicLaunchKeys["catalog-id"].Namespace);
        Assert.Equal("Fortnite", workspace.EpicLaunchKeys["catalog-id"].ArtifactId);
        Assert.Contains(workspace.ExternalIds, id => id.ReleaseId == 1 && id.Provider == "gog" && id.ProviderId == "1207658871");
        Assert.Contains(workspace.ExternalIds, id => id.ReleaseId == 2 && id.Provider == "epic" && id.ProviderId == "catalog-id");
        Assert.Equal("https://store.epicgames.com/p/fortnite", workspace.Storefronts["epic:fn"].StoreUrl);
        Assert.Equal("https://www.gog.com/game/panzer_general_2", workspace.Storefronts["gog:1207658871"].StoreUrl);
        Assert.Contains("Cloud Saves support", workspace.Storefronts["gog:1207658871"].PatchNotes);
        Assert.DoesNotContain("epic:bad", workspace.Storefronts.Keys);
        Assert.DoesNotContain("epic:wrong", workspace.Storefronts.Keys);
        await host.Library();
        await host.Workspace();
        await host.Control("sync");
        Assert.Equal(state.ProviderRequests, (await host.State()).ProviderRequests);
    }

    [Fact]
    public async Task Cached_Panzer_notes_remain_readable_and_missing_Epic_link_appears_only_after_the_actual_cache_changes()
    {
        await using var host = await Host.StartAsync("storefront");
        var library = await host.Library();
        var gog = Assert.Single(library.Games, game => game.WorkId == 1);
        Assert.Equal("Panzer General 2", gog.Title);
        Assert.Equal(0, gog.PlaytimeMinutes);
        Assert.Equal("gog", Assert.Single(gog.Entries).Store);
        var workspace = await host.Workspace();
        var details = workspace.Storefronts["gog:1207658871"];
        Assert.Equal("https://www.gog.com/game/panzer_general_2", details.StoreUrl);
        Assert.Contains("Internal Update", details.PatchNotes);
        Assert.Contains("Cloud Saves support", details.PatchNotes);
        Assert.Contains("\n", details.PatchNotes);
        Assert.DoesNotContain("bad()", details.PatchNotes);
        Assert.DoesNotContain("<", details.PatchNotes);
        Assert.Equal("min", workspace.EpicLaunchKeys["catalog-id"].Namespace);
        Assert.False(workspace.Storefronts.ContainsKey("epic:min"));
        Assert.Empty(StoreActions.LinksFor("epic", null, null));
        await host.Control("epic-link");
        workspace = await host.Workspace();
        Assert.Equal("https://store.epicgames.com/p/hades", Assert.Single(StoreActions.LinksFor("epic", null, null, workspace.Storefronts["epic:min"])).Uri);
        Assert.Empty((await host.State()).ProviderRequests);
        Assert.Equal(new long[] { 1, 2 }, (await host.Library()).Games.Select(game => game.WorkId).Order());
    }

    [Fact]
    public async Task Fixture_controls_and_real_account_routes_reject_unauthenticated_requests()
    {
        await using var host = await Host.StartAsync("account-empty");
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/ownership-snapshot/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("__fixture/ownership-snapshot/confirm", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("api/v1/connections/account-visibility", new SetAccountVisibility(true))).StatusCode);
        Assert.Null((await host.State()).AccountScope);
        Assert.Null((await host.State()).ConfirmedAccount);
    }

    private sealed class Host(string directory) : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;
        public HttpClient Http { get; private set; } = null!;
        private WinnowApiClient _api = null!;
        public static async Task<Host> StartAsync(string kind, int others = 1)
        {
            var host = new Host(Path.Combine(Path.GetTempPath(), "winnow-ownership-snapshot-api-" + Guid.NewGuid().ToString("N")));
            try
            {
                await host.OpenAsync();
                var seed = await host.Http.PostAsJsonAsync("__fixture/ownership-snapshot/seed", new OwnershipSnapshotSeed(kind, others));
                seed.EnsureSuccessStatusCode();
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        private async Task OpenAsync()
        {
            Directory.CreateDirectory(directory);
            App = BackendApplication.Build(["--data-dir", directory, "--no-sync"], OwnershipSnapshotFixture.Register);
            OwnershipSnapshotFixture.Map(App);
            await App.StartAsync();
            var endpoint = await BackendConnection.ReadAsync(directory);
            Http = new HttpClient { BaseAddress = new(endpoint.Address) };
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
            _api = WinnowApiClient.Attach(directory);
        }
        public async Task RestartAsync()
        {
            _api.Dispose(); Http.Dispose();
            await App.StopAsync(); await App.DisposeAsync();
            await OpenAsync();
        }
        public async Task Control(string operation)
            => (await Http.PostAsJsonAsync("__fixture/ownership-snapshot/" + operation, new { })).EnsureSuccessStatusCode();
        public async Task<OwnershipSnapshotState> State()
            => (await Http.GetFromJsonAsync<OwnershipSnapshotState>("__fixture/ownership-snapshot/state"))!;
        public Task<AccountVisibilityState> Account() => _api.GetAsync<AccountVisibilityState>("connections/account-visibility");
        public Task<LibraryResponse> Library() => _api.GetAsync<LibraryResponse>("library");
        public Task<LibraryWorkspaceResponse> Workspace() => _api.GetAsync<LibraryWorkspaceResponse>("library/workspace");
        public async ValueTask DisposeAsync()
        {
            _api?.Dispose(); Http?.Dispose();
            if (App is not null) { await App.StopAsync(); await App.DisposeAsync(); }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
