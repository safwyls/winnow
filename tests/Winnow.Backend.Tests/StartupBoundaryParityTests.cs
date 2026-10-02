using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class StartupBoundaryParityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task First_library_read_preserves_twenty_works_one_snapshot_lease_and_the_saved_list_before_all_startup_models_read(bool fullscreen, bool setup)
    {
        await using var host = await Host.StartAsync(fullscreen, setup);
        Assert.Empty(host.Database.Calls);
        var library = await host.Api.GetLibraryAsync();
        Assert.Equal(20, library.Games.Count);
        Assert.Equal(Enumerable.Range(1, 20).Select(id => (long)id), library.Games.Select(game => game.WorkId).Order());
        Assert.All(library.Games, game =>
        {
            Assert.Equal("Game " + game.WorkId, game.Title);
            var entry = Assert.Single(game.Entries);
            Assert.Equal(game.WorkId, entry.WorkId);
            Assert.Equal(game.WorkId, entry.ReleaseId);
            Assert.Equal(game.WorkId, entry.OwnershipId);
            Assert.Equal("steam", entry.Store);
            Assert.False(entry.Installed);
        });
        var list = Assert.Single(library.Lists);
        Assert.Equal("Try next", list.Name);
        Assert.Equal(new[] { 20L, 1L }, list.ReleaseIds);
        var first = Assert.Single(host.Database.Calls);
        Assert.Equal("/api/v1/library", first.Route);
        Assert.Single(first.Leases, lease => lease.Operation == "library.snapshot");
        Assert.Equal(3, first.Leases.Count(lease => lease.Operation == "request"));
        var snapshot = Assert.Single(first.Snapshots);
        Assert.Equal((20, 20, 20), (snapshot.WorkCount, snapshot.OwnershipCount, snapshot.BucketCount));
        Assert.Equal(new[] { 20L, 1L }, snapshot.ListReleaseIds);

        // These are the shared application replacements for MergeQueue, DisplaySettings and LibrarySettings.
        foreach (var route in new[] { "identity/review/", "preferences/presentation", "preferences/library", "hidden-games", "manual-games", "library/visibility-counts" })
        {
            var response = await host.Api.GetAsync<JsonElement>(route);
            Assert.True(response.ValueKind is JsonValueKind.Object or JsonValueKind.Array);
            var call = Assert.Single(host.Database.Calls, call => call.Route == "/api/v1/" + route);
            Assert.True(call.Completed);
            Assert.Equal(200, call.Status);
            Assert.NotEmpty(call.Leases);
        }
        Assert.All(host.Database.Calls.SelectMany(call => call.Leases), lease =>
        {
            Assert.Equal(Environment.ProcessId, lease.ProcessId);
            Assert.True(lease.ThreadPool);
            Assert.Null(lease.SynchronizationContext);
        });
        var progress = await host.Api.GetAsync<JsonElement>("setup");
        Assert.Equal(setup ? JsonValueKind.Number : JsonValueKind.Null, progress.GetProperty("step").ValueKind);
        if (setup) Assert.Equal(0, progress.GetProperty("step").GetInt32());
        var settings = await host.Api.GetAsync<JsonElement>("preferences/presentation");
        Assert.Equal(fullscreen ? "true" : "false", Assert.Single(settings.EnumerateArray(), value => value.GetProperty("preference").GetString() == "StartInFullscreen").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Startup_read_ledger_requires_the_real_backend_bearer_token()
    {
        await using var host = await Host.StartAsync(false, false);
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/startup-boundary/state")).StatusCode);
        var state = JsonDocument.Parse(await host.Http.GetStringAsync("__fixture/startup-boundary/state"));
        Assert.Equal(Environment.ProcessId, state.RootElement.GetProperty("processId").GetInt32());
        Assert.True(state.RootElement.GetProperty("ready").GetBoolean());
    }

    private sealed class Host(string directory, WebApplication app, WinnowApiClient api, HttpClient http) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public HttpClient Http => http;
        public StartupReadDatabase Database => app.Services.GetRequiredService<StartupReadDatabase>();
        public static async Task<Host> StartAsync(bool fullscreen, bool setup)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-startup-boundary-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services => StartupBoundaryFixture.Register(services, directory));
            await app.Services.GetRequiredService<StartupBoundaryFixture>().InitializeAsync(fullscreen, setup);
            StartupBoundaryFixture.Map(app);
            await app.StartAsync();
            var connection = await BackendConnection.ReadAsync(directory);
            var http = new HttpClient { BaseAddress = new(connection.Address) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            return new(directory, app, WinnowApiClient.Attach(directory), http);
        }
        public async ValueTask DisposeAsync()
        {
            api.Dispose(); http.Dispose();
            await app.StopAsync(); await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
