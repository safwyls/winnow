using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Actions;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class GameActionTests
{
    [Fact]
    public async Task DuplicateOperationDispatchesOnceAndCannotBeReusedForDifferentAction()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-action-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var launcher = new Launcher();
            await using var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.AddSingleton<IGameLaunchService>(launcher));
            var work = await host.Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = "Game" });
            var release = await host.Services.GetRequiredService<IReleaseRepository>().InsertAsync(new Release { WorkId = work, Name = "Game" });
            await host.Services.GetRequiredService<IReleaseRepository>().AddExternalIdAsync(new ExternalId
                { ReleaseId = release, Provider = "steam", ProviderId = "123" });
            var ownership = await host.Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new Ownership
                { ReleaseId = release, Store = "steam", Installed = true });
            await host.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            var command = new GameActionRequest(Guid.NewGuid(), GameActionKind.Play);
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                client.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post, $"entries/{ownership}/actions", command)));
            Assert.All(outcomes, result => Assert.Equal(LaunchDispatch.HandedOff, result));
            Assert.Equal(1, launcher.Count);
            Assert.Equal("steam://run/123", launcher.LastUri);
            var conflict = await Assert.ThrowsAsync<BackendApiException>(() =>
                client.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post, $"entries/{ownership}/actions",
                    command with { Action = GameActionKind.Uninstall }));
            Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
            var invalidInstall = await client.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post,
                $"entries/{ownership}/actions", new(Guid.NewGuid(), GameActionKind.Install));
            Assert.Equal(LaunchDispatch.Refused, invalidInstall);
            Assert.Equal(1, launcher.Count);
            await host.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class Launcher : IGameLaunchService
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public string? LastUri { get; private set; }
        public Task<LaunchDispatch> LaunchAsync(long ownershipId, GameLink action)
        {
            Interlocked.Increment(ref _count);
            LastUri = action.Uri;
            return Task.FromResult(LaunchDispatch.HandedOff);
        }
    }
}
