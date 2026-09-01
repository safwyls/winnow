using System.Net;
using System.Net.Http.Headers;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// What one authorized request produced, with nothing secret in it. Exactly
/// one of <see cref="Body"/>, <see cref="Status"/> and
/// <see cref="FailureType"/> is the interesting field; the caller decides
/// which sentence to log, because the two typed clients say different things
/// about the same status code.
/// </summary>
internal readonly record struct SteamAuthorizedOutcome(
    string? Body, HttpStatusCode? Status, string? FailureType, bool Renewed);

/// <summary>
/// The send path both Steam Web API clients share, and the only place the
/// reactive half of S6's renewal lives.
///
/// <para>The rule is one renewal per pass. A 401 means Steam refused the
/// credential that was sent; the provider is asked for a replacement, and if
/// it produces one the request is built again, with a fresh URI because the
/// credential travels in the query string, and sent exactly once more. A
/// second 401 is the answer, not an invitation.</para>
///
/// <para>The bound is structural: pass 0 may renew, pass 1 may not. It is
/// not a counter that a future edit could forget to increment, and it is not
/// a while-loop with a break.</para>
///
/// <para><see cref="SteamWebResilienceHandler"/> deliberately does not treat
/// 401 as transient, which is what lets a 401 arrive here as Steam's verdict
/// rather than as a blip the retry policy was still working through.</para>
/// </summary>
internal static class SteamAuthorizedRequest
{
    internal static async Task<SteamAuthorizedOutcome> SendAsync(
        HttpClient http,
        ISteamCredentialProvider credentials,
        SteamCredential credential,
        Func<SteamCredential, string> buildUri,
        CancellationToken ct)
    {
        var attempt = credential;
        var renewed = false;

        for (var pass = 0; pass < 2; pass++)
        {
            try
            {
                // Built inside the loop: the credential is part of the URI, so a
                // renewed credential means a different request rather than a
                // different header.
                using var request = new HttpRequestMessage(HttpMethod.Get, buildUri(attempt));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await http.SendAsync(request, ct);

                if (pass == 0
                    && response.StatusCode is HttpStatusCode.Unauthorized
                    && await credentials.RenewAfterUnauthorizedAsync(attempt, ct) is { } replacement)
                {
                    attempt = replacement;
                    renewed = true;
                    continue;
                }

                return response.IsSuccessStatusCode
                    ? new SteamAuthorizedOutcome(
                        await response.Content.ReadAsStringAsync(ct), response.StatusCode, null, renewed)
                    : new SteamAuthorizedOutcome(null, response.StatusCode, null, renewed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller asked to stop. Not an enrichment failure, and it
                // must not be swallowed into a silent empty result.
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                // The TYPE only. A full exception can carry an inner message that
                // quoted the request URI, and the request URI carries the
                // credential.
                return new SteamAuthorizedOutcome(null, null, ex.GetType().Name, renewed);
            }
        }

        // Unreachable: every path through the loop body returns or continues, and
        // only pass 0 can continue.
        return new SteamAuthorizedOutcome(null, null, null, renewed);
    }
}
