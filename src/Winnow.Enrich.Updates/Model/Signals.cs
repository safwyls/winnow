namespace Winnow.Enrich.Updates.Model;

/// <summary>
/// How a news fetch ended. The distinction between <see cref="NoFeed"/> and
/// <see cref="Unavailable"/> is the single most consequential decision in this
/// module — see <see cref="NoFeed"/>.
/// </summary>
public enum NewsOutcome
{
    /// <summary>A patch note was returned. <see cref="NewsFetch.Item"/> is set.</summary>
    Ok,

    /// <summary>
    /// The app has a feed and the filter matched nothing: HTTP 200 with
    /// <c>"newsitems": []</c> (verified live for appid 790). A fact about this
    /// app, cached like any other answer.
    /// </summary>
    NoItems,

    /// <summary>
    /// <b>HTTP 403 — this appid has no news feed at all.</b> Verified live: 460,
    /// 480, 520 and 750 all answer 403 with body <c>{}</c>.
    ///
    /// <para>This is emphatically <i>not</i> rate limiting, and reading it as
    /// such is the trap the spike names in capitals. A client that treats 403 as
    /// throttling backs off — for hours, if it also trips a circuit breaker —
    /// because one delisted game in the library has no community hub. Every
    /// delisted appid would then take the whole poller down with it.</para>
    ///
    /// <para>So 403 is a permanent per-appid negative: never retried within the
    /// request, never counted toward any breaker, cached for
    /// <c>UpdateSignalOptions.NoNewsFeedRetryAfter</c>, and invisible to every
    /// other app in the batch. Only 429 means slow down.</para>
    /// </summary>
    NoFeed,

    /// <summary>
    /// The request did not produce an answer — offline, a 5xx, a 429 that
    /// outlived its retries, an unparseable body. Nothing is learned and nothing
    /// is cached; the app stays due and is asked again next batch.
    /// </summary>
    Unavailable,
}

/// <summary>One patch note from <c>ISteamNews/GetNewsForApp</c>.</summary>
/// <param name="Gid">Steam's globally unique id for the item; stable across fetches.</param>
/// <param name="Title">Headline, as shown on the announcement.</param>
/// <param name="Url">
/// Where a human reads it. design-system.md §5.2's badge click opens this, and
/// it is not cheaply recoverable later — the endpoint pages backwards by date
/// with no lookup by gid.
/// </param>
/// <param name="PublishedAt">The item's <c>date</c>, UTC.</param>
/// <param name="TotalMatching">
/// The envelope's top-level <c>count</c>: how many items match the filter in
/// total, not how many were returned. A cheap secondary change detector;
/// <paramref name="PublishedAt"/> stays authoritative.
/// </param>
/// <param name="RawJson">The item verbatim, stored on the event row (§4.5).</param>
public sealed record SteamNewsItem(
    string Gid,
    string? Title,
    string? Url,
    DateTime PublishedAt,
    int TotalMatching,
    string RawJson);

/// <summary>The result of one <c>GetNewsForApp</c> call.</summary>
/// <param name="ServedFromCache">
/// True when no request was made — a live no-feed negative answered it. Reported
/// so <see cref="UpdatePollReport.NewsRequests"/> counts wire traffic rather than
/// method calls; a cost model built on the wrong one of those is not a cost model.
/// </param>
public sealed record NewsFetch(NewsOutcome Outcome, SteamNewsItem? Item, bool ServedFromCache = false)
{
    public static NewsFetch Ok(SteamNewsItem item) => new(NewsOutcome.Ok, item);

    public static NewsFetch NoItems { get; } = new(NewsOutcome.NoItems, null);

    public static NewsFetch NoFeed { get; } = new(NewsOutcome.NoFeed, null);

    /// <summary>A 403 answered from the negative cache, without a request.</summary>
    public static NewsFetch NoFeedCached { get; } = new(NewsOutcome.NoFeed, null, ServedFromCache: true);

    public static NewsFetch Unavailable { get; } = new(NewsOutcome.Unavailable, null);
}

/// <summary>How a steamcmd.net fetch ended.</summary>
public enum BuildInfoOutcome
{
    /// <summary>A <c>public</c> branch was returned. <see cref="BuildInfoFetch.Branch"/> is set.</summary>
    Ok,

    /// <summary>
    /// The service answered and had nothing: the spike's
    /// <c>{"data": {"999999999": {}}, "status": "success"}</c>, verified live.
    ///
    /// <para><b>A missing app is a 200, not a 404</b>, so this must be decided by
    /// branching on the empty inner object. Reading it as a parse failure would
    /// mark every app Steam has retired as "the service is broken" and keep
    /// re-asking; reading the HTTP status would never notice at all.</para>
    /// </summary>
    NoData,

    /// <summary>
    /// The service did not answer. §4.5 records this endpoint erroring outright
    /// during design, and it carries no SLA, so a failed fetch degrades to "no
    /// build signal" — never to an error, and never to a claim that the app was
    /// not updated.
    /// </summary>
    Unavailable,
}

