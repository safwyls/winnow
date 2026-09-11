using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Plugins.Tests;

public sealed class PluginHttpTests
{
    [Fact]
    public async Task Typed_client_sends_headers_and_bounds_canned_response_without_network()
    {
        using var host = new Host(_ => Response(HttpStatusCode.OK, "fixture-body"));
        var result = await host.Scope.SendAsync(new("https://api.example.com/art")
        { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-key" } });
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("fixture-body", Encoding.UTF8.GetString(result.Body));
        Assert.Equal("Bearer fixture-key", host.Handler.Authorization);
        Assert.Equal("application/json", result.Headers["Content-Type"].Split(';')[0]);
    }

    [Theory]
    [InlineData("http://api.example.com/art")]
    [InlineData("https://api.example.com.attacker.example/art")]
    [InlineData("https://fixture-key@api.example.com/art")]
    [InlineData("https://api.example.com:444/art")]
    [InlineData("https://api.example.com/art#secret")]
    public async Task Scope_refuses_hosts_and_urls_outside_manifest(string url)
    {
        using var host = new Host(_ => Response(HttpStatusCode.OK, "unused"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Scope.SendAsync(new(url)));
        Assert.Equal(0, host.Handler.Count);
    }

    [Fact]
    public async Task Host_header_override_is_rejected_before_sending()
    {
        using var host = new Host(_ => Response(HttpStatusCode.OK, "unused"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Scope.SendAsync(new("https://api.example.com/art")
        { Headers = new Dictionary<string, string> { ["Host"] = "other.example" } }));
        Assert.Equal(0, host.Handler.Count);
    }

    [Fact]
    public async Task Declared_and_streamed_oversize_responses_are_rejected()
    {
        using var declared = new Host(_ => Response(HttpStatusCode.OK, new string('x', 9)), maximumBytes: 8);
        await Assert.ThrowsAsync<InvalidDataException>(() => declared.Scope.SendAsync(new("https://api.example.com/art")));
        using var streamed = new Host(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(new byte[9])) }, maximumBytes: 8);
        await Assert.ThrowsAsync<InvalidDataException>(() => streamed.Scope.SendAsync(new("https://api.example.com/art")));
    }

    [Fact]
    public async Task Rate_limit_retry_replays_post_body_and_auth_within_shared_handler_policy()
    {
        using var host = new Host(count =>
        {
            var response = Response(count < 3 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        }, retries: 2);
        var result = await host.Scope.SendAsync(new("https://api.example.com/query")
        {
            Method = "POST", Body = Encoding.UTF8.GetBytes("fields name;"), ContentType = "text/plain",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-key" },
        });
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(3, host.Handler.Count);
        Assert.All(host.Handler.Bodies, body => Assert.Equal("fields name;", body));
        Assert.Equal("Bearer fixture-key", host.Handler.Authorization);
    }

    [Fact]
    public async Task Unauthorized_is_not_retried_and_redirect_is_not_followed()
    {
        using var unauthorized = new Host(_ => Response(HttpStatusCode.Unauthorized, "{}"), retries: 2);
        Assert.Equal(401, (await unauthorized.Scope.SendAsync(new("https://api.example.com/art"))).StatusCode);
        Assert.Equal(1, unauthorized.Handler.Count);
        using var redirect = new Host(_ =>
        {
            var response = Response(HttpStatusCode.Redirect, "");
            response.Headers.Location = new("https://other.example/art");
            return response;
        });
        Assert.Equal(302, (await redirect.Scope.SendAsync(new("https://api.example.com/art"))).StatusCode);
        Assert.Equal(1, redirect.Handler.Count);
    }

    [Fact]
    public async Task Cancellation_interrupts_queued_rate_limit_work()
    {
        using var host = new Host(_ => Response(HttpStatusCode.OK, "{}"), requestsPerSecond: 0.05);
        await host.Scope.SendAsync(new("https://api.example.com/first"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Scope.SendAsync(new("https://api.example.com/second"), cancellation.Token));
        Assert.Equal(1, host.Handler.Count);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _services;
        public Handler Handler { get; }
        public IPluginHttp Scope { get; }
        public Host(Func<int, HttpResponseMessage> response, int maximumBytes = 1024, int retries = 0, double requestsPerSecond = 4)
        {
            Handler = new(response);
            var services = new ServiceCollection();
            services.AddPluginHttp();
            services.AddHttpClient(PluginHttpClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
            _services = services.BuildServiceProvider();
            Scope = _services.GetRequiredService<PluginHttpClient>().CreateScope(new()
            {
                Id = "fixture", Name = "Fixture", Version = "1.0.0", EntryAssembly = "Fixture.dll", EntryType = "Fixture.Plugin",
                Capabilities = [PluginCapabilities.Artwork], Network = new()
                { AllowedHosts = ["api.example.com"], MaxResponseBytes = maximumBytes, MaxRetries = retries, RequestsPerSecond = requestsPerSecond },
            });
        }
        public void Dispose() => _services.Dispose();
    }

    private sealed class Handler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Authorization { get; private set; }
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization = request.Headers.Authorization?.ToString();
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return response(++Count);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
