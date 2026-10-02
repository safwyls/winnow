using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class ManualGameParityTests
{
    [Fact]
    public async Task Create_edit_and_delete_update_the_library_and_persist_across_restart()
    {
        await using var host = await Host.Start();
        var game = await host.Api.CreateManualGameAsync(new("Hand added", 2019, "itch.io", @"C:\Games\HandAdded\game.exe"));
        Assert.Equal(@"C:\Games\HandAdded", game.InstallPath);
        var tile = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(game.WorkId, tile.WorkId);
        Assert.Equal("Hand added", tile.Title);
        Assert.True(Assert.Single(tile.Entries).Installed);
        await host.Restart();
        var persisted = await host.Api.GetManualGameAsync(game.OwnershipId);
        Assert.Equal(game, persisted);
        var edited = await host.Api.UpdateManualGameAsync(game.OwnershipId,
            new("Corrected title", 2020, "Switch", ExpectedRevision: persisted.Revision, ExpectedIgdbMappingRevision: persisted.IgdbMappingRevision));
        Assert.Equal("Switch", edited.PlatformLabel);
        Assert.Equal("Corrected title", Assert.Single((await host.Api.GetLibraryAsync()).Games).Title);
        await host.Restart();
        Assert.Equal("Corrected title", (await host.Api.GetManualGameAsync(game.OwnershipId)).Title);
        await host.Api.DeleteManualGameAsync(game.OwnershipId);
        Assert.Empty(await host.Api.GetManualGamesAsync());
        Assert.Empty((await host.Api.GetLibraryAsync()).Games);
        await host.Restart();
        Assert.Empty(await host.Api.GetManualGamesAsync());
        Assert.Empty((await host.Api.GetLibraryAsync()).Games);
    }

    [Theory]
    [InlineData("correction")]
    [InlineData("legacy")]
    [InlineData("store")]
    [InlineData("mapping")]
    public async Task Fresh_form_identifiers_correct_safely_or_leave_stored_identifiers_unchanged(string scenario)
    {
        await using var host = await Host.Start();
        var entry = await host.Api.CreateManualGameAsync(new("Original", IgdbId: 333, SteamAppId: "123"));
        var pins = host.Services.GetRequiredService<IWorkIgdbPinRepository>();
        Assert.Equal(WorkIgdbPinOutcome.Pinned, await pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 444, Name = "Chosen title" }));
        var fresh = await host.Api.GetManualGameAsync(entry.OwnershipId);
        Assert.Equal("Chosen title", fresh.Title);
        Assert.Equal(444, fresh.IgdbId);
        if (scenario == "legacy")
        {
            using var connection = host.Services.GetRequiredService<ISqliteConnectionFactory>().Open();
            await connection.ExecuteAsync("DELETE FROM manual_entry_identifiers;");
        }
        else if (scenario == "store")
            await host.Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new() { ReleaseId = entry.ReleaseId, Store = "steam" });
        else if (scenario == "mapping")
            Assert.Equal(WorkIgdbPinOutcome.Pinned, await pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 555, Name = "Later choice" }));
        var draft = new ManualGameRequest("Corrected title", 2020, IgdbId: 666, SteamAppId: "456",
            ExpectedRevision: fresh.Revision, ExpectedIgdbMappingRevision: fresh.IgdbMappingRevision);
        if (scenario == "correction")
        {
            var saved = await host.Api.UpdateManualGameAsync(entry.OwnershipId, draft);
            Assert.Equal("Corrected title", saved.Title);
            Assert.Equal(2020, saved.FirstReleaseYear);
            Assert.Equal(666, saved.IgdbId);
            Assert.Equal("456", saved.SteamAppId);
            Assert.Equal("Corrected title", Assert.Single((await host.Api.GetLibraryAsync()).Games).Title);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<BackendApiException>(() => host.Api.UpdateManualGameAsync(entry.OwnershipId, draft));
            Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
            if (scenario != "mapping")
            {
                using var body = JsonDocument.Parse(failure.ResponseBody);
                Assert.Equal("SteamAppId", body.RootElement.GetProperty("field").GetString());
                Assert.Equal(scenario == "legacy" ? "LegacyIdentifierHistory" : "StorefrontObservation", body.RootElement.GetProperty("reason").GetString());
            }
            var saved = await host.Api.GetManualGameAsync(entry.OwnershipId);
            Assert.Equal("123", saved.SteamAppId);
            Assert.Equal(scenario == "mapping" ? 555 : 444, saved.IgdbId);
            Assert.NotEqual("Corrected title", saved.Title);
            if (scenario == "mapping") Assert.NotEqual(fresh.IgdbMappingRevision, saved.IgdbMappingRevision);
        }
    }

    [Fact]
    public async Task A_claimed_Steam_identifier_names_its_field_and_creates_no_manual_entry()
    {
        await using var host = await Host.Start();
        var work = await host.Services.GetRequiredService<IWorkRepository>().InsertAsync(new() { Name = "Store game" });
        var release = await host.Services.GetRequiredService<IReleaseRepository>().InsertAsync(new() { WorkId = work, Name = "Store game" });
        await host.Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new() { ReleaseId = release, Store = "steam" });
        await host.Services.GetRequiredService<IReleaseRepository>().AddExternalIdAsync(new() { ReleaseId = release, Provider = "steam", ProviderId = "123" });
        var failure = await Assert.ThrowsAsync<BackendApiException>(() => host.Api.CreateManualGameAsync(new("Draft", SteamAppId: "123")));
        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        using var body = JsonDocument.Parse(failure.ResponseBody);
        Assert.Equal("SteamAppId", body.RootElement.GetProperty("field").GetString());
        Assert.Equal("ClaimedByAnotherGame", body.RootElement.GetProperty("reason").GetString());
        Assert.Empty(await host.Api.GetManualGamesAsync());
        Assert.Equal("Store game", Assert.Single((await host.Api.GetLibraryAsync()).Games).Title);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-manual-tests", Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public IServiceProvider Services => _app.Services;
        public static async Task<Host> Start()
        {
            var host = new Host();
            Directory.CreateDirectory(host._directory);
            try { await host.Open(); return host; }
            catch { await host.DisposeAsync(); throw; }
        }
        private async Task Open()
        {
            _app = BackendApplication.Build(["--data-dir", _directory, "--no-sync"]);
            await _app.StartAsync();
            Api = WinnowApiClient.Attach(_directory);
        }
        public async Task Restart()
        {
            Api.Dispose(); await _app.StopAsync(); await _app.DisposeAsync(); await Open();
        }
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
