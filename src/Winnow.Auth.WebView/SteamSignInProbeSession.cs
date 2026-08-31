using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Winnow.Core.Auth;

namespace Winnow.Auth.WebView;

/// <summary>How a probe run ended.</summary>
public enum SteamSignInProbeOutcome
{
    /// <summary>The user signed in and a store page handed over a token.</summary>
    TokenMinted = 0,

    /// <summary>The user signed in, but no store page carried a token.</summary>
    SignedInWithoutToken = 1,

    /// <summary>Nobody ever signed in.</summary>
    NotSignedIn = 2,

    /// <summary>The window was closed before the run reached an answer.</summary>
    WindowClosed = 3,

    /// <summary>The run ran out of time.</summary>
    TimedOut = 4,

    /// <summary>There is no WebView2 runtime, or no Avalonia application to open a window in.</summary>
    BrowserUnavailable = 5,

    /// <summary>The installed runtime cannot open an off-the-record profile.</summary>
    PrivateModeUnavailable = 6,

    /// <summary>The browser failed in a way the run could not continue past.</summary>
    Failed = 7,
}

/// <summary>
/// What one probe run learned.
/// </summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Detail">A sentence naming the reason, safe to print.</param>
/// <param name="SteamId">The SteamID64 the store page reported, if it reported one.</param>
/// <param name="TokenSource">Which of the two mint routes produced the token.</param>
/// <param name="SawLoginForm">Whether a password field was ever rendered.</param>
/// <param name="Navigations">Navigations that completed on an approved origin.</param>
/// <param name="Token">
/// The minted token. NEVER log, print or persist this: the caller reads its
/// claims and spends it on three requests, and nothing else may touch it.
/// </param>
public sealed record SteamSignInProbeResult(
    SteamSignInProbeOutcome Outcome,
    string Detail,
    string? SteamId,
    string? TokenSource,
    bool SawLoginForm,
    int Navigations,
    string? Token);

/// <summary>
/// THROWAWAY VERIFICATION SCAFFOLDING — TASK-56, spike items 1 and 5.
///
/// <para>Not a feature and not a component of one. It exists to answer two
/// questions that only a live session can answer, and it is wired to a hidden
/// command-line switch and to nothing else. Delete it once
/// <c>docs/spikes/steam-web-session-auth.md</c> records what it found.</para>
///
/// <para>It reuses <see cref="WebView2Host"/> in private mode over an
/// <see cref="EphemeralBrowserProfile"/>, and tears both down in the order
/// <see cref="WebView2SteamPageHarvester"/> already established. A probe
/// against a convenient approximation would answer a question nobody asked;
/// this one exercises the exact context the shipped harvest runs in.</para>
///
/// <para>Three rules it does not bend. Navigation is gated by
/// <see cref="SteamAccountPagePolicy"/>, unchanged. No script ever runs in a
/// sign-in-journey document; the login form is on the store origin and is
/// still never read. Nothing captured is logged: the log lines carry origins
/// and counts, the token goes back to the caller in memory and nowhere
/// else.</para>
/// </summary>
public sealed class SteamSignInProbeSession
{
    /// <summary>Where the sign-in starts. Steam's own login page, on the store origin.</summary>
    public static readonly Uri LoginPage = new("https://store.steampowered.com/login/");

    /// <summary>
    /// Pages to try minting from, in order, once the user is signed in and
    /// whatever Steam redirected to has come up empty.
    ///
    /// <para>A list rather than the single page the first version steered to,
    /// because "which store page reliably carries a populated
    /// <c>webapi_token</c>" is one of the things this probe exists to find out.
    /// Each one is a route the spike names, best-evidenced first:
    /// <c>/explore/</c> is the page Playnite's shipping extension mints from
    /// (§4); <c>/replay/</c> was fetched anonymously on 2026-08-29 and verified
    /// to carry <c>data-store_user_config</c> with a <c>webapi_token</c> field
    /// (§1, route 3); <c>/points/shop/</c> is the points-summary route xPaw's
    /// documentation sends people to (§1, route 1).</para>
    /// </summary>
    public static IReadOnlyList<Uri> MintPages { get; } =
    [
        new("https://store.steampowered.com/explore/"),
        new("https://store.steampowered.com/replay/"),
        new("https://store.steampowered.com/points/shop/"),
    ];

