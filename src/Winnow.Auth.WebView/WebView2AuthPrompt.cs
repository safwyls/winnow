using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Winnow.Core.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace Winnow.Auth.WebView;

/// <summary>
/// Signs the user in by hosting the provider's page in an embedded WebView2
/// window and capturing the code via four parallel routes (session harvest,
/// launcher JS bridge, redirect interception, DOM read). Never throws.
///
/// <para><b>What this class is allowed to believe.</b> Every trust decision is
/// <see cref="AuthFlowPolicy"/>'s, built once per attempt from the request, and
/// this class only wires it up — which is what makes the security model testable
/// without a browser. Four rules, and none of them has an exception:</para>
///
/// <list type="number">
/// <item><description>The launcher bridge is defined only in the top-level
/// document of a trusted origin. WebView2's injection hook cannot filter by
/// frame or origin, so the filter travels inside the script.</description></item>
/// <item><description>A web message is read only when WebView2 reports it came
/// from a trusted origin. Shape is not identity: a page that thinks it is inside
/// the launcher can post anything.</description></item>
/// <item><description>The window goes only where the policy approves. A popup
/// elsewhere is handed to the user's own browser rather than hosted next to the
/// sign-in cookies.</description></item>
/// <item><description>A code on the registered redirect is spent only after the
/// OAuth <c>state</c> comes back unchanged, matched on scheme, host, port and
/// path — with the port, because a redirect on another port is another
/// principal.</description></item>
/// </list>
/// </summary>
public sealed class WebView2AuthPrompt : IInteractiveAuthPrompt
{
    /// <summary>
    /// How often the in-page harvester asks the provider whether a session
    /// exists yet. Bounded to avoid throttling the provider's own origin.
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
        public RunState(AuthPromptRequest request)
        {
            Request = request;
            Policy = AuthFlowPolicy.For(request);
        }

        public AuthPromptRequest Request { get; }

