using System.Globalization;
using System.Net;
using System.Text;

namespace Winnow.Tests.Updates;

/// <summary>
/// One outbound update-signal request, captured before it would have hit the
/// wire.
/// </summary>
public sealed record RecordedUpdateRequest(HttpMethod Method, Uri Uri, string? UserAgent)
{
    /// <summary>Which of the two hosts this request was aimed at.</summary>
    public UpdateHost Host => Uri.Host.Contains("steamcmd", StringComparison.OrdinalIgnoreCase)
        ? UpdateHost.SteamCmd
        : UpdateHost.SteamNews;

    /// <summary>
    /// The appid asked for: a query parameter for the news endpoint, the last
    /// path segment for steamcmd.net.
    /// </summary>
    public string AppId => Host == UpdateHost.SteamNews
        ? Query("appid") ?? string.Empty
        : Uri.Segments[^1].Trim('/');

    /// <summary>The <c>tags</c> filter, for the news endpoint.</summary>
    public string? Tags => Query("tags");

    /// <summary>The <c>feeds</c> filter — expected to be absent; the spike chose tags over feeds.</summary>
    public string? Feeds => Query("feeds");

    /// <summary>The <c>key</c> parameter — expected to be absent; both endpoints are keyless.</summary>
    public string? ApiKey => Query("key");

    /// <summary>One query-string value, decoded. Hand-rolled to keep the test assembly BCL-only.</summary>
    public string? Query(string name)
    {
        foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);
            var key = split < 0 ? pair : pair[..split];
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return split < 0 ? string.Empty : System.Uri.UnescapeDataString(pair[(split + 1)..]);
            }
        }

        return null;
    }
}

/// <summary>The two hosts this module talks to. They share no rate budget.</summary>
public enum UpdateHost
{
    SteamNews,
    SteamCmd,
}

/// <summary>
/// The only transport these tests use. Nothing in this file opens a socket:
/// every response is canned, per the enrichment charter's rule that HTTP clients
/// are tested against fixtures and never against live APIs.
/// </summary>
public sealed class FakeUpdateHandler : HttpMessageHandler
{
    private readonly Func<RecordedUpdateRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedUpdateRequest> _requests = [];

    /// <param name="responder">
    /// Given the request and the zero-based count of prior requests for the same
    /// (host, appid), returns the canned response. The counter is what lets a
    /// test say "fail the first attempt, succeed the second".
    /// </param>
    public FakeUpdateHandler(Func<RecordedUpdateRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order.</summary>
    public IReadOnlyList<RecordedUpdateRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>How many requests went to one host — the number the cost model is about.</summary>
    public int CountFor(UpdateHost host) => Requests.Count(r => r.Host == host);

    /// <summary>How many requests went to one host for one appid.</summary>
    public int CountFor(UpdateHost host, string appId)
        => Requests.Count(r => r.Host == host && string.Equals(r.AppId, appId, StringComparison.Ordinal));

    /// <summary>Distinct appids asked about on one host, in first-seen order.</summary>
    public IReadOnlyList<string> AppIdsFor(UpdateHost host)
        => Requests.Where(r => r.Host == host).Select(r => r.AppId).Distinct(StringComparer.Ordinal).ToArray();

    public void Clear()
    {
        lock (_lock)
        {
            _requests.Clear();
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = new RecordedUpdateRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(' ', agents) : null);

        int prior;
        lock (_lock)
        {
            prior = _requests.Count(r =>
                r.Host == recorded.Host && string.Equals(r.AppId, recorded.AppId, StringComparison.Ordinal));
            _requests.Add(recorded);
        }

        return Task.FromResult(_responder(recorded, prior));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// The shape that makes this module dangerous: 403 with body <c>{}</c>,
    /// meaning "this appid has no news feed" and nothing at all about Winnow's
    /// request rate. Verified live for appids 460, 480, 520 and 750.
    /// </summary>
    public static HttpResponseMessage NoNewsFeed() => Json(HttpStatusCode.Forbidden, "{}");

    /// <summary>429 carrying a delta-seconds <c>Retry-After</c> — the only status that means "slow down".</summary>
    public static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.TryAddWithoutValidation(
            "Retry-After",
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
        return response;
    }
}
