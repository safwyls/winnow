using System.Net;
using Winnow.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web.Http;

/// <summary>
/// Attaches the bearer token and handles 401 with a single refresh-and-retry.
/// A missing token produces a synthetic 401 rather than throwing.
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
