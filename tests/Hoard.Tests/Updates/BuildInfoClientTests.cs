using System.Net;
using Hoard.Enrich.Updates.Model;
using Xunit;

namespace Hoard.Tests.Updates;

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

    [Fact]
    public async Task One_appid_per_request_and_the_agent_identifies_hoard()
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
            Assert.Contains("Hoard", request.UserAgent, StringComparison.Ordinal);
        }
    }
}