        /// <summary>
        /// Every trust decision this run makes: which origins may hold the bridge,
        /// which may post a message, where the window may go, and whether a
        /// returned state is the one that was sent.
        /// </summary>
        public AuthFlowPolicy Policy { get; }

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
            // Before any of the page's own script runs. Registering it after
            // navigation would lose the race against a page that probes for the
            // bridge during load — and Epic's does, 21 times.
            //
            // WebView2 has no per-frame or per-origin filter on this hook: it
            // runs in every document of the session, iframes included. So the
            // filter travels inside the script, which defines nothing unless it
            // is the top frame of a document on a trusted origin. See
            // AuthBridgeScripts.
            await browser.AddScriptToExecuteOnDocumentCreatedAsync(
                AuthBridgeScripts.Bridge(state.Policy.TrustedOrigins));
        }

        // ALWAYS armed, whatever the capture strategies are: this is the
        // navigation policy, not a capture route. A window that carries the
        // launcher bridge must not be steerable onto an origin nobody approved,
        // and the redirect is intercepted from inside the same decision so a
        // single classification governs both.
        browser.NavigationStarting += (sender, e) =>
            OnNavigationStarting((CoreWebView2)sender!, state, e);

        browser.NavigationCompleted += async (sender, e) =>
            await OnNavigationCompletedAsync((CoreWebView2)sender!, state, e);

        // Popups. Several of Epic's alternative sign-in options (Google, Steam,
        // Xbox) open one, and a WebView2 with no handler simply drops it — the
        // button appears broken.
        //
        // An APPROVED destination is still folded into this window: best effort,
        // imperfect for a flow that expects to post back to its opener, and
        // strictly better than nothing happening. An unapproved one is handed to
        // the user's own browser instead of being hosted here, because hosting it
        // would put an arbitrary page inside the session that holds the sign-in
        // cookies and, before this change, the bridge.
        browser.NewWindowRequested += (sender, e) => OnNewWindowRequested((CoreWebView2)sender!, state, e);
    }

    /// <summary>
    /// The navigation gate: classifies where the window is being sent and either
    /// allows it, captures the redirect, or refuses.
    /// </summary>
    private void OnNavigationStarting(
        CoreWebView2 browser, RunState state, CoreWebView2NavigationStartingEventArgs e)
    {
        Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

        switch (state.Policy.ClassifyNavigation(uri))
        {
            case AuthNavigationDecision.Allow:
                return;

            case AuthNavigationDecision.CaptureRedirect:
                // Cancel unconditionally, even when there is nothing to take.
                // Nothing listens on this address, so allowing the navigation
                // only buys a connection failure and an error page the user has
                // to look at.
                e.Cancel = true;
                HandleRedirect(browser, state, uri!);
                return;

            default:
                e.Cancel = true;

                // The origin, never the URL: a blocked navigation's query is
                // exactly where an injected code would be sitting.
                _log.LogWarning(
                    "Refused to send the {Provider} sign-in window to {Origin}: it is not an approved "
                    + "origin for this flow.",
                    state.Request.ProviderName,
                    AuthFlowPolicy.OriginOf(uri) ?? "a non-web address");
                return;
        }
    }

    /// <summary>Handles a window the page asked to open.</summary>
    private void OnNewWindowRequested(
        CoreWebView2 browser, RunState state, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Handled on every path. Leaving it unhandled lets WebView2 open its own
        // popup window, which is the one outcome with no policy applied to it.
        e.Handled = true;

        Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

        switch (state.Policy.ClassifyPopup(uri))
        {
            case AuthNavigationDecision.Allow:
                browser.Navigate(uri!.ToString());
                return;

            case AuthNavigationDecision.CaptureRedirect:
                HandleRedirect(browser, state, uri!);
                return;

            case AuthNavigationDecision.OpenExternally:
                _log.LogInformation(
                    "The {Provider} sign-in page asked to open {Origin}, which is not part of this flow. "
                    + "Handing it to the default browser rather than hosting it here.",
                    state.Request.ProviderName,
                    AuthFlowPolicy.OriginOf(uri) ?? "another site");
                OpenExternally(uri!);
                return;

            default:
                _log.LogWarning(
                    "Refused a window the {Provider} sign-in page asked to open: it is not a web address.",
                    state.Request.ProviderName);
                return;
        }
    }

    /// <summary>
    /// Reads the registered redirect, once the navigation to it has been
    /// cancelled.
    ///
    /// <para><b>State is checked before the code is looked at, and there is no
    /// path around it.</b> A redirect carrying someone else's code is exactly
    /// what login-CSRF looks like, and the code is a full-account credential the
    /// moment it is spent.</para>
    /// </summary>
    private void HandleRedirect(CoreWebView2 browser, RunState state, Uri uri)
    {
        var request = state.Request;

        switch (state.Policy.VerifyState(uri))
        {
            case AuthStateVerification.Mismatched:
                // Not a degraded flow — a wrong answer. Something drove the
                // browser to the redirect with a state this attempt never
                // minted, so the code on it belongs to another attempt or
                // another account. Refuse, and do not go asking for a
                // replacement: the window is no longer trustworthy.
                _log.LogWarning(
                    "Discarded a {Provider} redirect whose OAuth state did not match this sign-in. "
                    + "Nothing was captured.",
                    request.ProviderName);
                return;

            case AuthStateVerification.Missing:
                // Either the provider dropped the parameter or someone crafted
                // the redirect. Both are handled the same way and neither spends
                // the code: fall through to asking the provider directly, which
                // answers for the session in THIS browser and cannot be aimed at
                // another account.
                _log.LogWarning(
                    "A {Provider} redirect arrived with no OAuth state on it, so its code was not used. "
                    + "Asking the provider for a code instead.",
                    request.ProviderName);
                TryHarvestByNavigation(browser, state, "the redirect carried no state");
                return;

            default:
                break;
        }

        if (AuthFlowPolicy.ReadQueryParameter(uri, request.RedirectCodeParameter) is { } code)
        {
            state.Capture(AuthCodeKind.AuthorizationCode, code, "redirect interception");
            return;
        }

        // Worth a line: it means the redirect half of the hypothesis is right and
        // only the parameter name is wrong, which is a much smaller thing to fix
        // than it looks from a silent failure. The URI is NOT logged — it is the
        // object that would carry a code.
        _log.LogWarning(
            "Reached the {Provider} redirect target with no '{Parameter}' parameter on it.",
            request.ProviderName, request.RedirectCodeParameter);

        // The session almost certainly exists at this point, so ask for the code
        // directly rather than treating a nameless redirect as the end of the road.
        TryHarvestByNavigation(browser, state, "the redirect fired without a code");
    }

    /// <summary>
    /// Hands a URL to the user's own browser. Best effort and deliberately silent
    /// on failure: the sign-in itself is unaffected either way.
    /// </summary>
    private void OpenExternally(Uri uri)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or FileNotFoundException)
        {
            _log.LogDebug("Could not open a link in the default browser ({ExceptionType}).", ex.GetType().Name);
        }
    }

    /// <summary>Handles one message from the injected bridge or harvester.</summary>
    private void OnWebMessage(CoreWebView2 browser, RunState state, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (state.Done)
        {
            return;
        }

        // THE ORIGIN CHECK, and it is first because everything below it treats
        // the message as coming from the provider. WebView2 reports the posting
        // document's URL — an iframe's own URL when an iframe posted it — and
        // that is the only identity a web message carries. Without this, any
        // document in the session can post {"kind":"exchange"} and have it spent.
        if (!state.Policy.AcceptsMessageFrom(e.Source))
        {
            _log.LogDebug(
                "Ignored a page message from {Origin}, which is not a trusted origin for this sign-in.",
                AuthFlowPolicy.OriginOf(e.Source) ?? "an unidentified document");
            return;
        }

        if (TryReadMessage(e) is not var (kind, value))
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
            // browser.Source is the TOP-level document, which is the only thing
            // ExecuteScriptAsync touches. Everything below reads or writes that
            // document, so an untrusted one is left entirely alone: not scraped,
            // not injected into, not treated as a signal.
            if (!Uri.TryCreate(browser.Source, UriKind.Absolute, out var current)
                || !state.Policy.IsTrustedOrigin(current))
            {
                return;
            }

            if (state.Request.Strategies.HasFlag(AuthCaptureStrategies.JsonBodyScrape)
                && state.Request.JsonCodeFields.Count > 0)
            {
                var body = UnwrapScriptResult(await browser.ExecuteScriptAsync(AuthBridgeScripts.ReadJsonBody));
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

            if (state.Request.Strategies.HasFlag(AuthCaptureStrategies.SessionHarvest)
                && state.Request.HarvestUrl is { } harvest)
            {
                if (IsSameOrigin(current, harvest))
                {
                    // Same origin, so the fetch carries the provider's cookies.
                    // The script no-ops if it is already running in this document,
                    // and refuses to run at all outside a trusted top-level one.
                    await browser.ExecuteScriptAsync(AuthBridgeScripts.Harvester(
                        harvest, state.Policy.TrustedOrigins, HarvestInterval, MaxHarvestAttempts));
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

    /// <summary>Acts on one reading of a code-bearing body.</summary>
    private void ApplyBodyReading(
        CoreWebView2 browser, RunState state, AuthCodeBodyReading reading, string via, bool navigateOnNoSession)
    {
        switch (reading.Outcome)
        {
            case AuthCodeBodyOutcome.CodeFound when reading.Code is { Length: > 0 } code:
                // Stops the in-page harvester so no second code is ever minted.
                _ = browser.ExecuteScriptAsync(AuthBridgeScripts.StopHarvesting);
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
    /// <para>720 is the provisional answer: <c>design-system.md</c> states no
    /// reading-measure rule, the Stores panel needed one first and settled on
    /// 12/18 capped at 720px, and it is reused here precisely so the next prose
    /// surface does not invent a competing number. TASK-82 is to settle it.</para>
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
    /// Builds the consent window. Appearance comes from class names only (this
    /// project has no theme reference). Keeps the system title bar; deliberately
    /// opaque.
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

            // THE HOST'S ICON, NOT A DEFAULT ONE, AND THIS SCREEN IS THE REASON.
            // Everything else here exists so the user can see who is asking for
            // their Epic session and decide; the window furniture should not be
            // the one part of it that stays anonymous. A "Sign in to Epic Games"
            // window wearing the stock Avalonia icon in the taskbar is a window
            // that does not say which application put it there, on the exact
            // screen where that is the question.
            //
            // Taken from the running application rather than from an asset,
            // because §5.1 keeps this project off Winnow.App: there is no
            // avares://Winnow/ this side of the boundary, and hard-coding a copy
            // of the mark here would be a second file to keep in step with the
            // first. This reads whatever the host window is already wearing, so
            // it follows the app's icon by construction and is simply null in a
            // host that has none.
            Icon = HostIcon(),

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
    /// The icon the host application's main window is wearing, or
    /// <see langword="null"/> if there is no desktop lifetime or no main window
    /// — a console host driving this prompt gets the platform default, which is
    /// the correct answer there.
    /// </summary>
    private static WindowIcon? HostIcon()
        => (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Icon;

    /// <summary>Turns the notice into blocks, without touching a word of it.</summary>
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

    /// <summary>Splits a notice on blank lines and reflows each block onto one line.</summary>
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
