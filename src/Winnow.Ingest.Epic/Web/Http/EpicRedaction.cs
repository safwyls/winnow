using System.Text;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web.Http;

/// <summary>
/// Redacts account and artifact identifiers from Epic request URIs for logging.
/// Uses an allowlist for safe query parameters and redacts path segments after
/// known collection names.
/// </summary>
public static class EpicRedaction
{
    /// <summary>What a redacted value is replaced with.</summary>
    public const string Placeholder = "<redacted>";

    /// <summary>
    /// Query parameters whose values may be logged: the request-shape flags
    /// worth being able to confirm from a log. <c>cursor</c> is deliberately
    /// absent — it is an opaque token that encodes position in a specific
    /// account's library.
    /// </summary>
    public static readonly IReadOnlySet<string> SafeParameters =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "includeMetadata",
            "platform",
            "label",
            "country",
            "locale",
        };

    /// <summary>
    /// Path segments after which the <i>next</i> segment is an identifier and is
    /// redacted. Covers <c>/account/{accountId}</c> and
    /// <c>/artifact/{artifactId}</c> on the library and playtime routes.
    /// </summary>
    public static readonly IReadOnlySet<string> IdentifierParents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "account",
            "artifact",
            "namespace",
        };

    /// <summary>
    /// The path and query, with account and artifact identifiers and every
    /// non-allowlisted parameter value replaced. Scheme and host are dropped:
    /// they are constant and add only noise.
    /// </summary>
    public static string Describe(Uri? uri)
    {
        if (uri is null)
        {
            return "<no uri>";
        }

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        var query = uri.IsAbsoluteUri
            ? uri.Query
            : uri.OriginalString.IndexOf('?') is var mark and >= 0
                ? uri.OriginalString[mark..]
                : string.Empty;

        if (!uri.IsAbsoluteUri && path.IndexOf('?') is var split and >= 0)
        {
            path = path[..split];
        }

        var builder = new StringBuilder(RedactPath(path));

        if (query.Length == 0)
        {
            return builder.ToString();
        }

        var first = true;
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(first ? '?' : '&');
            first = false;

            var equals = pair.IndexOf('=');
            if (equals < 0)
            {
                builder.Append(pair);
                continue;
            }

            var name = pair[..equals];
            builder.Append(name).Append('=');
            builder.Append(SafeParameters.Contains(name) ? pair[(equals + 1)..] : Placeholder);
        }

        return builder.ToString();
    }

    /// <summary>Replaces every segment that follows an <see cref="IdentifierParents"/> segment.</summary>
    private static string RedactPath(string path)
    {
        var segments = path.Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            if (segments[i].Length > 0 && IdentifierParents.Contains(segments[i - 1]))
            {
                segments[i] = Placeholder;
            }
        }

        return string.Join('/', segments);
    }
}

/// <summary>
/// Replaces the default <see cref="IHttpClientFactory"/> request logging with
/// redacted URIs via <see cref="EpicRedaction.Describe"/>.
/// </summary>
public sealed class RedactingEpicHttpClientLogger : IHttpClientLogger
{
    private readonly ILogger<RedactingEpicHttpClientLogger> _log;

    public RedactingEpicHttpClientLogger(ILogger<RedactingEpicHttpClientLogger> log) => _log = log;

    public object? LogRequestStart(HttpRequestMessage request)
    {
        _log.LogDebug("Epic {Method} {Request}", request.Method, EpicRedaction.Describe(request.RequestUri));
        return null;
    }

    public void LogRequestStop(
        object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed)
        => _log.LogDebug(
            "Epic {Method} {Request} responded {StatusCode} in {ElapsedMs}ms",
            request.Method,
            EpicRedaction.Describe(request.RequestUri),
            (int)response.StatusCode,
            (long)elapsed.TotalMilliseconds);

    public void LogRequestFailed(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed)
        // The exception is logged by type only. It is never passed as the
        // ILogger `exception` argument, because a handler deeper in the stack is
        // free to put the request into an inner exception's message and a full
        // stack dump would carry the account id — or, on the token client, the
        // form body — straight into the log.
        => _log.LogWarning(
            "Epic {Method} {Request} failed after {ElapsedMs}ms: {ExceptionType}",
            request.Method,
            EpicRedaction.Describe(request.RequestUri),
            (long)elapsed.TotalMilliseconds,
            exception.GetType().Name);
}
