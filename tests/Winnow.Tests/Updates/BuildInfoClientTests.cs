using System.Net;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Xunit;

namespace Winnow.Tests.Updates;

/// <summary>
/// api.steamcmd.net against canned responses. No live calls.
///
/// <para>The behaviour under most scrutiny here is the one the HTTP status code
/// hides: a missing app comes back as <b>HTTP 200 with an empty inner
/// object</b>, so "no data" and "the service is broken" cannot be told apart by
/// status. Getting that wrong either re-asks a delisted app forever or records a
/// real outage as a permanent negative.</para>
/// </summary>
public class BuildInfoClientTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Missing_app_is_a_two_hundred_with_an_empty_object_and_means_no_data()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.BuildInfoMissingResponse()));

        var result = await host.Builds.GetPublicBranchAsync("999999999");

        // NoData, not Unavailable: the service answered, and its answer was
        // "nothing". Reading this as a parse failure would keep re-asking every
        // delisted appid in the library forever.
        Assert.Equal(BuildInfoOutcome.NoData, result.Outcome);
        Assert.Null(result.Branch);
    }

    [Fact]
    public async Task Missing_app_answer_is_cached_as_a_miss()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.BuildInfoMissing("999999999")),
            now: new DateTimeOffset(Now));

        await host.Builds.GetPublicBranchAsync("999999999");
        var second = await host.Builds.GetPublicBranchAsync("999999999");

        Assert.Equal(BuildInfoOutcome.NoData, second.Outcome);
        Assert.True(second.ServedFromCache);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    [Fact]
    public async Task Public_branch_is_read_from_the_captured_response()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.BuildInfoResponse()));

        var result = await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);

        Assert.Equal(BuildInfoOutcome.Ok, result.Outcome);
        Assert.Equal("16826371", result.Branch!.BuildId);

        // timeupdated 1734826775 — when the branch pointer flipped and the build
        // reached users. §4.5 names this field, not timebuildupdated.
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1734826775).UtcDateTime, result.Branch.UpdatedAt);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1734825749).UtcDateTime, result.Branch.BuildUpdatedAt);
    }

    [Fact]
    public async Task Non_public_branches_are_ignored()
    {
        // The generated fixture carries previous_version and beta alongside
        // public, exactly as 620 and 413150 do live. Neither is what a user runs.
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.BuildInfo(UpdateFixtures.PortalAppId, updatedAt)));

        var result = await host.Builds.GetPublicBranchAsync(UpdateFixtures.PortalAppId);

        Assert.Equal(updatedAt, result.Branch!.UpdatedAt);
    }

    [Fact]
    public async Task Outage_degrades_to_unavailable_and_is_not_cached()
    {
        var failing = true;
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (request, _) => failing
                ? FakeUpdateHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, updatedAt)),
            options => options.MaxRetryAttempts = 1);

        var first = await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);

        // §4.5 watched this service go dark. A failed fetch degrades to "no
        // build signal", never to an error and never to a claim that the app
        // was not updated.
        Assert.Equal(BuildInfoOutcome.Unavailable, first.Outcome);

        // Nothing was cached, so recovery is immediate rather than delayed by a
        // 14-day negative that a single 503 had no business writing.
        failing = false;
        var second = await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);
        Assert.Equal(BuildInfoOutcome.Ok, second.Outcome);
    }

    [Fact]
    public async Task Successful_body_is_cached_for_the_ttl()
    {
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, updatedAt)),
            options => options.BuildInfoCacheTtl = TimeSpan.FromDays(14),
            now: new DateTimeOffset(Now));

        await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);
        host.Clock.Advance(TimeSpan.FromDays(13));
        var cached = await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);

        // The endpoint sends no ETag, no Last-Modified, no Cache-Control and no
        // working compression, so caching is the only lever there is for a free
        // volunteer service that pays ~12 KB per call.
        Assert.True(cached.ServedFromCache);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));

        host.Clock.Advance(TimeSpan.FromDays(2));
        await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    // ── common.name / common.type ────────────────────────────────────────────

    /// <summary>
    /// The third name source. Everwind Demo is one of the 18 appids that showed
    /// as <c>App 4028270</c> on the author's library because IGDB has no entry
    /// and the store endpoint returned nothing — and steamcmd.net names it,
    /// classifies it, and points at its parent, all in the body this module was
    /// already fetching.
    /// </summary>
    [Fact]
    public async Task Common_block_is_read_from_the_captured_response()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK, UpdateFixtures.DemoAppInfoResponse()));

        var result = await host.Builds.GetAppInfoAsync(UpdateFixtures.DemoAppId);

        Assert.Equal(AppInfoOutcome.Ok, result.Outcome);
        Assert.Equal("Everwind Demo", result.Info!.Name);
        Assert.Equal("Demo", result.Info.Type);
        Assert.Equal("2253100", result.Info.ParentAppId);
    }

    /// <summary>
    /// The restricted shape: HTTP 200, a NON-empty inner object, and no
    /// <c>common</c> at all. Six appids of the author's library answer this way
    /// and no anonymous request will ever get more. It is an answer — NoData —
    /// and emphatically not a parse failure or an outage.
    /// </summary>
    [Fact]
    public async Task Missing_token_response_is_no_data_not_a_failure()
    {
        using var host = new UpdateSignalTestHost(
            (_, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.RestrictedAppInfoResponse()));

        var result = await host.Builds.GetAppInfoAsync(UpdateFixtures.RestrictedAppId);

        Assert.Equal(AppInfoOutcome.NoData, result.Outcome);
        Assert.Null(result.Info);
    }

    /// <summary>
    /// …and it is NOT recorded as a genuine miss. Both negatives answer NoData
    /// to the caller, but only the nonexistent app is stored as a null payload;
    /// the refusal is stored verbatim, so "Steam has no such app" and "this
    /// appid needs a Web API key" stay distinguishable on disk without another
    /// request. Caching a refusal as a miss is the same class of mistake as
    /// caching a 503 as one.
    /// </summary>
    [Fact]
    public async Task A_refusal_is_cached_verbatim_while_a_nonexistent_app_is_cached_as_a_miss()
    {
        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK,
                request.AppId == UpdateFixtures.RestrictedAppId
                    ? UpdateFixtures.RestrictedAppInfoResponse()
                    : UpdateFixtures.BuildInfoMissing(request.AppId)),
            now: new DateTimeOffset(Now));

        await host.Builds.GetAppInfoAsync(UpdateFixtures.RestrictedAppId);
        await host.Builds.GetAppInfoAsync("999999999");

        var refusal = await host.Cache.GetAsync(
            SteamCmdBuildInfoClient.CacheProvider,
            SteamCmdBuildInfoClient.AppCacheKey(UpdateFixtures.RestrictedAppId));
        var miss = await host.Cache.GetAsync(
            SteamCmdBuildInfoClient.CacheProvider,
            SteamCmdBuildInfoClient.AppCacheKey("999999999"));

        Assert.NotNull(refusal);
        Assert.NotNull(refusal.Value.PayloadJson);
        Assert.Contains("_missing_token", refusal.Value.PayloadJson, StringComparison.Ordinal);
        Assert.NotNull(miss);
        Assert.Null(miss.Value.PayloadJson);

        // Both are still answers, so neither is re-asked inside the TTL: the
        // volunteer service does not pay for our bookkeeping distinction.
        Assert.Equal(AppInfoOutcome.NoData, (await host.Builds.GetAppInfoAsync(
            UpdateFixtures.RestrictedAppId)).Outcome);
        Assert.True((await host.Builds.GetAppInfoAsync(UpdateFixtures.RestrictedAppId)).ServedFromCache);
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    /// <summary>
    /// An outage is not an answer, so nothing is cached and the very next pass
    /// asks again — the same discipline <see cref="GetPublicBranchAsync"/>
    /// already had, now proven for the name projection too.
    /// </summary>
    [Fact]
    public async Task An_outage_leaves_the_name_unlearned_and_uncached()
    {
        var failing = true;

        using var host = new UpdateSignalTestHost(
            (request, _) => failing
                ? FakeUpdateHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : FakeUpdateHandler.Json(
                    HttpStatusCode.OK, UpdateFixtures.AppInfoOnly(request.AppId, "Everwind Demo", "Demo")),
            options => options.MaxRetryAttempts = 1);

        Assert.Equal(
            AppInfoOutcome.Unavailable,
            (await host.Builds.GetAppInfoAsync(UpdateFixtures.DemoAppId)).Outcome);

        failing = false;
        var second = await host.Builds.GetAppInfoAsync(UpdateFixtures.DemoAppId);
        Assert.Equal(AppInfoOutcome.Ok, second.Outcome);
        Assert.Equal("Everwind Demo", second.Info!.Name);
    }

    /// <summary>
    /// The two projections share one fetch and one <c>metadata_cache</c> row —
    /// the whole reason this lives on the build-info client rather than in a
    /// second module. Asking for the build and then the name costs ONE request
    /// to the volunteer service, not two.
    /// </summary>
    [Fact]
    public async Task The_build_and_the_name_share_one_request()
    {
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, updatedAt)),
            now: new DateTimeOffset(Now));

        var build = await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);
        var info = await host.Builds.GetAppInfoAsync(UpdateFixtures.StardewAppId);

        Assert.Equal(BuildInfoOutcome.Ok, build.Outcome);
        Assert.Equal(AppInfoOutcome.Ok, info.Outcome);
        Assert.Equal("Fixture", info.Info!.Name);
        Assert.True(info.ServedFromCache);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    /// <summary>
    /// The reverse direction, and the reason the cache write had to change: a
    /// body with a <c>common</c> block and NO public branch used to be stored as
    /// a null payload because the build projection found nothing, which threw
    /// away the name for the whole TTL.
    /// </summary>
    [Fact]
    public async Task A_body_with_no_public_branch_still_keeps_its_name()
    {
        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK,
                UpdateFixtures.AppInfoOnly(request.AppId, "Skyrim Creation Kit", "Tool", "72850")),
            now: new DateTimeOffset(Now));

        // The build call goes first and finds no branch — the order that used to
        // lose the name.
        var build = await host.Builds.GetPublicBranchAsync("202480");
        var info = await host.Builds.GetAppInfoAsync("202480");

        Assert.Equal(BuildInfoOutcome.NoData, build.Outcome);
        Assert.Equal(AppInfoOutcome.Ok, info.Outcome);
        Assert.Equal("Skyrim Creation Kit", info.Info!.Name);
        Assert.Equal("Tool", info.Info.Type);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    /// <summary>
    /// <c>cachedOnly</c> is the free read: it answers from
    /// <c>metadata_cache</c> or not at all, so a caller can harvest whatever
    /// some other pass already paid for without adding a single request.
    /// </summary>
    [Fact]
    public async Task A_cached_only_read_never_issues_a_request()
    {
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, updatedAt)),
            now: new DateTimeOffset(Now));

        var cold = await host.Builds.GetAppInfoAsync(UpdateFixtures.DotaAppId, cachedOnly: true);

        // Unavailable, not NoData: "we did not look" must never read as "the
        // service said no".
        Assert.Equal(AppInfoOutcome.Unavailable, cold.Outcome);
        Assert.Equal(0, host.Handler.CountFor(UpdateHost.SteamCmd));

        await host.Builds.GetPublicBranchAsync(UpdateFixtures.DotaAppId);
        var warm = await host.Builds.GetAppInfoAsync(UpdateFixtures.DotaAppId, cachedOnly: true);

        Assert.Equal(AppInfoOutcome.Ok, warm.Outcome);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    /// <summary>
    /// Names and types age far more slowly than builds do, so the two
    /// projections read the same cached body under different TTLs.
    /// </summary>
    [Fact]
    public async Task The_name_projection_honours_its_own_longer_ttl()
    {
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, updatedAt)),
            options =>
            {
                options.BuildInfoCacheTtl = TimeSpan.FromDays(14);
                options.AppInfoCacheTtl = TimeSpan.FromDays(30);
            },
            now: new DateTimeOffset(Now));

        await host.Builds.GetAppInfoAsync(UpdateFixtures.StardewAppId);
        host.Clock.Advance(TimeSpan.FromDays(20));

        // Past the build TTL, inside the name TTL.
        Assert.True((await host.Builds.GetAppInfoAsync(UpdateFixtures.StardewAppId)).ServedFromCache);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));

        Assert.False((await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId)).ServedFromCache);
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    [Fact]
    public async Task One_appid_per_request_and_the_agent_identifies_winnow()
    {
        var updatedAt = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc);

        using var host = new UpdateSignalTestHost(
            (request, _) => FakeUpdateHandler.Json(
                HttpStatusCode.OK, UpdateFixtures.BuildInfo(request.AppId, updatedAt)));

        await host.Builds.GetPublicBranchAsync(UpdateFixtures.StardewAppId);
        await host.Builds.GetPublicBranchAsync(UpdateFixtures.PortalAppId);

        // /openapi.json proves the entire API surface is /v1/info/{app_id},
        // /v1/version, /health and /ready. /v1/info/570,620 returns 422. There
        // is no batch route to discover later.
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamCmd));
        foreach (var request in host.Handler.Requests)
        {
            Assert.DoesNotContain(",", request.AppId, StringComparison.Ordinal);

            // The volunteer service has no contact channel other than whatever
            // its traffic identifies itself as.
            Assert.Contains("Winnow", request.UserAgent, StringComparison.Ordinal);
        }
    }
}
