using System.Net;
using System.Text;
using Hoard.App.Services;
using Hoard.Auth.WebView;
using Hoard.Core.Auth;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Hoard.Ingest.Epic.Web.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hoard.Tests.EpicWeb;

/// <summary>
/// The M4.6 sign-in machinery: the prompt chain, the two grants, the built-in
/// credential precedence, and the ways all of it is allowed to fail.
///
/// <para><b>What is deliberately NOT tested here.</b> Nothing in this file opens
/// a browser, and no test claims a capture route works. Whether Epic's page
/// calls <c>window.ue.signinprompt.requestexchangecodesignin</c> after a
/// successful sign-in, and whether the authenticated flow 302s to the registered
/// redirect carrying <c>?code=</c>, are both UNVERIFIED and can only be settled
/// by one real sign-in. A test that faked either would be a test asserting that
/// the fake works.</para>
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
        // quoted verbatim, and Hoard names itself as the third party it warns
        // about.
        Assert.Contains(
            "It allows full\n     access to your Epic account.",
            request.ConsentNotice,
            StringComparison.Ordinal);
        Assert.Contains("Hoard is a 3rd party service", request.ConsentNotice, StringComparison.Ordinal);

        // The single redirect Epic's launcher client accepts. Verified live that
        // the allowlist is exact — loopback and other ports are rejected — which
        // is why RFC 8252 is unavailable for Epic rather than merely worse.
        Assert.Equal(new Uri("https://localhost/launcher/authorized"), request.RedirectUrl);

        // All three capture routes armed at once, so one sign-in tests all of
        // them instead of three sign-ins burning three codes.
        Assert.Equal(AuthCaptureStrategies.All, request.Strategies);

        // Default start page is the JSON redirect endpoint, not /id/authorize:
        // the redirect route is a hypothesis, not a confirmed behaviour.
        Assert.Contains("/id/api/redirect", request.StartUrl.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_authorize_endpoint_is_opt_in_and_carries_the_redirect()
    {
        // The unverified route, behind a switch so it can be tested against a
        // real sign-in without a code change.
        var prompt = Captures("browser", AuthCodeKind.AuthorizationCode, "CODE");
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(),
            configure: o => o.UseAuthorizeEndpointForSignIn = true,
            prompts: [prompt]);

        await host.SignIn.SignInAsync();

        var start = prompt.LastRequest!.StartUrl.AbsoluteUri;
        Assert.Contains("/id/authorize", start, StringComparison.Ordinal);
        Assert.Contains(
            Uri.EscapeDataString("https://localhost/launcher/authorized"),
            start,
            StringComparison.Ordinal);
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
        var prompt = new WebView2AuthPrompt(Path.Combine(Path.GetTempPath(), "hoard-tests-webview2"));

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
        services.AddWebViewAuthPrompt(Path.Combine(Path.GetTempPath(), "hoard-tests-webview2"));
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
