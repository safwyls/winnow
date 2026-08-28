using System.Text;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web.Http;

/// <summary>
/// Turns an Epic request URI into something safe to write to a log.
///
/// <para><b>What is sensitive here is different from Steam.</b> The Steam Web
/// API puts the user's key in the query string, so
/// <c>SteamWebRedaction</c> is about a secret. Epic puts its credentials in
/// headers and bodies, so no Epic URI contains a secret — but every library and
/// playtime path contains the <c>{accountId}</c> segment, which identifies a
/// real person's Epic account, and the playtime paths additionally contain an
/// <c>{artifactId}</c>, which names a specific game that specific person owns.
/// Neither belongs in a log file.</para>
///
/// <para><b>Path segments, not just parameters.</b> That is the structural
/// difference from the Steam redactor and the reason this is a separate type
/// rather than a shared one: Steam's secret is a query value, Epic's identifiers
/// are path segments. A redactor that only walked the query string would print
/// the account id on every single request while looking like it was doing its
/// job.</para>
///
/// <para><b>Allowlist, not denylist</b>, on both. A denylist is one refactor
/// away from leaking: the day someone adds a parameter or a segment nobody
/// updated the list for, a denylist starts printing it. So every query value is
/// redacted except the handful named in <see cref="SafeParameters"/>, and every
/// path segment after a recognised collection name is redacted rather than being
/// matched against a list of things that look like ids.</para>
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
/// Replaces <see cref="IHttpClientFactory"/>'s built-in request logging for this
/// module's clients.
///
/// <para><b>The problem this solves.</b> A typed client built by
/// <c>IHttpClientFactory</c> gets two logging handlers for free — one outside the
/// handler chain and one just above the primary handler — and both write
/// <c>"Sending HTTP request {Method} {Uri}"</c> at <c>Information</c>, with
/// <c>{Uri}</c> being the whole request URI. For this module that URI carries the
/// user's Epic account id on every library and playtime call, so on any host with
/// <c>System.Net.Http.HttpClient</c> logging at Information — the default for a
/// generic host — the free logging would write it to the log file on every sync.
/// Careful logging inside this module would not help: the leak is upstream of
/// anything the module writes.</para>
///
/// <para><b>The fix.</b> The registration calls <c>RemoveAllLoggers()</c> on both
/// of this module's client builders and adds this one in their place. It prints
/// only what <see cref="EpicRedaction.Describe"/> allows through, and at
/// <c>Debug</c>/<c>Warning</c> rather than Information — a library sync is a
/// handful of requests and does not need announcing.</para>
///
/// <para>It is scoped to this module's builders alone, so no other module's HTTP
/// logging is affected.</para>
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
