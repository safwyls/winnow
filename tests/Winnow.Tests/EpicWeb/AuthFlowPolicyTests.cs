using Winnow.Auth.WebView;
using Winnow.Core.Auth;
using Xunit;

namespace Winnow.Tests.EpicWeb;

/// <summary>
/// The origin, navigation and state rules the embedded sign-in browser runs on.
///
/// <para>These exist because a WebView2 control cannot be created in a unit test
/// — no runtime, no Avalonia application, no window. Every decision that matters
/// to security was therefore pulled out of the host and into
/// <see cref="AuthFlowPolicy"/>, which is pure and can be asked directly. The
/// host is left holding wiring, and this file is what makes the wiring worth
/// trusting.</para>
/// </summary>
public class AuthFlowPolicyTests
{
    private const string ProviderOrigin = "https://www.epicgames.com";
    private const string RedirectUrl = "https://localhost/launcher/authorized";

    private static AuthPromptRequest Request(
        string? state = "THE-STATE-THAT-WAS-SENT",
        IReadOnlyList<Uri>? navigable = null,
        AuthCaptureStrategies strategies = AuthCaptureStrategies.All)
        => new()
        {
            ProviderName = "Epic Games",
            StartUrl = new Uri(ProviderOrigin + "/id/authorize?client_id=x&response_type=code"),
            HarvestUrl = new Uri(ProviderOrigin + "/id/api/redirect?clientId=x&responseType=code"),
            RedirectUrl = new Uri(RedirectUrl),
            ConsentNotice = "test notice",
            ExpectedState = state,
            Strategies = strategies,
            AdditionalNavigableOrigins = navigable ?? [],
        };

    private static AuthFlowPolicy Policy(
        string? state = "THE-STATE-THAT-WAS-SENT",
        IReadOnlyList<Uri>? navigable = null,
        AuthCaptureStrategies strategies = AuthCaptureStrategies.All)
        => AuthFlowPolicy.For(Request(state, navigable, strategies));

    // ---- Origin binding -----------------------------------------------------

    [Fact]
    public void The_trusted_set_is_exactly_the_origins_the_request_named()
    {
        var policy = Policy(navigable: [new Uri("https://accounts.google.com")]);

        Assert.Equal(
            ["https://localhost:443", "https://www.epicgames.com:443"],
            policy.TrustedOrigins.Order(StringComparer.Ordinal).ToArray());

        // The social provider is navigable but NOT trusted. That is the whole
        // two-tier model in one assertion: a page may render without being
        // allowed to hand Winnow a credential.
        Assert.False(policy.IsTrustedOrigin(new Uri("https://accounts.google.com/signin")));
        Assert.True(policy.IsNavigableOrigin(new Uri("https://accounts.google.com/signin")));
    }

    [Fact]
    public void A_message_from_the_wrong_origin_is_refused()
    {
        var policy = Policy();

        // The attack the finding describes: a page that believes it is inside the
        // launcher posts a shaped exchange message. Shape is not identity.
        Assert.False(policy.AcceptsMessageFrom("https://evil.example/steal"));
        Assert.False(policy.AcceptsMessageFrom("https://accounts.google.com/signin"));

        // Including a look-alike host that merely ends with the real one.
        Assert.False(policy.AcceptsMessageFrom("https://www.epicgames.com.evil.example/"));

        Assert.True(policy.AcceptsMessageFrom(ProviderOrigin + "/id/api/redirect"));
    }

    [Fact]
    public void A_message_from_an_unidentifiable_source_is_refused()
    {
        var policy = Policy();

        // A sandboxed frame, a data: document, a blob:, or a WebView2 build that
        // reports nothing. Fail closed on every one.
        Assert.False(policy.AcceptsMessageFrom(null));
        Assert.False(policy.AcceptsMessageFrom(string.Empty));
        Assert.False(policy.AcceptsMessageFrom("about:blank"));
        Assert.False(policy.AcceptsMessageFrom("data:text/html,<script>x</script>"));
        Assert.False(policy.AcceptsMessageFrom("/id/api/redirect"));
    }

    [Fact]
    public void A_third_party_iframe_on_the_sign_in_page_is_not_trusted()
    {
        // WebView2 reports the POSTING document's URL, so an iframe posting a
        // message is identified by the iframe's own origin. A captcha or
        // social-widget frame therefore fails the check even though the page
        // hosting it passes.
        var policy = Policy(navigable: [new Uri("https://talon-website-prod.ol.epicgames.com")]);

        Assert.False(policy.AcceptsMessageFrom("https://talon-website-prod.ol.epicgames.com/captcha"));
        Assert.False(policy.AcceptsMessageFrom("https://www.google.com/recaptcha/api2/anchor"));
    }

