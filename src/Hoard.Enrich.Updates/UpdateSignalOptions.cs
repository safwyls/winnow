namespace Hoard.Enrich.Updates;

/// <summary>
/// Tunables for the update-signal poller. Every default encodes something
/// <c>docs/spikes/update-signals.md</c> measured live on 2026-08-23 or something
/// <c>game-library-design.md</c> §4.5 requires; nothing here needs changing to
/// run correctly, only to run differently.
///
/// <para>The schedule parameters (<see cref="SweepPeriodDays"/>,
/// <see cref="MaxAppsPerBatch"/>, <see cref="CatchUpAfter"/>) are what turn the
/// spike's naive 1,232 requests per poll into ~63 per day. See
/// <see cref="UpdateSignalPoller"/> for how they compose.</para>
/// </summary>
public sealed class UpdateSignalOptions
{
    // ── Endpoints ────────────────────────────────────────────────────────────

    /// <summary>Steam Web API root, for <c>ISteamNews/GetNewsForApp</c>. Trailing slash required.</summary>
    public Uri NewsBaseAddress { get; set; } = new("https://api.steampowered.com/");

    /// <summary>
    /// steamcmd.net root, for <c>/v1/info/{appid}</c>. Trailing slash required.
    ///
    /// <para>A free, unofficial, volunteer-run PICS mirror with, in its own
    /// words, "no authentication or verification" and no SLA — §4.5 records it
    /// erroring outright during design. Everything about how this module talks
    /// to it (the 1 req/s ceiling, the 14-day cache, the cascade that only calls
    /// it on a news hit) follows from that, not from an observed limit.</para>
    /// </summary>
    public Uri BuildInfoBaseAddress { get; set; } = new("https://api.steamcmd.net/");

    /// <summary>
    /// Sent on every request to both hosts so the traffic is attributable — and
    /// so steamcmd.net's operator can contact whoever is generating it (§4.3's
    /// rule, applied here because the case for it is stronger, not weaker).
    /// </summary>
    public string UserAgent { get; set; } = "Hoard/0.1 (+https://github.com/hoard-app; local game library manager)";

    /// <summary>
    /// The <c>tags</c> filter for <c>GetNewsForApp</c>.
    ///
    /// <para>§4.5 says "filtered to community announcements"; the spike measured
    /// both and overrides it. For Stardew Valley, 527 total items become 74
    /// under <c>feeds=steam_community_announcements</c> (still merch promos and
    /// anniversary posts) but 34 under <c>tags=patchnotes</c>, all real patches.
    /// Valve's own API description misspells the tag as <c>patchnodes</c>; the
    /// working value is verified live below.</para>
    /// </summary>
    public string NewsTags { get; set; } = "patchnotes";

    // ── Eligibility (the "eliminate" of eliminate, cascade, stagger) ─────────

    /// <summary>
    /// Playtime at or above which a game stops being polled, matching
    /// <c>BucketThresholds.RetiredFloorMinutes</c>.
    ///
    /// <para>§6.1 gives <c>retired</c> precedence over <c>stale_but_patched</c>
    /// in the bucket query, so a retired game cannot display the badge whatever
    /// this module writes for it. Polling it spends a request to learn something
    /// that can never be shown.</para>
    /// </summary>
    public long RetiredFloorMinutes { get; set; } = 6_000;

    // ── Schedule (the "stagger") ────────────────────────────────────────────

    /// <summary>
    /// Days across which the eligible set is spread. Each app is pinned to one
    /// of these slots by a stable hash of its appid, so a library of E games
    /// costs about E/7 news requests a day instead of E.
    ///
    /// <para>Seven days is the spike's figure and the latency it buys is
    /// irrelevant: this badge is about a game last played six months ago, so
    /// noticing a patch up to a week late changes nothing a user could
    /// perceive.</para>
    /// </summary>
    public int SweepPeriodDays { get; set; } = 7;

    /// <summary>
    /// Hard ceiling on apps polled in one <c>PollDueBatchAsync</c> call.
    ///
    /// <para>A slot holds ~E/7 apps — 53 for the spike's E = 370 — so 120 is
    /// roughly double a normal day and never binds in the steady state. It binds
    /// after a long shutdown, when <see cref="CatchUpAfter"/> has made several
    /// slots' worth of apps due at once, and it is what stops that from becoming
    /// a several-hundred-request burst at the volunteer service. Whatever the cap
    /// truncates stays overdue and leads the next batch.</para>
    /// </summary>
    public int MaxAppsPerBatch { get; set; } = 120;

    /// <summary>
    /// How stale a poll may get before an app becomes due regardless of its
    /// slot. Covers the two ways the slot schedule alone loses apps: a machine
    /// switched off through its slot day, and a batch truncated by
    /// <see cref="MaxAppsPerBatch"/>.
    ///
    /// <para>Two sweep periods, so an app is only ever caught up after it has
    /// genuinely missed a turn.</para>
    /// </summary>
    public TimeSpan CatchUpAfter { get; set; } = TimeSpan.FromDays(14);

    // ── Cascade ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Symmetric window, in days, within which a build push and an announcement
    /// count as one major update. Mirrors
    /// <c>BucketThresholds.UpdateCorrelationWindowDays</c>, which is where the
    /// correlation is actually evaluated (§4.5: both raw signals are stored so
    /// the heuristic can be retuned without re-fetching).
    ///
    /// <para>This module uses it only to decide how long to keep an app on the
    /// daily watch list, never to decide what to store. Must be days, not hours:
    /// Stardew Valley's build landed <b>two days after</b> its announcement.</para>
    /// </summary>
    public int CorrelationWindowDays { get; set; } = 7;

