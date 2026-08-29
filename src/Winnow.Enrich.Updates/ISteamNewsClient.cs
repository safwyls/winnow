using Winnow.Enrich.Updates.Model;

namespace Winnow.Enrich.Updates;

/// <summary>
/// Keyless client for <c>ISteamNews/GetNewsForApp</c> -- the cheap announcement
/// signal (~440 bytes per app). Every method is total: failures yield an outcome, never an exception.
/// </summary>
public interface ISteamNewsClient
{
    /// <summary>The newest patch note for one appid, or an outcome explaining why there isn't one.</summary>
    Task<NewsFetch> GetLatestPatchNoteAsync(string appId, CancellationToken ct = default);
}
