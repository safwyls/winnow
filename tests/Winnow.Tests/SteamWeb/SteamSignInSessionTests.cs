using Winnow.App.Services;
using Winnow.Auth.WebView;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// The three tiers of the account-page policy, asked directly.
///
/// <para>The sign-in session's mint scope became a tier of the shipped policy in
/// S3 rather than a predicate of its own, so it is testable beside the harvest
/// gate it deliberately disagrees with — and so nobody can widen one while
/// meaning to widen the other.</para>
/// </summary>
public class SteamAccountPagePolicyTierTests
{
    private static SteamAccountPagePolicy Policy => SteamAccountPagePolicy.For(0, 0);

    [Fact]
    public void The_store_root_may_be_minted_from()
    {
        // THE REGRESSION THIS TIER EXISTS FOR. The first live probe stalled for
        // its whole ten-minute timeout because the scope predicate was copied
        // from the harvest session, whose IsSignInJourney counts an empty path as
        // part of signing in. Steam lands the user on the store ROOT after Steam
        // Guard, and in the verified run that root is the page that carried the
        // token.
        Assert.True(Policy.AllowsMint(new Uri("https://store.steampowered.com/")));
        Assert.True(Policy.AllowsMint(new Uri("https://store.steampowered.com")));
        Assert.True(Policy.AllowsMint(new Uri("https://store.steampowered.com/?snr=1_4_4__global-header")));
    }

    [Fact]
    public void Every_fallback_mint_page_is_in_scope()
    {
        // A candidate the walk steers to but the scope then refuses to read would
        // burn a settle period on a page nobody ever looked at.
        foreach (var page in WebView2SteamSignInSession.MintPages)
        {
            Assert.True(Policy.AllowsMint(page), page.ToString());
        }
    }

    [Theory]
    [InlineData("https://store.steampowered.com/login/")]
    [InlineData("https://store.steampowered.com/login/?redir=account")]
    [InlineData("https://store.steampowered.com/join/")]
    [InlineData("https://store.steampowered.com/twofactor/manage")]
    [InlineData("https://store.steampowered.com/password/reset")]
    [InlineData("https://store.steampowered.com/mobilelogin")]
    [InlineData("https://store.steampowered.com/account/security")]
    public void A_page_the_user_types_credentials_into_is_never_mintable(string url)
    {
        // The sign-in form is on the trusted origin and still fails this: being
        // trusted is what lets the window go somewhere, never what lets Winnow
        // run a script in it.
        Assert.False(Policy.AllowsMint(new Uri(url)));
    }

    [Theory]
    [InlineData("https://login.steampowered.com/jwt/ajaxrefresh")]
    [InlineData("https://steamcommunity.com/my/edit/info")]
    [InlineData("https://help.steampowered.com/en/")]
    [InlineData("https://store.steampowered.evil.com/")]
    [InlineData("https://store.steampowered.com.evil.com/")]
    [InlineData("http://store.steampowered.com/")]
    public void Nothing_off_the_store_origin_is_mintable(string url)
    {
        // Navigable is not mintable. The community origin would mint a token too
        // (spike section 1, route 2) and is deliberately still refused: the
        // session reads one origin, over HTTPS, and a bearer credential is not
        // read off a page the window merely tolerated.
        Assert.False(Policy.AllowsMint(new Uri(url)));
    }

    [Fact]
    public void A_null_address_is_not_mintable()
    {
        Assert.False(Policy.AllowsMint(null));
    }

    [Fact]
    public void The_mint_tier_is_a_subset_of_what_the_policy_already_trusts()
    {
        // The invariant that keeps the new tier honest: it can never admit a
        // document the account-page session would not already have been allowed
        // to navigate to.
        foreach (var page in WebView2SteamSignInSession.MintPages.Append(
            new Uri("https://store.steampowered.com/")))
        {
            Assert.True(Policy.AllowsMint(page));
            Assert.True(Policy.IsTrustedOrigin(page));
        }
    }

