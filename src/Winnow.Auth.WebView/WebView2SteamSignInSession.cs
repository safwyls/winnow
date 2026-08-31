using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;

namespace Winnow.Auth.WebView;

/// <summary>
/// Signs the user into Steam in an embedded browser they can see, mints a
/// <c>webapi_token</c> from the signed-in store, and — only if they separately
/// agreed to it — captures their two account pages in the same session. Never
/// throws.
///
/// <para><b>Promoted from the TASK-56 probe, whose live run against the
/// repository owner's own account is the evidence this class rests on</b>
/// (docs/spikes/steam-web-session-auth.md section 7.1): the token came from the
/// store root, via <c>application_config</c>'s
/// <c>data-store_user_config.webapi_token</c>, and its <c>sub</c> claim agreed
/// exactly with the <c>steamid</c> the page reported. Everything that verified
/// is kept — the window, the ephemeral profile, the teardown order, the poll,
/// the mint script — and everything that was scaffolding is gone.</para>
///
/// <para><b>The ordered refusals.</b> Each one is a place this class stops
/// rather than continues, and none has an exception:</para>
///
/// <list type="number">
/// <item><description><b>No consent, no window.</b> Checked before anything
/// exists, exactly as <see cref="WebView2SteamPageHarvester.HarvestAsync"/> does
/// it. The mechanism cannot grant itself permission to sign a user into their
/// Steam account and keep a credential for it.</description></item>
/// <item><description><b>No script in a sign-in document.</b> Navigation is
/// gated by <see cref="SteamAccountPagePolicy"/>, unchanged, and the mint runs
/// only where <see cref="SteamAccountPagePolicy.AllowsMint"/> says it may. The
/// login form is on the store origin and is still never read: the user's
/// password is typed into Steam's own page and Winnow never sees
/// it.</description></item>
/// <item><description><b>No session when the identities disagree.</b> The token
/// is decoded locally — no network, no signature validation — and if the page's
/// <c>steamid</c> and the token's <c>sub</c> name different accounts the token
/// is dropped and the sign-in fails. A credential whose owner is in doubt is
/// worse than none, because everything downstream would file another person's
/// library under this user's account.</description></item>
/// <item><description><b>No refresh token unless the user asked to stay signed
/// in.</b> And when they did, what is reported is what was actually found;
/// see <see cref="SteamSignInResult.RefreshTokenCaptured"/>.</description></item>
/// <item><description><b>No account page unless the user agreed to that
/// separately.</b> With <see cref="SteamSignInRequest.CapturePurchaseHistory"/>
/// false, the two pages are never navigated to, never scripted and never read,
/// and the sign-in is complete and fully functional without
/// them.</description></item>
/// </list>
///
/// <para>Nothing captured is logged. Log lines carry origins, counts and
/// outcomes; the tokens go back to the caller in memory, and the only place they
/// are permitted to land is the DPAPI-protected session store.</para>
/// </summary>
public sealed class WebView2SteamSignInSession : ISteamSignInSession
{
    /// <summary>Where the sign-in starts. Steam's own login page, on the store origin.</summary>
    public static readonly Uri LoginPage = new("https://store.steampowered.com/login/");

    /// <summary>
    /// Pages to steer to, in order, once the user is signed in and whatever
    /// Steam redirected to has come up empty.
    ///
    /// <para>Steam lands the user on the store root after Steam Guard, and the
    /// root carries <c>application_config</c>, so in the verified run none of
    /// these was needed. They are the fallback, best-evidenced first:
    /// <c>/explore/</c> is the page Playnite's shipping extension mints from
    /// (spike section 4); <c>/replay/</c> was verified on 2026-08-29 to carry
    /// <c>data-store_user_config</c> with a <c>webapi_token</c> field (section 1,
    /// route 3); <c>/points/shop/</c> is the points-summary route xPaw's
    /// documentation names (section 1, route 1).</para>
    /// </summary>
    public static IReadOnlyList<Uri> MintPages { get; } =
    [
        new("https://store.steampowered.com/explore/"),
        new("https://store.steampowered.com/replay/"),
        new("https://store.steampowered.com/points/shop/"),
    ];

    /// <summary>
    /// The cookie the refresh token lives in, and the origin it is scoped to.
    ///
    /// <para>Read through <see cref="CoreWebView2.CookieManager"/> rather than
    /// out of a document, because it is <c>httpOnly</c>: no script can see it,
    /// and the mint script is never asked to try. The cookie manager is a
    /// browser-process API and is not bound by that flag.</para>
    /// </summary>
    private const string RefreshCookieName = "steamRefresh_steam";

    private static readonly Uri RefreshCookieOrigin = new("https://login.steampowered.com/");