    [Fact]
    public void Plaintext_http_is_never_trusted_even_when_the_request_named_it()
    {
        // A code is a full-account credential; there is no address it may cross
        // in the clear, localhost included.
        var policy = AuthFlowPolicy.For(new AuthPromptRequest
        {
            ProviderName = "Test",
            StartUrl = new Uri("http://www.example.com/login"),
            RedirectUrl = new Uri("http://localhost/authorized"),
            ConsentNotice = "n",
        });

        Assert.Empty(policy.TrustedOrigins);
        Assert.False(policy.AcceptsMessageFrom("http://www.example.com/login"));
    }

    [Fact]
    public void The_bridge_is_offered_only_to_trusted_origins()
    {
        var policy = Policy(navigable: [new Uri("https://accounts.google.com")]);

        Assert.True(policy.AllowsBridge(new Uri(ProviderOrigin + "/id/login")));
        Assert.False(policy.AllowsBridge(new Uri("https://accounts.google.com/signin")));
        Assert.False(policy.AllowsBridge(new Uri("https://evil.example/")));
    }

    // ---- Redirect matching --------------------------------------------------

    [Fact]
    public void The_redirect_must_match_on_the_port_as_well()
    {
        var policy = Policy();

        Assert.True(policy.IsRedirectTarget(new Uri("https://localhost/launcher/authorized?code=X")));

        // THE finding's fourth item. Matching scheme/host/path only would accept
        // this, and a listener on 8443 is trivially arranged on the user's own
        // machine.
        Assert.False(policy.IsRedirectTarget(new Uri("https://localhost:8443/launcher/authorized?code=X")));

        // The default port written out is the same port, not a different one.
        Assert.True(policy.IsRedirectTarget(new Uri("https://localhost:443/launcher/authorized?code=X")));
    }

    [Fact]
    public void The_redirect_must_match_on_scheme_host_and_path_too()
    {
        var policy = Policy();

        Assert.False(policy.IsRedirectTarget(new Uri("http://localhost/launcher/authorized?code=X")));
        Assert.False(policy.IsRedirectTarget(new Uri("https://localhost.evil.example/launcher/authorized?code=X")));
        Assert.False(policy.IsRedirectTarget(new Uri("https://localhost/launcher/authorized/extra?code=X")));
        Assert.False(policy.IsRedirectTarget(new Uri("https://localhost/other?code=X")));

        // A trailing slash is not a different endpoint.
        Assert.True(policy.IsRedirectTarget(new Uri("https://localhost/launcher/authorized/?code=X")));
    }

    [Fact]
    public void There_is_no_redirect_target_when_the_request_registered_none()
    {
        var policy = AuthFlowPolicy.For(new AuthPromptRequest
        {
            ProviderName = "Test",
            StartUrl = new Uri(ProviderOrigin + "/id/authorize"),
            ConsentNotice = "n",
        });

        Assert.False(policy.IsRedirectTarget(new Uri(RedirectUrl)));
        Assert.False(policy.IsRedirectTarget(null));
    }

    // ---- Navigation ---------------------------------------------------------

    [Fact]
    public void The_sign_in_journey_and_the_redirect_are_the_two_allowed_outcomes()
    {
        var policy = Policy();

        Assert.Equal(
            AuthNavigationDecision.Allow,
            policy.ClassifyNavigation(new Uri(ProviderOrigin + "/id/login")));

        Assert.Equal(
            AuthNavigationDecision.CaptureRedirect,
            policy.ClassifyNavigation(new Uri(RedirectUrl + "?code=X")));

        // WebView2 starts here and returns here between documents.
        Assert.Equal(AuthNavigationDecision.Allow, policy.ClassifyNavigation(new Uri("about:blank")));
    }

