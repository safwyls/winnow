using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Winnow.Api.Contracts.Protocol;

namespace Winnow.Api.Client;

/// <summary>External frontend transport. Disposing a client does not terminate the backend.</summary>
public sealed partial class WinnowApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<CancellationToken, Task<BackendDiscovery>> _discover;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _reconnectDelay;
    private bool _disposed;

    public WinnowApiClient(BackendDiscovery connection, HttpClient? httpClient = null, TimeSpan? reconnectDelay = null)
        : this(_ => Task.FromResult(connection), httpClient, reconnectDelay)
    {
        BackendConnection.Validate(connection);
    }

    public WinnowApiClient(Func<CancellationToken, Task<BackendDiscovery>> discover,
        HttpClient? httpClient = null, TimeSpan? reconnectDelay = null)
    {
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(1);
        if (_reconnectDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reconnectDelay));
    }

    public static WinnowApiClient Attach(string dataDirectory) => new(ct => BackendConnection.ReadAsync(dataDirectory, ct));

    public Task<BackendHealth> GetHealthAsync(CancellationToken ct = default) => GetAsync<BackendHealth>("health", ct);
    public Task<BackendCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) => GetAsync<BackendCapabilities>("capabilities", ct);

    public async Task<T> GetAsync<T>(string route, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        using var request = await RequestAsync(HttpMethod.Get, route, linked.Token).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, linked.Token).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(Json, linked.Token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The backend returned an empty JSON response.");
    }

    /// <summary>Commands are never automatically retried: a lost response may follow a committed mutation.</summary>
    public async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string route, TRequest body,
        string? idempotencyKey = null, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        using var request = await RequestAsync(method, route, linked.Token);
        request.Content = JsonContent.Create(body, options: Json);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await _http.SendAsync(request, linked.Token);
        await EnsureSuccessAsync(response, linked.Token);
        return await response.Content.ReadFromJsonAsync<TResponse>(Json, linked.Token)
            ?? throw new InvalidDataException("The backend returned an empty JSON response.");
    }

    public async Task SendAsync<TRequest>(HttpMethod method, string route, TRequest body, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        using var request = await RequestAsync(method, route, linked.Token);
        request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, linked.Token);
        await EnsureSuccessAsync(response, linked.Token);
    }

    /// <summary>Subscribe before loading snapshots. Resync-required means refetch all observed state.
    /// The cursor advances only when an event is delivered to the caller.</summary>
    public async IAsyncEnumerable<BackendEvent> WatchEventsAsync(string? cursor = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var token = linked.Token;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await using var events = ReadEventsAsync(cursor, token).GetAsyncEnumerator(token);
            while (true)
            {
                bool next;
                try { next = await events.MoveNextAsync(); }
                catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500) { break; }
                catch (IOException) { break; }
                if (!next) break;
                var change = events.Current;
                if (cursor is not null && change.Kind != "resync-required")
                {
                    var separator = cursor.LastIndexOf(':');
                    if (separator > 0 && long.TryParse(cursor.AsSpan(separator + 1), out var previousSequence))
                    {
                        var previousEpoch = cursor[..separator];
                        if (change.Epoch == previousEpoch && change.Sequence <= previousSequence) continue;
                        if (change.Epoch != previousEpoch || change.Sequence != previousSequence + 1)
                            change = change with { Kind = "resync-required", Resource = null };
                    }
                }
                cursor = $"{change.Epoch}:{change.Sequence}";
                yield return change;
            }
            await Task.Delay(_reconnectDelay, token);
        }
    }

    private async IAsyncEnumerable<BackendEvent> ReadEventsAsync(string? cursor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = await RequestAsync(HttpMethod.Get, "events", ct);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (cursor is not null) request.Headers.Add("Last-Event-ID", cursor);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            (await _discover(ct)).Token != request.Headers.Authorization?.Parameter)
            throw new HttpRequestException("The backend authentication changed while connecting.");
        await EnsureSuccessAsync(response, ct);
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            throw new InvalidDataException("The backend did not return an event stream.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                var change = JsonSerializer.Deserialize<BackendEvent>(data.ToString(), Json)
                    ?? throw new InvalidDataException("The backend returned an empty event.");
                if (string.IsNullOrWhiteSpace(change.Epoch) || change.Sequence < 0)
                    throw new InvalidDataException("The backend returned an invalid event cursor.");
                data.Clear();
                yield return change;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line.AsSpan(5);
                if (value.StartsWith(" ")) value = value[1..];
                data.Append(value).Append('\n');
                if (data.Length > 1024 * 1024) throw new InvalidDataException("Backend event exceeds the size limit.");
            }
        }
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string route, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(route) || route.StartsWith('/') || route.Contains("..", StringComparison.Ordinal)
            || route.Contains('\\') || Uri.TryCreate(route, UriKind.Absolute, out _))
            throw new ArgumentException("A relative API route is required.", nameof(route));
        var connection = await _discover(ct).ConfigureAwait(false);
        var address = BackendConnection.Validate(connection);
        var uri = new Uri(address, "api/v1/" + route);
        if (!uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal))
            throw new ArgumentException("The route must remain inside the versioned API.", nameof(route));
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new BackendApiException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_ownsHttp) _http.Dispose();
        _lifetime.Dispose();
    }
}