    private static readonly TimeSpan BrowserStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many polls one page gets before the walk moves on.
    ///
    /// <para>Only ever counted after sign-in, so this is not a limit on the user:
    /// it is what turns "signed in and nothing is happening" into an answer in
    /// about fifteen seconds instead of the whole timeout.</para>
    /// </summary>
    private const int MintSettleCycles = 4;

    /// <summary>
    /// How many times the session will steer the window itself once a token is
    /// held. A loop guard, not a schedule: two pages need two navigations and the
    /// rest covers a redirect landing somewhere unexpected.
    /// </summary>
    private const int MaxDeliberateNavigations = 6;

    private readonly string _profileRoot;
    private readonly ILogger _log;

    /// <param name="profileRoot">
    /// Where the throwaway browser profile is created. Defaults to the machine's
    /// temp directory. Whatever is passed, a fresh subdirectory is made per run
    /// and deleted afterwards: this is not a place anything accumulates, and the
    /// refresh token's only durable home is the encrypted session store.
    /// </param>
    /// <param name="log">Optional. Never given a token, a cookie or a document.</param>
    public WebView2SteamSignInSession(
        string? profileRoot = null, ILogger<WebView2SteamSignInSession>? log = null)
    {
        _profileRoot = string.IsNullOrWhiteSpace(profileRoot) ? Path.GetTempPath() : profileRoot;
        _log = log ?? NullLogger<WebView2SteamSignInSession>.Instance;
    }

    /// <inheritdoc/>
    public string Name => "embedded browser";