    [Fact]
    public void The_harvest_gate_is_left_exactly_as_the_account_page_flow_needs_it()
    {
        // S3 gave the mint its own TIER rather than widening this one. If a later
        // edit relaxes AllowsHarvest to admit the store root, both browser flows
        // gain the right to run a capture script in a document the user never
        // agreed to hand over, and this is the test that says so.
        Assert.False(Policy.AllowsHarvest(new Uri("https://store.steampowered.com/")));
        Assert.False(Policy.AllowsHarvest(new Uri("https://store.steampowered.com/explore/")));
        Assert.True(Policy.AllowsHarvest(new Uri("https://store.steampowered.com/account/licenses/")));
        Assert.True(Policy.AllowsHarvest(new Uri("https://store.steampowered.com/account/history/")));
    }

    [Fact]
    public void The_two_tiers_deliberately_disagree_about_the_store_root()
    {
        // They answer different questions, and this pins that they are allowed to
        // differ so nobody "fixes" the divergence by collapsing them into one.
        var root = new Uri("https://store.steampowered.com/");

        Assert.True(Policy.AllowsMint(root));
        Assert.False(Policy.AllowsHarvest(root));
    }

    [Fact]
    public void The_two_account_pages_are_mintable_as_well_as_harvestable()
    {
        // Not a widening: a page that may be captured in full is obviously a page
        // a token may be read off. The tiers overlap here and diverge at the root.
        Assert.True(Policy.AllowsMint(SteamAccountPagePolicy.LicensesPage));
        Assert.True(Policy.AllowsMint(SteamAccountPagePolicy.PurchaseHistoryPage));
    }
}

