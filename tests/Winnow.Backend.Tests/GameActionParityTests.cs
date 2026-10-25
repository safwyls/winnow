using Microsoft.AspNetCore.Builder;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Actions;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>Replacement for tile URI assertions at the production action dispatch boundary.</summary>
public sealed class GameActionParityTests
{
    [Theory]
    [InlineData(true, 9007199254740992L)]
    [InlineData(false, 9007199254740993L)]
    [InlineData(true, long.MaxValue)]
    [InlineData(false, long.MaxValue)]
    public async Task Primary_action_preserves_Int64_identity_and_selects_current_play_or_install(bool installed, long id)
    {
        await using var host = await Host.Start("steam", installed, "620", id);
        var operation = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => host.Dispatch(GameActionKind.Primary, operation)));
        Assert.All(results, result => Assert.Equal(LaunchDispatch.HandedOff, result));
        var dispatched = Assert.Single(host.Launcher.Calls);
        Assert.Equal(id, dispatched.Id);
        Assert.Equal(installed ? "steam://run/620" : "steam://install/620", dispatched.Link.Uri);
        Assert.Equal(installed, dispatched.Link.StartsGame);
        var conflict = await Assert.ThrowsAsync<BackendApiException>(() => host.Dispatch(GameActionKind.Uninstall, operation));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Single(host.Launcher.Calls);
    }

    [Theory]
    [InlineData("steam", true, "620", "steam://run/620")]
    [InlineData("steam", false, "620", "steam://install/620")]
    [InlineData("gog", true, "1971477531", "goggalaxy://launchgame/gog_1971477531")]
    [InlineData("gog", false, "1971477531", "goggalaxy://installationscreen/1971477531")]
    [InlineData("epic", true, "7a70b499513441c792b541d53505e0b2", "com.epicgames.launcher://apps/41f47fd0d3e248bc938a5815d6d64daa%3A7a70b499513441c792b541d53505e0b2%3ABluebird?action=launch&silent=true")]
    [InlineData("epic", false, "7a70b499513441c792b541d53505e0b2", "com.epicgames.launcher://apps/41f47fd0d3e248bc938a5815d6d64daa%3A7a70b499513441c792b541d53505e0b2%3ABluebird?action=install")]
    public async Task Store_actions_keep_the_measured_launcher_URI_and_action_kind(string store, bool installed, string identifier, string expected)
    {
        await using var host = await Host.Start(store, installed, identifier);
        var action = installed ? GameActionKind.Play : GameActionKind.Install;
        Assert.Equal(LaunchDispatch.HandedOff, await host.Dispatch(action));
        var dispatched = Assert.Single(host.Launcher.Calls);
        Assert.Equal(host.OwnershipId, dispatched.Id);
        Assert.Equal(expected, dispatched.Link.Uri);
        Assert.Equal(installed, dispatched.Link.StartsGame);
        Assert.Equal(installed ? GameLinkKind.Play : GameLinkKind.Install, dispatched.Link.Kind);
    }

    [Theory]
    [InlineData("steam", true, "620", GameActionKind.Uninstall, "steam://uninstall/620")]
    [InlineData("gog", true, "1971477531", GameActionKind.Manage, "goggalaxy://opengameview/gog_1971477531")]
    [InlineData("gog", false, "1971477531", GameActionKind.Manage, "goggalaxy://opengameview/gog_1971477531")]
    [InlineData("epic", false, null, GameActionKind.Manage, "com.epicgames.launcher://store/library")]
    public async Task Management_goes_to_the_launcher_without_declaring_a_game_start(string store, bool installed, string? identifier, GameActionKind action, string expected)
    {
        await using var host = await Host.Start(store, installed, identifier);
        Assert.Equal(LaunchDispatch.HandedOff, await host.Dispatch(action));
        var dispatched = Assert.Single(host.Launcher.Calls);
        Assert.Equal(expected, dispatched.Link.Uri);
        Assert.False(dispatched.Link.StartsGame);
    }

    [Theory]
    [InlineData("steam", true, null)]
    [InlineData("steam", false, "12?run=evil")]
    [InlineData("steam", true, "12345678901")]
    [InlineData("gog", true, null)]
    [InlineData("gog", false, "../7")]
    [InlineData("gog", true, "1234567890123")]
    [InlineData("epic", true, "missing-composite-key")]
    [InlineData("epic", false, "missing-composite-key")]
    public async Task Missing_or_malformed_store_identity_cannot_reach_the_launcher(string store, bool installed, string? identifier)
    {
        await using var host = await Host.Start(store, installed, identifier);
        Assert.Equal(LaunchDispatch.Refused, await host.Dispatch(installed ? GameActionKind.Play : GameActionKind.Install));
        Assert.Equal(LaunchDispatch.Refused, await host.Dispatch(GameActionKind.Primary));
        Assert.Empty(host.Launcher.Calls);
    }

    private sealed class Host(string directory, WebApplication application, WinnowApiClient client, long ownershipId, Launcher launcher) : IAsyncDisposable
    {
        public long OwnershipId { get; } = ownershipId;
        public Launcher Launcher { get; } = launcher;
        public Task<LaunchDispatch> Dispatch(GameActionKind action, Guid? operationId = null) => client.SendAsync<GameActionRequest, LaunchDispatch>(
            HttpMethod.Post, $"entries/{OwnershipId}/actions", new(operationId ?? Guid.NewGuid(), action));

        public static async Task<Host> Start(string store, bool installed, string? identifier, long? exactOwnershipId = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-action-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var launcher = new Launcher();
            var application = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
            {
                services.AddSingleton<IGameLaunchService>(launcher);
                services.AddSingleton<IEpicLaunchKeys>(new EpicKeys());
            });
            try
            {
                var work = await application.Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = "Fez" });
                var releases = application.Services.GetRequiredService<IReleaseRepository>();
                var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Fez" });
                if (identifier is not null)
                    await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = store, ProviderId = identifier });
                var ownership = await application.Services.GetRequiredService<IOwnershipRepository>().InsertAsync(
                    new Ownership { ReleaseId = release, Store = store, Installed = installed });
                if (exactOwnershipId is { } exact)
                {
                    using var connection = application.Services.GetRequiredService<ISqliteConnectionFactory>().Open();
                    await connection.ExecuteAsync("UPDATE ownerships SET id = @exact WHERE id = @ownership", new { exact, ownership });
                    ownership = exact;
                }
                await application.StartAsync();
                return new(directory, application, WinnowApiClient.Attach(directory), ownership, launcher);
            }
            catch
            {
                await application.DisposeAsync();
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await application.StopAsync();
            await application.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class Launcher : IGameLaunchService
    {
        public List<(long Id, GameLink Link)> Calls { get; } = [];
        public Task<LaunchDispatch> LaunchAsync(long ownershipId, GameLink action)
        {
            Calls.Add((ownershipId, action));
            return Task.FromResult(LaunchDispatch.HandedOff);
        }
    }
    private sealed class EpicKeys : IEpicLaunchKeys
    {
        public Task<IReadOnlyDictionary<string, EpicLaunchKey>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EpicLaunchKey>>(new Dictionary<string, EpicLaunchKey>
            {
                ["7a70b499513441c792b541d53505e0b2"] = EpicLaunchKey.Create(
                    "41f47fd0d3e248bc938a5815d6d64daa", "7a70b499513441c792b541d53505e0b2", "Bluebird")!.Value
            });
    }
}
