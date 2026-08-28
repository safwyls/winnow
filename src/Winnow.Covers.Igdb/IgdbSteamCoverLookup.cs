using System.Collections.Concurrent;
using Winnow.Enrich.Igdb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Covers.Igdb;

/// <summary>
/// Steam appid → IGDB cover URL, resolved in batches.
///
/// <para><see cref="ICoverSource.TryFetchAsync"/> is a per-key contract and
/// <c>ResolveBySteamAppIdsAsync</c> is a per-batch one, and the gap between them
/// is worth 96 HTTP requests on a real library. This closes it: a call for an
/// unresolved appid parks on a shared batch, the batch goes when it is full or
/// when <see cref="IgdbCoverOptions.BatchLinger"/> elapses, and every waiter is
/// completed from the one response. Results — including "IGDB has no cover for
/// this appid" — are memoised for the process, on top of the 30-day SQLite cache
/// <c>IgdbClient</c> already keeps, so a warm library costs nothing.</para>
///
/// <para>A failed batch faults its waiters rather than answering "no cover".
/// That distinction is the same one <c>SteamCapsuleSource</c> draws between a
/// 404 and a 403, and it matters for the same reason: <see cref="CoverPipeline"/>
/// writes a 30-day negative marker on the strength of a source declining, so a
/// transport failure that looked like a decline would cost the user a month of
/// placeholder art. A fault propagates as "not yet" and caches nothing.</para>
/// </summary>
internal sealed class IgdbSteamCoverLookup
{
    private readonly IIgdbClient _igdb;
    private readonly IgdbCoverOptions _options;
    private readonly ILogger _log;

    /// <summary>appid → cover URL, or null for "IGDB knows this appid has no cover".</summary>
    private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();
    private Dictionary<string, TaskCompletionSource<string?>> _pending = new(StringComparer.Ordinal);
    private Task? _linger;

    public IgdbSteamCoverLookup(IIgdbClient igdb, IgdbCoverOptions options, ILogger? log = null)
    {
        _igdb = igdb;
        _options = options;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>Batches actually sent. Diagnostics and tests — this is the number the design is about.</summary>
    public int BatchCount => Volatile.Read(ref _batches);

    private int _batches;

    /// <summary>The IGDB cover URL for one Steam appid, or null when there is none.</summary>
    public async Task<string?> GetCoverUrlAsync(string appId, CancellationToken ct)
    {
        if (_resolved.TryGetValue(appId, out var memo))
        {
            return memo;
        }

        Task<string?> waiter;
        Dictionary<string, TaskCompletionSource<string?>>? full = null;

        lock (_gate)
        {
            if (_resolved.TryGetValue(appId, out memo))
            {
                return memo;
            }

            if (!_pending.TryGetValue(appId, out var tcs))
            {
                tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[appId] = tcs;
            }

            waiter = tcs.Task;

            if (_pending.Count >= Math.Max(1, _options.MaxBatchSize))
            {
                full = TakePendingLocked();
            }
            else if (_linger is null)
            {
                _linger = LingerAsync();
            }
        }

        if (full is not null)
        {
            await SendAsync(full).ConfigureAwait(false);
        }

        // WaitAsync, not a linked token: the batch is shared, and one caller
        // walking away — a tile scrolled out of view — must not cancel it for
        // everyone still waiting on the same response.
        return await waiter.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves <paramref name="appIds"/> up front, in batches, without anyone
    /// waiting on the result. Used for the negative-marker pre-warm and
    /// available to a caller that already knows the whole library.
    /// </summary>
    public async Task PrewarmAsync(IEnumerable<string> appIds, CancellationToken ct = default)
    {
        var wanted = appIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => !_resolved.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return;
        }

        foreach (var chunk in wanted.Chunk(Math.Max(1, _options.MaxBatchSize)))
        {
            Interlocked.Increment(ref _batches);
            var matches = await _igdb.ResolveBySteamAppIdsAsync(chunk, ct: ct).ConfigureAwait(false);
            foreach (var appId in chunk)
            {
                _resolved[appId] = matches.TryGetValue(appId, out var match) ? match.CoverUrl : null;
            }
        }

        _log.LogDebug("Pre-warmed {Count} Steam appids against IGDB for cover art.", wanted.Length);
    }

    private async Task LingerAsync()
    {
        await Task.Delay(_options.BatchLinger).ConfigureAwait(false);

        Dictionary<string, TaskCompletionSource<string?>> batch;
        lock (_gate)
        {
            _linger = null;
            batch = TakePendingLocked();
        }

        await SendAsync(batch).ConfigureAwait(false);
    }

    /// <summary>Swaps the forming batch out. Caller holds <see cref="_gate"/>.</summary>
    private Dictionary<string, TaskCompletionSource<string?>> TakePendingLocked()
    {
        var batch = _pending;
        _pending = new Dictionary<string, TaskCompletionSource<string?>>(StringComparer.Ordinal);
        return batch;
    }

    private async Task SendAsync(Dictionary<string, TaskCompletionSource<string?>> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        Interlocked.Increment(ref _batches);
        try
        {
            // CancellationToken.None on purpose: the batch belongs to every
            // waiter, not to whichever tile happened to open it.
            var matches = await _igdb
                .ResolveBySteamAppIdsAsync(batch.Keys, ct: CancellationToken.None)
                .ConfigureAwait(false);

            foreach (var (appId, tcs) in batch)
            {
                var url = matches.TryGetValue(appId, out var match) ? match.CoverUrl : null;
                _resolved[appId] = url;
                tcs.TrySetResult(url);
            }
        }
        catch (Exception ex)
        {
            // Nothing is memoised: this said nothing about whether the art
            // exists, and the waiters must hear a failure rather than a "no".
            _log.LogWarning(ex, "IGDB lookup failed for {Count} cover keys.", batch.Count);
            foreach (var tcs in batch.Values)
            {
                tcs.TrySetException(ex);
            }
        }
    }
}
