using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Replaces <see cref="IHttpClientFactory"/>'s built-in request logging for this
/// module's client, redacting the API key from all URIs via
/// <see cref="SteamWebRedaction.Describe"/>.
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
