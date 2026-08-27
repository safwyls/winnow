using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Hoard.Core.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace Hoard.Auth.WebView;

/// <summary>
/// Signs the user in by hosting the provider's own page in an embedded Chromium
/// window, and reading the code the moment the provider issues it.
///
/// <para><b>What this replaces, and why the replacement is structural.</b> The
/// manual flow asks the user to copy an authorization code out of a page and
/// paste it back. That code is single-use and dies within minutes, so every
/// misstep between issuing and spending it — a prompt that hung, a terminal that
/// ate the input, an environment variable that did not propagate — burns the
/// code and needs a fresh one. Reading it the instant the provider issues it
/// removes the window rather than making it easier to hit.</para>
///
/// <para><b>The shape of the flow, and the mistake it was built out of.</b> The
/// first version started on Epic's <c>id/api/redirect</c> endpoint, on the
/// reasoning that it is the page that prints a code and that the alternative
/// route was unverified. It never worked for a single user, because that
/// endpoint is an API that answers <i>for a browser that already has a
/// session</i> — and an embedded browser opens an isolated profile with no
/// cookies at all. Every first-time sign-in landed on
/// <c>{"authorizationCode":null,"exchangeCode":null,…}</c> and no login form was
/// ever rendered. The caution was right; the framing was wrong, because only one
/// of the two candidate URLs can BEGIN an unauthenticated flow. So:</para>
/// <list type="number">
///   <item><description><b>Start somewhere that renders a login form</b> —
///   confirmed to be <c>/id/authorize</c> with the registered redirect.</description></item>
///   <item><description><b>Notice that authentication finished</b>, by any of
///   several independent signals.</description></item>
///   <item><description><b>Then go and ASK for the code</b> at the harvest URL,
///   which now has a session to answer about.</description></item>
/// </list>
/// <para>Step 3 is what makes the whole thing independent of whether the
/// provider volunteers anything. The other routes all wait for the provider to
/// DO something; this one stops hoping and asks.</para>
///
/// <para><b>Four capture routes, all armed at once.</b> Their evidential
/// standing differs and the difference is deliberately preserved:</para>
/// <list type="bullet">
///   <item><description><b>Session harvest — the backbone.</b> A same-origin
///   <c>fetch</c> of the harvest URL from inside the page, repeated while the
///   browser sits on the provider's origin. Non-destructive: it never navigates
///   the user's page, so it can run while they are still typing a password, and
///   it returns nothing but "no session" until there is one.</description></item>
///   <item><description><b>The launcher JS bridge — CONFIRMED live.</b> Epic's
///   sign-in page reads <c>window.ue</c> 21 times of its own accord (measured
///   2026-08-26, identically with and without a spoofed launcher user-agent),
///   and the injected bridge was driven end to end. Yields an
///   <see cref="AuthCodeKind.ExchangeCode"/>. That the page CALLS it after a
///   successful sign-in is the part no unauthenticated probe could
///   settle.</description></item>
///   <item><description><b>Redirect interception — mechanism CONFIRMED, premise
///   UNVERIFIED.</b> <c>NavigationStarting</c> delivers the full URL including
///   the query before any connection is attempted, so an unroutable https
///   redirect needs no listener and no certificate — that half is proven. That
///   the authenticated flow actually 302s there carrying <c>?code=</c> is a
///   hypothesis. It is armed because arming it is free; nothing depends on
///   it.</description></item>
///   <item><description><b>DOM read — CONFIRMED.</b> The JSON body renders into
///   a <c>&lt;pre&gt;</c> even at <c>application/json</c>, and
///   <c>ExecuteScriptAsync</c> reads it. This is what reads the harvest page when
///   the flow navigates to it rather than fetching it.</description></item>
/// </list>
///
/// <para><b>All four together rather than in sequence</b>, for a reason that
/// survives the redesign: each route needs a whole interactive sign-in to test,
/// and codes are single-use, so trying them one at a time would make the user
/// sign in repeatedly and burn a code on every miss. The result records which
/// one fired.</para>
///
/// <para><b>The user-agent is deliberately NOT spoofed.</b> Legendary sends a
/// launcher string and the obvious move is to copy it, but the spike measured
/// identical behaviour with and without — same 21 probes, same page, same form —
/// so it is cargo cult until something authenticated says otherwise. It also
/// makes the browser more fingerprintable to Epic's bot detection, not less.
/// (<c>EpicWebOptions.UserAgent</c> still sends a launcher string on the API
/// client; that is a separate, older decision and is left alone.)</para>
///
/// <para><b>Nothing here throws.</b> Runtime missing, window closed, page
/// changed, network gone, nobody signed in — every one is an
/// <see cref="AuthCodeResult"/> with a reason, and every one leaves the existing
/// local ingest exactly as it was.</para>
/// </summary>
public sealed class WebView2AuthPrompt : IInteractiveAuthPrompt
{
    /// <summary>
    /// How often the in-page harvester asks the provider whether a session
    /// exists yet.
    ///
    /// <para>An unauthenticated answer mints NOTHING — the endpoint returns null
    /// code fields — and the harvester stops permanently on the first populated
    /// one, so exactly one code is ever issued however long the user takes.</para>
    ///
    /// <para><b>Five seconds rather than one, and bounded, because this polls
    /// Epic's own origin while the user is signing in to it.</b> Epic throttles
    /// (<c>errors.com.epicgames.common.throttled</c> is real; the thresholds are
    /// unpublished) and the one thing that must not happen is Hoard's own
    /// polling getting the sign-in it is watching throttled. Twelve requests a
    /// minute, ceasing at <see cref="MaxHarvestAttempts"/>, against a flow that
    /// also fires one immediately on every navigation.</para>
    /// </summary>
    private static readonly TimeSpan HarvestInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many times one document's harvester will ask before giving up.
    ///
    /// <para>150 × 5s ≈ 12 minutes, comfortably longer than
    /// <see cref="AuthPromptRequest.Timeout"/>'s default. A ceiling rather than a
    /// schedule: it exists so a page left open in a background window cannot poll
    /// a provider forever.</para>
    /// </summary>
    private const int MaxHarvestAttempts = 150;

