using System.Net;
using System.Text;
using System.Text.Json;

namespace Hoard.Tests.SteamStore;

/// <summary>
/// One outbound store request, captured before it would have hit the wire.
/// </summary>
/// <param name="Uri">The full request URI, <c>input_json</c> still URL-encoded.</param>
/// <param name="UserAgent">What §4.3 requires Hoard to identify itself with.</param>
public sealed record RecordedStoreRequest(HttpMethod Method, Uri Uri, string? UserAgent)
{
    /// <summary><c>IStoreBrowseService/GetItems</c> — service and method, without the version segment.</summary>
    public string Endpoint
    {
        get
        {
            var segments = Uri.Segments
                .Select(s => s.Trim('/'))
                .Where(s => s.Length > 0)
                .ToArray();

            // .../IStoreBrowseService/GetItems/v1/ → drop the trailing "v1".
            return segments.Length >= 3 ? segments[^3] + "/" + segments[^2] : string.Join('/', segments);
        }
    }

    /// <summary>The decoded <c>input_json</c> query parameter — the entire request payload.</summary>
    public string InputJson
    {
        get
        {
            const string marker = "input_json=";
            var query = Uri.Query.TrimStart('?');
            var start = query.IndexOf(marker, StringComparison.Ordinal);
            return start < 0 ? string.Empty : Uri.UnescapeDataString(query[(start + marker.Length)..]);
        }
    }

    /// <summary>The appids this request asked for, in order.</summary>
    public IReadOnlyList<string> RequestedAppIds
    {
        get
        {
            if (InputJson.Length == 0)
            {
                return [];
            }

            using var document = JsonDocument.Parse(InputJson);
            return document.RootElement.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray()
                    .Where(e => e.TryGetProperty("appid", out _))
                    .Select(e => e.GetProperty("appid").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray()
                : [];
        }
    }
}

/// <summary>
/// The only transport these tests use. Nothing in this file opens a socket:
/// every store response is canned, per the enrichment charter's rule that HTTP
/// clients are tested against fixtures and never against live APIs.
/// </summary>
public sealed class FakeStoreHandler : HttpMessageHandler
{
    private readonly Func<RecordedStoreRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedStoreRequest> _requests = [];

    /// <param name="responder">
    /// Given the request and the zero-based count of prior requests to the same
    /// endpoint, returns the canned response. The counter is what lets a test
    /// say "fail the first attempt, succeed the second".
    /// </param>
    public FakeStoreHandler(Func<RecordedStoreRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order.</summary>
    public IReadOnlyList<RecordedStoreRequest> Requests
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
        var recorded = new RecordedStoreRequest(
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

    /// <summary>429 carrying a delta-seconds <c>Retry-After</c>, the form §4.2 reports Steam sending.</summary>
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
