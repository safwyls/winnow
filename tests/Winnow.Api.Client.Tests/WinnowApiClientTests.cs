using System.Net;
using System.Text;
using System.Text.Json;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Protocol;
using Xunit;

namespace Winnow.Api.Client.Tests;

public sealed class WinnowApiClientTests
{
    private static BackendDiscovery Connection(string epoch = "epoch", string token = "secret") =>
        new("http://127.0.0.1:8123", token, epoch, 123, "1");

    [Fact]
    public async Task PluginStoreActionSendsOnlyOwnershipAndOperationIdentity()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal("/api/v1/entries/42/actions", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("OpenStore", json.RootElement.GetProperty("action").GetString());
            Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("operationId").GetGuid());
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
            return JsonResponse(Winnow.App.Services.LaunchDispatch.HandedOff);
        }));
        using var client = new WinnowApiClient(Connection(), http);
        var dispatcher = new ApiPluginActionDispatcher(client);
        var link = Winnow.App.ViewModels.GameLink.Create("Store", "https://example.com")!;
        Assert.True(await dispatcher.ExecuteAsync(42, link));
    }

    [Fact]
    public async Task QueryAuthenticatesWithoutPuttingTokenInUrl()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("http://127.0.0.1:8123/api/v1/health", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("secret", request.Headers.Authorization.Parameter);
            return Task.FromResult(JsonResponse(new BackendHealth("1", "epoch", 0)));
        }));
        using var client = new WinnowApiClient(Connection(), http);
        Assert.Equal("epoch", (await client.GetHealthAsync()).Epoch);
    }

    [Fact]
    public async Task SharedSnapshotTransportDoesNotDependOnTheCallingUiDispatcher()
    {
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
            return JsonResponse(new BackendHealth("1", "epoch", 0));
        }));
        using var client = new WinnowApiClient(async _ =>
        {
            await Task.Delay(20).ConfigureAwait(false);
            return Connection();
        }, http);
        var previous = SynchronizationContext.Current;
        var context = new NonPumpingContext();
        Task<BackendHealth> request;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            request = client.GetHealthAsync();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.Equal("epoch", (await request.WaitAsync(TimeSpan.FromSeconds(5))).Epoch);
        Assert.Equal(0, context.Posts);
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref Posts);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("http://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("http://127.0.0.1/path")]
    public void RejectsUntrustedDiscoveryAddresses(string address) =>
        Assert.Throws<ArgumentException>(() => new WinnowApiClient(Connection() with { Address = address }));

    [Fact]
    public async Task ReconnectReadsFreshDiscoveryAndResumesDeliveredCursor()
    {
        var calls = 0;
        var discoveries = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.False(request.Headers.Contains("Last-Event-ID"));
                return Task.FromResult(Events(Change("old", 10, "resync-required")));
            }
            Assert.Equal("old:10", Assert.Single(request.Headers.GetValues("Last-Event-ID")));
            Assert.Equal("new-secret", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Events(Change("new", 0, "resync-required")));
        }));
        using var client = new WinnowApiClient(_ => Task.FromResult(++discoveries == 1
            ? Connection("old") : Connection("new", "new-secret")), http, TimeSpan.Zero);
        await using var events = client.WatchEventsAsync().GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("old", events.Current.Epoch);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("new", events.Current.Epoch);
        Assert.Equal("resync-required", events.Current.Kind);
        Assert.Equal(2, discoveries);
    }

    [Fact]
    public async Task DuplicateEventsAreIgnoredAndSequenceGapRequiresResync()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Events(
            Change("epoch", 1), Change("epoch", 2), Change("epoch", 5)))));
        using var client = new WinnowApiClient(Connection(), http);
        await using var events = client.WatchEventsAsync("epoch:1").GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(2, events.Current.Sequence);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(5, events.Current.Sequence);
        Assert.Equal("resync-required", events.Current.Kind);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotReconnectForever()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));
        using var client = new WinnowApiClient(Connection(), http, TimeSpan.Zero);
        await using var events = client.WatchEventsAsync().GetAsyncEnumerator();
        var error = await Assert.ThrowsAsync<BackendApiException>(async () => await events.MoveNextAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AuthenticationRotationBetweenDiscoveryAndConnectRetriesWithNewToken()
    {
        var calls = 0;
        var discoveries = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (++calls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            Assert.Equal("rotated", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Events(Change("new", 0, "resync-required")));
        }));
        using var client = new WinnowApiClient(_ => Task.FromResult(++discoveries == 1
            ? Connection() : Connection("new", "rotated")), http, TimeSpan.Zero);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = client.WatchEventsAsync(ct: deadline.Token).GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("new", events.Current.Epoch);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedMutationIsNotRetried()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            throw new HttpRequestException("Response lost after commit");
        }));
        using var client = new WinnowApiClient(Connection(), http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(HttpMethod.Post, "lists", new { name = "Next" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposalCancelsInFlightWorkWithoutDisposingBorrowedHttpClient()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        }));
        var client = new WinnowApiClient(Connection(), http);
        var query = client.GetHealthAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.Dispose();
        client.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.GetAsync("http://127.0.0.1", cancelled.Token));
    }

    [Fact]
    public async Task StreamCancellationStopsReconnectDelay()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Events())));
        using var client = new WinnowApiClient(Connection(), http, TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await using var events = client.WatchEventsAsync(ct: cancellation.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await events.MoveNextAsync());
    }

    [Fact]
    public async Task MalformedEventFailsInsteadOfSilentlyReconnecting()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage
        {
            Content = new StringContent("data: {\"epoch\":\"\",\"sequence\":-1}\n\n", Encoding.UTF8, "text/event-stream")
        })));
        using var client = new WinnowApiClient(Connection(), http);
        await using var events = client.WatchEventsAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await events.MoveNextAsync());
    }

    private static BackendEvent Change(string epoch, long sequence, string kind = "library-changed") =>
        new(epoch, sequence, kind, "library", DateTimeOffset.UnixEpoch);

    private static HttpResponseMessage Events(params BackendEvent[] changes) => new()
    {
        Content = new StringContent(": heartbeat\n\n" + string.Concat(changes.Select(change =>
            $"id: {change.Cursor}\nevent: change\ndata: {JsonSerializer.Serialize(change, new JsonSerializerOptions(JsonSerializerDefaults.Web))}\n\n")),
            Encoding.UTF8, "text/event-stream")
    };

    private static HttpResponseMessage JsonResponse<T>(T value) => new()
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
