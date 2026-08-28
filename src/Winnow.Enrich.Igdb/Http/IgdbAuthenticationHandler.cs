using System.Net;
using Winnow.Enrich.Igdb.Auth;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Igdb.Http;

/// <summary>
/// Attaches the two headers IGDB requires on every request (§4.4) —
/// <c>Client-ID</c> and <c>Authorization: Bearer …</c> — and handles the one
/// case that invalidates a cached token: a 401.
///
/// <para>Outermost handler in the pipeline, so its re-auth attempt goes back
/// through retry and rate limiting like any other request. A 401 is answered by
/// exactly one refresh-and-retry; a second 401 is returned to the caller rather
/// than looped on, because at that point the credentials themselves are wrong
/// and re-minting forever would just burn the rate limit.</para>
/// </summary>
public sealed class IgdbAuthenticationHandler : DelegatingHandler
{
    private readonly IIgdbTokenProvider _tokens;
    private readonly ILogger<IgdbAuthenticationHandler> _log;

    public IgdbAuthenticationHandler(IIgdbTokenProvider tokens, ILogger<IgdbAuthenticationHandler> log)
    {
        _tokens = tokens;
        _log = log;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAsync(cancellationToken)
            ?? throw new IgdbNotConfiguredException(
                "No IGDB credentials are configured, so this request cannot be authenticated. "
                + "Callers must check IIgdbClient.IsConfiguredAsync and skip enrichment instead.");

        var body = await RequestReplay.BufferAsync(request, cancellationToken);

        var first = RequestReplay.Clone(request, body);
        Authenticate(first, token);
        var response = await base.SendAsync(first, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        _log.LogInformation("IGDB returned 401; refreshing the Twitch access token and retrying once.");
        response.Dispose();

        var refreshed = await _tokens.RefreshAsync(token, cancellationToken);
        if (refreshed is null)
        {
            // Credentials disappeared or Twitch refused. Surface the 401 rather
            // than an exception: the caller treats it as "no enrichment".
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        var second = RequestReplay.Clone(request, body);
        Authenticate(second, refreshed);
        return await base.SendAsync(second, cancellationToken);
    }

    private static void Authenticate(HttpRequestMessage request, IgdbAccessToken token)
    {
        request.Headers.Remove("Client-ID");
        request.Headers.TryAddWithoutValidation("Client-ID", token.ClientId);
        RequestReplay.SetBearer(request, token.AccessToken);
    }
}
