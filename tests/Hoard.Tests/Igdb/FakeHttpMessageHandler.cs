using System.Net;
using System.Text;

namespace Hoard.Tests.Igdb;

/// <summary>One outbound request, captured before it would have hit the wire.</summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string Body,
    string? ContentType,
    string? ClientId,
    string? Authorization)
{
    /// <summary>Last path segment: "token", "external_games", "games".</summary>
    public string Endpoint => Uri.Segments[^1].TrimEnd('/');
}

/// <summary>
/// The only transport these tests use. Nothing in this file opens a socket:
/// every IGDB and Twitch response is canned, per the enrichment charter's rule
/// that HTTP clients are tested against fixtures and never against live APIs.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedRequest> _requests = [];

    /// <param name="responder">
    /// Given the request and the zero-based count of prior requests to the same
    /// endpoint, returns the canned response. The counter is what lets a test
    /// say "fail the first attempt, succeed the second".
    /// </param>
    public FakeHttpMessageHandler(Func<RecordedRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
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

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Content?.Headers.ContentType?.MediaType,
            request.Headers.TryGetValues("Client-ID", out var ids) ? ids.FirstOrDefault() : null,
            request.Headers.Authorization?.ToString());

        int priorForEndpoint;
        lock (_lock)
        {
            priorForEndpoint = _requests.Count(
                r => string.Equals(r.Endpoint, recorded.Endpoint, StringComparison.Ordinal));
            _requests.Add(recorded);
        }

        return _responder(recorded, priorForEndpoint);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>429 carrying a delta-seconds <c>Retry-After</c>, the form Steam and IGDB both send.</summary>
    public static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation(
            "Retry-After",
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }
}
