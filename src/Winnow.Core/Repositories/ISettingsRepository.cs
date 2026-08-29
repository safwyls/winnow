namespace Winnow.Core.Repositories;

/// <summary>
/// General-purpose string key/value store over the <c>settings</c> table.
/// Keys should be namespaced (e.g. <c>module.thing</c>) to avoid collisions.
/// </summary>
public interface ISettingsRepository
{
    /// <summary>Returns the stored value, or null if the key has never been set.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Writes a value under the given key, replacing any previous value.</summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