    /// <inheritdoc/>
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!WebView2Runtime.IsAvailable)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(Application.Current is not null);
    }

    /// <inheritdoc/>
    public Task<SteamSignInResult> SignInAsync(SteamSignInRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Before anything else, and before any window exists. This flow signs a
        // user into Steam and keeps a credential that re-mints access to their
        // account; the record that they agreed to it is the caller's to produce,
        // and the mechanism will not proceed without it.
        if (!request.ConsentGranted)
        {
            return Task.FromResult(SteamSignInResult.Cancelled(
                "the user has not agreed to sign in to Steam inside Winnow"));
        }

        if (!WebView2Runtime.IsAvailable)
        {
            return Task.FromResult(SteamSignInResult.Unavailable(
                "no WebView2 runtime is installed on this machine"));
        }

        if (Application.Current is null)
        {
            return Task.FromResult(SteamSignInResult.Unavailable(
                "no Avalonia application is running, so there is no window to host a browser in"));
        }

        // Marshalled by hand for the reason the harvester does it: the whole flow
        // is asynchronous and lives on the UI thread, and Post with an explicit
        // TaskCompletionSource says so without depending on which Func<Task<T>>
        // overloads this Avalonia version exposes.
        var completion = new TaskCompletionSource<SteamSignInResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await RunOnUiThreadAsync(request, ct));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(SteamSignInResult.Cancelled("the sign-in was cancelled"));
            }
            catch (Exception ex)
            {
                // Type name only. A browser host's exception messages quote URLs,
                // and a URL in this flow can carry a token.
                _log.LogWarning("The Steam sign-in failed ({ExceptionType}).", ex.GetType().Name);
                completion.TrySetResult(SteamSignInResult.Failed(
                    "the embedded browser could not complete the sign-in (" + ex.GetType().Name + ")"));
            }
        });

        return completion.Task;
    }

    /// <summary>One run's mutable state. UI-thread only: every event and every navigation is on the dispatcher.</summary>
    private sealed class RunState
    {
        public RunState(SteamSignInRequest request, ILogger log, Action<string> say, Action<bool> working)
        {
            Request = request;
            Policy = SteamAccountPagePolicy.For(request.MaxLoadMoreClicks, request.MaxLicensesPages);
            Say = say;
            Working = working;
            Reader = new SteamAccountPageReader(Policy, log, say);
        }

        public SteamSignInRequest Request { get; }

        /// <summary>Every trust decision this run makes.</summary>
        public SteamAccountPagePolicy Policy { get; }

        /// <summary>Drives the two account pages, if and only if the user agreed to that.</summary>
        public SteamAccountPageReader Reader { get; }

        /// <summary>Updates the line of text under the browser. Never given page content.</summary>
        public Action<string> Say { get; }

        /// <summary>Raises the "please wait" banner and the block on input to the browser.</summary>
        public Action<bool> Working { get; }

        public bool SawSignedIn { get; set; }

        /// <summary>The account the page reported, before any token was seen.</summary>
        public string? PageSteamId { get; set; }

        /// <summary>The minted token, once a page has handed one over.</summary>
        public string? Token { get; set; }

        /// <summary>What the token says about itself. Decoded once, locally.</summary>
        public SteamJwtClaims Claims { get; set; } = SteamJwtClaims.Unreadable;

        /// <summary>The refresh cookie's value, or null. Read once, after the mint.</summary>
        public string? RefreshToken { get; set; }

        /// <summary>How many of <see cref="MintPages"/> have been steered to.</summary>
        public int NextMintPage { get; set; }

        /// <summary>Polls spent on the current document. Reset whenever the path changes.</summary>
        public int CyclesOnPage { get; set; }

        /// <summary>The path last seen, so a navigation can be told from a re-poll.</summary>
        public string? LastPath { get; set; }

        /// <summary>Pages the walk steered to, for the failure message.</summary>
        public List<string> Tried { get; } = [];

        /// <summary>Documents captured after the mint. Empty unless the capture was consented to.</summary>
        public Dictionary<SteamAccountPageKind, string> Captured { get; } = new();

        /// <summary>Navigations this flow performed itself during the capture, capped as a loop guard.</summary>
        public int DeliberateNavigations { get; set; }

        /// <summary>One page at a time. Navigations can overlap; captures must not.</summary>
        public bool Busy { get; set; }

        public TaskCompletionSource<bool> CaptureFinished { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The next page still needed, in policy order, or null when both are held.</summary>
        public SteamAccountPageKind? NextMissing
        {
            get
            {
                foreach (var kind in SteamAccountPagePolicy.Pages)
                {
                    if (!Captured.ContainsKey(kind))
                    {
                        return kind;
                    }
                }

                return null;
            }
        }
    }

    private async Task<SteamSignInResult> RunOnUiThreadAsync(SteamSignInRequest request, CancellationToken ct)
    {
        var profile = EphemeralBrowserProfile.Create(_profileRoot, _log);

        // Private mode AND a throwaway directory, exactly as the harvester does
        // it. Private mode keeps cookies, history and cache off disk; the
        // directory catches whatever Chromium writes regardless and takes it
        // away at the end. Neither alone is the guarantee, and the refresh token
        // this session may capture makes that guarantee matter more, not less.
        var host = new WebView2Host(profile.Path, inPrivate: true);

        var status = new TextBlock
        {
            Classes = { "para" },
            Margin = new Thickness(16, 10, 16, 12),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Starting a private browser session…",
        };

        var banner = BuildBanner();
        var window = BuildWindow(host, banner, status);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = new RunState(
            request,
            _log,
            text => status.Text = text,
            working =>
            {
                banner.IsVisible = working;

                // Both, and neither is redundant. IsHitTestVisible stops Avalonia
                // routing anything into the control; the native call is what
                // stops Windows delivering a click to the browser window inside
                // it.
                host.IsHitTestVisible = !working;
                host.SetInputEnabled(!working);
            });

        window.Closed += (_, _) => closed.TrySetResult(true);

        // Cancelled on the way out, on every path. Without it the poll below
        // would go on asking a destroyed browser where it is, once a second,
        // for the rest of the process's life: the caller's token is not
        // cancelled by a window closing or by a token being minted.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var cancelRegistration = ct.Register(
            () => Dispatcher.UIThread.Post(() => window.Close()));

        window.Show();

        // One budget for the whole session, sign-in and capture together. A
        // capture that started its own fresh timeout could double the time the
        // caller was promised.
        var deadline = DateTimeOffset.UtcNow + request.Timeout;

        try
        {
            CoreWebView2Controller controller;
            try
            {
                controller = await host.Ready.WaitAsync(BrowserStartTimeout, ct);
            }
            catch (TimeoutException)
            {
                return SteamSignInResult.Failed("the embedded browser did not start within 30 seconds");
            }
            catch (NotSupportedException)
            {
                // Refused rather than degraded: a persistent Steam session left in
                // a browser profile is exactly what this flow promised not to
                // leave behind.
                return SteamSignInResult.Unavailable(
                    "this WebView2 runtime cannot open a private browsing session, and this sign-in is "
                    + "not allowed to persist one");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("The embedded browser could not start ({ExceptionType}).", ex.GetType().Name);
                return SteamSignInResult.Failed(
                    "the embedded browser could not start (" + ex.GetType().Name + ")");
            }

            var browser = controller.CoreWebView2;
            Arm(browser, run);

            run.Say(
                "Sign in to Steam. Winnow never sees your password — you are typing it into Steam's own page.");
            Navigate(browser, LoginPage);

            var minting = MintAsync(browser, run, budget.Token);
            var finished = await Task.WhenAny(minting, closed.Task, Delay(deadline, budget.Token));

            if (finished != minting)
            {
                return Conclude(run, windowClosed: finished == closed.Task, exhausted: false);
            }

            if (await minting is { } refused)
            {
                return refused;
            }

            // A token is held and its subject has been checked. Everything from
            // here is optional and cannot take the session away.
            if (!request.CapturePurchaseHistory)
            {
                _log.LogInformation(
                    "The Steam account pages were not captured: the user did not agree to that in this "
                    + "sign-in. The session is complete without them.");

                return Signed(run);
            }

            run.Say("Signed in. Reading your licenses and purchase history…");
            await CapturePagesAsync(browser, run, closed.Task, deadline, budget.Token);

            return Signed(run);
        }
        finally
        {
            // First, so nothing is still polling a browser that is about to stop
            // existing, and so the timers above are released rather than left to
            // run out on their own.
            budget.Cancel();

            // Then always, on every path, in this order. The harvester's order,
            // unchanged. The window closing destroys the control, which closes
            // the controller, which is what lets the profile be deleted.
            window.Close();

            try
            {
                await host.Closed.WaitAsync(TeardownTimeout);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                // The browser never attached, or is taking its time. The delete
                // retries anyway, so this is a hint about how long to wait rather
                // than a precondition.
            }

            await profile.DeleteAsync();
        }
    }

    /// <summary>Wires the navigation gate. The harvester's two rules, unchanged.</summary>
    private void Arm(CoreWebView2 browser, RunState run)
    {
        // The user is about to type a Steam password into this window. An
        // ephemeral session would discard a saved password anyway; not offering
        // to save it is the difference between "discarded" and "never taken".
        try
        {
            browser.Settings.IsPasswordAutosaveEnabled = false;
            browser.Settings.IsGeneralAutofillEnabled = false;
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidOperationException)
        {
            _log.LogDebug(
                "Could not turn off browser autofill for the Steam sign-in ({ExceptionType}).",
                ex.GetType().Name);
        }

        browser.NavigationStarting += (_, e) =>
        {
            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

            if (run.Policy.ClassifyNavigation(uri) == AuthNavigationDecision.Allow)
            {
                return;
            }

            e.Cancel = true;

            // The origin, never the URL. A blocked navigation's query is the part
            // most likely to carry something that should not be written down.
            _log.LogWarning(
                "Refused to send the Steam sign-in window to {Origin}: it is not an approved origin.",
                AuthFlowPolicy.OriginOf(uri) ?? "a non-web address");
        };

        browser.NewWindowRequested += (sender, e) =>
        {
            // Handled on every path: leaving it unhandled lets WebView2 open a
            // popup with no policy applied to it.
            e.Handled = true;

            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

            switch (run.Policy.ClassifyPopup(uri))
            {
                case AuthNavigationDecision.Allow:
                    ((CoreWebView2)sender!).Navigate(uri!.ToString());
                    return;

                case AuthNavigationDecision.OpenExternally:
                    _log.LogInformation(
                        "The Steam page asked to open {Origin}, which is not part of this sign-in. Handing "
                        + "it to the default browser rather than hosting it next to the session cookies.",
                        AuthFlowPolicy.OriginOf(uri) ?? "another site");
                    OpenExternally(uri!);
                    return;

                default:
                    _log.LogWarning("Refused a window the Steam page asked to open: it is not a web address.");
                    return;
            }
        };
    }

    /// <summary>
    /// Waits for the user to sign in and for a store page to hand over a token.
    ///
    /// <para>Returns null once a token is held and accepted, and a refusal when
    /// the run reached an answer that is not a session. A poll rather than a
    /// navigation event, because Steam's store is a single-page application: the
    /// document that first carries a populated <c>application_config</c> is
    /// frequently one no navigation was reported for.</para>
    /// </summary>
    private async Task<SteamSignInResult?> MintAsync(CoreWebView2 browser, RunState run, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, ct);

            var current = SourceOf(browser);

            // A fresh document gets a fresh grace period, so a steer is never
            // charged for the second or two the navigation itself takes.
            var path = current?.AbsolutePath;
            if (!string.Equals(path, run.LastPath, StringComparison.Ordinal))
            {
                run.LastPath = path;
                run.CyclesOnPage = 0;
            }

            run.CyclesOnPage++;

            // THE MINT GATE. Not "is this origin trusted", but "may this document
            // be asked for a token". A sign-in form is on the trusted origin and
            // fails this, which is how the password page is never scripted.
            if (run.Policy.AllowsMint(current) && await ReadAsync(browser) is { } page)
            {
                if (page.SteamId is not null)
                {
                    run.PageSteamId = page.SteamId;
                }

                if (page.LoggedIn)
                {
                    run.SawSignedIn = true;
                }

                if (page.Token is { Length: > 0 } token)
                {
                    // Origin and route. Never the token, and never the page.
                    _log.LogInformation(
                        "A Steam store page at {Origin} minted a session token via {Route}.",
                        run.Policy.HarvestOrigin, page.TokenSource ?? "an unnamed route");

                    return await AcceptAsync(browser, run, token);
                }

                run.Say(page.LoggedIn
                    ? "Signed in. Looking for a store page that will mint a token…"
                    : "Sign in to Steam. Winnow never sees your password — you are typing it into Steam's own page.");
            }

            // Only once the user is actually through Steam Guard. Before that
            // there is nothing to hurry and the window is theirs.
            if (!run.SawSignedIn || run.CyclesOnPage < MintSettleCycles)
            {
                continue;
            }

            if (run.NextMintPage >= MintPages.Count)
            {
                // Every candidate has had its look. Failing now beats spending the
                // rest of the timeout re-reading the same page.
                return Conclude(run, windowClosed: false, exhausted: true);
            }

            var next = MintPages[run.NextMintPage++];
            run.Tried.Add(Describe(next));
            Navigate(browser, next);
        }

        ct.ThrowIfCancellationRequested();
        return Conclude(run, windowClosed: false, exhausted: false);
    }

    /// <summary>
    /// Decides whether a minted token becomes a session: decode it, check who it
    /// belongs to, and take the refresh token if the user asked to stay signed
    /// in. Returns null when the token was accepted.
    /// </summary>
    private async Task<SteamSignInResult?> AcceptAsync(CoreWebView2 browser, RunState run, string token)
    {
        // Locally. No network call, no signature validation: Steam decides
        // whether a token is good and does so on every request. What is read
        // here is when it dies and whose account it is.
        var claims = SteamJwtClaims.Read(token);

        // THE IDENTITY REFUSAL. The page said one account and the token says
        // another: something is wrong that no downstream code could detect, and
        // the cost of continuing is filing another person's library under this
        // user's account. Verified to agree in the live run; refused here rather
        // than assumed to always agree.
        if (!IdentitiesAgree(run.PageSteamId, claims.Subject))
        {
            // Neither id is logged. That the two disagree is the finding; naming
            // them puts two real Steam accounts in a log file.
            _log.LogWarning(
                "Refused a Steam sign-in: the store page and the minted token name different accounts.");

            return SteamSignInResult.IdentityMismatch(
                "the Steam page and the token it handed over name different accounts, so the sign-in was "
                + "refused");
        }

        run.Token = token;
        run.Claims = claims;

        if (run.Request.StaySignedIn)
        {
            run.RefreshToken = await ReadRefreshTokenAsync(browser);
        }
        else
        {
            _log.LogInformation(
                "The Steam refresh token was not read: the user did not ask to stay signed in. The session "
                + "lasts as long as its access token.");
        }

        return null;
    }

    /// <summary>
    /// Whether the account the page reported and the account the token claims
    /// are the same one.
    ///
    /// <para>Pure, and public so the rule can be asked directly rather than
    /// inferred from a live browser session. Two accounts named in one sign-in
    /// is a state nothing downstream could detect and everything downstream
    /// would act on.</para>
    ///
    /// <para><b>A missing value on either side is not a disagreement.</b> The
    /// page carries no <c>steamid</c> on some documents and a token could in
    /// principle carry no <c>sub</c>; refusing those would refuse the verified
    /// live case for lack of a fact rather than because of one. Only two present
    /// values that differ are a refusal.</para>
    /// </summary>
    public static bool IdentitiesAgree(string? pageSteamId, string? tokenSubject)
        => pageSteamId is not { Length: > 0 } page
            || tokenSubject is not { Length: > 0 } subject
            || string.Equals(page, subject, StringComparison.Ordinal);

    /// <summary>
    /// Reads <c>steamRefresh_steam</c> out of the session's cookie jar.
    ///
    /// <para><b>The cookie manager, not the document.</b> The cookie is
    /// <c>httpOnly</c> and scoped to <c>login.steampowered.com</c>, so no script
    /// on any page can see it and the mint script never tries;
    /// <see cref="CoreWebView2.CookieManager"/> runs in the browser process and
    /// is not bound by that flag.</para>
    ///
    /// <para>Absence is a normal outcome and is reported rather than papered
    /// over: Steam issues this cookie only when the user ticked "remember me" on
    /// its own login form, and that form is never scripted, so nothing here can
    /// tick it for them. Returning null gives a working sign-in that cannot be
    /// renewed, which is a state the Stores screen has to be able to
    /// describe.</para>
    /// </summary>
    private async Task<string?> ReadRefreshTokenAsync(CoreWebView2 browser)
    {
        try
        {
            var cookies = await browser.CookieManager.GetCookiesAsync(RefreshCookieOrigin.ToString());

            foreach (var cookie in cookies)
            {
                if (string.Equals(cookie.Name, RefreshCookieName, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(cookie.Value))
                {
                    // The name and the fact, never the value.
                    _log.LogInformation(
                        "Captured the Steam refresh token from {Origin}. It is held in memory and written "
                        + "only to the encrypted session store.",
                        AuthFlowPolicy.OriginOf(RefreshCookieOrigin));

                    return cookie.Value;
                }
            }

            _log.LogInformation(
                "No Steam refresh token was present in this session, so it cannot be renewed unattended. "
                + "Steam issues one only when \"remember me\" is chosen on its own sign-in form.");

            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or NotImplementedException
            or System.Runtime.InteropServices.COMException)
        {
            // Type name only, and never a failure of the sign-in: an access
            // token with no refresh token is a complete, usable session.
            _log.LogWarning(
                "Could not read the Steam refresh token from this session ({ExceptionType}). The sign-in "
                + "stands; it cannot be renewed unattended.",
                ex.GetType().Name);

            return null;
        }
    }

    /// <summary>
    /// Captures the two account pages, and is reached only when the user agreed
    /// to that separately.
    ///
    /// <para>Runs after the mint rather than instead of it, so a capture that
    /// fails or is closed halfway cannot cost the user the sign-in they already
    /// completed. The reading itself is
    /// <see cref="SteamAccountPageReader"/>'s — the same pipeline the shipped
    /// account-page session uses, so there is one implementation of "exhaust the
    /// list, then take the document" and one set of truncation counters.</para>
    /// </summary>
    private async Task CapturePagesAsync(
        CoreWebView2 browser, RunState run, Task closed, DateTimeOffset deadline, CancellationToken ct)
    {
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            => _ = OnCaptureNavigationAsync((CoreWebView2)sender!, run, e, ct);

        browser.NavigationCompleted += OnNavigationCompleted;

        try
        {
            Steer(browser, run, SteamAccountPageKind.Licenses);

            await Task.WhenAny(run.CaptureFinished.Task, closed, Delay(deadline, ct));
        }
        finally
        {
            browser.NavigationCompleted -= OnNavigationCompleted;
            run.Working(false);
        }
    }

    /// <summary>
    /// What is left of the session's one budget.
    ///
    /// <para>Never negative: a sign-in that used the whole allowance leaves a
    /// capture that finishes immediately rather than one that starts a timer for
    /// a moment already past.</para>
    /// </summary>
    private static Task Delay(DateTimeOffset deadline, CancellationToken ct)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;

        return Task.Delay(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining, ct);
    }

    /// <summary>The capture pipeline: is this one of the two pages, and if so read it.</summary>
    private async Task OnCaptureNavigationAsync(
        CoreWebView2 browser, RunState run, CoreWebView2NavigationCompletedEventArgs e, CancellationToken ct)
    {
        if (run.CaptureFinished.Task.IsCompleted || !e.IsSuccess || run.Busy)
        {
            return;
        }

        if (!Uri.TryCreate(browser.Source, UriKind.Absolute, out var current))
        {
            return;
        }

        // THE HARVEST GATE, unchanged and deliberately not the mint gate. Not
        // "is this origin trusted" and not "may a token be read here", but "is
        // this document one of the two pages the user agreed to hand over".
        if (run.Policy.PageOf(current) is not { } kind)
        {
            if (run.NextMissing is not null)
            {
                Advance(browser, run);
            }

            return;
        }

        if (run.Captured.ContainsKey(kind))
        {
            Advance(browser, run);
            return;
        }

        run.Busy = true;

        try
        {
            run.Working(true);

            var html = await run.Reader.ReadAsync(
                browser, kind, () => run.CaptureFinished.Task.IsCompleted, ct);

            if (html is null)
            {
                _log.LogWarning(
                    "The Steam {Page} page at {Origin} could not be read in full, so it was discarded.",
                    kind, run.Policy.HarvestOrigin);
            }
            else
            {
                run.Captured[kind] = html;

                // Origin and size. The only two facts about a captured document
                // that are safe to write down.
                _log.LogInformation(
                    "Captured the Steam {Page} page from {Origin} ({Bytes} bytes).",
                    kind, run.Policy.HarvestOrigin, System.Text.Encoding.UTF8.GetByteCount(html));
            }

            Advance(browser, run);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or OperationCanceledException
            or System.Runtime.InteropServices.COMException)
        {
            // The browser went away mid-script, the navigation was superseded, or
            // the caller cancelled. This handler is effectively async void: it
            // must not throw, whatever happens inside it.
            _log.LogDebug("Could not read the page ({ExceptionType}).", ex.GetType().Name);

            run.Working(false);
            run.CaptureFinished.TrySetResult(true);
        }
        finally
        {
            run.Busy = false;
        }
    }

    /// <summary>Moves to the next page still needed, or finishes the capture.</summary>
    private void Advance(CoreWebView2 browser, RunState run)
    {
        if (run.NextMissing is not { } next)
        {
            run.Say("Done. Both pages have been read.");
            run.CaptureFinished.TrySetResult(true);
            return;
        }

        if (run.DeliberateNavigations >= MaxDeliberateNavigations)
        {
            _log.LogWarning(
                "Stopping the Steam account-page capture after {Navigations} navigations without reaching "
                + "every page. The sign-in itself stands.",
                run.DeliberateNavigations);

            run.CaptureFinished.TrySetResult(true);
            return;
        }

        Steer(browser, run, next);
    }

    /// <summary>
    /// Navigates to one of the two account pages.
    ///
    /// <para>The only place this class ever chooses an account-page address, and
    /// it is reached only from the consented capture. With the capture declined
    /// nothing calls it, which is what "never navigates there" means
    /// mechanically rather than as a promise.</para>
    /// </summary>
    private void Steer(CoreWebView2 browser, RunState run, SteamAccountPageKind kind)
    {
        run.DeliberateNavigations++;
        Navigate(browser, SteamAccountPagePolicy.PageUrl(kind));
    }

    /// <summary>
    /// Sends the window somewhere, and swallows a browser that has already gone.
    ///
    /// <para>Every caller is either a poll or an event handler running while the
    /// window may be closing under it, and none of them has anything useful to do
    /// about that beyond stopping. The gate is unaffected: this steers to an
    /// address the policy has already approved, and
    /// <see cref="Arm"/>'s handler still classifies the navigation.</para>
    /// </summary>
    private void Navigate(CoreWebView2 browser, Uri uri)
    {
        try
        {
            browser.Navigate(uri.ToString());
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            _log.LogDebug("Could not steer the Steam sign-in window ({ExceptionType}).", ex.GetType().Name);
        }
    }

    /// <summary>Builds the successful result out of what the run actually holds.</summary>
    private static SteamSignInResult Signed(RunState run)
    {
        var pages = run.Captured.Count == 0
            ? null
            : new SteamAccountPages
            {
                LicensesHtml = run.Captured.GetValueOrDefault(SteamAccountPageKind.Licenses),
                HistoryHtml = run.Captured.GetValueOrDefault(SteamAccountPageKind.PurchaseHistory),
                CapturedAt = DateTimeOffset.UtcNow,
                Source = SteamAccountPageSource.EmbeddedSession,
            };

        return SteamSignInResult.SignedIn(
            run.Token!,
            run.Claims.ExpiresAt,

            // The token's subject is the authority on whose session this is; the
            // page's id is what it was checked against. They agree by the time
            // this runs, and the page's value is the fallback only when the token
            // carries no subject at all.
            run.Claims.Subject ?? run.PageSteamId,
            run.Claims.Audiences,
            run.Claims.Issuer,
            run.RefreshToken,
            pages,
            run.Reader.LoadMoreClicks,
            run.Reader.LoadMoreStop,
            run.Reader.LicensesPagesWalked,
            run.Reader.LicensesStop,
            run.Request.CapturePurchaseHistory
                ? "signed in, with the account pages captured in the same session"
                : "signed in; the account pages were not captured");
    }

    /// <param name="exhausted">
    /// True when every page in <see cref="MintPages"/> was steered to and none
    /// carried a token. That is a finding rather than a timeout, and it names the
    /// pages so a support conversation can start from what was actually tried.
    /// </param>
    private static SteamSignInResult Conclude(RunState run, bool windowClosed, bool exhausted)
        => run.SawSignedIn
            ? SteamSignInResult.NoToken(
                exhausted
                    ? "signed in, but no token appeared on the page Steam landed on or on any of: "
                        + string.Join(", ", run.Tried)
                    : windowClosed
                        ? "the window was closed after signing in but before a page carried a token"
                        : "the sign-in ran out of time after signing in, with no page carrying a token")
            : SteamSignInResult.NotSignedIn(
                windowClosed
                    ? "the window was closed before anyone signed in to Steam"
                    : "nobody signed in to Steam before the sign-in ran out of time");

    /// <summary>What one document said about itself.</summary>
    private readonly record struct PageReading(
        bool LoggedIn, string? SteamId, string? Token, string? TokenSource);

    private static async Task<PageReading?> ReadAsync(CoreWebView2 browser)
    {
        var raw = await SteamAccountPageReader.TryExecuteAsync(browser, SteamSignInScripts.Mint);

        if (SteamAccountPageReader.ReadObject(raw) is not { } root)
        {
            return null;
        }

        return new PageReading(
            Flag(root, "loggedIn"),
            Text(root, "steamid"),
            Text(root, "token"),
            Text(root, "tokenSource"));
    }

    private static bool Flag(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Uri? SourceOf(CoreWebView2 browser)
    {
        try
        {
            return Uri.TryCreate(browser.Source, UriKind.Absolute, out var uri) ? uri : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>The origin and path of an address, with the query dropped. Safe to print.</summary>
    private static string Describe(Uri? uri)
        => uri is null
            ? "no address"
            : (AuthFlowPolicy.OriginOf(uri) ?? uri.Scheme + ":") + uri.AbsolutePath;

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

    /// <summary>
    /// The "please wait" strip, hidden until Winnow starts reading a page.
    ///
    /// <para>The harvester's banner and the harvester's reason: the window is a
    /// real browser and looks like one, and nothing about it says that clicking a
    /// link during the read would navigate the page out from under the capture.
    /// It never appears during the sign-in itself, which is the user's to
    /// drive.</para>
    /// </summary>
    private static Border BuildBanner() => new()
    {
        Classes = { "harvest-banner" },
        IsVisible = false,
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2A, 0x21, 0x0E)),
        Padding = new Thickness(16, 12),
        Child = new TextBlock
        {
            Classes = { "para", "lead" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF2, 0xE4, 0xC8)),
            Text = "Please wait while Winnow reads your licenses and purchase history. "
                + "Clicking in this window now would interrupt it.",
        },
    };

    private const double BrowserWidth = 1100;
    private const double BrowserHeight = 860;

    private static Window BuildWindow(WebView2Host host, Control banner, TextBlock status)
    {
        DockPanel.SetDock(banner, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(banner);
        root.Children.Add(status);
        root.Children.Add(host);

        return new Window
        {
            Title = "Sign in to Steam",

            // The host application's icon, for the reason the other two windows
            // take it: a window showing a Steam login should say which
            // application put it there.
            Icon = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Icon,

            Width = BrowserWidth,
            Height = BrowserHeight,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
        };
    }
}

