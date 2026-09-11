using Winnow.Http;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>Provider-local name for bounded shared replay used by auth and retry.</summary>
internal static class SteamRequestReplay
{
    internal static Task<byte[]?> BufferAsync(HttpRequestMessage request, CancellationToken ct)
        => ProviderHttpTransport.BufferAsync(request, ct);

    internal static HttpRequestMessage Clone(HttpRequestMessage template, byte[]? body)
        => ProviderHttpTransport.Clone(template, body);
}