    /// <summary>
    /// How many times the flow will navigate to the harvest URL, or back to the
    /// login page, before giving up.
    ///
    /// <para>A loop guard, not an expectation. Without it, a provider that
    /// answers "no session" from both URLs — which is precisely what a cold
    /// profile pointed at an API endpoint does — would bounce between them
    /// forever with a window open in front of the user.</para>
    /// </summary>
    private const int MaxDeliberateNavigations = 2;

    /// <summary>
    /// The launcher bridge, injected before any of the page's own script runs.
    ///
    /// <para>Shaped after <c>legendary/utils/webview_login.py</c>, which is the
    /// reference implementation of this mechanism. Epic's page probes for
    /// <c>window.ue.signinprompt</c> and, believing it is inside the launcher,
    /// hands the exchange code out through it.</para>
    ///
    /// <para><c>registersignincompletecallback</c> is reported too, and not as
    /// noise: the page calling it is the page saying sign-in finished, which is
    /// one of the signals that triggers the harvest step.</para>
    ///
    /// <para>Defensive in two ways that matter. It never throws into the page —
    /// a bridge that raises inside Epic's own handler could take the sign-in down
    /// with it — and it posts a structured object rather than a bare string, so
    /// the host is never guessing what a message means.</para>
    /// </summary>
    private const string BridgeScript = """
        (function () {
            function post(kind, value) {
                try { window.chrome.webview.postMessage({ kind: kind, value: value }); } catch (e) { }
            }
            window.ue = {
                signinprompt: {
                    requestexchangecodesignin: function (code) { post('exchange', code); },
                    registersignincompletecallback: function () { post('signed-in', null); }
                },
                common: {
                    launchexternalurl: function (url) { post('external', url); }
                }
            };
        })();
        """;

    /// <summary>
    /// Reads a rendered JSON body out of the page, or null when the document is
    /// not JSON.
    ///
    /// <para>The <c>contentType</c> test is what makes this provider-neutral and
    /// precise rather than "scrape every page and hope". Chromium reports
    /// <c>application/json</c> for these responses while still building a DOM
    /// around them, which is exactly the pair of facts this depends on and both
    /// were confirmed by the spike.</para>
    /// </summary>
    private const string ReadJsonBodyScript = """
        (function () {
            try {
                if (document.contentType !== 'application/json') { return null; }
                return document.body ? document.body.innerText : null;
            } catch (e) { return null; }
        })();
        """;

    /// <summary>
    /// The in-page harvester: asks the provider for a code on a timer, from the
    /// provider's own origin, without ever navigating away.
    ///
    /// <para><b>Same-origin <c>fetch</c> is what makes this safe to run during
    /// sign-in.</b> Navigating to the harvest URL to look would rip the login
    /// form out from under a user mid-password; a fetch is invisible to them.
    /// Cookies ride along because the request is same-origin, which is the whole
    /// mechanism: the moment the session cookie exists, the same request that has
    /// been answering "null" starts answering with a code.</para>
    ///
    /// <para>Guarded to one instance per document, and it stops itself on the
    /// first populated answer so that exactly one code is ever minted. Failures
    /// are swallowed — a CSP that blocks the fetch, an HTML challenge, an offline
    /// moment — because the deliberate navigation is the belt for all of
    /// them.</para>
    /// </summary>
    private const string HarvesterScriptTemplate = """
        (function () {
            if (window.__hoardHarvesting) { return; }
            window.__hoardHarvesting = true;
            var url = %URL%;
            var remaining = %ATTEMPTS%;
            var timer = null;
            function ask() {
                if (window.__hoardHarvested || remaining <= 0) {
                    if (timer) { clearInterval(timer); }
                    return;
                }
                remaining--;
                try {
                    fetch(url, { credentials: 'include', cache: 'no-store' })
                        .then(function (r) { return r.text(); })
                        .then(function (body) {
                            if (window.__hoardHarvested) { return; }
                            window.chrome.webview.postMessage({ kind: 'harvest', value: body });
                        })
                        .catch(function () { });
                } catch (e) { }
            }
            // Immediately, then on a slow timer. The immediate call is what makes
            // a completed sign-in visible on the very next navigation rather than
            // up to one interval later.
            ask();
            timer = setInterval(ask, %INTERVAL%);
        })();
        """;