/// <summary>
/// The parts of the sign-in session a test can reach.
///
/// <para>The session itself is a person in front of a browser, and a WebView2
/// control cannot be created in a unit test. What <em>is</em> reachable is every
/// refusal that happens before a window exists or that is decided from values
/// rather than from a live document — and those are exactly the ones whose
/// failure would be silent.</para>
/// </summary>
public class SteamSignInSessionTests
{
    [Fact]
    public async Task Without_consent_no_window_is_opened_and_the_sign_in_is_cancelled()
    {
        // The mechanism cannot grant itself permission, exactly as
        // WebView2SteamPageHarvester.HarvestAsync refuses. This returns before
        // the runtime check, which is why it is answerable in a test with no
        // browser and no Avalonia application anywhere near it.
        var result = await new WebView2SteamSignInSession().SignInAsync(
            new SteamSignInRequest { ConsentGranted = false });

        Assert.Equal(SteamSignInOutcome.Cancelled, result.Outcome);
        Assert.False(result.HasSession);
        Assert.Null(result.AccessToken);
        Assert.Null(result.RefreshToken);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public async Task Consent_is_checked_before_the_purchase_history_flag_can_matter()
    {
        // A caller that asked for everything still gets nothing without the first
        // consent. The two are not a hierarchy: neither implies the other.
        var result = await new WebView2SteamSignInSession().SignInAsync(new SteamSignInRequest
        {
            ConsentGranted = false,
            CapturePurchaseHistory = true,
            StaySignedIn = true,
        });

        Assert.Equal(SteamSignInOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Pages);
    }

    [Fact]
    public void A_page_and_a_token_naming_the_same_account_agree()
    {
        Assert.True(WebView2SteamSignInSession.IdentitiesAgree(
            SteamSessionFixtures.Subject, SteamSessionFixtures.Subject));
    }

    [Fact]
    public void A_page_and_a_token_naming_different_accounts_do_not_agree()
    {
        // The refusal. Nothing downstream could detect this, and everything
        // downstream would act on it: the whole library would be filed under
        // whichever id won.
        Assert.False(WebView2SteamSignInSession.IdentitiesAgree(
            SteamSessionFixtures.Subject, "76561198000000002"));
    }

    [Theory]
    [InlineData(null, "76561198000000001")]
    [InlineData("76561198000000001", null)]
    [InlineData(null, null)]
    [InlineData("", "76561198000000001")]
    public void A_missing_identity_on_either_side_is_not_a_disagreement(string? page, string? subject)
    {
        // Refusing these would refuse the verified live case for lack of a fact
        // rather than because of one: not every store document carries a steamid.
        Assert.True(WebView2SteamSignInSession.IdentitiesAgree(page, subject));
    }

    [Fact]
    public void A_refused_identity_carries_no_credential_at_all()
    {
        var refusal = SteamSignInResult.IdentityMismatch("they disagree");

        Assert.Equal(SteamSignInOutcome.IdentityMismatch, refusal.Outcome);
        Assert.False(refusal.HasSession);
        Assert.Null(refusal.AccessToken);
        Assert.Null(refusal.RefreshToken);
        Assert.False(refusal.RefreshTokenCaptured);
        Assert.Null(refusal.SteamId);
        Assert.Null(refusal.Pages);
    }

    [Fact]
    public void The_sign_in_starts_and_mints_on_the_store_origin_only()
    {
        Assert.Equal("store.steampowered.com", WebView2SteamSignInSession.LoginPage.Host);
        Assert.Equal("https", WebView2SteamSignInSession.LoginPage.Scheme);

        Assert.NotEmpty(WebView2SteamSignInSession.MintPages);
        foreach (var page in WebView2SteamSignInSession.MintPages)
        {
            Assert.Equal("store.steampowered.com", page.Host);
            Assert.Equal("https", page.Scheme);
        }
    }

    [Fact]
    public void The_login_page_is_navigable_and_never_mintable()
    {
        // Both halves matter. The window has to be able to render Steam's own
        // login form, and Winnow must never run a script in it.
        var policy = SteamAccountPagePolicy.For(0, 0);

        Assert.True(policy.IsNavigableOrigin(WebView2SteamSignInSession.LoginPage));
        Assert.False(policy.AllowsMint(WebView2SteamSignInSession.LoginPage));
    }

    [Fact]
    public void The_mint_script_reads_the_two_routes_the_spike_documented()
    {
        // Playnite's route and Valve's own auth_refresh.js global. If a later
        // edit drops either, the session answers "no token" on a page that was
        // carrying one.
        Assert.Contains("application_config", SteamSignInScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("data-store_user_config", SteamSignInScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("webapi_token", SteamSignInScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("g_wapit", SteamSignInScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("data-userinfo", SteamSignInScripts.Mint, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mint_script_never_asks_for_a_cookie()
    {
        // The refresh token is httpOnly and is read through the cookie manager,
        // in the browser process. A script that reached for document.cookie would
        // be reading a page for a credential, which is the line this flow does not
        // cross.
        Assert.DoesNotContain("document.cookie", SteamSignInScripts.Mint, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declined_purchase_history_capture_is_the_default()
    {
        // Acceptance criterion 2: declining is a complete answer, so it is also
        // the shape of a request nobody filled in.
        var request = new SteamSignInRequest { ConsentGranted = true };

        Assert.False(request.CapturePurchaseHistory);
        Assert.True(request.StaySignedIn);
    }
}

/// <summary>
/// What a sign-in result may be turned into a string.
///
/// <para>The type carries two live bearer credentials. The compiler-generated
/// record <c>ToString</c> would have printed both of them the first time anyone
/// interpolated a result into a log line, which is the same reason
/// <c>SteamCredential</c>, <c>SteamSession</c> and <c>SteamAccountPages</c> all
/// override theirs.</para>
/// </summary>
public class SteamSignInResultRedactionTests
{
    private const string Access = "eyJhbGciOiJFZERTQSJ9.eyJzdWIiOiI3NjU2MTE5ODAwMDAwMDAwMSJ9.SIGNATURE";
    private const string Refresh = "76561198000000001||eyJhbGciOiJFZERTQSJ9.cmVmcmVzaA.SIGNATURE";

    private static SteamSignInResult Signed() => SteamSignInResult.SignedIn(
        Access,
        new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        SteamSessionFixtures.Subject,
        ["web:store"],
        "steam",
        Refresh);

    [Fact]
    public void Neither_token_is_rendered_into_a_string()
    {
        var rendered = Signed().ToString();

        Assert.DoesNotContain(Access, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(Refresh, rendered, StringComparison.Ordinal);

        foreach (var segment in Access.Split('.'))
        {
            Assert.DoesNotContain(segment, rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_facts_a_log_line_exists_for_survive()
    {
        // A redaction that ate the outcome and the expiry would be safe and
        // useless: these are the values that explain a failed sync.
        var rendered = Signed().ToString();

        Assert.Contains("SignedIn", rendered, StringComparison.Ordinal);
        Assert.Contains("access token held", rendered, StringComparison.Ordinal);
        Assert.Contains("refresh token held", rendered, StringComparison.Ordinal);
        Assert.Contains("2026-09-01", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_captured_page_set_is_reported_by_size_and_never_by_content()
    {
        var result = SteamSignInResult.SignedIn(
            Access,
            null,
            SteamSessionFixtures.Subject,
            [],
            null,
            Refresh,
            new SteamAccountPages
            {
                LicensesHtml = "<html>SECRET-LICENCE-ROW</html>",
                HistoryHtml = "<html>SECRET-PURCHASE-ROW</html>",
                CapturedAt = DateTimeOffset.UnixEpoch,
            });

        var rendered = result.ToString();

        Assert.DoesNotContain("SECRET-LICENCE-ROW", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-PURCHASE-ROW", rendered, StringComparison.Ordinal);
        Assert.Contains("bytes", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sign_in_with_no_refresh_token_says_so_rather_than_implying_one()
    {
        var result = SteamSignInResult.SignedIn(
            Access, null, SteamSessionFixtures.Subject, [], null, refreshToken: null);

        Assert.False(result.RefreshTokenCaptured);
        Assert.Null(result.RefreshToken);
        Assert.True(result.HasSession);
        Assert.Contains("refresh token absent", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_whitespace_refresh_token_is_no_refresh_token()
    {
        // The capture returns whatever the cookie held. An empty value must not
        // become a credential the renewal design later depends on.
        var result = SteamSignInResult.SignedIn(
            Access, null, SteamSessionFixtures.Subject, [], null, refreshToken: "   ");

        Assert.False(result.RefreshTokenCaptured);
        Assert.Null(result.RefreshToken);
    }
}

/// <summary>
/// The join between the browser sign-in and S2's storage: what actually reaches
/// the session store, and what must not.
/// </summary>
public class SteamSignInServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A sign-in session that returns a scripted result and records what it was asked for.</summary>
    private sealed class FakeSignInSession(SteamSignInResult result) : ISteamSignInSession
    {
        public SteamSignInRequest? Requested { get; private set; }

        public string Name => "fake";

        public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<SteamSignInResult> SignInAsync(SteamSignInRequest request, CancellationToken ct = default)
        {
            Requested = request;
            return Task.FromResult(result);
        }
    }

    private static (SteamSignInService Service, ISteamSessionProvider Sessions, FakeSignInSession Session)
        Build(SteamSignInResult result)
    {
        var session = new FakeSignInSession(result);
        var provider = new SteamSessionProvider(
            new InMemorySteamSessionStore(),
            new SteamWebOptions(),
            new FixedClock(Now));

        return (new SteamSignInService(session, provider, new FixedClock(Now)), provider, session);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SteamSignInResult Minted(bool withRefresh = true, SteamAccountPages? pages = null)
        => SteamSignInResult.SignedIn(
            SteamSessionFixtures.AccessToken(Now.AddHours(24)),
            Now.AddHours(24),
            SteamSessionFixtures.Subject,
            ["web:store"],
            "steam",
            withRefresh ? SteamSessionFixtures.RefreshToken(Now.AddDays(207)) : null,
            pages);

    [Fact]
    public async Task A_minted_session_is_written_where_the_credential_selector_will_find_it()
    {
        // The point of the stage: until this write, every credential lookup found
        // a key or nothing.
        var (service, sessions, _) = Build(Minted());

        var report = await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.True(report.SignedIn);
        Assert.True(report.RefreshTokenCaptured);
        Assert.Equal(SteamSessionFixtures.Subject, report.SteamId);

        var stored = await sessions.GetAsync();
        Assert.NotNull(stored);
        Assert.Equal(SteamSessionFixtures.Subject, stored!.SteamId.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

        // The expiry is the token's own, not the caller's assertion of it.
        Assert.Equal(Now.AddHours(24).ToUnixTimeSeconds(), stored.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_declined_purchase_history_capture_still_produces_a_working_session()
    {
        // ACCEPTANCE CRITERION 2. Declining leaves the sign-in fully functional
        // for identity and playtime backfill, and nothing about the account pages
        // reaches the result: the session's only navigation to them is behind
        // this flag.
        var (service, sessions, session) = Build(Minted(pages: null));

        var report = await service.SignInAsync(new SteamSignInRequest
        {
            ConsentGranted = true,
            CapturePurchaseHistory = false,
        });

        Assert.False(session.Requested!.CapturePurchaseHistory);
        Assert.True(report.SignedIn);
        Assert.Null(report.Pages);
        Assert.Equal(SteamSessionFixtures.Subject, report.SteamId);
        Assert.NotNull(await sessions.GetAsync());
    }

    [Fact]
    public async Task A_consented_capture_carries_its_pages_through_without_storing_them()
    {
        // The session store holds two secrets and nothing else — no cookie, no
        // page content — so the documents ride on the report for the caller to
        // hand to the importer.
        var pages = new SteamAccountPages
        {
            LicensesHtml = "<html>licences</html>",
            HistoryHtml = "<html>history</html>",
            CapturedAt = Now,
        };

        var (service, _, _) = Build(Minted(pages: pages));

        var report = await service.SignInAsync(new SteamSignInRequest
        {
            ConsentGranted = true,
            CapturePurchaseHistory = true,
        });

        Assert.Same(pages, report.Pages);
    }

    [Fact]
    public async Task A_refused_sign_in_writes_nothing()
    {
        var (service, sessions, _) = Build(
            SteamSignInResult.IdentityMismatch("the page and the token disagree"));

        var report = await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.Equal(SteamSignInOutcome.IdentityMismatch, report.Outcome);
        Assert.False(report.SignedIn);
        Assert.False(report.Persisted);
        Assert.Equal(SteamSessionHealth.NotSignedIn, report.Health);
        Assert.Null(await sessions.GetAsync());
    }

    [Fact]
    public async Task A_cancelled_sign_in_writes_nothing_and_says_why()
    {
        var (service, sessions, _) = Build(SteamSignInResult.Cancelled("the user closed the window"));

        var report = await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.Equal(SteamSignInOutcome.Cancelled, report.Outcome);
        Assert.Equal("the user closed the window", report.Detail);
        Assert.Null(await sessions.GetAsync());
    }

    [Fact]
    public async Task A_sign_in_with_no_refresh_token_is_kept_and_reported_rather_than_discarded()
    {
        // S2 defined a stored session as BOTH secrets, so this sign-in persisted
        // nothing and threw away a working 24-hour access token the user had just
        // earned by typing their password. The record was relaxed: the refresh
        // token is persisted WHEN THERE IS ONE, not required before anything is
        // stored.
        var (service, sessions, _) = Build(Minted(withRefresh: false));

        var report = await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.True(report.SignedIn);
        Assert.False(report.RefreshTokenCaptured);

        var stored = await sessions.GetAsync();
        Assert.NotNull(stored);
        Assert.False(stored.HasRefreshToken);
        Assert.Equal(SteamSessionFixtures.Subject, stored.SteamId.ToString());

        // And it says so out loud, because "signed in but unrenewable" is exactly
        // the state §4.7's legibility condition forbids degrading silently into.
        Assert.Contains("refresh token", report.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be renewed", report.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_stored_token_only_session_is_live_then_expired_with_nothing_in_between()
    {
        // The health distinction on the session the service actually wrote,
        // rather than on one a test built by hand.
        var (service, sessions, _) = Build(Minted(withRefresh: false));

        await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        var stored = await sessions.GetAsync();
        Assert.NotNull(stored);

        Assert.Equal(SteamSessionHealth.Live, SteamSessionProvider.Classify(stored, Now));

        // Deep in the lead window where a renewable session would read
        // RenewalDue. This one has no renewal to be due.
        Assert.Equal(
            SteamSessionHealth.Live,
            SteamSessionProvider.Classify(stored, Now.AddHours(23).AddMinutes(30)));

        Assert.Equal(
            SteamSessionHealth.Expired, SteamSessionProvider.Classify(stored, Now.AddHours(25)));
    }

    [Fact]
    public async Task Signing_out_discards_the_stored_session()
    {
        var (service, sessions, _) = Build(Minted());

        await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });
        Assert.NotNull(await sessions.GetAsync());

        await service.SignOutAsync();

        Assert.Null(await sessions.GetAsync());
        Assert.Equal(SteamSessionHealth.NotSignedIn, await service.GetHealthAsync());
    }

    [Fact]
    public async Task The_request_reaches_the_browser_session_unchanged()
    {
        // The service composes; it does not decide. A stage that quietly turned
        // on a capture the user declined would be the worst possible bug here.
        var (service, _, session) = Build(Minted());

        var request = new SteamSignInRequest
        {
            ConsentGranted = true,
            CapturePurchaseHistory = true,
            StaySignedIn = false,
            MaxLoadMoreClicks = 7,
        };

        await service.SignInAsync(request);

        Assert.Same(request, session.Requested);
    }
}
