using System.Text;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Turns a Steam Web API request URI into something safe to write to a log.
///
/// <para><b>Why this is not optional.</b> §4.2's <c>GetOwnedGames</c> takes the
/// API key as a query parameter and offers no alternative — there is no header
/// form and no POST body form, so unlike the IGDB module (which moved its secret
/// out of the query string into a form body for exactly this reason) this client
/// cannot avoid putting a secret in a URI. The URI therefore has to be treated
/// as secret-bearing everywhere it could be printed.</para>
///
/// <para><b>Allowlist, not denylist.</b> Redacting a parameter called
/// <c>key</c> would be one refactor away from leaking: the day someone adds
/// <c>access_token</c> or <c>webapi_key</c>, a denylist silently starts printing
/// it. So every parameter value is redacted <i>except</i> the handful named in
/// <see cref="SafeParameters"/>, all of which are the request-shape flags §4.2
/// requires and the account id being queried.</para>
/// </summary>
public static class SteamWebRedaction
{
    /// <summary>What a redacted value is replaced with.</summary>
    public const string Placeholder = "<redacted>";

    /// <summary>
    /// Parameters whose values may be logged: the three §4.2-mandated flags
    /// (whose presence is the thing worth being able to confirm from a log), the
    /// account being queried, and the response format. Everything else is
    /// redacted whether or not it is known to be a secret.
    /// </summary>
    public static readonly IReadOnlySet<string> SafeParameters =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steamid",
            "include_appinfo",
            "include_played_free_games",
            "skip_unvetted_apps",
            "format",
        };

    /// <summary>
    /// The path and query with every non-allowlisted parameter value replaced.
    /// Scheme and host are dropped: they are constant and add only noise.
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

        if (query.Length == 0)
        {
            return path;
        }

        var builder = new StringBuilder(path);
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
}