    private readonly string _profileRoot;
    private readonly ILogger _log;

    /// <param name="profileRoot">
    /// Directory under which each provider gets its own Chromium profile. Must be
    /// writable — WebView2's default is beside the executable, which is read-only
    /// for an installed app.
    /// </param>
    /// <param name="log">Optional. Never given a code, a token or a URL query.</param>
    public WebView2AuthPrompt(string profileRoot, ILogger<WebView2AuthPrompt>? log = null)
    {
        _profileRoot = profileRoot;
        _log = log ?? NullLogger<WebView2AuthPrompt>.Instance;
    }

    /// <inheritdoc/>
    public string Name => "embedded browser";

    /// <inheritdoc/>
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Two independent requirements, and both are real failures in the wild:
        // a Windows install with no Evergreen runtime (Server, LTSC, a stripped
        // image, a fleet that blocks Edge updates), and a process that has no
        // Avalonia application — the console entry point runs before Avalonia
        // starts, and a window cannot be created there at all.
        if (!WebView2Runtime.IsAvailable)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(Application.Current is not null);
    }

    /// <inheritdoc/>
    public Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!WebView2Runtime.IsAvailable)
        {
            return Task.FromResult(AuthCodeResult.Unavailable(
                "no WebView2 runtime is installed on this machine"));
        }

        if (Application.Current is null)
        {
            return Task.FromResult(AuthCodeResult.Unavailable(
                "no Avalonia application is running, so there is no window to host a browser in"));
        }

        // Marshalled by hand rather than through an InvokeAsync overload: the
        // whole flow is asynchronous and lives on the UI thread, and Post with an
        // explicit TaskCompletionSource says so without depending on which
        // Func<Task<T>> overloads this Avalonia version happens to expose.
        var completion = new TaskCompletionSource<AuthCodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await RunOnUiThreadAsync(request, ct));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(AuthCodeResult.Cancelled("the sign-in was cancelled"));
            }
            catch (Exception ex)
            {
                // Type name only. A browser host's exception messages quote URLs,
                // and this flow's URLs are the ones carrying codes.
                _log.LogWarning("The embedded sign-in browser failed ({ExceptionType}).", ex.GetType().Name);
                completion.TrySetResult(AuthCodeResult.Failed(
                    "the embedded browser could not complete the sign-in (" + ex.GetType().Name + ")"));
            }
        });

        return completion.Task;
    }

    /// <summary>
    /// One run's mutable state. UI-thread only, so no synchronisation: every
    /// WebView2 event and every navigation happens on the dispatcher.
    /// </summary>
    private sealed class RunState
    {
        public RunState(AuthPromptRequest request) => Request = request;

        public AuthPromptRequest Request { get; }

        public TaskCompletionSource<AuthCodeResult> Captured { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Deliberate navigations to the harvest URL, capped as a loop guard.</summary>
        public int HarvestNavigations { get; set; }

        /// <summary>Times the flow has sent the user back to the login page after an empty harvest.</summary>
        public int ReturnsToLogin { get; set; }

        /// <summary>
        /// Whether the provider has answered "no session" at least once. Kept so
        /// the final message can say which of the two very different things went
        /// wrong: nobody signed in, or the capture broke.
        /// </summary>
        public bool SawNoSession { get; set; }

        public bool Done => Captured.Task.IsCompleted;

        public void Capture(AuthCodeKind kind, string code, string via)
            => Captured.TrySetResult(AuthCodeResult.Captured(kind, code, via));
    }

    private async Task<AuthCodeResult> RunOnUiThreadAsync(AuthPromptRequest request, CancellationToken ct)
    {
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new RunState(request);

        var host = new WebView2Host(Path.Combine(_profileRoot, Sanitize(request.ProfileKey)));
        var window = BuildConsentWindow(request, consent);

        window.Closed += (_, _) =>
        {
            closed.TrySetResult(true);
            consent.TrySetResult(false);
        };

        using var cancelRegistration = ct.Register(
            () => Dispatcher.UIThread.Post(() => window.Close()));

        window.Show();

        try
        {
            // THE CONSENT MOMENT. Nothing is navigated until the user acts on it.
            // The manual flow showed Epic's warning at the moment the user copied
            // the code; an embedded flow never shows a code, so this is where
            // that moment went. It is not a formality and it is not skippable.
            if (!await consent.Task)
            {
                return AuthCodeResult.Cancelled("the user did not accept the sign-in notice");
            }

            // The browser REPLACES the consent panel rather than being revealed
            // underneath it. A hosted HWND paints over Avalonia content
            // regardless of z-order — the classic airspace problem — so a browser
            // sharing a Panel with the notice would sit on top of the thing the
            // user has to read. Swapping the content sidesteps it entirely, and
            // has the useful side effect that the browser is not even created
            // until consent is given.
            //
            // The window was sized to the NOTICE, which is a fraction of the
            // browser's height; it is grown here, on the same line's worth of
            // work as the swap, so that the consent screen is never drawn into
            // a window sized for something else. Presentation only — nothing
            // below this point changes, and the browser is still created by the
            // very next statement.
            PrepareForBrowser(window);
            window.Content = host;

            CoreWebView2Controller controller;
            try
            {
                controller = await host.Ready.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            catch (TimeoutException)
            {
                return AuthCodeResult.Failed("the embedded browser did not start within 30 seconds");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("The embedded browser could not start ({ExceptionType}).", ex.GetType().Name);
                return AuthCodeResult.Failed("the embedded browser could not start (" + ex.GetType().Name + ")");
            }

            await ArmCaptureAsync(controller.CoreWebView2, state);

            controller.CoreWebView2.Navigate(request.StartUrl.ToString());

            var finished = await Task.WhenAny(
                state.Captured.Task,
                closed.Task,
                Task.Delay(request.Timeout, ct));

            if (finished == state.Captured.Task)
            {
                return await state.Captured.Task;
            }

            if (finished == closed.Task)
            {
                // Closing after the provider said "no session" is a different
                // story from closing at random, and the user should be told the
                // one that is true.
                return state.SawNoSession
                    ? AuthCodeResult.NoSession(
                        "the sign-in window was closed before an account was signed in")
                    : AuthCodeResult.Cancelled("the sign-in window was closed");
            }

            return AuthCodeResult.Cancelled("the sign-in was not completed in time");
        }
        finally
        {
            // Always, on every path. A left-open browser window with a
            // half-finished sign-in in it is worse than no window.
            window.Close();
        }
    }

    /// <summary>Wires every capture route onto one browser session.</summary>
    private async Task ArmCaptureAsync(CoreWebView2 browser, RunState state)
    {
        var request = state.Request;

        if (request.Strategies.HasFlag(AuthCaptureStrategies.LauncherJsBridge)
            || request.Strategies.HasFlag(AuthCaptureStrategies.SessionHarvest))
        {
            browser.WebMessageReceived += (sender, e) => OnWebMessage((CoreWebView2)sender!, state, e);
        }

        if (request.Strategies.HasFlag(AuthCaptureStrategies.LauncherJsBridge))
        {
            // Before any of the page's own script runs, on every document
            // including iframes. Registering it after navigation would lose the
            // race against a page that probes for the bridge during load — and
            // Epic's does, 21 times.
            await browser.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
        }

        if (request.Strategies.HasFlag(AuthCaptureStrategies.RedirectInterception) && request.RedirectUrl is not null)
        {
            browser.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !IsRedirectTarget(uri, request.RedirectUrl))
                {
                    return;
                }

                // Cancel unconditionally, even when there is no code to take.
                // Nothing listens on this address, so allowing the navigation
                // only buys a connection failure and an error page the user has
                // to look at.
                e.Cancel = true;

                if (ReadQueryParameter(uri, request.RedirectCodeParameter) is { } code)
                {
                    state.Capture(AuthCodeKind.AuthorizationCode, code, "redirect interception");
                    return;
                }

                // Worth a line: it means the redirect half of the hypothesis is
                // right and only the parameter name is wrong, which is a much
                // smaller thing to fix than it looks from a silent failure. The
                // URI is NOT logged — it is the object that would carry a code.
                _log.LogWarning(
                    "Reached the {Provider} redirect target with no '{Parameter}' parameter on it.",
                    request.ProviderName, request.RedirectCodeParameter);

                // The session almost certainly exists at this point, so ask for
                // the code directly rather than treating a nameless redirect as
                // the end of the road.
                TryHarvestByNavigation(browser, state, "the redirect fired without a code");
            };
        }

        browser.NavigationCompleted += async (sender, e) =>
            await OnNavigationCompletedAsync((CoreWebView2)sender!, state, e);

        // Popups. Several of Epic's alternative sign-in options (Google, Steam,
        // Xbox) open one, and a WebView2 with no handler simply drops it — the
        // button appears broken. Folding it into the same window is best effort
        // and imperfect for a flow that expects to post back to its opener, but
        // it is strictly better than nothing happening.
        browser.NewWindowRequested += (sender, e) =>
        {
            e.Handled = true;
            ((CoreWebView2)sender!).Navigate(e.Uri);
        };
    }

    /// <summary>Handles one message from the injected bridge or harvester.</summary>
    private void OnWebMessage(CoreWebView2 browser, RunState state, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (state.Done || TryReadMessage(e) is not var (kind, value))
        {
            return;
        }

        switch (kind)
        {
            case "exchange" when !string.IsNullOrWhiteSpace(value):
                state.Capture(AuthCodeKind.ExchangeCode, value!, "launcher JS bridge");
                return;

            case "signed-in":
                // The page told the launcher that sign-in completed. That is a
                // direct statement rather than an inference from a URL, so it is
                // the best trigger the flow has for going to collect the code.
                TryHarvestByNavigation(browser, state, "the page reported sign-in complete");
                return;

            case "harvest":
                ApplyBodyReading(
                    browser,
                    state,
                    AuthCodeBody.Read(value, state.Request.JsonCodeFields),
                    via: "session harvest",
                    navigateOnNoSession: false);
                return;

            default:
                // 'external' (the page asking for a URL to be opened outside) and
                // anything a page invents. A page that thinks it is talking to a
                // launcher can post whatever it likes; none of it is a code.
                return;
        }
    }

    private async Task OnNavigationCompletedAsync(
        CoreWebView2 browser, RunState state, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (state.Done || !e.IsSuccess)
        {
            return;
        }

        try
        {
            if (state.Request.Strategies.HasFlag(AuthCaptureStrategies.JsonBodyScrape)
                && state.Request.JsonCodeFields.Count > 0)
            {
                var body = UnwrapScriptResult(await browser.ExecuteScriptAsync(ReadJsonBodyScript));
                var reading = AuthCodeBody.Read(body, state.Request.JsonCodeFields);

                if (reading.Outcome != AuthCodeBodyOutcome.NotACodeBody)
                {
                    // This navigation landed ON the provider's code endpoint, so
                    // its answer is the authoritative one for this moment —
                    // including "nobody is signed in", which sends the user to a
                    // login form rather than ending the flow.
                    ApplyBodyReading(browser, state, reading, via: "JSON body", navigateOnNoSession: true);
                    return;
                }
            }

            if (!Uri.TryCreate(browser.Source, UriKind.Absolute, out var current))
            {
                return;
            }

            if (state.Request.Strategies.HasFlag(AuthCaptureStrategies.SessionHarvest)
                && state.Request.HarvestUrl is { } harvest)
            {
                if (IsSameOrigin(current, harvest))
                {
                    // Same origin, so the fetch carries the provider's cookies.
                    // Injected on every document; the script no-ops if it is
                    // already running in this one.
                    await browser.ExecuteScriptAsync(BuildHarvesterScript(harvest));
                }

                if (HasLeftTheSignInJourney(current, state.Request))
                {
                    TryHarvestByNavigation(browser, state, "the browser left the sign-in pages");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            // The browser went away mid-script, or the navigation was superseded.
            // Another route may still fire; either way this is an event handler
            // and must not throw.
            _log.LogDebug("Could not inspect the page ({ExceptionType}).", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Acts on one reading of a code-bearing body.
    ///
    /// <para><b>"No session" is handled as its own thing here</b>, which is the
    /// point of the redesign. A body with every code field null is not a failed
    /// capture — it is the provider saying nobody has signed in — and answering
    /// it by falling through to "no code captured" is what made the original
    /// flow report a symptom instead of a cause. When the flow navigated to the
    /// code endpoint to get this answer, the remedy is to put a login page back
    /// in front of the user; when it merely polled in the background, the remedy
    /// is to keep waiting, because the user is probably still typing.</para>
    /// </summary>
    private void ApplyBodyReading(
        CoreWebView2 browser, RunState state, AuthCodeBodyReading reading, string via, bool navigateOnNoSession)
    {
        switch (reading.Outcome)
        {
            case AuthCodeBodyOutcome.CodeFound when reading.Code is { Length: > 0 } code:
                // Stops the in-page harvester so no second code is ever minted.
                _ = browser.ExecuteScriptAsync("window.__hoardHarvested = true;");
                state.Capture(reading.Kind, code, via);
                return;

            case AuthCodeBodyOutcome.NoSession:
                state.SawNoSession = true;

                if (!navigateOnNoSession)
                {
                    // A background poll. Silent by design: this is the ordinary
                    // answer for every second the user spends on the login form,
                    // and logging it would produce a line every three seconds.
                    return;
                }

                if (state.ReturnsToLogin >= MaxDeliberateNavigations)
                {
                    _log.LogWarning(
                        "{Provider} reports no signed-in account after {Attempts} attempts, so no code can "
                        + "be issued. Nothing was changed.",
                        state.Request.ProviderName, state.ReturnsToLogin);

                    state.Captured.TrySetResult(AuthCodeResult.NoSession(
                        "the provider reports no signed-in account, so it will not issue a code"));
                    return;
                }

                state.ReturnsToLogin++;
                _log.LogInformation(
                    "{Provider} has no signed-in account yet; returning to the sign-in page.",
                    state.Request.ProviderName);
                browser.Navigate(state.Request.StartUrl.ToString());
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Navigates to the harvest URL to ask for a code, at most
    /// <see cref="MaxDeliberateNavigations"/> times.
    ///
    /// <para>The belt for the in-page harvester, which can be defeated by a
    /// content-security policy, an HTML challenge, or a page that never finishes
    /// loading. Deliberately capped and deliberately triggered only on a real
    /// authentication signal: navigating speculatively would take the login form
    /// away from a user who is still using it.</para>
    /// </summary>
    private void TryHarvestByNavigation(CoreWebView2 browser, RunState state, string because)
    {
        if (state.Done
            || state.Request.HarvestUrl is not { } harvest
            || !state.Request.Strategies.HasFlag(AuthCaptureStrategies.SessionHarvest)
            || state.HarvestNavigations >= MaxDeliberateNavigations)
        {
            return;
        }

        state.HarvestNavigations++;

        // Information, not Debug. This is the step the whole redesign turns on
        // and it happens at most twice, so a user running the sign-in should be
        // able to see it happen without changing a log level.
        _log.LogInformation(
            "Asking {Provider} for a code because {Reason}.", state.Request.ProviderName, because);
        browser.Navigate(harvest.ToString());
    }

    /// <summary>
    /// Whether the browser has left the provider's sign-in journey.
    ///
    /// <para>Provider-neutral and deliberately conservative: same host as the
    /// start URL, and a first path segment that differs from the start URL's. For
    /// Epic that means everything under <c>/id/</c> — the login form, MFA, the
    /// social-provider hand-offs, the authorize endpoint — counts as still
    /// signing in, and landing on <c>/account/…</c> or the store front counts as
    /// finished. Guessing wrong in the cautious direction costs nothing, because
    /// the in-page harvester is running the whole time anyway.</para>
    /// </summary>
    private static bool HasLeftTheSignInJourney(Uri current, AuthPromptRequest request)
        => string.Equals(current.Host, request.StartUrl.Host, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                FirstSegment(current), FirstSegment(request.StartUrl), StringComparison.OrdinalIgnoreCase);

    private static string FirstSegment(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? path : path[..slash];
    }

    private static bool IsSameOrigin(Uri a, Uri b)
        => string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;

    /// <summary>
    /// Builds the harvester with its URL and interval substituted in as JSON
    /// literals — which is also the escaping, since a JSON string literal is a
    /// JavaScript string literal.
    /// </summary>
    private static string BuildHarvesterScript(Uri harvestUrl)
        => HarvesterScriptTemplate
            .Replace("%URL%", JsonSerializer.Serialize(harvestUrl.ToString()), StringComparison.Ordinal)
            .Replace(
                "%INTERVAL%",
                ((int)HarvestInterval.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace(
                "%ATTEMPTS%",
                MaxHarvestAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

    /// <summary>
    /// Pulls <c>kind</c> and <c>value</c> out of a message posted by the injected
    /// scripts, or null when it is not one of ours.
    ///
    /// <para>A page that believes it is talking to a launcher can post anything
    /// it likes to the host, so nothing is trusted by shape: a message without a
    /// recognised <c>kind</c> is discarded rather than being scanned for
    /// something code-looking.</para>
    /// </summary>
    private static (string Kind, string? Value)? TryReadMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || kind.GetString() is not { Length: > 0 } name)
            {
                return null;
            }

            var value = root.TryGetProperty("value", out var raw) && raw.ValueKind == JsonValueKind.String
                ? raw.GetString()
                : null;

            return (name, value);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Unwraps an <c>ExecuteScriptAsync</c> result, which is JSON-encoded.
    ///
    /// <para>A script that returns a JSON document therefore returns a JSON
    /// STRING containing that document, and reading it takes two passes. This is
    /// the outer one; <see cref="AuthCodeBody.Read"/> does the inner one.</para>
    /// </summary>
    private static string? UnwrapScriptResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The reading measure for the notice, and the window width that follows
    /// from it.
    ///
    /// <para>720 is <c>design-system.md</c> §13 gap 5's provisional answer — the
    /// system has no reading-measure rule, the Stores panel needed one first and
    /// settled on 12/18 capped at 720px, and that gap exists precisely so the
    /// next prose surface does not invent a competing number. This is the next
    /// prose surface.</para>
    /// </summary>
    private const double NoticeMeasure = 720;

    /// <summary>Measure plus §4's 32px gutter on each side.</summary>
    private const double ConsentWidth = NoticeMeasure + 64;

    /// <summary>
    /// A ceiling, not a size. The notice fits well inside it, and past it the
    /// prose scrolls under a docked action strip rather than pushing the buttons
    /// off a short screen.
    /// </summary>
    private const double ConsentMaxHeight = 760;

    private const double BrowserWidth = 1024;
    private const double BrowserHeight = 820;

    /// <summary>
    /// The window, showing the notice and nothing else until the user accepts.
    ///
    /// <para><b>Appearance comes entirely from class names.</b> This project
    /// references Avalonia and <c>Hoard.Core</c> and nothing else — no theme, no
    /// <c>tokens.axaml</c>, no <c>Hoard.App</c> — because that quarantine is what
    /// keeps WebView2 and its Windows-only code in one leaf project (§5.1).
    /// Wiring the token dictionary in here would drag the application's theme
    /// into the auth machinery and point the dependency the wrong way. So the
    /// window is built out of structure and CLASS NAMES, and whichever
    /// application is running supplies the paint: the classes used here —
    /// <c>consent</c>, <c>consent-quote</c>, <c>consent-actions</c>,
    /// <c>display-l</c>, <c>para</c>, <c>lead</c>, <c>act primary</c>,
    /// <c>act quiet</c> — are defined in <c>Hoard.App/Themes/controls.axaml</c>
    /// and <c>tokens.axaml</c>. A host that does not merge those styles gets the
    /// theme's own defaults, which is legible; nothing here half-paints.</para>
    ///
    /// <para><b>Nothing is set as a local value that a style is meant to
    /// own.</b> A local value outranks a style setter in Avalonia, so a "safe
    /// fallback" colour written on the control here would silently win over the
    /// application's own and there would be no way to tell from the running
    /// window. Layout — margins, spacing, alignment — is the tree's business and
    /// is set here; ink, type and edges are not.</para>
    ///
    /// <para><b>The window keeps the system title bar</b>, unlike
    /// <c>MainWindow</c>, which draws its own (§9). The browser REPLACES this
    /// window's content, so a hand-drawn caption would be replaced along with
    /// it and leave a browser nobody can drag or close. Recorded as a gap rather
    /// than worked around.</para>
    /// </summary>
    private static Window BuildConsentWindow(AuthPromptRequest request, TaskCompletionSource<bool> consent)
    {
        var prose = new StackPanel
        {
            Margin = new Thickness(32, 26, 32, 26),
            MaxWidth = NoticeMeasure,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 14,
        };

        prose.Children.Add(new TextBlock
        {
            // The window title, said again where there is room to read it. Not
            // new copy: §7's rule is that this screen's words were written from
            // the spike's posture reasoning, and inventing a headline here would
            // be writing consent copy in the auth machinery.
            Text = "Sign in to " + request.ProviderName,
            Classes = { "display-l" },
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var element in RenderNotice(request.ConsentNotice))
        {
            prose.Children.Add(element);
        }

        var accept = new Button
        {
            Content = "Continue to " + request.ProviderName,
            Classes = { "act", "primary" },
            IsDefault = true,
        };
        accept.Click += (_, _) => consent.TrySetResult(true);

        var cancel = new Button
        {
            Content = "Cancel",
            Classes = { "act", "quiet" },
            IsCancel = true,
        };
        cancel.Click += (_, _) => consent.TrySetResult(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        // Cancel is declared first and so is reached first by Tab — Avalonia
        // walks declaration order (§10.7). Deliberate on a consent screen, and
        // it is also the order this window already had, so no keyboard
        // behaviour changes: Enter still fires the default button and Escape
        // still fires the cancel one.
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);

        var actions = new Border
        {
            Classes = { "consent-actions" },
            Child = buttons,
        };
        DockPanel.SetDock(actions, Dock.Bottom);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = prose,
        };

        // No ScrollBarEdgeInset here, and the reason is §9.1's own: that rule is
        // about the 8px band Windows hit-tests as HTRIGHT *because the client
        // area was extended over the decorations*. This window keeps its normal
        // frame, so the resize border is outside the client area entirely and
        // the scrollbar is reachable flush to the edge.
        var root = new DockPanel { Classes = { "consent" }, LastChildFill = true };
        root.Children.Add(actions);
        root.Children.Add(scroll);

        return new Window
        {
            Title = "Sign in to " + request.ProviderName,
            Classes = { "consent" },

            // Sized to the notice, not to the browser that comes later. The
            // window was 1024x820 for the browser's benefit from the first
            // frame, which left the consent text hugging the top of a large
            // empty window — a shape that reads as a debug dialog on the one
            // screen that must not. PrepareForBrowser grows it at the swap.
            Width = ConsentWidth,
            SizeToContent = SizeToContent.Height,
            MaxHeight = ConsentMaxHeight,
            CanResize = false,

            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
        };
    }

    /// <summary>
    /// Turns the notice into blocks, without touching a word of it.
    ///
    /// <para><b>The provider's own warning is the most important text on this
    /// screen and is drawn as its own block.</b> The notice carries it as an
    /// indented quotation, which is how it reads in a terminal; indentation in
    /// proportional type is not emphasis, it is a ragged left edge. So an
    /// indented block becomes an inset, Amber-edged field at Body L while the
    /// paragraphs around it stay at the 12/18 measure. Same words, in the order
    /// they were written.</para>
    ///
    /// <para><b>Hard line breaks inside a block are joined, blank lines are
    /// kept.</b> The notice is wrapped at about 76 columns for a console; run
    /// through a 720px proportional measure those breaks would land mid-sentence
    /// at arbitrary places. Joining the lines of one paragraph with a space and
    /// letting the layout wrap it changes the line endings and nothing else — no
    /// word is added, removed, reordered or softened, which is the whole
    /// constraint on this screen (<c>docs/spikes/epic-oauth.md</c> §1: the
    /// user's protection here is a promise rather than a structure, and the
    /// screen has to keep saying so).</para>
    ///
    /// <para><b>The two paragraphs touching the quotation take full ink</b>
    /// (<c>lead</c>) rather than the sage the rest of the prose wears: the one
    /// before it says whose warning it is, the one after it says that Hoard is
    /// the third party the warning is about. Those two sentences are the
    /// disclosure, and a screen that made them quieter while making the window
    /// prettier would have made it less honest.</para>
    ///
    /// <para>Provider-neutral: it keys off the notice's shape, not off Epic.</para>
    /// </summary>
    private static IEnumerable<Control> RenderNotice(string notice)
    {
        var blocks = ReadNoticeBlocks(notice);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].IsQuotation)
            {
                yield return new Border
                {
                    Classes = { "consent-quote" },
                    Margin = new Thickness(0, 2, 0, 4),
                    Child = new TextBlock
                    {
                        Text = blocks[i].Text,
                        Classes = { "consent-quote" },
                    },
                };
                continue;
            }

            var framesQuotation =
                (i > 0 && blocks[i - 1].IsQuotation)
                || (i + 1 < blocks.Count && blocks[i + 1].IsQuotation);

            var paragraph = new TextBlock { Text = blocks[i].Text, Classes = { "para" } };
            if (framesQuotation)
            {
                paragraph.Classes.Add("lead");
            }

            yield return paragraph;
        }
    }

    /// <summary>One block of the notice: a paragraph, or an indented quotation.</summary>
    private readonly record struct NoticeBlock(string Text, bool IsQuotation);

    /// <summary>
    /// Splits a notice on blank lines and reflows each block onto one line.
    ///
    /// <para>A block every one of whose lines is indented is a quotation. That is
    /// the only structure the notice has and the only structure read out of it —
    /// nothing here parses meaning, matches a provider, or rewrites text.</para>
    /// </summary>
    private static IReadOnlyList<NoticeBlock> ReadNoticeBlocks(string notice)
    {
        var blocks = new List<NoticeBlock>();

        foreach (var block in notice.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split("\n\n", StringSplitOptions.None))
        {
            var lines = block.Split('\n')
                .Where(line => line.Trim().Length > 0)
                .ToArray();

            if (lines.Length == 0)
            {
                continue;
            }

            var quotation = lines.All(line => line.StartsWith("  ", StringComparison.Ordinal));
            blocks.Add(new NoticeBlock(string.Join(" ", lines.Select(line => line.Trim())), quotation));
        }

        return blocks;
    }

    /// <summary>
    /// Grows the window from notice-sized to browser-sized, at the moment the
    /// browser replaces the content and not before.
    ///
    /// <para>Presentation only: it changes no gating and creates nothing. The
    /// browser is still constructed by the caller's next line, which is still
    /// the first line that runs after consent.</para>
    ///
    /// <para>It keeps the window's centre rather than its top-left, so a window
    /// that was centred on the user's screen is still centred after it grows by
    /// 240x260, and clamps into the working area so growing near an edge cannot
    /// push the browser off it.</para>
    /// </summary>
    private static void PrepareForBrowser(Window window)
    {
        var screen = window.Screens?.ScreenFromTopLevel(window);
        var scaling = screen?.Scaling ?? window.RenderScaling;

        var centreX = window.Position.X + (int)Math.Round(window.ClientSize.Width * scaling / 2);
        var centreY = window.Position.Y + (int)Math.Round(window.ClientSize.Height * scaling / 2);

        // MaxHeight first: it is 760 for the notice, and setting Height under it
        // would clamp the browser to a short window.
        window.MaxHeight = double.PositiveInfinity;
        window.SizeToContent = SizeToContent.Manual;
        window.CanResize = true;
        window.Width = BrowserWidth;
        window.Height = BrowserHeight;

        var width = (int)Math.Round(BrowserWidth * scaling);
        var height = (int)Math.Round(BrowserHeight * scaling);
        var x = centreX - (width / 2);
        var y = centreY - (height / 2);

        if (screen is { } target)
        {
            var area = target.WorkingArea;
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        }

        window.Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// Whether a navigation is heading for the registered redirect. Scheme, host
    /// and path only — the query is the payload and must not take part in the
    /// match.
    /// </summary>
    private static bool IsRedirectTarget(Uri candidate, Uri redirect)
        => string.Equals(candidate.Scheme, redirect.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Host, redirect.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                candidate.AbsolutePath.TrimEnd('/'),
                redirect.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One decoded query parameter, or null. Hand-parsed rather than through a
    /// helper so nothing constructs an intermediate collection that a debugger, a
    /// log sink or a crash dump would show the code in.
    /// </summary>
    private static string? ReadQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                continue;
            }

            if (string.Equals(Uri.UnescapeDataString(pair[..equals]), name, StringComparison.Ordinal))
            {
                var value = Uri.UnescapeDataString(pair[(equals + 1)..]);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Makes a profile key safe to be a directory name. The key comes from the
    /// caller rather than the user today, and this keeps it that way if that ever
    /// stops being true.
    /// </summary>
    private static string Sanitize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "default";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. key.Select(c => invalid.Contains(c) ? '_' : c)]);
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}
