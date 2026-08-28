using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Replaces <see cref="IHttpClientFactory"/>'s built-in request logging for this
/// module's client.
///
/// <para><b>The problem this solves.</b> A typed client built by
/// <c>IHttpClientFactory</c> gets two logging handlers for free — one outside
/// the handler chain and one just above the primary handler — and both write
/// <c>"Sending HTTP request {Method} {Uri}"</c> at <c>Information</c>, with
/// <c>{Uri}</c> being the <i>whole</i> request URI. §4.2's <c>GetOwnedGames</c>
/// carries the user's API key in that URI, so on any host with
/// <c>System.Net.Http.HttpClient</c> logging at Information — the default for a
/// generic host — the free logging would write the user's key to the log file on
/// every sync. Careful logging inside this module would not help: the leak is
/// upstream of anything the module writes.</para>
///
/// <para><b>The fix.</b> The registration calls <c>RemoveAllLoggers()</c> on this
/// client's builder, which strips both built-in loggers, and then adds this one
/// in their place. This logger prints only what
/// <see cref="SteamWebRedaction.Describe"/> allows through, and at
/// <c>Debug</c>/<c>Warning</c> rather than Information — the successful case is
/// one request per sync and does not need announcing.</para>
///
/// <para>It is scoped to this client's builder alone, so no other module's HTTP
/// logging is affected.</para>
/// </summary>
public sealed class RedactingHttpClientLogger : IHttpClientLogger
{
    private readonly ILogger<RedactingHttpClientLogger> _log;

    public RedactingHttpClientLogger(ILogger<RedactingHttpClientLogger> log) => _log = log;

    public object? LogRequestStart(HttpRequestMessage request)
    {
        _log.LogDebug(
            "Steam Web API {Method} {Request}",
            request.Method, SteamWebRedaction.Describe(request.RequestUri));
        return null;
    }

    public void LogRequestStop(
        object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed)
        => _log.LogDebug(
            "Steam Web API {Method} {Request} responded {StatusCode} in {ElapsedMs}ms",
            request.Method,
            SteamWebRedaction.Describe(request.RequestUri),
            (int)response.StatusCode,
            (long)elapsed.TotalMilliseconds);

    public void LogRequestFailed(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed)
        // The exception is logged by type and message only. It is never passed
        // as the ILogger `exception` argument, because a handler deeper in the
        // stack is free to put the request URI into an inner exception's message
        // and a full stack dump would carry it straight into the log.
        => _log.LogWarning(
            "Steam Web API {Method} {Request} failed after {ElapsedMs}ms: {ExceptionType}",
            request.Method,
            SteamWebRedaction.Describe(request.RequestUri),
            (long)elapsed.TotalMilliseconds,
            exception.GetType().Name);
}
