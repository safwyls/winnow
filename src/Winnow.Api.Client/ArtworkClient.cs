using System.Net;

namespace Winnow.Api.Client;

public sealed partial class WinnowApiClient
{
    public async Task<byte[]?> GetArtworkAsync(string provider, string id, int width = 1920,
        CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        using var request = await RequestAsync(HttpMethod.Get,
            $"artwork/image?provider={Uri.EscapeDataString(provider)}&id={Uri.EscapeDataString(id)}&width={width}", linked.Token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, linked.Token);
        const int limit = 32 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Artwork exceeds the image limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
        using var result = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, linked.Token)) > 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("Artwork exceeds the image limit.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}
