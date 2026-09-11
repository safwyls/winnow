using System.Collections.Concurrent;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Winnow.PluginSdk;

namespace Winnow.Plugins;

/// <summary>A typed host client. Plugins receive a restricted scope, never the underlying HttpClient.</summary>
public sealed class PluginHttpClient(HttpClient client, PluginHttpPolicies policies)
{
    public const string ClientName = "Winnow.Plugins.Http";
    internal static readonly HttpRequestOptionsKey<PluginHttpPolicy> PolicyKey = new("Winnow.PluginPolicy");

    public IPluginHttp CreateScope(PluginManifest manifest, int? maxResponseBytes = null)
    {
        PluginManifestReader.Validate(manifest);
        if (maxResponseBytes is <= 0 or > 32 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
        return new Scope(client, policies.Get(manifest), manifest.Network,
            maxResponseBytes ?? manifest.Network.MaxResponseBytes);
    }

    private sealed class Scope(HttpClient client, PluginHttpPolicy policy, PluginNetworkOptions network, int maximumBytes) : IPluginHttp
    {
        public async Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
                || !network.AllowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("The plugin request URL is outside its declared HTTPS hosts.");
            if (request.Method is not ("GET" or "POST" or "HEAD"))
                throw new InvalidOperationException("The plugin HTTP method is unsupported.");
            if (request.Body?.Length > 2 * 1024 * 1024)
                throw new InvalidOperationException("The plugin request body is too large.");
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
            foreach (var header in request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    || !message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    throw new InvalidOperationException("The plugin supplied an unsupported HTTP header.");
            }
            if (request.Body is { } body)
            {
                message.Content = new ByteArrayContent(body);
                if (request.ContentType is { } contentType)
                    message.Content.Headers.ContentType = new(contentType);
            }
            message.Options.Set(PolicyKey, policy);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(network.TimeoutSeconds));
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new InvalidDataException("The plugin response exceeded its size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (output.Length + count > maximumBytes)
                    throw new InvalidDataException("The plugin response exceeded its size limit.");
                output.Write(buffer, 0, count);
            }
            var headers = response.Headers.Concat(response.Content.Headers)
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => string.Join(", ", x.SelectMany(value => value.Value)), StringComparer.OrdinalIgnoreCase);
            return new((int)response.StatusCode, output.ToArray(), headers);
        }
    }
}

/// <summary>Each plugin's scopes share one budget, including image fetches and every retry.</summary>
public sealed class PluginHttpPolicies : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<PluginHttpPolicy>> _policies = new(StringComparer.Ordinal);
    internal PluginHttpPolicy Get(PluginManifest manifest) => _policies.GetOrAdd(manifest.Id,
        _ => new Lazy<PluginHttpPolicy>(() => new(manifest.Network))).Value;
    public void Dispose()
    {
        foreach (var policy in _policies.Values)
            if (policy.IsValueCreated) policy.Value.Dispose();
    }
}

internal sealed class PluginHttpPolicy : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;
    public ResiliencePipeline<HttpResponseMessage> Pipeline { get; }

    public PluginHttpPolicy(PluginNetworkOptions options)
    {
        _limiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1 / options.RequestsPerSecond),
            AutoReplenishment = true, QueueLimit = 1000, QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        if (options.MaxRetries > 0)
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.MaxRetries, Delay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromSeconds(30),
                BackoffType = DelayBackoffType.Exponential, UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>()
                    .HandleResult(response => response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                        || (int)response.StatusCode >= 500),
                DelayGenerator = args => ValueTask.FromResult(RetryAfter(args.Outcome.Result)),
                OnRetry = args => { args.Outcome.Result?.Dispose(); return default; },
            });
        Pipeline = builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = args => _limiter.AcquireAsync(1, args.Context.CancellationToken),
        }).Build();
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage? response)
    {
        var header = response?.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - (response!.Headers.Date ?? DateTimeOffset.UtcNow) : null);
        return delay is null ? null : delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
    }

    public void Dispose() => _limiter.Dispose();
}

public sealed class PluginHttpPolicyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(PluginHttpClient.PolicyKey, out var policy))
            throw new InvalidOperationException("Plugin HTTP requests require a host policy.");
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return await policy.Pipeline.ExecuteAsync(async token =>
        {
            using var attempt = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) attempt.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (body is not null)
            {
                attempt.Content = new ByteArrayContent(body);
                foreach (var header in request.Content!.Headers) attempt.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return await base.SendAsync(attempt, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}

public static class PluginHttpServiceCollectionExtensions
{
    public static IServiceCollection AddPluginHttp(this IServiceCollection services)
    {
        services.TryAddSingleton<PluginHttpPolicies>();
        services.AddTransient<PluginHttpPolicyHandler>();
        services.AddHttpClient<PluginHttpClient>(PluginHttpClient.ClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Winnow/0.1 (+https://github.com/winnow-app/winnow)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .AddHttpMessageHandler<PluginHttpPolicyHandler>()
            // URLs and arbitrary plugin headers may carry credentials; host diagnostics use fixed messages.
            .RemoveAllLoggers();
        return services;
    }
}
