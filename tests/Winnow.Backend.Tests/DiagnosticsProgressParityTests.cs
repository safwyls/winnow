using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Monitor;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class DiagnosticsProgressParityTests
{
    [Fact]
    public async Task Progress_and_watcher_recovery_publish_independent_events_and_authenticated_safe_snapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-diagnostics-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
        try
        {
            await app.StartAsync();
            var connection = await BackendConnection.ReadAsync(directory);
            using var client = new HttpClient { BaseAddress = new(connection.Address) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/progress")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/diagnostics")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            var hub = app.Services.GetRequiredService<BackendEventHub>();
            using var events = hub.Subscribe($"{hub.Epoch}:{hub.Sequence}");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reporter = app.Services.GetRequiredService<IProgress<EnrichmentProgress>>();
            foreach (var remaining in new[] { 997, 1, 0 })
            {
                reporter.Report(new(997, remaining));
                Assert.Equal(new BackendProgress(997, remaining), await client.GetFromJsonAsync<BackendProgress>("api/v1/progress"));
                var change = await events.Reader.ReadAsync(deadline.Token);
                Assert.Equal("progress.changed", change.Kind);
                Assert.Equal("enrichment", change.Resource);
            }

            var health = app.Services.GetRequiredService<SessionWatcherHealth>();
            health.ReportFailure(SessionWatcherOperation.ExecutableIndex, new IOException("unstructured-private-secret"));
            Assert.Equal("diagnostics.changed", (await events.Reader.ReadAsync(deadline.Token)).Kind);
            var failed = await client.GetStringAsync("api/v1/diagnostics");
            Assert.Contains("IOException", failed);
            Assert.DoesNotContain("unstructured-private-secret", failed);
            Assert.DoesNotContain(directory, failed);
            health.ReportSuccess(SessionWatcherOperation.Tick);
            Assert.Single((await client.GetFromJsonAsync<BackendDiagnostics>("api/v1/diagnostics"))!.SessionFailures);
            Assert.False(events.Reader.TryRead(out _));
            health.ReportSuccess(SessionWatcherOperation.ExecutableIndex);
            Assert.Equal("diagnostics.changed", (await events.Reader.ReadAsync(deadline.Token)).Kind);
            Assert.Empty((await client.GetFromJsonAsync<BackendDiagnostics>("api/v1/diagnostics"))!.SessionFailures);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
