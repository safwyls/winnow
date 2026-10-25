using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Actions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Monitor;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class LaunchObservationParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_copy_dispatches_its_URI_declares_only_its_intent_and_publishes_its_observation(bool firstInstalled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-launch-observation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dispatcher = new Dispatcher();
            await using var application = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.AddSingleton<IUriDispatcher>(dispatcher));
            var works = application.Services.GetRequiredService<IWorkRepository>();
            var releases = application.Services.GetRequiredService<IReleaseRepository>();
            var ownerships = application.Services.GetRequiredService<IOwnershipRepository>();
            var intents = application.Services.GetRequiredService<LaunchIntents>();
            var workId = await works.InsertAsync(new Work { Name = "Two copies" });
            var ids = new List<long>();
            for (var index = 0; index < 2; index++)
            {
                var release = await releases.InsertAsync(new Release { WorkId = workId, Name = $"Copy {index + 1}" });
                await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = index == 0 ? "10" : "20" });
                ids.Add(await ownerships.InsertAsync(new Ownership { ReleaseId = release, Store = "steam", Installed = index == 1 || firstInstalled }));
            }
            dispatcher.BeforeOpen = () =>
            {
                Assert.False(intents.IsLive(ids[0], DateTime.UtcNow));
                Assert.True(intents.IsLive(ids[1], DateTime.UtcNow));
            };
            await application.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var events = client.WatchEventsAsync(ct: deadline.Token).GetAsyncEnumerator(deadline.Token);
            Assert.True(await events.MoveNextAsync());
            Assert.Empty(dispatcher.Uris);
            var result = await client.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post,
                $"entries/{ids[1]}/actions", new(Guid.NewGuid(), GameActionKind.Play), ct: deadline.Token);
            Assert.Equal(LaunchDispatch.HandedOff, result);
            Assert.Equal("steam://run/20", Assert.Single(dispatcher.Uris));
            Assert.False(intents.IsLive(ids[0], DateTime.UtcNow));
            Assert.True(intents.IsLive(ids[1], DateTime.UtcNow));
            Assert.Equal(LaunchDispatch.AlreadyRunning,
                await client.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post,
                    $"entries/{ids[1]}/actions", new(Guid.NewGuid(), GameActionKind.Play), ct: deadline.Token));
            Assert.Single(dispatcher.Uris);
            intents.Fulfil(ids[1], DateTime.UtcNow);
            do { Assert.True(await events.MoveNextAsync()); } while (events.Current.Kind != "launch.observed");
            Assert.Equal(ids[1].ToString(System.Globalization.CultureInfo.InvariantCulture), events.Current.Resource);
            await events.DisposeAsync();
            await application.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class Dispatcher : IUriDispatcher
    {
        public List<string> Uris { get; } = [];
        public Action? BeforeOpen { get; set; }
        public Task<bool> OpenAsync(Uri uri)
        {
            BeforeOpen?.Invoke();
            Uris.Add(uri.OriginalString);
            return Task.FromResult(true);
        }
    }
}
