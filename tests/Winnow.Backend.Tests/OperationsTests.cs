using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class OperationsTests
{
    [Fact]
    public async Task OperationIdPreventsDuplicateWorkAndFrontendDisconnectDoesNotCancelIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var metadata = new ControlledMetadata();
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services => services.AddSingleton<IManualMetadataSyncService>(metadata));
            await app.StartAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var request = new StartMetadataSync(Guid.NewGuid().ToString("N"));
            using (var first = WinnowApiClient.Attach(directory))
            {
                var started = await first.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post, "operations/metadata-sync", request, ct: timeout.Token);
                Assert.Equal("running", started.State);
                await metadata.Started.Task.WaitAsync(timeout.Token);
                var duplicate = await first.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post, "operations/metadata-sync", request, ct: timeout.Token);
                Assert.Equal(started.Id, duplicate.Id);
                Assert.Equal(1, metadata.Calls);
            }
            using var second = WinnowApiClient.Attach(directory);
            Assert.Equal("running", (await second.GetAsync<BackendOperation>("operations/" + request.OperationId, timeout.Token)).State);
            metadata.Finish.TrySetResult(MetadataSyncResult.Completed);
            BackendOperation completed;
            do
            {
                completed = await second.GetAsync<BackendOperation>("operations/" + request.OperationId, timeout.Token);
                if (completed.State == "running") await Task.Delay(10, timeout.Token);
            } while (completed.State == "running");
            Assert.Equal(MetadataSyncResult.Completed, completed.MetadataResult);
            Assert.Equal(1, metadata.Calls);
            await app.StopAsync(timeout.Token);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class ControlledMetadata : IManualMetadataSyncService
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<MetadataSyncResult> Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            progress?.Report("Testing the independent operation.");
            Started.TrySetResult();
            return await Finish.Task.WaitAsync(ct);
        }
    }
}