    [Fact]
    public void An_unapproved_navigation_is_blocked_rather_than_hosted()
    {
        var policy = Policy();

        Assert.Equal(
            AuthNavigationDecision.Block,
            policy.ClassifyNavigation(new Uri("https://evil.example/harvest")));

        // Non-web destinations too: a page asking the browser to launch a
        // protocol handler, render a data: document, or read the disk.
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(new Uri("file:///C:/Windows/win.ini")));
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(new Uri("data:text/html,<b>x</b>")));
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyNavigation(null));
    }

    [Fact]
    public void A_configured_social_provider_is_navigable()
    {
        // The reason the navigable tier exists at all: blocking these would break
        // "Sign in with Google" on Epic's own login page.
        var policy = Policy(navigable: [new Uri("https://accounts.google.com/")]);

        Assert.Equal(
            AuthNavigationDecision.Allow,
            policy.ClassifyNavigation(new Uri("https://accounts.google.com/o/oauth2/auth?x=1")));
    }

    [Fact]
    public void The_redirect_is_only_captured_when_that_strategy_is_armed()
    {
        var policy = Policy(strategies: AuthCaptureStrategies.SessionHarvest);

        // Still a trusted origin, so it renders — but nothing reads a code off it.
        Assert.Equal(
            AuthNavigationDecision.Allow,
            policy.ClassifyNavigation(new Uri(RedirectUrl + "?code=X")));
    }

    // ---- Popups -------------------------------------------------------------

    [Fact]
    public void An_unapproved_popup_goes_to_the_users_own_browser()
    {
        // The finding's third item. Folding it into this window would put an
        // arbitrary page inside the session that holds the sign-in cookies.
        var policy = Policy();

        Assert.Equal(
            AuthNavigationDecision.OpenExternally,
            policy.ClassifyPopup(new Uri("https://evil.example/oauth")));

        Assert.Equal(
            AuthNavigationDecision.OpenExternally,
            policy.ClassifyPopup(new Uri("https://accounts.google.com/signin")));
    }

    [Fact]
    public void An_approved_popup_is_still_folded_into_the_same_window()
    {
        // Epic's alternative sign-in buttons open one, and a WebView2 with no
        // handler simply drops it — the button appears broken.
        var policy = Policy(navigable: [new Uri("https://accounts.google.com")]);

        Assert.Equal(
            AuthNavigationDecision.Allow,
            policy.ClassifyPopup(new Uri("https://accounts.google.com/signin")));

        Assert.Equal(
            AuthNavigationDecision.Allow,
            policy.ClassifyPopup(new Uri(ProviderOrigin + "/id/mfa")));
    }

    [Fact]
    public void A_popup_can_carry_the_redirect_and_is_captured_not_opened()
    {
        var policy = Policy();

        Assert.Equal(
            AuthNavigationDecision.CaptureRedirect,
            policy.ClassifyPopup(new Uri(RedirectUrl + "?code=X&state=Y")));
    }

    [Fact]
    public void A_popup_to_something_that_is_not_a_web_page_is_refused_outright()
    {
        var policy = Policy();

        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyPopup(new Uri("file:///C:/Windows/win.ini")));
        Assert.Equal(AuthNavigationDecision.Block, policy.ClassifyPopup(null));
    }

    // ---- State --------------------------------------------------------------

    [Fact]
    public void The_matching_state_is_the_only_one_that_passes()
    {
        var policy = Policy(state: "THE-STATE-THAT-WAS-SENT");

        Assert.Equal(
            AuthStateVerification.Matched,
            policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=THE-STATE-THAT-WAS-SENT")));

        // Order on the query string is not significance.
        Assert.Equal(
            AuthStateVerification.Matched,
            policy.VerifyState(new Uri(RedirectUrl + "?state=THE-STATE-THAT-WAS-SENT&code=X")));
    }

    [Fact]
    public void A_missing_state_is_reported_as_missing_rather_than_accepted()
    {
        var policy = Policy(state: "THE-STATE-THAT-WAS-SENT");

        Assert.Equal(AuthStateVerification.Missing, policy.VerifyState(new Uri(RedirectUrl + "?code=X")));
        Assert.Equal(AuthStateVerification.Missing, policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=")));
        Assert.Equal(AuthStateVerification.Missing, policy.VerifyState(null));
    }

    [Fact]
    public void A_wrong_state_is_a_mismatch_however_close_it_is()
    {
        var policy = Policy(state: "THE-STATE-THAT-WAS-SENT");

        Assert.Equal(
            AuthStateVerification.Mismatched,
            policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=SOMEONE-ELSES-STATE")));

        // A prefix of the real one, which is what a naive comparison written with
        // StartsWith would let through.
        Assert.Equal(
            AuthStateVerification.Mismatched,
            policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=THE-STATE-THAT-WAS-SEN")));

        // Case is significant: the value is opaque bytes, not a word.
        Assert.Equal(
            AuthStateVerification.Mismatched,
            policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=the-state-that-was-sent")));
    }

    [Fact]
    public void A_state_that_needed_escaping_still_matches_after_the_round_trip()
    {
        // Base64url avoids this in practice, but the query is unescaped before
        // comparison and that has to be true rather than assumed.
        var policy = Policy(state: "a+b/c=d");

        Assert.Equal(
            AuthStateVerification.Matched,
            policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=" + Uri.EscapeDataString("a+b/c=d"))));
    }

    [Fact]
    public void No_state_sent_means_nothing_to_verify()
    {
        // The opt-out path: a flow that starts on the provider's code endpoint
        // makes no authorization request, so it has no state to demand back and
        // must not fail for the lack of one.
        var policy = Policy(state: null);

        Assert.Equal(AuthStateVerification.NotRequired, policy.VerifyState(new Uri(RedirectUrl + "?code=X")));
        Assert.Equal(
            AuthStateVerification.NotRequired,
            policy.VerifyState(new Uri(RedirectUrl + "?code=X&state=anything")));
    }
}

/// <summary>The state value itself: how it is minted and how it is compared.</summary>
public class AuthStateTests
{
    [Fact]
    public void Every_state_is_unique_and_carries_its_full_entropy()
    {
        var minted = Enumerable.Range(0, 200).Select(_ => AuthState.Create()).ToArray();

        Assert.Equal(minted.Length, minted.Distinct(StringComparer.Ordinal).Count());

        foreach (var value in minted)
        {
            // 32 bytes, base64url, unpadded.
            Assert.Equal(43, value.Length);
            Assert.DoesNotContain("=", value, StringComparison.Ordinal);
            Assert.DoesNotContain("+", value, StringComparison.Ordinal);
            Assert.DoesNotContain("/", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_state_survives_a_query_string_without_escaping()
    {
        var value = AuthState.Create();

        Assert.Equal(value, Uri.EscapeDataString(value));
    }

    [Fact]
    public void Matching_is_exact_and_never_matches_nothing()
    {
        var value = AuthState.Create();

        Assert.True(AuthState.Matches(value, value));

        // A separately allocated copy, so the comparison is over the bytes and
        // not over a reference that happened to be interned.
        Assert.True(AuthState.Matches(value, new string(value.ToCharArray())));

        Assert.False(AuthState.Matches(value, value[..^1]));
        Assert.False(AuthState.Matches(value, value + "x"));
        Assert.False(AuthState.Matches(value, AuthState.Create()));

        // "No state" is never a match, on either side. This is the case that
        // would otherwise turn a provider that drops the parameter into a
        // provider that disables the check.
        Assert.False(AuthState.Matches(value, null));
        Assert.False(AuthState.Matches(value, "   "));
        Assert.False(AuthState.Matches(null, value));
        Assert.False(AuthState.Matches(null, null));
    }
}

/// <summary>
/// The injected scripts, which cannot be run here but can be read: the point is
/// that the origin guard is present and baked with this attempt's origins.
/// </summary>
public class AuthBridgeScriptTests
{
    private static readonly string[] Trusted = ["https://www.epicgames.com:443", "https://localhost:443"];

    [Fact]
    public void The_bridge_refuses_to_define_itself_outside_a_trusted_top_level_document()
    {
        var script = AuthBridgeScripts.Bridge(Trusted);

        // WebView2's document-created hook has no per-frame or per-origin filter,
        // so the filter has to travel inside the script.
        Assert.Contains("window.top !== window.self", script, StringComparison.Ordinal);
        Assert.Contains("\"https://www.epicgames.com:443\"", script, StringComparison.Ordinal);
        Assert.Contains("\"https://localhost:443\"", script, StringComparison.Ordinal);

        // And it still is the launcher bridge afterwards.
        Assert.Contains("requestexchangecodesignin", script, StringComparison.Ordinal);
        Assert.Contains("registersignincompletecallback", script, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_trusted_set_produces_a_bridge_that_can_never_arm()
    {
        // Fail closed: a request naming only plaintext URLs trusts nothing, and
        // the script it produces defines nothing anywhere.
        var script = AuthBridgeScripts.Bridge([]);

        Assert.Contains("var __winnowAllowed = [];", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_harvester_carries_the_same_guard_and_its_url_as_a_literal()
    {
        var script = AuthBridgeScripts.Harvester(
            new Uri("https://www.epicgames.com/id/api/redirect?clientId=x&responseType=code"),
            Trusted,
            TimeSpan.FromSeconds(5),
            150);

        Assert.Contains("window.top !== window.self", script, StringComparison.Ordinal);
        Assert.Contains("\"https://www.epicgames.com:443\"", script, StringComparison.Ordinal);

        // JSON-serialised, which is also the escaping: a JSON string literal is a
        // JavaScript string literal, and System.Text.Json's default encoder
        // additionally escapes the ampersand — harmless in JS, and one less way
        // for a URL to break out of the literal.
        Assert.Contains(
            "var url = \"https://www.epicgames.com/id/api/redirect?clientId=x\\u0026responseType=code\";",
            script,
            StringComparison.Ordinal);
        Assert.Contains("var remaining = 150;", script, StringComparison.Ordinal);
        Assert.Contains("setInterval(ask, 5000)", script, StringComparison.Ordinal);
    }
}
