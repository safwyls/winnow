using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;

namespace Winnow.Covers.Tests;

/// <summary>
/// A stand-in <see cref="IIgdbClient"/>. Records every batch it is handed,
/// because "one lookup per cover" and "one lookup per library" are the same code
/// path and only the call log tells them apart.
/// </summary>
internal sealed class FakeIgdbClient : IIgdbClient
{
    private readonly Dictionary<string, string?> _covers = new(StringComparer.Ordinal);

    /// <summary>Every batch of appids that reached the client, in order.</summary>
    public List<string[]> Batches { get; } = [];

    public int BatchCount
    {
        get
        {
            lock (Batches)
            {
                return Batches.Count;
            }
        }
    }

    public int AppIdsRequested
    {
        get
        {
            lock (Batches)
            {
                return Batches.Sum(b => b.Length);
            }
        }
    }

    public bool Configured { get; set; } = true;

    /// <summary>Thrown from the next resolve, once, when set — a transport failure.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>Binds a Steam appid to an IGDB cover url, in IGDB's own t_cover_big form.</summary>
    public void AddCover(string appId, string imageId)
        => _covers[appId] = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";

    /// <summary>Binds an appid to an IGDB game that genuinely has no cover art.</summary>
    public void AddWithoutCover(string appId) => _covers[appId] = null;

    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => ValueTask.FromResult(Configured);

    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var batch = appIds.ToArray();
        lock (Batches)
        {
            Batches.Add(batch);
        }

        if (FailWith is { } boom)
        {
            return Task.FromException<IReadOnlyDictionary<string, IgdbExternalMatch>>(boom);
        }

        var results = new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal);
        foreach (var appId in batch)
        {
            if (_covers.TryGetValue(appId, out var url))
            {
                results[appId] = new IgdbExternalMatch(appId, 1, "Game " + appId, url, 2012, null);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(results);
    }

    /// <summary>
    /// The generalised form. The cover source only ever reaches IGDB through a
    /// Steam appid, so anything else answers empty — which is exactly what a
    /// real client would do when handed a source it has no rows for, and keeps
    /// this fake from inventing coverage the product does not have.
    /// </summary>
    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(
        int externalGameSourceId,
        IEnumerable<string> uids,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default)
        => externalGameSourceId == 1
            ? ResolveBySteamAppIdsAsync(uids, cacheTtl, ct)
            : Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(
                new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal));

    public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
        IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IgdbGame>>([]);
}