/// <summary>
/// The one script the sign-in runs. Public so a test can read it.
///
/// <para>It reads exactly what Playnite reads — <c>application_config</c>'s
/// <c>data-store_user_config.webapi_token</c> and <c>data-userinfo.steamid</c> —
/// with <c>window.g_wapit</c>, the global Valve's own <c>auth_refresh.js</c>
/// keeps the token in, as the second route. It is a total function: it catches
/// its own exceptions and always answers a shape the host can parse.</para>
///
/// <para>It runs only in a document
/// <see cref="SteamAccountPagePolicy.AllowsMint"/> has approved, which is never a
/// sign-in form, and it reads two fields rather than a document. Nothing it
/// returns is logged.</para>
/// </summary>
public static class SteamSignInScripts
{
    /// <summary>Asks the document whether it is signed in and whether it is carrying a token.</summary>
    public const string Mint = """
        (function () {
            try {
                var token = null;
                var tokenSource = null;
                var steamid = null;
                var loggedIn = false;

                var config = document.getElementById('application_config');
                if (config) {
                    try {
                        var store = JSON.parse(config.getAttribute('data-store_user_config') || '{}');
                        if (store && typeof store.webapi_token === 'string' && store.webapi_token.length > 0) {
                            token = store.webapi_token;
                            tokenSource = 'application_config/data-store_user_config';
                        }
                    } catch (e) { }

                    try {
                        var info = JSON.parse(config.getAttribute('data-userinfo') || '{}');
                        if (info) {
                            loggedIn = !!info.logged_in;
                            if (info.steamid) { steamid = String(info.steamid); }
                        }
                    } catch (e) { }
                }

                if (!token && typeof window.g_wapit === 'string' && window.g_wapit.length > 0) {
                    token = window.g_wapit;
                    tokenSource = 'window.g_wapit';
                }

                if (!loggedIn) {
                    loggedIn = !!document.getElementById('account_pulldown')
                        || !!document.querySelector('a[href*="/logout"]');
                }

                return {
                    loggedIn: loggedIn,
                    steamid: steamid,
                    token: token,
                    tokenSource: tokenSource
                };
            } catch (e) {
                return {
                    loggedIn: false,
                    steamid: null,
                    token: null,
                    tokenSource: null
                };
            }
        })();
        """;
}