    /// <summary>
    /// How recent an announcement must be to justify a steamcmd.net call.
    ///
    /// <para><c>timeupdated</c> is the app's <i>latest</i> push. Against a patch
    /// note from 2019 it will not correlate no matter what it says, so the call
    /// only spends the volunteer service's bandwidth to confirm a "no". Thirty
    /// days is comfortably wider than <see cref="CorrelationWindowDays"/>, so a
    /// pushed-then-announced pair still resolves even when the sweep reaches the
    /// app three weeks later.</para>
    /// </summary>
    public int CascadeMaxAnnouncementAgeDays { get; set; } = 30;

    /// <summary>
    /// Whether the first observation of an app's newest patch note is recorded
    /// as an event, or silently absorbed as a baseline.
    ///
    /// <para>The spike argues for absorbing it: "patched since you last played"
    /// is meaningless without a prior observation. This defaults the other way,
    /// because the comparison the spike is protecting already happens downstream
    /// — §6.1 only buckets a release as <c>stale_but_patched</c> when the
    /// correlated push post-dates last-played by <c>StaleWindowMonths</c>, so an
    /// announcement the user was present for is filtered out at read time on the
    /// evidence, not assumed away at write time.</para>
    ///
    /// <para>Absorbing it costs more than it saves: a freshly imported library
    /// would show no badge at all until every game had patched <i>twice</i>,
    /// which for the product's differentiating feature is a worse first month
    /// than an occasional badge for a patch the user already knew about. Set
    /// false to follow the spike's stricter reading.</para>
    /// </summary>
    public bool EmitOnBaseline { get; set; } = true;

    // ── Caching ─────────────────────────────────────────────────────────────

    /// <summary>
    /// How long a per-appid "this app has no news feed" negative is honoured.
    ///
    /// <para>This is the cache behind the spike's loudest warning: a 403 from
    /// <c>GetNewsForApp</c> means <b>this appid has no feed</b> (delisted games
    /// and tool appids — 460, 480, 520, 750 — all answer 403 with body
    /// <c>{}</c>), not that Hoard is being throttled. Without a negative cache
    /// every sweep re-asks the same dead appids forever; with a backoff policy
    /// wired to it, one delisted game stalls the whole poller for hours.</para>
    ///
    /// <para>Not permanent, because "no feed" is a fact about a Steam page and
    /// pages change: a re-release or a revived community hub can gain one. Ninety
    /// days makes that cost one request per quarter per dead app.</para>
    /// </summary>
    public TimeSpan NoNewsFeedRetryAfter { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How long a fetched steamcmd.net body stays authoritative, so a cascade
    /// re-triggered inside the window is served from <c>metadata_cache</c>.
    ///
    /// <para>The spike found the endpoint sends no <c>ETag</c>, no
    /// <c>Last-Modified</c>, no <c>Cache-Control</c> and no working compression:
    /// every call pays the full ~12.1 KB. Caching is therefore the only lever
    /// available, and this is the "cache aggressively" the no-SLA volunteer
    /// service warrants.</para>
    /// </summary>
    public TimeSpan BuildInfoCacheTtl { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How long a fetched steamcmd.net body stays authoritative for the
    /// <c>common</c> projection — Steam's name and type for the appid.
    ///
    /// <para>Longer than <see cref="BuildInfoCacheTtl"/>, over the very same
    /// cached body, because the two projections age at completely different
    /// rates: <c>timeupdated</c> moves whenever a developer pushes a depot,
    /// while a game's name and its <c>Game</c>/<c>Demo</c> classification are
    /// effectively immutable. Thirty days matches the IGDB enrichment TTL, so a
    /// library that has been named once costs nothing to re-name.</para>
    ///
    /// <para>Not infinite, because names do change — early-access titles get
    /// renamed at launch — and because a body cached as a <i>miss</i> ages under
    /// this same clock: the appids that answer <c>_missing_token</c> today may
    /// become readable, and one request a month per unnamed appid is the price
    /// of finding out.</para>
    /// </summary>
    public TimeSpan AppInfoCacheTtl { get; set; } = TimeSpan.FromDays(30);

    // ── Rate limits and retry ───────────────────────────────────────────────

    /// <summary>
    /// Ceiling on requests to api.steampowered.com, enforced by a shared
    /// token-bucket limiter on the HttpClient pipeline.
    ///
    /// <para>The spike sent 25 rapid requests and saw 25 × 200 at 0.10–0.21 s
    /// each, and explicitly calls that a lower bound rather than a ceiling. Two
    /// per second matches the sibling Steam store module and keeps a 53-app
    /// daily sweep under half a minute.</para>
    /// </summary>
    public int NewsRequestsPerSecond { get; set; } = 2;

    /// <summary>
    /// Ceiling on requests to api.steamcmd.net.
    ///
    /// <para>Deliberately half the news rate despite the spike observing no
    /// throttling across 20 back-to-back calls. "No rate limiting observed" on a
    /// free volunteer mirror is an absence of evidence, not a licence; the
    /// cascade already keeps this to ~10 calls a day, so 1 req/s costs nothing
    /// and cannot look like scraping.</para>
    /// </summary>
    public int BuildInfoRequestsPerSecond { get; set; } = 1;

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures only.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ceiling on any single backoff, including one derived from
    /// <c>Retry-After</c>. §4.2 reports Steam sending 60–120 s on throttled
    /// endpoints, so the cap must be able to honour that; this is background work
    /// with nothing blocked behind it.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(120);
}
