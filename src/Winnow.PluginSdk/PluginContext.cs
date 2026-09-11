namespace Winnow.PluginSdk;

/// <summary>Services are scoped to one plugin ID. Secrets are never returned through ordinary settings.</summary>
public interface IPluginContext
{
    string PluginId { get; }
    IPluginSettings Settings { get; }
    IPluginSecrets Secrets { get; }
    IPluginCache Cache { get; }
    IPluginHttp Http { get; }
}

public interface IPluginSettings
{
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

public interface IPluginSecrets
{
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record PluginCacheEntry(byte[] Payload, DateTimeOffset ExpiresAt);

public interface IPluginCache
{
    /// <summary>Expired entries remain available so a provider can support offline fallback.</summary>
    ValueTask<PluginCacheEntry?> GetAsync(string key, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken = default);
}

public sealed record PluginHttpRequest(string Url)
{
    public string Method { get; init; } = "GET";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public byte[]? Body { get; init; }
    public string? ContentType { get; init; }
}

public sealed record PluginHttpResponse(int StatusCode, byte[] Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>HTTPS only, with manifest host restrictions, response bounds, and host-controlled rate/retry policies.</summary>
public interface IPluginHttp
{
    Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default);
}
