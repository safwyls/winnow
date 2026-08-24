using System.Net;
using Hoard.Enrich.Igdb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Covers.Igdb;

/// <summary>
/// The gap-filler <c>ICoverSource</c> the cover pipeline was designed around and
/// never had. Steam's portrait capsule answers most of a library and 404s for
/// the rest — on a real 616-game library that is 96 games with no art, and IGDB
/// has covers for 64 of them: Cry of Fear, Nosgoth, Tribes: Ascend, Marvel
/// Heroes, PixelJunk Eden, Gnomoria. Delisted games, which is the population the
/// product exists to surface. The remaining 32 are demos and tools IGDB genuinely
/// does not carry.
///
/// <para>Registered after <c>SteamCapsuleSource</c>, so Steam's 600x900 portrait
/// — the shape <c>design-system.md</c> §5 specifies — always wins and this only
/// runs where Steam declined.</para>
///
/// <para>With no IGDB credentials configured this is a clean no-op: it declines
/// every key without a request, exactly as the grid behaved before it existed.
/// It also reports a different <see cref="SourceSetId"/> in that state, so the
/// negative markers written while it was silent are retried the moment
/// credentials appear rather than standing for the full 30-day TTL.</para>
/// </summary>
public sealed class IgdbCoverSource : ICoverSource
{
    /// <summary>Named client for images.igdb.com — separate from the API client, which is authenticated.</summary>
    public const string HttpClientName = "hoard-covers-igdb";

    private const int Unknown = 0;
    private const int Configured = 1;
    private const int NotConfigured = 2;

    private readonly IIgdbClient _igdb;
    private readonly IHttpClientFactory _clients;
    private readonly CoverCacheOptions _coverOptions;
    private readonly IgdbCoverOptions _options;
    private readonly IgdbSteamCoverLookup _lookup;
    private readonly ILogger<IgdbCoverSource> _log;

    private readonly Lock _prewarmLock = new();
    private Task? _prewarm;

    // A cheap synchronous view of an asynchronous fact. Only SourceSetId reads
    // it, and only TryFetchAsync writes it — which is why CanHandle stays about
    // key shape: a source that stopped being asked could never notice that it
    // had become able to answer.
    private int _configuration = Unknown;

    public IgdbCoverSource(
        IIgdbClient igdb,
        IHttpClientFactory clients,
        CoverCacheOptions coverOptions,
        IgdbCoverOptions options,
        ILogger<IgdbCoverSource>? log = null)
    {
        _igdb = igdb;
        _clients = clients;
        _coverOptions = coverOptions;
        _options = options;
        _log = log ?? NullLogger<IgdbCoverSource>.Instance;
        _lookup = new IgdbSteamCoverLookup(igdb, options, _log);
    }

    public string Name => "igdb-cover";

    /// <inheritdoc/>
    public string SourceSetId => Volatile.Read(ref _configuration) == NotConfigured
        ? "igdb-cover(unconfigured)"
        : "igdb-cover";

    /// <summary>Lookups actually sent to IGDB. Diagnostics and tests.</summary>
    public int LookupBatchCount => _lookup.BatchCount;

    /// <summary>
    /// Steam appids only. IGDB is reached <em>through</em> the Steam appid — the
    /// <c>external_games</c> hard join — so a key already carrying an IGDB id is
    /// not something this source has a lookup for.
    /// </summary>
    public bool CanHandle(CoverKey key)
        => key.Provider == CoverProviders.Steam
           && key.Id.Length > 0
           && key.Id.All(char.IsAsciiDigit);

    /// <summary>
    /// Resolves <paramref name="appIds"/> to IGDB covers ahead of demand, in
    /// batches. Optional — the source batches lazily on its own — but a caller
    /// that already holds the whole library can collapse the gap-fill into a
    /// single request with it.
    /// </summary>
    public Task PrewarmAsync(IEnumerable<string> appIds, CancellationToken ct = default)
        => _lookup.PrewarmAsync(appIds, ct);

    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        if (!CanHandle(key))
        {
            return null;
        }

        if (!await IsConfiguredAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        await EnsurePrewarmedAsync().ConfigureAwait(false);

        var coverUrl = await _lookup.GetCoverUrlAsync(key.Id, ct).ConfigureAwait(false);
        if (IgdbImageUrl.WithSize(coverUrl, _options.ImageSizeToken) is not { Length: > 0 } url)
        {
            _log.LogDebug("No IGDB cover for {Key}", key);
            return null;
        }

        var client = _clients.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

        // 404 from the image CDN means this image id is not there — an answer
        // about existence, and a normal one. Anything else is a transport
        // failure and must surface: the pipeline stamps a 30-day `.none` on a
        // decline, and a CDN refusing traffic is not the CDN saying the game has
        // no art. Same distinction SteamCapsuleSource draws for its 403.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log.LogDebug("IGDB image 404 for {Key} at {Url}", key, url);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return bytes.Length > 0 ? bytes : null;
    }

    private async ValueTask<bool> IsConfiguredAsync(CancellationToken ct)
    {
        // Cheap after the first call — the credential provider memoises — so
        // this is re-asked every time rather than latched, and credentials added
        // mid-session take effect without a restart.
        var configured = await _igdb.IsConfiguredAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _configuration, configured ? Configured : NotConfigured);
        return configured;
    }

    /// <summary>
    /// One batch, once, covering every key the previous run recorded as having
    /// no art anywhere. See <see cref="IgdbCoverOptions.PrewarmFromNegativeMarkers"/>
    /// for why those markers are exactly the right set to ask about.
    /// </summary>
    private Task EnsurePrewarmedAsync()
    {
        if (!_options.PrewarmFromNegativeMarkers)
        {
            return Task.CompletedTask;
        }

        var started = Volatile.Read(ref _prewarm);
        if (started is not null)
        {
            return started;
        }

        lock (_prewarmLock)
        {
            // One task, shared by every waiter, and never cancelled by whichever
            // tile happened to be first: it answers for all of them.
            return _prewarm ??= PrewarmFromNegativeMarkersAsync();
        }
    }

    private async Task PrewarmFromNegativeMarkersAsync()
    {
        try
        {
            var appIds = ReadNegativeMarkerAppIds();
            if (appIds.Count > 0)
            {
                await _lookup.PrewarmAsync(appIds, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // A pre-warm is an optimisation. Failing it must cost nothing but
            // the batching it would have saved — the lazy path still answers
            // every key, and a failure there is what surfaces.
            _log.LogDebug(ex, "IGDB cover pre-warm skipped.");
        }
    }

    private List<string> ReadNegativeMarkerAppIds()
    {
        var ids = new List<string>();
        var root = _coverOptions.CacheDirectory;
        if (!Directory.Exists(root))
        {
            return ids;
        }

        var prefix = CoverProviders.Steam + "_";
        foreach (var path in Directory.EnumerateFiles(root, "*.none"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!stem.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var appId = stem[prefix.Length..];
            if (appId.Length > 0 && appId.All(char.IsAsciiDigit))
            {
                ids.Add(appId);
            }

            if (ids.Count >= _options.MaxBatchSize)
            {
                break;
            }
        }

        return ids;
    }
}
