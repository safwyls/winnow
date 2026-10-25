using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Contracts.Protocol;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class BackendHttpTests
{
    [Fact]
    public async Task AuthenticationHostAndOriginAreEnforcedAndDiscoveryIsRemovedOnShutdown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using (var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]))
            {
                await app.StartAsync();
                var discovery = JsonSerializer.Deserialize<BackendDiscovery>(await File.ReadAllTextAsync(Path.Combine(directory, "backend", "endpoint.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                using var client = new HttpClient { BaseAddress = new Uri(discovery.Address) };
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/health")).StatusCode);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", discovery.Token);
                var health = await client.GetFromJsonAsync<BackendHealth>("/api/v1/health");
                Assert.Equal(discovery.Epoch, health!.Epoch);
                client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/health")).StatusCode);
                client.DefaultRequestHeaders.Remove("Origin");
                client.DefaultRequestHeaders.Host = "attacker.example";
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/health")).StatusCode);
                client.DefaultRequestHeaders.Host = null;
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/openapi.json")).StatusCode);
                Assert.Throws<IOException>(() => new BackendOwnership(directory));
                await app.StopAsync();
            }
            Assert.False(File.Exists(Path.Combine(directory, "backend", "endpoint.json")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TwoHttpStreamsReceiveCommitAndDisconnectLeavesHostRunning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await app.StartAsync();
            var ownership = app.Services.GetRequiredService<BackendOwnership>();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownership.Token);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var a = await client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            using var b = await client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            using var readerA = new StreamReader(await a.Content.ReadAsStreamAsync(timeout.Token));
            using var readerB = new StreamReader(await b.Content.ReadAsStreamAsync(timeout.Token));
            Assert.Equal("resync-required", (await ReadEvent(readerA, timeout.Token)).Kind);
            Assert.Equal("resync-required", (await ReadEvent(readerB, timeout.Token)).Kind);
            app.Services.GetRequiredService<BackendEventHub>().Publish("library-changed", "42");
            Assert.Equal(await ReadEvent(readerA, timeout.Token), await ReadEvent(readerB, timeout.Token));
            a.Dispose(); b.Dispose();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/health", timeout.Token)).StatusCode);
            await app.StopAsync(timeout.Token);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<BackendEvent> ReadEvent(StreamReader reader, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line)
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<BackendEvent>(line[6..], new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        throw new EndOfStreamException();
    }
}
