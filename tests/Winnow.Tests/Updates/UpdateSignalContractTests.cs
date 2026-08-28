using System.Text.Json;
using Xunit;

namespace Winnow.Tests.Updates;

/// <summary>
/// Shape assertions against the bytes captured on 2026-08-23
/// (`tests/fixtures/update-signals/`). Neither source is safe to assume stable:
/// the `tags` filter is undocumented (Valve's own description misspells the
/// value), and api.steamcmd.net is an unofficial volunteer mirror that §4.5
/// recorded erroring outright during design.
///
/// <para><b>This test is the early-warning system.</b> When someone recaptures a
/// fixture and these break, the contract changed — and the soft-fail paths,
/// which already degrade to "no signal" rather than throw, can be re-verified
/// against the new reality instead of discovered in production.</para>
/// </summary>
public class UpdateSignalContractTests
{
    [Fact]
    public void News_response_carries_gid_date_title_and_url()
    {
        using var document = JsonDocument.Parse(UpdateFixtures.NewsResponse());
        var item = document.RootElement
            .GetProperty("appnews")
            .GetProperty("newsitems")[0];

        Assert.False(string.IsNullOrEmpty(item.GetProperty("gid").GetString()));
        Assert.Equal(JsonValueKind.Number, item.GetProperty("date").ValueKind);
        Assert.False(string.IsNullOrEmpty(item.GetProperty("title").GetString()));

        // The url is the whole reason the announcement row carries one: it is
        // what design-system §5.2's badge click opens, and the endpoint offers no
        // lookup by gid to recover it later.
        Assert.StartsWith("https://", item.GetProperty("url").GetString()!, StringComparison.Ordinal);

        // The filter really is `patchnotes`, not the `patchnodes` Valve's own
        // API description writes.
        Assert.Contains(
            "patchnotes",
            item.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));

        // The top-level count is the TOTAL matching the filter, not the number
        // returned — the request asked for one item and 34 exist.
        Assert.True(document.RootElement.GetProperty("appnews").GetProperty("count").GetInt32() > 1);
    }

    [Fact]
    public void News_response_for_an_app_with_no_matching_items_is_an_empty_array()
    {
        using var document = JsonDocument.Parse(UpdateFixtures.NewsNoMatchesResponse());
        var items = document.RootElement.GetProperty("appnews").GetProperty("newsitems");

        // Verified live for appid 790. Distinct from 403 (no feed at all), and
        // the distinction is what stops a normal app from being cached as dead.
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public void Build_info_response_carries_a_public_branch_with_timeupdated()
    {
        using var document = JsonDocument.Parse(UpdateFixtures.BuildInfoResponse());
        var branches = document.RootElement
            .GetProperty("data")
            .GetProperty(UpdateFixtures.StardewAppId)
            .GetProperty("depots")
            .GetProperty("branches");

        var publicBranch = branches.GetProperty("public");

        // §4.5 names `timeupdated`, and the spike confirms the choice: it is the
        // branch-pointer flip, i.e. when the build reached users.
        Assert.Equal(JsonValueKind.String, publicBranch.GetProperty("timeupdated").ValueKind);
        Assert.Equal(JsonValueKind.String, publicBranch.GetProperty("buildid").ValueKind);

        // Not interchangeable with timebuildupdated, which ran 279 seconds ahead
        // for Dota 2 and thirty days ahead for Elden Ring.
        Assert.NotEqual(
            publicBranch.GetProperty("timeupdated").GetString(),
            publicBranch.GetProperty("timebuildupdated").GetString());

        // Non-public branches exist and must be ignored — this app carries four
        // of them, none of which is what a user is running.
        Assert.True(
            branches.EnumerateObject().Count() > 1,
            "The fixture should still carry the non-public branches the parser has to skip.");
    }

    [Fact]
    public void Missing_app_response_is_a_success_envelope_with_an_empty_object()
    {
        using var document = JsonDocument.Parse(UpdateFixtures.BuildInfoMissingResponse());

        // HTTP 200, "status": "success", and an EMPTY inner object. Branch on
        // the object; never on the status code. Reading this any other way
        // either re-asks every delisted appid forever or records a real outage
        // as a permanent negative.
        Assert.Equal("success", document.RootElement.GetProperty("status").GetString());

        var app = document.RootElement.GetProperty("data").GetProperty("999999999");
        Assert.Equal(JsonValueKind.Object, app.ValueKind);
        Assert.Empty(app.EnumerateObject());
    }
}
