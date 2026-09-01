namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Makes a request re-sendable. Until S6 every request this module sent was a
/// bodyless GET, and the retry policy replayed the original message as-is.
/// S6's renewal exchange sends form POSTs, and an
/// <see cref="HttpRequestMessage"/> does not survive being sent twice: its
/// content stream is consumed, and often disposed, by the first attempt.
/// Buffering the body once and rebuilding a fresh message per attempt is the
/// only correct option; it is cheap because these bodies are a few hundred
/// bytes.
///
/// <para>A GET buffers to null and clones to an equivalent message, so the
/// two shipped typed clients are unaffected.</para>
///
/// <para>Mirrors <c>Winnow.Enrich.Igdb.Http.RequestReplay</c> rather than
/// sharing it: each enrichment module owns its own HTTP pipeline pieces, and
/// a shared helper here would be the first dependency between two of
/// them.</para>
/// </summary>
internal static class SteamRequestReplay
{
    /// <summary>Reads the body into memory, returning null when there is none.</summary>
    internal static async Task<byte[]?> BufferAsync(HttpRequestMessage request, CancellationToken ct)
        => request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);

    /// <summary>
    /// A fresh, unsent copy carrying the buffered body. Header collections
    /// are copied rather than shared, so a handler mutating one attempt's
    /// headers cannot corrupt the next.
    /// </summary>
    internal static HttpRequestMessage Clone(HttpRequestMessage template, byte[]? body)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri)
        {
            Version = template.Version,
            VersionPolicy = template.VersionPolicy,
        };

        foreach (var header in template.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)template.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (template.Content is not null)
            {
                foreach (var header in template.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
