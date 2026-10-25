using System.Net;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Library;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class MultiClientLibraryTests
{
    [Fact]
    public async Task RestartRotatesDiscoveryAndResyncsExistingClientWithoutLosingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            using var client = WinnowApiClient.Attach(directory);
            string oldEpoch;
            string oldCursor;
            await using (var first = BackendApplication.Build(["--data-dir", directory, "--no-sync"]))
            {
                await first.StartAsync(timeout.Token);
                oldEpoch = (await client.GetHealthAsync(timeout.Token)).Epoch;
                await client.CreateListAsync(new CreateListRequest("Persistent", []), timeout.Token);
                oldCursor = $"{oldEpoch}:{(await client.GetHealthAsync(timeout.Token)).Sequence}";
                await first.StopAsync(timeout.Token);
            }
            await using var restarted = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await restarted.StartAsync(timeout.Token);
            Assert.NotEqual(oldEpoch, (await client.GetHealthAsync(timeout.Token)).Epoch);
            Assert.Equal("Persistent", Assert.Single((await client.GetLibraryAsync(timeout.Token)).Lists).Name);
            await using (var stream = client.WatchEventsAsync(oldCursor, timeout.Token).GetAsyncEnumerator(timeout.Token))
            {
                Assert.True(await stream.MoveNextAsync());
                Assert.Equal("resync-required", stream.Current.Kind);
                Assert.NotEqual(oldEpoch, stream.Current.Epoch);
            }
            await restarted.StopAsync(timeout.Token);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task CommittedLibraryEditsAreSharedAndStaleEditsConflict()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await app.StartAsync();
            using var first = WinnowApiClient.Attach(directory);
            using var second = WinnowApiClient.Attach(directory);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var firstEvents = first.WatchEventsAsync(ct: timeout.Token).GetAsyncEnumerator(timeout.Token);
            await using var secondEvents = second.WatchEventsAsync(ct: timeout.Token).GetAsyncEnumerator(timeout.Token);
            Assert.True(await firstEvents.MoveNextAsync());
            Assert.True(await secondEvents.MoveNextAsync());
            var created = await first.CreateListAsync(new CreateListRequest("Shared list", []), timeout.Token);
            Assert.True(await firstEvents.MoveNextAsync());
            Assert.True(await secondEvents.MoveNextAsync());
            Assert.Equal(firstEvents.Current, secondEvents.Current);
            var observed = Assert.Single((await second.GetLibraryAsync(timeout.Token)).Lists);
            Assert.Equal(created.Id, observed.Id);
            Assert.Equal(created.Name, observed.Name);
            Assert.Equal(created.Revision, observed.Revision);
            var updated = await second.EditListAsync(created.Id, new EditListRequest("Edited", null, observed.Revision), timeout.Token);
            var conflict = await Assert.ThrowsAsync<BackendApiException>(() => first.EditListAsync(created.Id,
                new EditListRequest("Stale", null, created.Revision), timeout.Token));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(updated.Name, Assert.Single((await first.GetLibraryAsync(timeout.Token)).Lists).Name);
            await firstEvents.DisposeAsync();
            await secondEvents.DisposeAsync();
            first.Dispose();
            Assert.Equal("1", (await second.GetHealthAsync(timeout.Token)).ApiVersion);
            await app.StopAsync(timeout.Token);
        }
        finally { Directory.Delete(directory, true); }
    }
}