    private static readonly TimeSpan BrowserStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many polls one page gets before the walk moves on.
    ///
    /// <para>Only ever counted after sign-in, so this is not a limit on the
    /// user: it is what turns "signed in and nothing is happening" into an
    /// answer in about fifteen seconds instead of ten minutes.</para>
    /// </summary>
    private const int MintSettleCycles = 4;

    private readonly string _profileRoot;
    private readonly ILogger _log;
    private readonly Action<string>? _progress;

    /// <param name="profileRoot">Where the throwaway profile is made. Defaults to the machine's temp directory.</param>
    /// <param name="log">Optional. Never given a token, a cookie or a document.</param>
    /// <param name="progress">
    /// Optional. Receives one heartbeat line per poll, for a caller that has a
    /// console to put it on. Carries origins, paths and states only — never a
    /// token, a query string or anything read out of a page.
    /// </param>
    public SteamSignInProbeSession(
        string? profileRoot = null,
        ILogger<SteamSignInProbeSession>? log = null,
        Action<string>? progress = null)
    {
        _profileRoot = string.IsNullOrWhiteSpace(profileRoot) ? Path.GetTempPath() : profileRoot;
        _log = log ?? NullLogger<SteamSignInProbeSession>.Instance;
        _progress = progress;
    }

    /// <summary>
    /// Opens the browser, waits for the user to sign in, and mints a token from
    /// the first store page that carries one. Never throws.
    /// </summary>
    public Task<SteamSignInProbeResult> RunAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!WebView2Runtime.IsAvailable)
        {
            return Task.FromResult(Fail(
                SteamSignInProbeOutcome.BrowserUnavailable,
                "no WebView2 runtime is installed on this machine"));
        }

        if (Application.Current is null)
        {
            return Task.FromResult(Fail(
                SteamSignInProbeOutcome.BrowserUnavailable,
                "no Avalonia application is running, so there is no window to host a browser in"));
        }

        var completion = new TaskCompletionSource<SteamSignInProbeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await RunOnUiThreadAsync(timeout, ct));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(Fail(
                    SteamSignInProbeOutcome.WindowClosed, "the probe was cancelled"));
            }
            catch (Exception ex)
            {
                // Type name only. A browser host's exception messages quote URLs.
                _log.LogWarning("The Steam sign-in probe failed ({ExceptionType}).", ex.GetType().Name);
                completion.TrySetResult(Fail(
                    SteamSignInProbeOutcome.Failed,
                    "the embedded browser could not complete the probe (" + ex.GetType().Name + ")"));
            }
        });

        return completion.Task;
    }

    /// <summary>One run's mutable state. UI-thread only.</summary>
    private sealed class RunState
    {
        public RunState(SteamAccountPagePolicy policy, Action<string> say, Action<string> note)
        {
            Policy = policy;
            Say = say;
            Note = note;
        }

        public SteamAccountPagePolicy Policy { get; }

        /// <summary>Updates the line of text under the browser.</summary>
        public Action<string> Say { get; }

        /// <summary>Writes one heartbeat line wherever the caller wants them.</summary>
        public Action<string> Note { get; }

        public bool SawSignedIn { get; set; }

        public bool SawLoginForm { get; set; }

        public string? SteamId { get; set; }

        public int Navigations { get; set; }

        /// <summary>How many of <see cref="MintPages"/> have been steered to.</summary>
        public int NextMintPage { get; set; }

        /// <summary>Polls spent on the current document. Reset whenever the path changes.</summary>
        public int CyclesOnPage { get; set; }

        /// <summary>The path last seen, so a navigation can be told from a re-poll.</summary>
        public string? LastPath { get; set; }

        /// <summary>Pages the walk steered to, for the failure message.</summary>
        public List<string> Tried { get; } = [];
    }

    private async Task<SteamSignInProbeResult> RunOnUiThreadAsync(TimeSpan timeout, CancellationToken ct)
    {
        var profile = EphemeralBrowserProfile.Create(_profileRoot, _log);
        var host = new WebView2Host(profile.Path, inPrivate: true);

        var status = new TextBlock
        {
            Classes = { "para" },
            Margin = new Thickness(16, 10, 16, 12),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Starting a private browser session…",
        };

        var window = BuildWindow(host, status);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new RunState(
            SteamAccountPagePolicy.For(0, 0),
            text => status.Text = text,
            line => _progress?.Invoke(line));

        window.Closed += (_, _) => closed.TrySetResult(true);

        using var cancelRegistration = ct.Register(
            () => Dispatcher.UIThread.Post(() => window.Close()));

        window.Show();

        try
        {
            CoreWebView2Controller controller;
            try
            {
                controller = await host.Ready.WaitAsync(BrowserStartTimeout, ct);
            }
            catch (TimeoutException)
            {
                return Fail(
                    SteamSignInProbeOutcome.Failed, "the embedded browser did not start within 30 seconds");
            }
            catch (NotSupportedException)
            {
                return Fail(
                    SteamSignInProbeOutcome.PrivateModeUnavailable,
                    "this WebView2 runtime cannot open a private browsing session, and the probe is not "
                    + "allowed to persist one");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("The embedded browser could not start ({ExceptionType}).", ex.GetType().Name);
                return Fail(
                    SteamSignInProbeOutcome.Failed,
                    "the embedded browser could not start (" + ex.GetType().Name + ")");
            }

            var browser = controller.CoreWebView2;
            Arm(browser, run);

            run.Say("Sign in to Steam. Winnow never sees your password — you are typing it into Steam's own page.");
            browser.Navigate(LoginPage.ToString());

            var probing = PollAsync(browser, run, ct);
            var finished = await Task.WhenAny(probing, closed.Task, Task.Delay(timeout, ct));

            if (finished == probing)
            {
                return await probing;
            }

            return Conclude(run, finished == closed.Task, exhausted: false);
        }
        finally
        {
            // Always, on every path, in this order. The harvester's order,
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
                // retries anyway.
            }

            await profile.DeleteAsync();
        }
    }

    /// <summary>Wires the navigation gate. The same two rules the harvest session applies.</summary>
    private void Arm(CoreWebView2 browser, RunState run)
    {
        try
        {
            browser.Settings.IsPasswordAutosaveEnabled = false;
            browser.Settings.IsGeneralAutofillEnabled = false;
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidOperationException)
        {
            _log.LogDebug(
                "Could not turn off browser autofill for the Steam sign-in probe ({ExceptionType}).",
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

            // The origin, never the URL.
            _log.LogWarning(
                "Refused to send the Steam sign-in probe to {Origin}: it is not an approved origin.",
                AuthFlowPolicy.OriginOf(uri) ?? "a non-web address");
        };

        browser.NewWindowRequested += (sender, e) =>
        {
            // Handled on every path: leaving it unhandled lets WebView2 open a
            // popup with no policy applied to it.
            e.Handled = true;

            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

            if (run.Policy.ClassifyPopup(uri) == AuthNavigationDecision.Allow)
            {
                ((CoreWebView2)sender!).Navigate(uri!.ToString());
                return;
            }

            _log.LogInformation(
                "The Steam sign-in probe refused a window the page asked to open at {Origin}.",
                AuthFlowPolicy.OriginOf(uri) ?? "a non-web address");
        };

        browser.NavigationCompleted += (sender, e) =>
        {
            if (e.IsSuccess && run.Policy.IsNavigableOrigin(SourceOf((CoreWebView2)sender!)))
            {
                run.Navigations++;
            }
        };
    }

    /// <summary>
    /// Asks the current document, once a second, whether it is signed in and
    /// whether it is carrying a token.
    ///
    /// <para>A poll rather than a navigation event, because Steam's store is a
    /// single-page application: the document that first carries a populated
    /// <c>application_config</c> is frequently one no navigation was reported
    /// for.</para>
    /// </summary>
    private async Task<SteamSignInProbeResult> PollAsync(
        CoreWebView2 browser, RunState run, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;

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

            // The probe's own scope, not the harvest gate. A page outside it
            // still reaches the walk below, which is the point: in the first
            // version the steer sat inside the read-succeeded branch and could
            // only fire on a page that had already answered the question.
            if (!IsMintScope(current))
            {
                run.Note(Heartbeat(
                    started, current, run, current is null ? "no address yet" : "outside the mint scope"));
            }
            else if (await ReadAsync(browser) is not { } page)
            {
                run.Note(Heartbeat(started, current, run, "read attempted; the page did not answer"));
            }
            else
            {
                run.SawLoginForm |= page.LoginForm;

                if (page.SteamId is not null)
                {
                    run.SteamId = page.SteamId;
                }

                if (page.LoggedIn)
                {
                    run.SawSignedIn = true;
                }

                if (page.Token is { Length: > 0 })
                {
                    run.Note(Heartbeat(started, current, run, "read attempted; TOKEN FOUND"));
                    run.Say("Signed in. A token was minted; running the three endpoint checks…");

                    // Origin and route. Never the token, and never the page.
                    _log.LogInformation(
                        "The Steam sign-in probe minted a token from {Origin} via {Route}.",
                        run.Policy.HarvestOrigin, page.TokenSource ?? "an unnamed route");

                    return new SteamSignInProbeResult(
                        SteamSignInProbeOutcome.TokenMinted,
                        "a store page handed over a token",
                        run.SteamId,
                        page.TokenSource,
                        run.SawLoginForm,
                        run.Navigations,
                        page.Token);
                }

                run.Note(Heartbeat(
                    started,
                    current,
                    run,
                    page.LoggedIn
                        ? "read attempted; signed in, no token on this page"
                        : "read attempted; not signed in yet"));

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
                // Every candidate has had its look. Failing now beats spending
                // the rest of a ten-minute timeout re-reading the same page.
                return Conclude(run, windowClosed: false, exhausted: true);
            }

            var next = MintPages[run.NextMintPage++];
            run.Tried.Add(Describe(next));
            run.Note(Heartbeat(started, current, run, "no token here; steering to " + Describe(next)));
            browser.Navigate(next.ToString());
        }

        ct.ThrowIfCancellationRequested();
        return Conclude(run, windowClosed: false, exhausted: false);
    }

    /// <summary>
    /// One line of the running commentary.
    ///
    /// <para>It exists because the first run of this probe gave the user ten
    /// minutes of an empty console and a browser that had visibly finished
    /// signing in. A stall has to be legible while it is happening, not only
    /// once it has timed out.</para>
    ///
    /// <para>Origin and path, never the query: a post-login URL carries a
    /// <c>redir</c> and Steam's own parameters, and none of that belongs on a
    /// terminal.</para>
    /// </summary>
    private static string Heartbeat(DateTimeOffset started, Uri? current, RunState run, string what)
    {
        var elapsed = DateTimeOffset.UtcNow - started;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"  [{elapsed.Minutes:00}:{elapsed.Seconds:00}] {Describe(current),-46} {what}");
    }

    /// <param name="exhausted">
    /// True when every page in <see cref="MintPages"/> was steered to and none
    /// carried a token. That is a finding rather than a timeout, and it names
    /// the pages so the spike can record which routes were actually tried.
    /// </param>
    private static SteamSignInProbeResult Conclude(
        RunState run, bool windowClosed, bool exhausted) => run.SawSignedIn
        ? new SteamSignInProbeResult(
            SteamSignInProbeOutcome.SignedInWithoutToken,
            exhausted
                ? "signed in, but no token appeared on the page Steam landed on or on any of: "
                    + string.Join(", ", run.Tried)
                : windowClosed
                    ? "the window was closed after signing in but before a page carried a token"
                    : "the run ran out of time after signing in, with no page carrying a token",
            run.SteamId,
            TokenSource: null,
            run.SawLoginForm,
            run.Navigations,
            Token: null)
        : new SteamSignInProbeResult(
            windowClosed ? SteamSignInProbeOutcome.WindowClosed : SteamSignInProbeOutcome.TimedOut,
            windowClosed
                ? "the window was closed before anyone signed in to Steam"
                : "nobody signed in to Steam before the probe ran out of time",
            run.SteamId,
            TokenSource: null,
            run.SawLoginForm,
            run.Navigations,
            Token: null);

    private static SteamSignInProbeResult Fail(SteamSignInProbeOutcome outcome, string detail)
        => new(outcome, detail, SteamId: null, TokenSource: null,
            SawLoginForm: false, Navigations: 0, Token: null);

    /// <summary>What one document said about itself.</summary>
    private readonly record struct PageReading(
        bool LoggedIn, bool LoginForm, string? SteamId, string? Token, string? TokenSource);

    private static async Task<PageReading?> ReadAsync(CoreWebView2 browser)
    {
        string? raw;
        try
        {
            raw = await browser.ExecuteScriptAsync(SteamSignInProbeScripts.Mint);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            var root = document.RootElement;

            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            return new PageReading(
                Flag(root, "loggedIn"),
                Flag(root, "loginForm"),
                Text(root, "steamid"),
                Text(root, "token"),
                Text(root, "tokenSource"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool Flag(System.Text.Json.JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.True;

    private static string? Text(System.Text.Json.JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
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

    /// <summary>
    /// Whether the probe may read this document for a token.
    ///
    /// <para><b>The probe's own scope, deliberately not the harvest session's.</b>
    /// <see cref="SteamAccountPagePolicy.AllowsHarvest"/> admits exactly two
    /// paths, because that session exists to capture two documents and reading a
    /// third would be a widening of what the user agreed to. The probe captures
    /// no document at all: it reads one field that Valve puts on every store
    /// page, so the question it has to answer is a different one and it asks it
    /// with its own predicate. The shipped policy is left exactly as TASK-53's
    /// harvester needs it.</para>
    ///
    /// <para>Narrower than the shipped policy in the way that matters: this is a
    /// strict subset of that policy's trusted origin, so the probe can never
    /// read a document the account-page session would not already have been
    /// allowed to navigate to.</para>
    /// </summary>
    public static bool IsMintScope(Uri? uri)
        => uri is not null
            && string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "store.steampowered.com", StringComparison.OrdinalIgnoreCase)
            && !IsSignInForm(uri);

    /// <summary>
    /// Whether this address is a page the user types credentials into.
    ///
    /// <para><b>This is the harvest session's rule with one clause deliberately
    /// removed, and that clause is what stalled the first run of this probe.</b>
    /// <c>WebView2SteamPageHarvester.IsSignInJourney</c> treats the store ROOT —
    /// an empty path — as part of signing in, and it is right to: the root is a
    /// waypoint in Steam's post-login redirect, the harvester has no interest in
    /// reading it, and calling it "still signing in" is how the harvester avoids
    /// bouncing off it. The probe's situation is the exact inverse. The root is
    /// where Steam lands the user after Steam Guard, and it is one of the store
    /// pages carrying <c>application_config</c>, so it is precisely the document
    /// the probe most needs to read. Copying the clause across meant the poll
    /// refused the only page it was ever shown and waited out its whole
    /// timeout in silence.</para>
    ///
    /// <para>The login form itself is still never read. That was never the
    /// clause doing that work — the named paths below are.</para>
    /// </summary>
    private static bool IsSignInForm(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();

        return path.StartsWith("login", StringComparison.Ordinal)
            || path.StartsWith("join", StringComparison.Ordinal)
            || path.StartsWith("password", StringComparison.Ordinal)
            || path.StartsWith("twofactor", StringComparison.Ordinal)
            || path.StartsWith("mobilelogin", StringComparison.Ordinal)
            || path.StartsWith("account/security", StringComparison.Ordinal);
    }

    /// <summary>The origin and path of an address, with the query dropped. Safe to print.</summary>
    private static string Describe(Uri? uri)
        => uri is null
            ? "no address"
            : (AuthFlowPolicy.OriginOf(uri) ?? uri.Scheme + ":") + uri.AbsolutePath;

    private static Window BuildWindow(WebView2Host host, TextBlock status)
    {
        DockPanel.SetDock(status, Dock.Bottom);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(status);
        root.Children.Add(host);

        return new Window
        {
            Title = "Steam sign-in probe",
            Icon = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Icon,
            Width = 1100,
            Height = 860,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
        };
    }
}

/// <summary>
/// The one script the probe runs. Public so a test can read it.
///
/// <para>It reads exactly what Playnite reads — <c>application_config</c>'s
/// <c>data-store_user_config.webapi_token</c> and <c>data-userinfo.steamid</c> —
/// with <c>window.g_wapit</c>, the global Valve's own <c>auth_refresh.js</c>
/// keeps the token in, as the second route. It is a total function: it catches
/// its own exceptions and always answers a shape the host can parse.</para>
/// </summary>
public static class SteamSignInProbeScripts
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

                var loginForm = !!document.querySelector('input[type="password"]');

                if (!loggedIn) {
                    loggedIn = !!document.getElementById('account_pulldown')
                        || !!document.querySelector('a[href*="/logout"]');
                }

                return {
                    loggedIn: loggedIn,
                    loginForm: loginForm,
                    steamid: steamid,
                    token: token,
                    tokenSource: tokenSource
                };
            } catch (e) {
                return {
                    loggedIn: false,
                    loginForm: false,
                    steamid: null,
                    token: null,
                    tokenSource: null
                };
            }
        })();
        """;
}
