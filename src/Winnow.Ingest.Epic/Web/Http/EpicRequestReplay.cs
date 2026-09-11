using Winnow.Http;

namespace Winnow.Ingest.Epic.Web.Http;

/// <summary>Provider-local name for bounded shared replay used by auth and retry.</summary>
internal static class EpicRequestReplay
{
    internal static Task<byte[]?> BufferAsync(HttpRequestMessage request, CancellationToken ct)
        => ProviderHttpTransport.BufferAsync(request, ct);

    internal static HttpRequestMessage Clone(HttpRequestMessage template, byte[]? body)
        => ProviderHttpTransport.Clone(template, body);

    internal static void SetBearer(HttpRequestMessage request, string accessToken)
        => request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
}