/// <summary>The <c>public</c> branch of an app's depots, from steamcmd.net.</summary>
/// <param name="BuildId">
/// <c>depots.branches.public.buildid</c> — the build users are actually on.
/// </param>
/// <param name="UpdatedAt">
/// <c>timeupdated</c>: when the branch pointer flipped, i.e. when the build
/// reached users. §4.5 names this field and the spike confirms the choice.
/// </param>
/// <param name="BuildUpdatedAt">
/// <c>timebuildupdated</c>: when the build itself was produced. Stored but not
/// used for correlation — it ran 279 seconds ahead of <paramref name="UpdatedAt"/>
/// for Dota 2 and thirty days ahead for Elden Ring, so the two are not
/// interchangeable.
/// </param>
/// <param name="RawJson">The branch object verbatim, plus the change number.</param>
public sealed record BuildBranch(
    string? BuildId,
    DateTime UpdatedAt,
    DateTime? BuildUpdatedAt,
    string RawJson);

/// <summary>The result of one steamcmd.net call.</summary>
/// <param name="ServedFromCache">True when the answer came from <c>metadata_cache</c>, with no request.</param>
public sealed record BuildInfoFetch(BuildInfoOutcome Outcome, BuildBranch? Branch, bool ServedFromCache = false)
{
    public static BuildInfoFetch Ok(BuildBranch branch) => new(BuildInfoOutcome.Ok, branch);

    public static BuildInfoFetch OkCached(BuildBranch branch) => new(BuildInfoOutcome.Ok, branch, true);

    public static BuildInfoFetch NoData { get; } = new(BuildInfoOutcome.NoData, null);

    public static BuildInfoFetch NoDataCached { get; } = new(BuildInfoOutcome.NoData, null, true);

    public static BuildInfoFetch Unavailable { get; } = new(BuildInfoOutcome.Unavailable, null);
}

/// <summary>
/// The <c>common</c> block of a steamcmd.net <c>/v1/info/{appid}</c> response —
/// Steam's own name and classification for an appid, riding along in the body
/// this module already fetches for its build signal.
/// </summary>
/// <param name="AppId">The appid asked about.</param>
/// <param name="Name">
/// <c>common.name</c>: Steam's title for the appid. The third and last name
/// source in the enrichment chain, behind IGDB (§4.4's backbone) and the Steam
/// store — it names appids both of them refuse, verified live: 4028270
/// "Everwind Demo", 2614110 "Enshrouded Demo", 202480 "Skyrim Creation Kit".
/// </param>
/// <param name="Type">
/// <c>common.type</c>: Valve's classification, verbatim including its casing,
/// which is NOT stable — Bastion answers <c>game</c>, Monster Hunter Wilds
/// answers <c>Game</c>. Every comparison must be case-insensitive.
/// </param>
/// <param name="ParentAppId">
/// <c>common.parent</c>: the appid this one is a demo/tool OF, when Steam
/// publishes one (107110 Bastion Demo → 107100). Read and carried but not yet
/// consumed — binding on it would be a hard external-id join, which is a
/// stronger rule than the title match consolidation uses today and a change to
/// §5.3's matching contract rather than to this client.
/// </param>
public sealed record SteamAppInfo(
    string AppId,
    string? Name,
    string? Type,
    string? ParentAppId);

/// <summary>How a <c>common</c> read ended. Mirrors <see cref="BuildInfoOutcome"/>.</summary>
public enum AppInfoOutcome
{
    /// <summary>A <c>common</c> block was returned. <see cref="AppInfoFetch.Info"/> is set.</summary>
    Ok,

    /// <summary>
    /// The service answered and carried no <c>common</c> block for this appid.
    ///
    /// <para>Two verified shapes reach here and both are answers, not failures.
    /// The first is the missing-app body — <c>{"data":{"999999999":{}}}</c> at
    /// HTTP 200. The second is the <b>restricted</b> one: HTTP 200 with
    /// <c>"_missing_token": true, "public_only": "1"</c> and no <c>common</c> at
    /// all, which 8510, 854040, 1883690, 3065170 and 3562740 all answer
    /// on the author's library. Those appids need a Steam Web API key; nothing
    /// this module can do differently will name them, so re-asking daily would
    /// spend a volunteer service's bandwidth to relearn the same nothing.</para>
    /// </summary>
    NoData,

    /// <summary>
    /// The service did not answer — offline, 5xx, an unparseable body. Nothing
    /// is learned and nothing is cached, so the appid is asked again next pass.
    /// </summary>
    Unavailable,
}

/// <summary>The result of one <c>common</c> read.</summary>
/// <param name="ServedFromCache">True when the answer came from <c>metadata_cache</c>, with no request.</param>
public sealed record AppInfoFetch(AppInfoOutcome Outcome, SteamAppInfo? Info, bool ServedFromCache = false)
{
    public static AppInfoFetch Ok(SteamAppInfo info) => new(AppInfoOutcome.Ok, info);

    public static AppInfoFetch OkCached(SteamAppInfo info) => new(AppInfoOutcome.Ok, info, true);

    public static AppInfoFetch NoData { get; } = new(AppInfoOutcome.NoData, null);

    public static AppInfoFetch NoDataCached { get; } = new(AppInfoOutcome.NoData, null, true);

    public static AppInfoFetch Unavailable { get; } = new(AppInfoOutcome.Unavailable, null);
}
