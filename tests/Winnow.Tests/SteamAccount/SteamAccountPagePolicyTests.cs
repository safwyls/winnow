using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Xunit;

namespace Winnow.Tests.SteamAccount;

/// <summary>
/// The rules the embedded Steam account-page session runs on.
///
/// <para>These exist for the reason the sign-in prompt's do: a WebView2 control
/// cannot be created in a unit test — no runtime, no Avalonia application, no
/// window — so every decision that matters to security was pulled out of the
/// host and into <see cref="SteamAccountPagePolicy"/>, which is pure and can be
/// asked directly. The host is left holding wiring, and this file is what makes
/// the wiring worth trusting.</para>
/// </summary>
public class SteamAccountPagePolicyTests
{
    private static SteamAccountPagePolicy Policy(int maxLoadMoreClicks = 100)
        => SteamAccountPagePolicy.For(maxLoadMoreClicks);

    // ---- Origin binding -----------------------------------------------------

    [Fact]
    public void Only_the_store_origin_can_be_read()
    {
        var policy = Policy();

        Assert.Equal(["https://store.steampowered.com:443"], policy.TrustedOrigins.ToArray());
        Assert.Equal("https://store.steampowered.com:443", policy.HarvestOrigin);
    }

    [Fact]
    public void Valves_login_and_support_origins_are_navigable_but_never_read()
    {
        var policy = Policy();

        // The two tiers, in one assertion each. A sign-in that crosses these has
        // to render; none of them may be scripted or captured.
        foreach (var url in new[]
                 {
                     "https://login.steampowered.com/jwt/finalizelogin",
                     "https://help.steampowered.com/en/wizard/HelpWithLogin",
                     "https://steamcommunity.com/login/home/",
                     "https://www.steampowered.com/",
                 })
        {
            var uri = new Uri(url);

            Assert.True(policy.IsNavigableOrigin(uri), url);
            Assert.False(policy.IsTrustedOrigin(uri), url);
            Assert.False(policy.AllowsHarvest(uri), url);
            Assert.Equal(AuthNavigationDecision.Allow, policy.ClassifyNavigation(uri));
        }
    }

