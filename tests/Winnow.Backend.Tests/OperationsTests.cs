using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class OperationsTests
{
    [Theory]
    [InlineData(MetadataSyncResult.Completed)]
    [InlineData(MetadataSyncResult.MissingCredentials)]
    [InlineData(MetadataSyncResult.PartialFailure)]
    [InlineData(MetadataSyncResult.RefreshFailed)]
    public async Task MetadataResultsSurviveHttpAndLateProgressCannotReplaceCompletion(MetadataSyncResult result)
    {
        var metadata = new ControlledMetadata();
        await WithBackend(metadata, async (client, ct) =>
        {
            var id = Guid.NewGuid().ToString("N");
            await client.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post,
                "operations/metadata-sync", new(id), ct: ct);
            await metadata.Started.Task.WaitAsync(ct);
            metadata.Finish.TrySetResult(result);
            var completed = await WaitForCompletion(client, id, ct);
            Assert.Equal("completed", completed.State);
            Assert.Equal(result, completed.MetadataResult);
            var wire = await client.GetAsync<System.Text.Json.JsonElement>("operations/" + id, ct);
            Assert.Equal((int)result, wire.GetProperty("metadataResult").GetInt32());

            metadata.Progress!.Report("Late progress from an already completed sync.");
            Assert.Equal(completed, await client.GetAsync<BackendOperation>("operations/" + id, ct));
            var duplicate = await client.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post,
                "operations/metadata-sync", new(id), ct: ct);
            Assert.Equal(completed, duplicate);
            Assert.Equal(1, metadata.Calls);
        });
    }

    [Fact]
    public async Task FailedMetadataHidesPrivateDetailsAndANewIdentityCanRetry()
    {
        var metadata = new RetryMetadata();
        await WithBackend(metadata, async (client, ct) =>
        {
            var firstId = Guid.NewGuid().ToString("N");
            await client.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post,
                "operations/metadata-sync", new(firstId), ct: ct);
            var failed = await WaitForCompletion(client, firstId, ct);
            Assert.Equal("failed", failed.State);
            Assert.Equal("The operation could not complete. Try again.", failed.Message);
            Assert.Null(failed.MetadataResult);
            metadata.FirstProgress!.Report("Private late failure details.");
            Assert.Equal(failed, await client.GetAsync<BackendOperation>("operations/" + firstId, ct));

            var secondId = Guid.NewGuid().ToString("N");
            await client.SendAsync<StartMetadataSync, BackendOperation>(HttpMethod.Post,
                "operations/metadata-sync", new(secondId), ct: ct);
            var retried = await WaitForCompletion(client, secondId, ct);
            Assert.Equal("completed", retried.State);
            Assert.Equal(MetadataSyncResult.Completed, retried.MetadataResult);
            Assert.Equal(2, metadata.Calls);
        });
    }

    private static async Task WithBackend(IManualMetadataSyncService metadata, Func<WinnowApiClient, CancellationToken, Task> verify)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.AddSingleton(metadata));
            await app.StartAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = WinnowApiClient.Attach(directory);
            await verify(client, timeout.Token);
            await app.StopAsync(timeout.Token);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<BackendOperation> WaitForCompletion(WinnowApiClient client, string id, CancellationToken ct)
    {
        for (;;)
        {
            var operation = await client.GetAsync<BackendOperation>("operations/" + id, ct);
            if (operation.State != "running") return operation;
            await Task.Delay(10, ct);
        }
    }

    private sealed class RetryMetadata : IManualMetadataSyncService
    {
        public int Calls;
        public IProgress<string>? FirstProgress;
        public Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref Calls) == 1)
            {
                FirstProgress = progress;
                throw new InvalidOperationException("private diagnostic detail /secret/token");
            }
            return Task.FromResult(MetadataSyncResult.Completed);
        }
    }

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
        public IProgress<string>? Progress;
        public async Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            Progress = progress;
            progress?.Report("Testing the independent operation.");
            Started.TrySetResult();
            return await Finish.Task.WaitAsync(ct);
        }
    }
}
