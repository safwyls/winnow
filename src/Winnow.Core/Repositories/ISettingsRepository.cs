namespace Winnow.Core.Repositories;

/// <summary>
/// The general-purpose key/value store over the §6 <c>settings</c> table, for
/// the small scalars that are user preferences rather than domain facts —
/// remembered view mode, remembered sort, window placement.
///
/// <para><b>Strings on both sides, deliberately.</b> A preference store that
/// knew about types would have to take a position on what happens when the
/// stored text no longer parses, and it has no basis for one: only the caller
/// knows whether an unreadable value should fall back to a default, be ignored,
/// or be repaired. So this returns exactly what was written, and every typed
/// wrapper (see <see cref="IResolveStateRepository"/>, which keeps its own
/// narrow contract for the same reason) decides that for itself.</para>
///
/// <para><b>Namespace your keys.</b> One table is shared by every module —
/// IGDB credentials, the cached Twitch token, the resolver's sweep timestamp —
/// so a bare <c>view_mode</c> is a collision waiting for the second module that
/// wants one. Use <c>module.thing</c>.</para>
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// The stored value for <paramref name="key"/>, or null when nothing has
    /// ever been written under it. Null is "unset", never "empty": a caller
    /// that stores the empty string gets the empty string back.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Writes <paramref name="value"/> under <paramref name="key"/>, replacing
    /// any previous value. Last write wins; there is no read-modify-write here
    /// for a caller to lose a race on.
    /// </summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
