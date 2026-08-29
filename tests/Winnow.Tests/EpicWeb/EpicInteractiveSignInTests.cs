using System.Net;
using System.Text;
using Winnow.App.Services;
using Winnow.Auth.WebView;
using Winnow.Core.Auth;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Winnow.Tests.EpicWeb;

/// <summary>
/// Epic sign-in machinery: prompt chain, grants, credential precedence and
/// failure paths, all without a real browser.
/// </summary>
public class EpicInteractiveSignInTests
{
    /// <summary>
    /// A prompt that answers whatever a test tells it to, and records that it was
    /// asked.
    /// </summary>
    private sealed class ScriptedPrompt : IInteractiveAuthPrompt
    {
        private readonly Func<AuthPromptRequest, AuthCodeResult> _answer;
        private readonly bool _available;

        public ScriptedPrompt(
            string name, bool available, Func<AuthPromptRequest, AuthCodeResult> answer)
        {
            Name = name;
            _available = available;
            _answer = answer;
        }

        public string Name { get; }

        public int Calls { get; private set; }

        public AuthPromptRequest? LastRequest { get; private set; }

        public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_available);

        public Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest request, CancellationToken ct = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(_answer(request));
        }
    }

    private static ScriptedPrompt Captures(string name, AuthCodeKind kind, string code)
        => new(name, available: true, _ => AuthCodeResult.Captured(kind, code, "test"));

    [Fact]
    public async Task An_authorization_code_is_spent_on_the_authorization_code_grant()
    {
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "AUTH-CODE-FROM-PROMPT");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        var result = await host.SignIn.SignInAsync();

        Assert.True(result.Succeeded);

        var request = Assert.Single(host.Handler.Requests, r => r.Endpoint == EpicEndpoint.Token);
        Assert.Equal("authorization_code", request.GrantType);
        Assert.Equal("AUTH-CODE-FROM-PROMPT", request.Form["code"]);
        Assert.Equal("eg1", request.Form["token_type"]);
    }

    [Fact]
    public async Task An_exchange_code_is_spent_on_the_exchange_code_grant()
    {
        // The whole reason the second grant exists: the launcher JS bridge hands
        // back an exchange code, which the authorization_code grant would reject.
        var prompt = Captures("browser", AuthCodeKind.ExchangeCode, "EXCHANGE-CODE-FROM-BRIDGE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        var result = await host.SignIn.SignInAsync();

        Assert.True(result.Succeeded);

        var request = Assert.Single(host.Handler.Requests, r => r.Endpoint == EpicEndpoint.Token);
        Assert.Equal("exchange_code", request.GrantType);
        Assert.Equal("EXCHANGE-CODE-FROM-BRIDGE", request.Form["exchange_code"]);

        // And it does NOT also send the authorization-code field. Epic validates
        // the client before the grant, so a body carrying both would fail in a
        // way that reads as a credential problem.
        Assert.False(request.Form.ContainsKey("code"));
    }

    [Fact]
    public async Task The_code_never_reaches_the_uri_or_the_log()
    {
        var prompt = Captures("browser", AuthCodeKind.ExchangeCode, "SECRET-EXCHANGE-CODE-VALUE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        await host.SignIn.SignInAsync();

        var request = Assert.Single(host.Handler.Requests, r => r.Endpoint == EpicEndpoint.Token);
        Assert.DoesNotContain("SECRET-EXCHANGE-CODE-VALUE", request.Uri.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SECRET-EXCHANGE-CODE-VALUE",
            string.Join('\n', host.Logs.Lines),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unavailable_prompt_falls_through_to_the_next()
    {
        // The headless / no-WebView2-runtime case: the browser declines, the
        // console peer answers, and nothing about the outcome is degraded.
        var browser = new ScriptedPrompt("browser", available: false, _ => AuthCodeResult.Failed("unused"));
        var console = Captures("console", AuthCodeKind.AuthorizationCode, "PASTED-CODE");

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser, console]);

        var result = await host.SignIn.SignInAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, browser.Calls);
        Assert.Equal(1, console.Calls);
        Assert.Contains("console", host.SignIn.LastCaptureRoute, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prompt_that_captures_nothing_falls_through_to_the_next()
    {
        // Epic changed its page: the browser ran, nothing fired. The manual flow
        // is exactly the remedy, so it is tried rather than reported as fatal.
        var browser = new ScriptedPrompt("browser", available: true, _ => AuthCodeResult.Failed("page changed"));
        var console = Captures("console", AuthCodeKind.AuthorizationCode, "PASTED-CODE");

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser, console]);

        Assert.True((await host.SignIn.SignInAsync()).Succeeded);
        Assert.Equal(1, browser.Calls);
        Assert.Equal(1, console.Calls);
    }

    [Fact]
    public async Task A_prompt_that_throws_falls_through_instead_of_taking_the_app_down()
    {
        // The contract says a prompt never throws. UI code throws anyway — a
        // window on the wrong thread, a native handle that went away — and an
        // exception escaping here would take out an optional feature's caller.
        var browser = new ScriptedPrompt(
            "browser", available: true, _ => throw new InvalidOperationException("native handle went away"));
        var console = Captures("console", AuthCodeKind.AuthorizationCode, "PASTED-CODE");

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser, console]);

        Assert.True((await host.SignIn.SignInAsync()).Succeeded);
        Assert.Equal(1, console.Calls);
    }

    [Fact]
    public async Task Cancelling_does_not_escalate_to_the_next_prompt()
    {
        // The one outcome that must NOT fall through. The user closed a window;
        // answering that by opening a different one is nagging, not a fallback.
        var browser = new ScriptedPrompt("browser", available: true, _ => AuthCodeResult.Cancelled("closed"));
        var console = Captures("console", AuthCodeKind.AuthorizationCode, "PASTED-CODE");

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser, console]);

        var result = await host.SignIn.SignInAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(EpicSignInFailure.Cancelled, result.Failure);
        Assert.Equal(0, console.Calls);
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task No_usable_prompt_is_a_reason_not_a_crash()
    {
        var browser = new ScriptedPrompt("browser", available: false, _ => AuthCodeResult.Failed("unused"));
        var console = new ScriptedPrompt("console", available: false, _ => AuthCodeResult.Failed("unused"));

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser, console]);

        var result = await host.SignIn.SignInAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(EpicSignInFailure.NoInteractivePrompt, result.Failure);
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task A_host_that_registers_no_prompt_at_all_is_also_a_reason()
    {
        // A headless composition — or a future host that forgot to register one.
        // It must be a clean no-op, not a resolution failure at startup.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());

        var result = await host.SignIn.SignInAsync();

        Assert.Equal(EpicSignInFailure.NoInteractivePrompt, result.Failure);
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task A_rejected_code_reports_the_code_not_the_credentials()
    {
        // Opposite remedies: "sign in again" versus "the client pair is wrong".
        using var host = new EpicWebTestHost(
            (request, _) => request.Endpoint == EpicEndpoint.Token
                ? FakeEpicHandler.Json(HttpStatusCode.BadRequest, EpicFixturesWeb.InvalidRefresh())
                : FakeEpicHandler.Json(HttpStatusCode.NotFound, "{}"),
            prompts: [Captures("browser", AuthCodeKind.ExchangeCode, "STALE-CODE")]);

        var result = await host.SignIn.SignInAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(EpicSignInFailure.InvalidAuthorizationCode, result.Failure);
    }

    [Fact]
    public async Task The_request_carries_the_consent_notice_and_the_registered_redirect()
    {
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        await host.SignIn.SignInAsync();

        var request = prompt.LastRequest!;

        // The consent moment, moved rather than dropped. Epic's own warning is
        // quoted verbatim, and Winnow names itself as the third party it warns
        // about.
        Assert.Contains("Do not share this code with any 3rd party service.", request.ConsentNotice, StringComparison.Ordinal);
        Assert.Contains("access to your Epic account.", request.ConsentNotice, StringComparison.Ordinal);
        Assert.Contains("Winnow is a 3rd party service", request.ConsentNotice, StringComparison.Ordinal);

        // The single redirect Epic's launcher client accepts. Verified live that
        // the allowlist is exact — loopback and other ports are rejected — which
        // is why RFC 8252 is unavailable for Epic rather than merely worse.
        Assert.Equal(new Uri("https://localhost/launcher/authorized"), request.RedirectUrl);

        // Every capture route armed at once, so one sign-in exercises all of them
        // instead of several sign-ins burning several codes.
        Assert.Equal(AuthCaptureStrategies.All, request.Strategies);
        Assert.True(request.Strategies.HasFlag(AuthCaptureStrategies.SessionHarvest));
    }

    [Fact]
    public async Task The_default_start_url_can_render_a_login_form()
    {
        // THE regression this file exists for. The first build started on
        // id/api/redirect, which is an API that answers only for a browser that
        // already holds Epic's cookies — so an embedded browser's fresh profile
        // got {"authorizationCode":null,…} and no user ever saw a password box.
        // The start URL must be one that can begin an UNAUTHENTICATED flow.
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        await host.SignIn.SignInAsync();

        var request = prompt.LastRequest!;
        var start = request.StartUrl.AbsoluteUri;

        Assert.Contains("/id/authorize", start, StringComparison.Ordinal);
        Assert.DoesNotContain("/id/api/redirect", start, StringComparison.Ordinal);

        // With the registered redirect on it — the authorize endpoint rejects
        // anything else with client_redirect_domain_mismatch.
        Assert.Contains(
            Uri.EscapeDataString("https://localhost/launcher/authorized"),
            start,
            StringComparison.Ordinal);

        // And the code endpoint is carried as the HARVEST url, which is what the
        // flow navigates to (or fetches) once a session exists.
        Assert.NotNull(request.HarvestUrl);
        Assert.Contains("/id/api/redirect", request.HarvestUrl!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_attempt_mints_its_own_state_and_sends_it_on_the_authorize_url()
    {
        // F05. Without this the flow will spend whatever code reaches the
        // redirect, whoever put it there — login-CSRF, and the resulting session
        // is somebody else's account.
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        await host.SignIn.SignInAsync();
        var first = prompt.LastRequest!;

        Assert.False(string.IsNullOrWhiteSpace(first.ExpectedState));
        Assert.Equal("state", first.StateParameter);

        // On the URL Epic is asked with, not merely held in memory: a state the
        // provider never saw cannot come back.
        Assert.Contains(
            "state=" + Uri.EscapeDataString(first.ExpectedState!),
            first.StartUrl.AbsoluteUri,
            StringComparison.Ordinal);

        // Per ATTEMPT, not per process. A state reused across sign-ins is a state
        // an earlier redirect can still satisfy.
        await host.SignIn.SignInAsync();
        Assert.NotEqual(first.ExpectedState, prompt.LastRequest!.ExpectedState);
    }

    [Fact]
    public async Task The_state_binds_the_redirect_the_browser_is_watching_for()
    {
        // The two halves have to agree, so this asserts them together: the policy
        // the browser runs on is built from this very request.
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        await host.SignIn.SignInAsync();
        var policy = AuthFlowPolicy.For(prompt.LastRequest!);
        var state = prompt.LastRequest!.ExpectedState!;

        Assert.Equal(
            AuthStateVerification.Matched,
            policy.VerifyState(new Uri(
                "https://localhost/launcher/authorized?code=REAL&state=" + Uri.EscapeDataString(state))));

        Assert.Equal(
            AuthStateVerification.Mismatched,
            policy.VerifyState(new Uri("https://localhost/launcher/authorized?code=INJECTED&state=attacker")));

        Assert.Equal(
            AuthStateVerification.Missing,
            policy.VerifyState(new Uri("https://localhost/launcher/authorized?code=INJECTED")));
    }

    [Fact]
    public async Task Epics_own_origins_are_trusted_and_the_social_providers_are_only_navigable()
    {
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [prompt]);

        await host.SignIn.SignInAsync();
        var policy = AuthFlowPolicy.For(prompt.LastRequest!);

        Assert.True(policy.IsTrustedOrigin(new Uri("https://www.epicgames.com/id/login")));
        Assert.True(policy.IsTrustedOrigin(new Uri("https://localhost/launcher/authorized")));

        // Google is offered on Epic's login page, so the window may render it —
        // and that is all it may do. No bridge, no messages, no body reads.
        Assert.True(policy.IsNavigableOrigin(new Uri("https://accounts.google.com/o/oauth2/auth")));
        Assert.False(policy.IsTrustedOrigin(new Uri("https://accounts.google.com/o/oauth2/auth")));
        Assert.False(policy.AllowsBridge(new Uri("https://accounts.google.com/o/oauth2/auth")));

        // Anything else is refused rather than hosted, and a popup to it goes to
        // the user's own browser.
        Assert.Equal(
            AuthNavigationDecision.Block,
            policy.ClassifyNavigation(new Uri("https://evil.example/steal")));
        Assert.Equal(
            AuthNavigationDecision.OpenExternally,
            policy.ClassifyPopup(new Uri("https://evil.example/steal")));
    }

    [Fact]
    public async Task Starting_on_the_code_endpoint_is_opt_out_and_still_carries_a_harvest_url()
    {
        // Only useful for a browser profile that is already signed in. Kept
        // because it is a legitimate shortcut, defaulted off because it cannot
        // start a cold sign-in.
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(),
            configure: o => o.UseAuthorizeEndpointForSignIn = false,
            prompts: [prompt]);

        await host.SignIn.SignInAsync();

        var request = prompt.LastRequest!;
        Assert.Contains("/id/api/redirect", request.StartUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(request.StartUrl, request.HarvestUrl);

        // And no state, because there is no authorization request to bind. A
        // state demanded back from a flow that never sent one would fail every
        // redirect on this path.
        Assert.Null(request.ExpectedState);
        Assert.Equal(
            AuthStateVerification.NotRequired,
            AuthFlowPolicy.For(request).VerifyState(
                new Uri("https://localhost/launcher/authorized?code=X")));
    }

    [Fact]
    public async Task No_signed_in_account_is_reported_as_itself_not_as_a_broken_capture()
    {
        // The two have opposite remedies — "complete the sign-in" versus "the
        // page changed, use the manual flow" — so they must not collapse into
        // one failure.
        var browser = new ScriptedPrompt(
            "browser", available: true, _ => AuthCodeResult.NoSession("nobody signed in"));

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser]);

        var result = await host.SignIn.SignInAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(EpicSignInFailure.NoAuthenticatedSession, result.Failure);
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task No_signed_in_account_still_falls_through_to_the_console_peer()
    {
        // Unlike a cancel. The console flow sends the user to their OWN browser,
        // where they are very likely already signed in — so it can succeed
        // precisely where the embedded one found no session.
        var browser = new ScriptedPrompt(
            "browser", available: true, _ => AuthCodeResult.NoSession("nobody signed in"));
        var console = Captures("console", AuthCodeKind.AuthorizationCode, "PASTED-CODE");

        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), prompts: [browser, console]);

        Assert.True((await host.SignIn.SignInAsync()).Succeeded);
        Assert.Equal(1, console.Calls);
    }

    [Fact]
    public void The_captured_result_never_prints_its_code()
    {
        // The record ToString would print it the first time anyone interpolated
        // one of these into a log line, which is how full-account credentials
        // reach log files.
        var result = AuthCodeResult.Captured(AuthCodeKind.ExchangeCode, "SECRET-CODE-VALUE", "launcher JS bridge");

        Assert.DoesNotContain("SECRET-CODE-VALUE", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("redacted", result.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// The built-in launcher credentials: that they exist, and that they lose to
/// anything the user supplies.
/// </summary>
public class BuiltInEpicCredentialTests
{
    [Fact]
    public async Task The_built_in_pair_makes_a_fresh_install_configured()
    {
        // The point of shipping them: a sign-in button cannot ask a user for an
        // OAuth client secret, and Epic issues no client the user could get.
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(), clientId: null, clientSecret: null, builtInCredentials: true);

        Assert.True(await host.Client.IsConfiguredAsync());

        // Still not signed in — credentials are not a session.
        Assert.False(await host.Client.IsSignedInAsync());
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task A_user_supplied_pair_beats_the_built_in_one()
    {
        // The precedence rule, and the workaround for the day Epic rotates the
        // built-in pair.
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(),
            clientId: "user-client-id",
            clientSecret: "user-client-secret",
            builtInCredentials: true);

        await host.Client.SignInAsync("CODE");

        var request = Assert.Single(host.Handler.Requests, r => r.Endpoint == EpicEndpoint.Token);
        var basic = Encoding.UTF8.GetString(
            Convert.FromBase64String(request.Authorization!["Basic ".Length..]));

        Assert.Equal("user-client-id:user-client-secret", basic);
    }

    [Fact]
    public void The_built_in_source_is_registered_last()
    {
        // Order in the container IS the precedence order, so this is the
        // assertion that keeps "a user-supplied pair still wins" true.
        var services = new ServiceCollection();
        services.AddEpicWebApi();

        var sources = services
            .Where(d => d.ServiceType == typeof(IEpicCredentialSource))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Equal(typeof(BuiltInEpicCredentialSource), sources[^1]);
        Assert.True(sources.Count >= 3);
    }

    [Fact]
    public async Task The_built_in_source_yields_a_complete_pair_that_redacts_itself()
    {
        var credentials = await new BuiltInEpicCredentialSource().TryGetAsync();

        Assert.NotNull(credentials);
        Assert.Equal("built-in", credentials.Source);

        // Neither half may reach a log line through ToString — the client id
        // included, because it names which client is being impersonated.
        Assert.DoesNotContain(credentials.ClientId, credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(credentials.ClientSecret, credentials.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// The two prompt implementations, as far as they can be exercised without a
/// real browser or a real terminal.
/// </summary>
public class InteractiveAuthPromptTests
{
    private static AuthPromptRequest Request() => new()
    {
        ProviderName = "Epic Games",
        StartUrl = new Uri("https://www.epicgames.com/id/api/redirect?clientId=x&responseType=code"),
        RedirectUrl = new Uri("https://localhost/launcher/authorized"),
        ConsentNotice = "test notice",
    };

    [Fact]
    public async Task The_browser_prompt_declines_when_no_avalonia_application_is_running()
    {
        // This is the real runtime-missing shape of the fallback, and it is
        // exercisable here because a test process has no Avalonia application —
        // the same condition the console entry point runs under, which is why
        // --epic-login resolves to the console peer without selecting it.
        var prompt = new WebView2AuthPrompt(Path.Combine(Path.GetTempPath(), "winnow-tests-webview2"));

        Assert.False(await prompt.IsAvailableAsync());

        // And calling it anyway is a reason, not an exception. That matters: the
        // chain calls RequestCodeAsync on anything that reports available, and a
        // runtime can be uninstalled between the two.
        var result = await prompt.RequestCodeAsync(Request());

        Assert.Equal(AuthPromptOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Code);
    }

    [Fact]
    public void Probing_for_the_webview2_runtime_never_throws()
    {
        // GetAvailableBrowserVersionString THROWS when there is no runtime —
        // there is no TryGet form — so the throw is the answer and swallowing it
        // is the detection. Whether this machine has one is not asserted: the
        // contract is that asking is safe.
        var version = WebView2Runtime.Version;

        Assert.Equal(version is not null, WebView2Runtime.IsAvailable);

        // Memoised, so a second call cannot answer differently.
        Assert.Equal(version, WebView2Runtime.Version);
    }

    [Fact]
    public async Task The_console_prompt_is_available_when_output_is_redirected()
    {
        // xUnit redirects the standard streams, which is exactly the piped
        // invocation the console flow has to keep working under — and the case
        // where attaching to the parent console would HANG the flow instead.
        var prompt = new ConsoleAuthPrompt();

        Assert.True(await prompt.IsAvailableAsync());
    }

    [Fact]
    public void The_two_prompts_are_registered_in_fallback_order()
    {
        // Registration order is the fallback order, so this is the assertion
        // that keeps the embedded browser ahead of the console.
        var services = new ServiceCollection();
        services.AddWebViewAuthPrompt(Path.Combine(Path.GetTempPath(), "winnow-tests-webview2"));
        services.AddSingleton<IInteractiveAuthPrompt, ConsoleAuthPrompt>();

        var registered = services
            .Where(d => d.ServiceType == typeof(IInteractiveAuthPrompt))
            .ToList();

        Assert.Equal(2, registered.Count);

        // The browser is factory-built (it takes a path), so it has no
        // implementation type to assert on; the console is the second entry and
        // that is the fact that matters.
        Assert.Null(registered[0].ImplementationType);
        Assert.Equal(typeof(ConsoleAuthPrompt), registered[1].ImplementationType);
    }
}
