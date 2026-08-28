using System.Net;
using System.Text;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// One outbound Steam Web API request, captured before it would have hit the
/// wire.
/// </summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Uri">The full request URI, including the <c>key</c> parameter.</param>
/// <param name="UserAgent">What §4.3's rule requires Winnow to identify itself with.</param>
public sealed record RecordedSteamWebRequest(HttpMethod Method, Uri Uri, string? UserAgent)
{
    /// <summary>
    /// The query parameters, decoded. Parsed here by hand rather than with
    /// <c>HttpUtility</c> so the assertion sees exactly the bytes that would have
    /// gone on the wire, including any parameter sent twice.
    /// </summary>
    public IReadOnlyDictionary<string, string> Query
    {
        get
        {
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var name = equals < 0 ? pair : pair[..equals];
                var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
                parsed[Uri.UnescapeDataString(name)] = value;
            }

            return parsed;
        }
    }

    /// <summary><c>IPlayerService/GetOwnedGames</c> — service and method, without the version segment.</summary>
    public string Endpoint
    {
        get
        {
            var segments = Uri.Segments
                .Select(s => s.Trim('/'))
                .Where(s => s.Length > 0)
                .ToArray();

            // .../IPlayerService/GetOwnedGames/v1/ → drop the trailing "v1".
            return segments.Length >= 3 ? segments[^3] + "/" + segments[^2] : string.Join('/', segments);
        }
    }

    /// <summary>A decoded query parameter, or null when it was not sent at all.</summary>
    public string? Parameter(string name) => Query.GetValueOrDefault(name);

    /// <summary>Whether the parameter was sent, whatever its value.</summary>
    public bool HasParameter(string name) => Query.ContainsKey(name);
}

/// <summary>
/// The only transport these tests use. Nothing in this file opens a socket:
/// every Steam response is canned, per the enrichment charter's rule that HTTP
/// clients are tested against fixtures and never against live APIs.
/// </summary>
public sealed class FakeSteamWebHandler : HttpMessageHandler
{
    private readonly Func<RecordedSteamWebRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedSteamWebRequest> _requests = [];

    /// <param name="responder">
    /// Given the request and the zero-based count of prior requests to the same
    /// endpoint, returns the canned response. The counter is what lets a test say
    /// "fail the first attempt, succeed the second".
    /// </param>
    public FakeSteamWebHandler(Func<RecordedSteamWebRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order.</summary>
    public IReadOnlyList<RecordedSteamWebRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    public int CountFor(string endpoint)
        => Requests.Count(r => string.Equals(r.Endpoint, endpoint, StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = new RecordedSteamWebRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(' ', agents) : null);

        int priorForEndpoint;
        lock (_lock)
        {
            priorForEndpoint = _requests.Count(
                r => string.Equals(r.Endpoint, recorded.Endpoint, StringComparison.Ordinal));
            _requests.Add(recorded);
        }

        return Task.FromResult(_responder(recorded, priorForEndpoint));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>429 carrying a delta-seconds <c>Retry-After</c> — the form §4.2 reports Steam sending.</summary>
    public static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation(
            "Retry-After",
            ((int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }
}
