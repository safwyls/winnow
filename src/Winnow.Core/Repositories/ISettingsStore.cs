namespace Winnow.Enrich.Igdb.Storage;

/// <summary>
/// The <c>settings(key, value)</c> table (§6). Holds user-supplied IGDB
/// credentials and the cached Twitch access token, so neither is re-entered
/// nor re-minted across restarts.
/// </summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string? value, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}
