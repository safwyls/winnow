using Winnow.Enrich.Updates.Model;

namespace Winnow.Enrich.Updates;

/// <summary>
/// Client for <c>api.steamcmd.net/v1/info/{appid}</c> -- the expensive build-push
/// signal (~12 KB per call). Called only when news indicates a change. Caches bodies
/// for <see cref="UpdateSignalOptions.BuildInfoCacheTtl"/>; failures degrade to "no signal".
/// </summary>
public interface IBuildInfoClient
{
    /// <summary>The <c>public</c> branch for one appid, or an outcome explaining why there isn't one.</summary>
    Task<BuildInfoFetch> GetPublicBranchAsync(
        string appId, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// The <c>common</c> block for one appid (Steam's name and type), or an outcome
    /// explaining why there isn't one. Shares the same cached response body as
    /// <see cref="GetPublicBranchAsync"/>.
    /// </summary>
    /// <param name="cachedOnly">When true, answers from cache or not at all -- never issues a request.</param>
    Task<AppInfoFetch> GetAppInfoAsync(
        string appId,
        TimeSpan? cacheTtl = null,
        bool cachedOnly = false,
        CancellationToken ct = default);
}