    [Fact]
    public void Anything_outside_the_flow_is_refused_and_a_popup_to_it_leaves_the_window()
    {
        var policy = Policy();

        foreach (var url in new[]
                 {
                     "https://evil.example/steal",
                     "https://store.steampowered.com.evil.example/account/licenses/",
                     "https://steamcommunity.com.evil.example/",
                 })
        {
            var uri = new Uri(url);

            Assert.False(policy.IsNavigableOrigin(uri), url);
            Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(uri));

            // A real web page the user asked for still reaches them — in their
            // own browser, not in the window holding the Steam session.
            Assert.Equal(AuthNavigationDecision.OpenExternally, policy.ClassifyPopup(uri));
        }
    }

    [Fact]
    public void Plaintext_http_on_the_right_host_is_still_refused()
    {
        var policy = Policy();
        var uri = new Uri("http://store.steampowered.com/account/licenses/");

        Assert.False(policy.IsTrustedOrigin(uri));
        Assert.False(policy.IsNavigableOrigin(uri));
        Assert.False(policy.AllowsHarvest(uri));
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(uri));
    }

    [Fact]
    public void A_non_web_scheme_is_blocked_and_not_handed_anywhere()
    {
        var policy = Policy();

        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(new Uri("steam://open/games")));
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyPopup(new Uri("steam://open/games")));
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(null));

        // WebView2 starts on about:blank and returns there between documents. It
        // carries no origin and hosts nothing, so cancelling it would only break
        // the browser.
        Assert.Equal(AuthNavigationDecision.Allow, policy.ClassifyNavigation(new Uri("about:blank")));
        Assert.False(policy.AllowsHarvest(new Uri("about:blank")));
    }

    [Fact]
    public void There_is_no_redirect_to_capture_in_this_flow()
    {
        var policy = Policy();

        // The sign-in prompt's fourth decision. Nothing in a page harvest is a
        // credential-bearing redirect, so the classification must never be
        // reachable — it is armed by a redirect URL and a strategy flag, and this
        // flow supplies neither.
        Assert.NotEqual(
            AuthNavigationDecision.CaptureRedirect,
            policy.ClassifyNavigation(new Uri("https://store.steampowered.com/login/?redir=account")));
    }

    // ---- The two paths, and only the two ------------------------------------

    [Theory]
    [InlineData("https://store.steampowered.com/account/licenses/", SteamAccountPageKind.Licenses)]
    [InlineData("https://store.steampowered.com/account/licenses", SteamAccountPageKind.Licenses)]
    [InlineData("https://store.steampowered.com/account/LICENSES/", SteamAccountPageKind.Licenses)]
    // The paginator's own links, which carry a continuation token and an offset.
    // The query is not part of a page's identity, so page two of the licences
    // list is the licences page: the same document may be read and no new
    // address is admitted by admitting it.
    [InlineData(
        "https://store.steampowered.com/account/licenses/?continuationToken=A5F2C1&offset=100",
        SteamAccountPageKind.Licenses)]
    [InlineData(
        "https://store.steampowered.com/account/licenses?offset=900&continuationToken=ZZ",
        SteamAccountPageKind.Licenses)]
    [InlineData("https://store.steampowered.com/account/history/", SteamAccountPageKind.PurchaseHistory)]
    [InlineData("https://store.steampowered.com/account/history", SteamAccountPageKind.PurchaseHistory)]
    [InlineData("https://store.steampowered.com/account/history/?l=english", SteamAccountPageKind.PurchaseHistory)]
    public void The_two_pages_are_recognised(string url, SteamAccountPageKind expected)
    {
        var policy = Policy();

        Assert.Equal(expected, policy.PageOf(new Uri(url)));
        Assert.True(policy.AllowsHarvest(new Uri(url)));
    }

    [Theory]
    [InlineData("https://store.steampowered.com/")]
    [InlineData("https://store.steampowered.com/account/")]
    [InlineData("https://store.steampowered.com/account/registerkey/")]
    [InlineData("https://store.steampowered.com/account/licenses/detail/1234")]
    [InlineData("https://store.steampowered.com/account/history/detail/1234")]
    [InlineData("https://store.steampowered.com/login/?redir=account%2Flicenses")]
    [InlineData("https://store.steampowered.com/twofactor/manage")]
    [InlineData("https://help.steampowered.com/en/accountdata/AccountSpend")]
    [InlineData("https://steamcommunity.com/my/games")]
    public void Nothing_else_is_ever_read(string url)
    {
        var policy = Policy();
        var uri = new Uri(url);

        // The whole reading surface is two URLs. Being on the trusted origin —
        // which the store login form and every other account page are — buys a
        // document the right to render and nothing more.
        Assert.Null(policy.PageOf(uri));
        Assert.False(policy.AllowsHarvest(uri));
    }

    [Fact]
    public void The_session_only_ever_navigates_to_those_two_pages()
    {
        Assert.Equal(
            [SteamAccountPageKind.Licenses, SteamAccountPageKind.PurchaseHistory],
            SteamAccountPagePolicy.Pages.ToArray());

        Assert.Equal(
            new Uri("https://store.steampowered.com/account/licenses/"),
            SteamAccountPagePolicy.PageUrl(SteamAccountPageKind.Licenses));

        Assert.Equal(
            new Uri("https://store.steampowered.com/account/history/"),
            SteamAccountPagePolicy.PageUrl(SteamAccountPageKind.PurchaseHistory));

        // Both are pages the policy will then admit, which is what stops the
        // navigation set and the reading set from drifting apart.
        var policy = Policy();
        foreach (var kind in SteamAccountPagePolicy.Pages)
        {
            Assert.True(policy.AllowsHarvest(SteamAccountPagePolicy.PageUrl(kind)));
        }
    }

    // ---- The load-more cap --------------------------------------------------

    [Fact]
    public void The_cap_is_clamped_into_range_whatever_the_request_asks_for()
    {
        Assert.Equal(
            SteamAccountPagePolicy.MaxAllowedLoadMoreClicks,
            SteamAccountPagePolicy.For(int.MaxValue).MaxLoadMoreClicks);

        Assert.Equal(0, SteamAccountPagePolicy.For(-5).MaxLoadMoreClicks);
        Assert.Equal(7, SteamAccountPagePolicy.For(7).MaxLoadMoreClicks);

        Assert.Equal(
            SteamAccountPagePolicy.DefaultMaxLoadMoreClicks,
            SteamAccountPagePolicy.For(new SteamPageHarvestRequest { ConsentGranted = true })
                .MaxLoadMoreClicks);
    }

    [Fact]
    public void Clicking_continues_while_the_control_is_there_and_the_rows_grow()
    {
        var policy = Policy(maxLoadMoreClicks: 3);

        // The first probe: nothing clicked yet, so there is no "before" to have
        // failed to grow past.
        Assert.Equal(
            SteamLoadMoreDecision.Continue,
            policy.ClassifyLoadMore(clicksCompleted: 0, rowsBefore: -1, rowsAfter: 20, controlPresent: true));

        Assert.Equal(
            SteamLoadMoreDecision.Continue,
            policy.ClassifyLoadMore(clicksCompleted: 1, rowsBefore: 20, rowsAfter: 40, controlPresent: true));
    }

    [Fact]
    public void Clicking_stops_when_the_control_goes_away()
    {
        var policy = Policy(maxLoadMoreClicks: 3);

        Assert.Equal(
            SteamLoadMoreDecision.Exhausted,
            policy.ClassifyLoadMore(clicksCompleted: 2, rowsBefore: 40, rowsAfter: 60, controlPresent: false));
    }

    [Fact]
    public void Clicking_stops_at_the_cap_even_with_more_to_load()
    {
        var policy = Policy(maxLoadMoreClicks: 3);

        Assert.Equal(
            SteamLoadMoreDecision.ReachedCap,
            policy.ClassifyLoadMore(clicksCompleted: 3, rowsBefore: 60, rowsAfter: 80, controlPresent: true));

        // A cap of zero means the control is never clicked at all, not that the
        // cap is ignored.
        Assert.Equal(
            SteamLoadMoreDecision.ReachedCap,
            Policy(maxLoadMoreClicks: 0)
                .ClassifyLoadMore(clicksCompleted: 0, rowsBefore: -1, rowsAfter: 20, controlPresent: true));
    }

    // ---- The licences paginator, which is the same rule with another cap -----

    [Fact]
    public void The_licences_cap_is_clamped_independently_of_the_load_more_one()
    {
        Assert.Equal(
            SteamAccountPagePolicy.MaxAllowedLicensesPages,
            SteamAccountPagePolicy.For(10, int.MaxValue).MaxLicensesPages);

        Assert.Equal(0, SteamAccountPagePolicy.For(10, -1).MaxLicensesPages);
        Assert.Equal(12, SteamAccountPagePolicy.For(10, 12).MaxLicensesPages);

        // Each cap governs its own page. Setting one must not move the other.
        var policy = SteamAccountPagePolicy.For(maxLoadMoreClicks: 3, maxLicensesPages: 12);
        Assert.Equal(3, policy.MaxLoadMoreClicks);
        Assert.Equal(12, policy.MaxLicensesPages);

        Assert.Equal(
            SteamAccountPagePolicy.DefaultMaxLicensesPages,
            SteamAccountPagePolicy.For(new SteamPageHarvestRequest { ConsentGranted = true })
                .MaxLicensesPages);

        // Ten pages carry a 979-licence account, which is what the cap was sized
        // against.
        Assert.True(SteamAccountPagePolicy.DefaultMaxLicensesPages >= 10);
    }

    [Fact]
    public void The_paginator_is_followed_while_there_is_a_next_page_and_the_rows_grow()
    {
        var policy = SteamAccountPagePolicy.For(maxLoadMoreClicks: 100, maxLicensesPages: 3);

        Assert.Equal(
            SteamLoadMoreDecision.Continue,
            policy.ClassifyLicensesPage(pagesWalked: 0, rowsBefore: -1, rowsAfter: 100, nextLinkPresent: true));

        Assert.Equal(
            SteamLoadMoreDecision.Continue,
            policy.ClassifyLicensesPage(pagesWalked: 1, rowsBefore: 100, rowsAfter: 200, nextLinkPresent: true));
    }

    [Fact]
    public void The_paginator_stops_when_there_is_no_next_page()
    {
        var policy = SteamAccountPagePolicy.For(maxLoadMoreClicks: 100, maxLicensesPages: 50);

        // The last page of the list, and also the whole story for an account with
        // fewer than a hundred licences, which has no paginator at all.
        Assert.Equal(
            SteamLoadMoreDecision.Exhausted,
            policy.ClassifyLicensesPage(pagesWalked: 9, rowsBefore: 900, rowsAfter: 979, nextLinkPresent: false));

        Assert.Equal(
            SteamLoadMoreDecision.Exhausted,
            policy.ClassifyLicensesPage(pagesWalked: 0, rowsBefore: -1, rowsAfter: 42, nextLinkPresent: false));
    }

    [Fact]
    public void The_paginator_stops_at_the_cap_with_more_pages_to_go()
    {
        var policy = SteamAccountPagePolicy.For(maxLoadMoreClicks: 100, maxLicensesPages: 3);

        Assert.Equal(
            SteamLoadMoreDecision.ReachedCap,
            policy.ClassifyLicensesPage(pagesWalked: 3, rowsBefore: 300, rowsAfter: 400, nextLinkPresent: true));

        // A cap of zero captures the first page and follows nothing, which is the
        // pre-pagination behaviour and is still a truncated document.
        Assert.Equal(
            SteamLoadMoreDecision.ReachedCap,
            SteamAccountPagePolicy.For(100, 0)
                .ClassifyLicensesPage(pagesWalked: 0, rowsBefore: -1, rowsAfter: 100, nextLinkPresent: true));
    }

    [Fact]
    public void The_paginator_stops_when_a_page_adds_no_rows()
    {
        var policy = SteamAccountPagePolicy.For(maxLoadMoreClicks: 100, maxLicensesPages: 50);

        // A next link that keeps pointing at a page which merges nothing is how
        // an infinite walk starts. It is caught by the same rule that catches a
        // load-more control that has stopped working.
        Assert.Equal(
            SteamLoadMoreDecision.Stalled,
            policy.ClassifyLicensesPage(pagesWalked: 2, rowsBefore: 200, rowsAfter: 200, nextLinkPresent: true));
    }

    [Fact]
    public void Both_pages_are_judged_by_one_rule()
    {
        // The two mechanisms differ (a click, a fetched page) and the caps differ,
        // but the decision is one rule and stays one rule: same inputs, same
        // cap, same answer.
        var policy = SteamAccountPagePolicy.For(maxLoadMoreClicks: 5, maxLicensesPages: 5);

        foreach (var (steps, before, after, present) in new[]
                 {
                     (0, -1, 20, true),
                     (2, 20, 40, true),
                     (5, 40, 60, true),
                     (3, 40, 40, true),
                     (3, 40, 60, false),
                 })
        {
            Assert.Equal(
                policy.ClassifyLoadMore(steps, before, after, present),
                policy.ClassifyLicensesPage(steps, before, after, present));
        }
    }

    [Fact]
    public void Clicking_stops_when_a_click_stops_producing_rows()
    {
        var policy = Policy(maxLoadMoreClicks: 100);

        // A control that is still on the page but no longer adds anything is how
        // an infinite loop starts. The row count is the only thing that says so.
        Assert.Equal(
            SteamLoadMoreDecision.Stalled,
            policy.ClassifyLoadMore(clicksCompleted: 4, rowsBefore: 80, rowsAfter: 80, controlPresent: true));

        Assert.Equal(
            SteamLoadMoreDecision.Stalled,
            policy.ClassifyLoadMore(clicksCompleted: 4, rowsBefore: 80, rowsAfter: 12, controlPresent: true));
    }
}
