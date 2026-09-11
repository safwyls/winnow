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
    internal static readonly HttpRequestOptionsKey<EpicSessionIdentity> ExpectedSession = new("Winnow.Epic.ExpectedSession");
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
        request.Options.TryGetValue(ExpectedSession, out var expected);
        if (token is null || !await MatchesAsync(token, expected, cancellationToken))
        {
            // Synthetic, and never sent. The provider has already logged why at
            // the appropriate level; adding a line here would repeat it once per
            // request on a library that has many.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        var body = await EpicRequestReplay.BufferAsync(request, cancellationToken);

        using var first = EpicRequestReplay.Clone(request, body);
        EpicRequestReplay.SetBearer(first, token.AccessToken);
        var response = await base.SendAsync(first, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        _log.LogInformation("Epic returned 401; refreshing the access token and retrying once.");
        response.Dispose();

        var refreshed = await _tokens.RefreshAsync(token, cancellationToken);
        if (refreshed is null || !await MatchesAsync(refreshed, expected, cancellationToken))
        {
            // The session is gone and the provider has already dealt with the
            // stored copy. Surface the 401 rather than an exception: the client
            // reads it as "no Epic data this pass".
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        using var second = EpicRequestReplay.Clone(request, body);
        EpicRequestReplay.SetBearer(second, refreshed.AccessToken);
        return await base.SendAsync(second, cancellationToken);
    }

    private async ValueTask<bool> MatchesAsync(EpicOAuthToken token, EpicSessionIdentity? expected, CancellationToken ct)
        => expected is null || token.AccountId == expected.AccountId && token.ClientId == expected.ClientId
            && await _tokens.GetIdentityAsync(ct) == expected;
}
