using Hoard.Enrich.Updates.Model;

namespace Hoard.Enrich.Updates;

/// <summary>
/// <c>GET https://api.steamcmd.net/v1/info/{appid}</c> — the build-push half of
/// §4.5's pair, read from <c>depots.branches.public.timeupdated</c>.
///
/// <para><b>This is the expensive, fragile side of the pair and is called
/// sparingly by design.</b> §4.5 recorded the service erroring outright during
/// design; the spike found it alive but confirms what it is: a free, unofficial,
/// volunteer-run PICS mirror, "not affiliated with Steam or Valve", with no
/// authentication, no caching headers, no compression and no SLA. Every call
/// pays ~12.1 KB and ~0.77 s, against ~440 bytes for a news call.</para>
///
/// <para>So the poller cascades — it only reaches here when the cheap news
/// signal says something changed — and this client caches every body for
/// <see cref="UpdateSignalOptions.BuildInfoCacheTtl"/>. A failed fetch degrades
/// to "no build signal", never to an error and never to a claim that the app was
/// not updated.</para>
///
/// <para>One appid per request, authoritatively: the spike read
/// <c>/openapi.json</c> and found the entire API surface to be
/// <c>/v1/info/{app_id}</c>, <c>/v1/version</c>, <c>/health</c> and
/// <c>/ready</c>. <c>/v1/info/570,620</c> returns 422. There is no batch route
/// to discover later.</para>
/// </summary>
public interface IBuildInfoClient
{
    /// <summary>
    /// The <c>public</c> branch for one appid, or an outcome explaining why
    /// there isn't one.
    ///
    /// <para>Served from <c>metadata_cache</c> when a body was fetched within
    /// the TTL. Non-public branches are ignored: 620 carries beta and
    /// previous_release, 413150 carries compatibility and three legacy pins, and
    /// none of them is what a user is running.</para>
    /// </summary>
    /// <param name="cacheTtl">Overrides <see cref="UpdateSignalOptions.BuildInfoCacheTtl"/> for this call.</param>
    Task<BuildInfoFetch> GetPublicBranchAsync(
        string appId, TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
