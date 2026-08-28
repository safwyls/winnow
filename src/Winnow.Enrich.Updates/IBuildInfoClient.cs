using Winnow.Enrich.Updates.Model;

namespace Winnow.Enrich.Updates;

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

    /// <summary>
    /// The <c>common</c> block for one appid — Steam's own <b>name</b> and
    /// <b>type</b> — or an outcome explaining why there isn't one.
    ///
    /// <para><b>Why this lives on the build-info client rather than in a second
    /// module.</b> It is the same URL, the same response body and the same
    /// <c>metadata_cache</c> row: <c>/v1/info/{appid}</c> returns
    /// <c>common</c>, <c>config</c>, <c>extended</c>, <c>ufs</c> and
    /// <c>depots</c> together, ~12.1 KB of it, and this client already pays for
    /// and stores all of it. A separate HTTP path to the same endpoint would
    /// double a volunteer service's traffic to re-read a block already on disk,
    /// and would need its own rate limiter, its own retry policy and its own
    /// cache to stay honest about it. So the two projections share one fetch:
    /// whichever call happens first populates the body, and the other is free
    /// for the rest of the TTL.</para>
    ///
    /// <para><b>Third in the name chain, deliberately last.</b> §4.4 keeps IGDB
    /// the metadata backbone and the Steam store is the documented-ish fallback;
    /// this is an unofficial, unaffiliated, no-SLA mirror, so it answers only
    /// for what those two could not. It earns its place because it demonstrably
    /// can: for the author's library it names appids that IGDB has no entry for
    /// and that <c>IStoreBrowseService/GetItems</c> returns nothing for.</para>
    ///
    /// <para><b>No <c>common</c> is no data, not a failure.</b> Restricted
    /// appids answer HTTP 200 with <c>"_missing_token": true</c> and no
    /// <c>common</c> block. That is a real answer and is cached as one — the
    /// same soft-fail discipline as the rest of this module — while a 5xx, a
    /// timeout or an unparseable body caches nothing and is asked again.</para>
    /// </summary>
    /// <param name="cacheTtl">
    /// Overrides <see cref="UpdateSignalOptions.AppInfoCacheTtl"/> for this
    /// call. Names and types change far more slowly than builds do, which is why
    /// the two projections have different default TTLs over one shared body.
    /// </param>
    /// <param name="cachedOnly">
    /// When true, answers from <c>metadata_cache</c> or not at all — never
    /// issues a request. Lets a caller harvest the type of an appid some other
    /// pass already fetched without spending anything at the volunteer service.
    /// </param>
    Task<AppInfoFetch> GetAppInfoAsync(
        string appId,
        TimeSpan? cacheTtl = null,
        bool cachedOnly = false,
        CancellationToken ct = default);
}
