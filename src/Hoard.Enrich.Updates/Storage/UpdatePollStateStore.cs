namespace Hoard.Enrich.Updates.Storage;

/// <summary>
/// Per-appid poll state, on top of <see cref="IUpdateSignalCache"/>. Separated
/// from the cache interface so the poller depends on "what do we remember about
/// this app" rather than on a JSON blob in a key-value table.
/// </summary>
public interface IUpdatePollStateStore
{
    /// <summary>State for each appid, in one round trip. Appids never polled are absent.</summary>
    Task<IReadOnlyDictionary<string, UpdatePollState>> GetManyAsync(
        IEnumerable<string> appIds, CancellationToken ct = default);

    /// <summary>
    /// Records the state and stamps <c>polledAt</c> as the app's last-polled
    /// time — the value the stagger reads back to decide what is due.
    /// </summary>
    Task SetAsync(string appId, UpdatePollState state, DateTime polledAt, CancellationToken ct = default);
}

/// <summary><see cref="IUpdatePollStateStore"/> over <c>metadata_cache</c>.</summary>
public sealed class UpdatePollStateStore : IUpdatePollStateStore
{
    /// <summary><c>metadata_cache.provider</c> for poll state.</summary>
    public const string CacheProvider = "update-poll";

    private readonly IUpdateSignalCache _cache;

    public UpdatePollStateStore(IUpdateSignalCache cache) => _cache = cache;

    /// <summary>Cache key for one app's poll state.</summary>
    public static string StateCacheKey(string appId) => "state:" + appId;

    public async Task<IReadOnlyDictionary<string, UpdatePollState>> GetManyAsync(
        IEnumerable<string> appIds, CancellationToken ct = default)
    {
        var ids = appIds.Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, UpdatePollState>(StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return result;
        }

        var entries = await _cache.GetManyAsync(CacheProvider, ids.Select(StateCacheKey), ct);
        foreach (var appId in ids)
        {
            if (entries.TryGetValue(StateCacheKey(appId), out var entry))
            {
                result[appId] = UpdatePollState.FromCache(entry);
            }
        }

        return result;
    }

    public Task SetAsync(string appId, UpdatePollState state, DateTime polledAt, CancellationToken ct = default)
        => _cache.SetAsync(CacheProvider, StateCacheKey(appId), state.ToJson(), polledAt, ct);
}
