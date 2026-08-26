using System.Net.Http.Headers;

namespace Hoard.Ingest.Epic.Web.Http;

/// <summary>
/// Makes a request re-sendable.
///
/// <para>Both retry paths in this pipeline — the Polly backoff for 429/5xx and
/// the single re-auth after a 401 — need to send the same request twice.
/// <see cref="HttpRequestMessage"/> does not survive that: its content stream is
/// consumed, and often disposed, by the first attempt. Buffering the body once
/// and rebuilding a fresh message per attempt is the only correct option, and it
/// is cheap here because the only bodies this module sends are form-encoded
/// token requests of a few hundred bytes.</para>
///
/// <para>Deliberately a copy of the IGDB module's <c>RequestReplay</c> rather
/// than a shared utility: both are internal to their own module's HTTP pipeline,
/// and the shared abstraction would have to live somewhere that made
/// <c>Hoard.Core</c> or <c>Hoard.Data</c> depend on <c>System.Net.Http</c>
/// semantics for the benefit of two call sites.</para>
/// </summary>
internal static class EpicRequestReplay
{
    /// <summary>Reads the body into memory, returning null when there is none.</summary>
    internal static async Task<byte[]?> BufferAsync(HttpRequestMessage request, CancellationToken ct)
        => request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);

    /// <summary>
    /// A fresh, unsent copy of <paramref name="template"/> carrying
    /// <paramref name="body"/>. Header collections are copied, not shared, so a
    /// handler mutating one attempt's headers — the Authorization refresh — cannot
    /// corrupt the next.
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

    /// <summary>Replaces the bearer credential on an already-built request.</summary>
    internal static void SetBearer(HttpRequestMessage request, string accessToken)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
