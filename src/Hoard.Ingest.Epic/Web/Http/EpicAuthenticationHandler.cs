using System.Net;
using Hoard.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.Logging;

namespace Hoard.Ingest.Epic.Web.Http;

/// <summary>
/// Attaches the bearer token to every library request and handles the one case
/// that invalidates it: a 401.
///
/// <para>Outermost handler in the pipeline, so its re-auth attempt goes back
/// through retry and rate limiting like any other request. A 401 is answered by
/// exactly one refresh-and-retry; a second 401 is returned to the caller rather
/// than looped on, because at that point the session itself is gone and
/// re-refreshing forever would burn the rate limit to rediscover that on every
/// sync.</para>
///
/// <para><b>Unlike the IGDB handler, this one never throws when unauthenticated.</b>
/// <c>IgdbAuthenticationHandler</c> raises <c>IgdbNotConfiguredException</c> when
/// no token can be had, on the grounds that a caller reaching it has skipped its
/// <c>IsConfiguredAsync</c> check. The rule here is different on purpose: "no
/// session" is not a programming error in this module, it is the expected steady
/// state of a user whose refresh token lapsed while the app was closed. So a
/// missing token becomes a synthetic 401 and the client degrades to the local
/// readers — which is the entire point of the fallback and must not depend on
/// nobody having forgotten a check.</para>
/// </summary>
public sealed class EpicAuthenticationHandler : DelegatingHandler
{
    private readonly IEpicTokenProvider _tokens;
    private readonly ILogger<EpicAuthenticationHandler> _log;

    public EpicAuthenticationHandler(IEpicTokenProvider tokens, ILogger<EpicAuthenticationHandler> log)
    {
        _tokens = tokens;
        _log = log;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAsync(cancellationToken);
        if (token is null)
        {
            // Synthetic, and never sent. The provider has already logged why at
            // the appropriate level; adding a line here would repeat it once per
            // request on a library that has many.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        var body = await EpicRequestReplay.BufferAsync(request, cancellationToken);

        var first = EpicRequestReplay.Clone(request, body);
        EpicRequestReplay.SetBearer(first, token.AccessToken);
        var response = await base.SendAsync(first, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        _log.LogInformation("Epic returned 401; refreshing the access token and retrying once.");
        response.Dispose();

        var refreshed = await _tokens.RefreshAsync(token, cancellationToken);
        if (refreshed is null)
        {
            // The session is gone and the provider has already dealt with the
            // stored copy. Surface the 401 rather than an exception: the client
            // reads it as "no Epic data this pass".
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        var second = EpicRequestReplay.Clone(request, body);
        EpicRequestReplay.SetBearer(second, refreshed.AccessToken);
        return await base.SendAsync(second, cancellationToken);
    }
}
