using System.Net;
using System.Text;

namespace Winnow.Tests.EpicWeb;

/// <summary>
/// One outbound Epic request, captured before it would have hit the wire.
/// </summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Uri">The full request URI.</param>
/// <param name="Authorization">The raw <c>Authorization</c> header, if any — Basic on the token client, Bearer on the library client.</param>
/// <param name="UserAgent">What Winnow identified itself with.</param>
/// <param name="Body">The request body as text, or null for a bodyless GET.</param>
public sealed record RecordedEpicRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    string? UserAgent,
    string? Body)
{
    /// <summary>Which of the module's two endpoints this was, for a responder to switch on.</summary>
    public EpicEndpoint Endpoint =>
        Uri.AbsolutePath.Contains("/oauth/token", StringComparison.Ordinal) ? EpicEndpoint.Token
        : Uri.AbsolutePath.Contains("/playtime/", StringComparison.Ordinal) ? EpicEndpoint.Playtime
        : Uri.AbsolutePath.Contains("/library/api/public/items", StringComparison.Ordinal) ? EpicEndpoint.LibraryItems
        : Uri.AbsolutePath.Contains("/catalog/api/shared/namespace/", StringComparison.Ordinal)
            ? EpicEndpoint.CatalogItems
        : EpicEndpoint.Other;

    /// <summary>
    /// The namespace segment of a catalog bulk-items request, or null. The route
    /// is keyed by namespace, so a responder that serves per-namespace fixtures
    /// switches on this.
    /// </summary>
    public string? CatalogNamespace
    {
        get
        {
            const string marker = "/catalog/api/shared/namespace/";
            var index = Uri.AbsolutePath.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            var rest = Uri.AbsolutePath[(index + marker.Length)..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            return Uri.UnescapeDataString(slash < 0 ? rest : rest[..slash]);
        }
    }

    /// <summary>Every <c>id=</c> the request carried, decoded, in order.</summary>
    public IReadOnlyList<string> CatalogIds
    {
        get
        {
            var ids = new List<string>();
            foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                if (equals > 0 && Uri.UnescapeDataString(pair[..equals]) == "id")
                {
                    ids.Add(Uri.UnescapeDataString(pair[(equals + 1)..]));
                }
            }

            return ids;
        }
    }

    /// <summary>
    /// The form-encoded body as a dictionary. Parsed by hand so an assertion sees
    /// exactly the bytes that would have gone on the wire.
    /// </summary>
    public IReadOnlyDictionary<string, string> Form
    {
        get
        {
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(Body))
            {
                return parsed;
            }

            foreach (var pair in Body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var name = equals < 0 ? pair : pair[..equals];
                var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
                parsed[Uri.UnescapeDataString(name)] = value;
            }

            return parsed;
        }
    }

    /// <summary>A decoded query parameter, or null when it was not sent at all.</summary>
    public string? Query(string name)
    {
        foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair[..equals];
            if (string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                return equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
        }

        return null;
    }

    /// <summary>The <c>grant_type</c> of a token request, or null.</summary>
    public string? GrantType => Form.GetValueOrDefault("grant_type");
}

/// <summary>The endpoints this module talks to.</summary>
public enum EpicEndpoint
{
    Other = 0,
    Token,
    LibraryItems,
    Playtime,
    CatalogItems,
}

/// <summary>
/// The only transport these tests use. Nothing in this file opens a socket:
/// every Epic response is canned, per the enrichment charter's rule that HTTP
/// clients are tested against fixtures and never against live APIs.
/// </summary>
public sealed class FakeEpicHandler : HttpMessageHandler
{
    private readonly Func<RecordedEpicRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedEpicRequest> _requests = [];

    /// <param name="responder">
    /// Given the request and the zero-based count of prior requests to the same
    /// endpoint, returns the canned response. The counter is what lets a test say
    /// "reject the first refresh, accept the second".
    /// </param>
    public FakeEpicHandler(Func<RecordedEpicRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order.</summary>
    public IReadOnlyList<RecordedEpicRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    public int CountFor(EpicEndpoint endpoint) => Requests.Count(r => r.Endpoint == endpoint);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedEpicRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization is { } auth ? auth.Scheme + " " + auth.Parameter : null,
            request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(' ', agents) : null,
            body);

        int priorForEndpoint;
        lock (_lock)
        {
            priorForEndpoint = _requests.Count(r => r.Endpoint == recorded.Endpoint);
            _requests.Add(recorded);
        }

        return _responder(recorded, priorForEndpoint);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// 429 with NO <c>Retry-After</c> — the form Epic actually sends. Epic
    /// returns <c>x-epic-error-code</c> and <c>x-epic-correlation-id</c> instead,
    /// which is exactly why the resilience handler cannot lean on a server-stated
    /// delay the way the Steam one does.
    /// </summary>
    public static HttpResponseMessage TooManyRequests()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                "{\"errorCode\":\"errors.com.epicgames.common.throttled\"}", Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("x-epic-error-code", "1041");
        return response;
    }
}
