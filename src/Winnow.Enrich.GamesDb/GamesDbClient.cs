using System.Net;
using System.Text.Json;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.GamesDb.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.GamesDb;

/// <summary>
/// Client for gamesdb.gog.com's cross-store identity graph. Never throws;
/// failures return null and are not cached.
/// </summary>
public sealed class GamesDbClient : IGameIdentityGraph
{
    /// <summary>Named/typed <see cref="HttpClient"/> for gamesdb.gog.com.</summary>
    public const string HttpClientName = "gamesdb";

    private readonly HttpClient _http;
    private readonly IGamesDbCache _cache;
    private readonly GamesDbOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<GamesDbClient> _log;

    public GamesDbClient(
        HttpClient http,
        IGamesDbCache cache,
        GamesDbOptions options,
        TimeProvider clock,
        ILogger<GamesDbClient> log)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.BaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <summary>Cache key for one lookup. Platform-scoped: <c>Bluebird</c> means nothing without <c>epic</c>.</summary>
    public static string CacheKey(string platform, string externalId)
        => "release:" + platform + ":" + externalId;

    /// <summary>Requests actually sent. Diagnostics and tests — a warm library must read zero.</summary>
    public int RequestCount => Volatile.Read(ref _requests);

    private int _requests;

    public async Task<GamesDbGame?> ResolveAsync(
        string platform, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        platform = platform.Trim();
        externalId = externalId.Trim();

        var key = CacheKey(platform, externalId);
        var cutoff = Cutoff();
        var cached = await _cache.GetAsync(key, ct).ConfigureAwait(false);
        if (cached is { } entry && entry.FetchedAt >= cutoff)
        {
            // A null payload is a cached 404: the graph has no release under
            // this id, and asking again for 90 days would spend an unpublished
            // endpoint's goodwill to re-learn the same nothing.
            return Deserialize(entry.PayloadJson)?.ToDomain(platform, externalId);
        }

        var fetched = await FetchAsync(platform, externalId, ct).ConfigureAwait(false);
        switch (fetched.Outcome)
        {
            case FetchOutcome.Found:
                await _cache.SetAsync(
                    key, Serialize(fetched.Payload!), _clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
                return fetched.Payload!.ToDomain(platform, externalId);

            case FetchOutcome.NotFound:
                // 404 is an ANSWER — "no release here" — so it is cached, like
                // IGDB's cached misses.
                await _cache.SetAsync(key, null, _clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
                return null;

            default:
                // Failed is NOT an answer. Nothing is written: a 503 or a dead
                // socket must never become 90 days of "this game has no
                // cross-store twin". This is the distinction that decides
                // whether an outage costs one run or a quarter.
                return null;
        }
    }

    private DateTime Cutoff()
    {
        var ttl = _options.CacheTtl;
        return ttl <= TimeSpan.Zero ? DateTime.MaxValue : _clock.GetUtcNow().UtcDateTime - ttl;
    }

    private async Task<Fetch> FetchAsync(string platform, string externalId, CancellationToken ct)
    {
        // Both segments are escaped: an Epic AppName is an opaque string from a
        // file on disk, and while every observed value has been alphanumeric,
        // building a URL by concatenation without escaping is how a path
        // traversal gets into a request one day.
        var path = "platforms/" + Uri.EscapeDataString(platform)
                   + "/external_releases/" + Uri.EscapeDataString(externalId);

        Interlocked.Increment(ref _requests);

        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new Fetch(FetchOutcome.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.LogDebug(
                    "gamesdb {Platform}/{ExternalId} returned {StatusCode}; treated as unknown, not as absent.",
                    platform, externalId, (int)response.StatusCode);
                return new Fetch(FetchOutcome.Failed, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer
                .DeserializeAsync<GamesDbLookupDto>(stream, GamesDbJson.Options, ct)
                .ConfigureAwait(false);

            var payload = CachedRelease.From(dto);
            return payload is null
                // A 200 whose body carries no game id is a shape change, not an
                // absence. Failed, so nothing is cached and the next run looks
                // again — a service that has been reshaped should cost one
                // wasted request per run, not a silent quarter of blank tiles.
                ? new Fetch(FetchOutcome.Failed, null)
                : new Fetch(FetchOutcome.Found, payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not a lookup failure, and it must not be
            // swallowed into a silent "no twin".
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                       or IOException or JsonException)
        {
            _log.LogDebug(
                ex, "gamesdb {Platform}/{ExternalId} lookup failed; continuing without it.",
                platform, externalId);
            return new Fetch(FetchOutcome.Failed, null);
        }
    }

    private static string Serialize(CachedRelease value)
        => JsonSerializer.Serialize(value, GamesDbJson.Options);

    private static CachedRelease? Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<CachedRelease>(json, GamesDbJson.Options);

    private enum FetchOutcome
    {
        /// <summary>The graph answered and knows this id.</summary>
        Found,

        /// <summary>The graph answered 404: no release under this id. Cacheable.</summary>
        NotFound,

        /// <summary>Nothing was learned. Never cacheable.</summary>
        Failed,
    }

    private readonly record struct Fetch(FetchOutcome Outcome, CachedRelease? Payload);

    /// <summary>The cached projection: a game id and the store releases sharing it.</summary>
    private sealed record CachedRelease(string GameId, IReadOnlyList<CachedStoreId> Releases)
    {
        internal static CachedRelease? From(GamesDbLookupDto? dto)
        {
            if (dto?.GameId is not { Length: > 0 } gameId)
            {
                return null;
            }

            var releases = new List<CachedStoreId>();
            foreach (var release in dto.Game?.Releases ?? [])
            {
                if (release.PlatformId is { Length: > 0 } platform
                    && release.ExternalId is { Length: > 0 } externalId)
                {
                    releases.Add(new CachedStoreId(platform, externalId));
                }
            }

            return new CachedRelease(gameId, releases);
        }

        internal GamesDbGame ToDomain(string platform, string externalId)
            => new(
                platform,
                externalId,
                GameId,
                Releases.Select(r => new GamesDbRelease(r.Platform, r.ExternalId)).ToArray());
    }

    private sealed record CachedStoreId(string Platform, string ExternalId);
}
