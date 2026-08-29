using System.Globalization;
using System.Text;
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
/// Captures the two Steam account pages from an embedded browser the user signs
/// in to and watches. Never throws.
///
/// <para><b>What this class is allowed to do.</b> Every trust decision is
/// <see cref="SteamAccountPagePolicy"/>'s, which is
/// <see cref="AuthFlowPolicy"/>'s origin model with a harvest tier on top, so the
/// rules can be asked directly in a test and this class is left holding wiring.
/// Four of them, and none has an exception:</para>
///
/// <list type="number">
/// <item><description>A script runs in a document only when the policy names that
/// document as one of the two pages. Not the origin, the two exact paths. The
/// login form is on the same origin and is never scripted, never read.</description></item>
/// <item><description>The window goes only where the policy approves: the store,
/// and Valve's login and support origins so that Steam Guard, a captcha and
/// account recovery work. A popup elsewhere is handed to the user's own browser
/// rather than hosted next to the session cookies.</description></item>
/// <item><description>The session is off-the-record and its profile directory is
/// deleted when the window closes. Nothing about it survives the operation, and
/// no Steam credential is ever seen by Winnow. The password is typed into
/// Steam's own page.</description></item>
/// <item><description>Nothing captured is logged. Log lines carry origins, byte
/// counts and click counts; the documents go back to the caller in memory and
/// nowhere else.</description></item>
/// </list>
///
/// <para>The user is present throughout by construction: the browser is a visible
/// window, the sign-in is theirs to perform, and the two navigations that follow
/// happen in front of them.</para>
/// </summary>
public sealed class WebView2SteamPageHarvester : ISteamAccountPageHarvester
{
    /// <summary>How long to wait for the browser to attach before giving up on it.</summary>
    private static readonly TimeSpan BrowserStartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for a load-more click to produce rows before treating it as stalled.</summary>
    private static readonly TimeSpan LoadMoreGrowthTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How often to re-count rows while waiting for a click to land.</summary>
    private static readonly TimeSpan LoadMorePollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How long to give the browser process to let go of its profile before deleting it.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Characters per slice when reading a captured document back out of the page.
    ///
    /// <para>A script result crosses WebView2's IPC as one JSON string. An account
    /// with a decade of purchases produces a document of several megabytes, which
    /// is enough to make a single return a gamble; 128k characters is not.</para>
    /// </summary>
    private const int CaptureChunkChars = 128 * 1024;

    /// <summary>
    /// How many times the flow will steer the window itself.
    ///
    /// <para>A loop guard, not a schedule. Two pages need two navigations; the
    /// rest of the allowance covers a sign-in that lands somewhere unexpected.
    /// Without a ceiling, a Steam redesign that never renders a recognised page
    /// would bounce the window forever in front of the user.</para>
    /// </summary>
    private const int MaxDeliberateNavigations = 6;

    private readonly string _profileRoot;
    private readonly ILogger _log;

    /// <param name="profileRoot">
    /// Where the throwaway browser profile is created. Defaults to the machine's
    /// temp directory. Whatever is passed, a fresh subdirectory is made per run
    /// and deleted afterwards. This is not a place anything accumulates.
    /// </param>
    /// <param name="log">Optional. Never given page content, a URL query or a credential.</param>
    public WebView2SteamPageHarvester(
        string? profileRoot = null, ILogger<WebView2SteamPageHarvester>? log = null)
    {
        _profileRoot = string.IsNullOrWhiteSpace(profileRoot) ? Path.GetTempPath() : profileRoot;
        _log = log ?? NullLogger<WebView2SteamPageHarvester>.Instance;
    }

    /// <inheritdoc/>
    public string Name => "embedded browser";

    /// <inheritdoc/>
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // The same two requirements the sign-in prompt has, and both are real
        // failures in the wild: a Windows install with no Evergreen runtime, and
        // a process with no Avalonia application to open a window in.
        if (!WebView2Runtime.IsAvailable)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(Application.Current is not null);
    }

    /// <inheritdoc/>
    public Task<SteamPageHarvestResult> HarvestAsync(
        SteamPageHarvestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Before anything else, and before any window exists. This flow signs a
        // user into Steam and reads what they bought; the record that they agreed
        // to it is the caller's to produce, and the mechanism will not proceed
        // without it.
        if (!request.ConsentGranted)
        {
            return Task.FromResult(SteamPageHarvestResult.Cancelled(
                "the user has not agreed to the Steam account-page session"));
        }

        if (!WebView2Runtime.IsAvailable)
        {
            return Task.FromResult(SteamPageHarvestResult.Unavailable(
                "no WebView2 runtime is installed on this machine"));
        }

        if (Application.Current is null)
        {
            return Task.FromResult(SteamPageHarvestResult.Unavailable(
                "no Avalonia application is running, so there is no window to host a browser in"));
        }

        // Marshalled by hand for the same reason the sign-in prompt does it: the
        // whole flow is asynchronous and lives on the UI thread, and Post with an
        // explicit TaskCompletionSource says so without depending on which
        // Func<Task<T>> overloads this Avalonia version exposes.
        var completion = new TaskCompletionSource<SteamPageHarvestResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await RunOnUiThreadAsync(request, ct));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(SteamPageHarvestResult.Cancelled("the session was cancelled"));
            }
            catch (Exception ex)
            {
                // Type name only. A browser host's exception messages quote URLs,
                // and this flow's URLs are account pages.
                _log.LogWarning(
                    "The embedded Steam page session failed ({ExceptionType}).", ex.GetType().Name);
                completion.TrySetResult(SteamPageHarvestResult.Failed(
                    "the embedded browser could not complete the session (" + ex.GetType().Name + ")"));
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
        public RunState(SteamPageHarvestRequest request, Action<string> say)
        {
            Request = request;
            Policy = SteamAccountPagePolicy.For(request);
            Say = say;
        }

        public SteamPageHarvestRequest Request { get; }

        /// <summary>Every trust decision this run makes.</summary>
        public SteamAccountPagePolicy Policy { get; }

        /// <summary>Updates the line of text under the browser. Never given page content.</summary>
        public Action<string> Say { get; }

        public Dictionary<SteamAccountPageKind, string> Captured { get; } = new();

        public TaskCompletionSource<bool> Finished { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>One page at a time. Navigations can overlap; captures must not.</summary>
        public bool Busy { get; set; }

        /// <summary>Navigations this flow performed itself, capped as a loop guard.</summary>
        public int DeliberateNavigations { get; set; }

        /// <summary>Whether an account page was ever rendered for a signed-in account.</summary>
        public bool SignedIn { get; set; }

        /// <summary>Whether an account page was ever rendered signed out. Separates "nobody signed in" from "capture broke".</summary>
        public bool SawSignedOut { get; set; }

        public int LoadMoreClicks { get; set; }

        public SteamLoadMoreDecision? LoadMoreStop { get; set; }

        /// <summary>Licences pages followed past the first and merged into the captured document.</summary>
        public int LicensesPagesWalked { get; set; }

        public SteamLoadMoreDecision? LicensesStop { get; set; }

        public bool Done => Finished.Task.IsCompleted;

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

    private async Task<SteamPageHarvestResult> RunOnUiThreadAsync(
        SteamPageHarvestRequest request, CancellationToken ct)
    {
        var profile = EphemeralBrowserProfile.Create(_profileRoot, _log);

        // Private mode AND a throwaway directory. Private mode keeps cookies,
        // history and cache off disk; the directory catches everything Chromium
        // writes regardless and takes it away at the end. Neither alone is the
        // guarantee.
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
        var run = new RunState(request, text => status.Text = text);

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
                return SteamPageHarvestResult.Failed("the embedded browser did not start within 30 seconds");
            }
            catch (NotSupportedException)
            {
                // The runtime cannot make an off-the-record profile. Refused
                // rather than degraded: a persistent Steam session is exactly
                // what this flow promised not to leave behind.
                return SteamPageHarvestResult.Unavailable(
                    "this WebView2 runtime cannot open a private browsing session, and this session is "
                    + "not allowed to persist one");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("The embedded browser could not start ({ExceptionType}).", ex.GetType().Name);
                return SteamPageHarvestResult.Failed(
                    "the embedded browser could not start (" + ex.GetType().Name + ")");
            }

            Arm(controller.CoreWebView2, run, ct);

            run.Say("Sign in to Steam. Winnow reads two pages from your account and nothing else.");
            Steer(controller.CoreWebView2, run, SteamAccountPageKind.Licenses);

            var finished = await Task.WhenAny(
                run.Finished.Task,
                closed.Task,
                Task.Delay(request.Timeout, ct));

            return Conclude(
                run,
                windowClosed: finished == closed.Task,
                timedOut: finished != closed.Task && finished != run.Finished.Task);
        }
        finally
        {
            // Always, on every path, in this order. The window closing is what
            // destroys the control, which closes the controller, which is what
            // lets the profile directory be deleted.
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

    /// <summary>Turns what happened into an outcome, without ever inspecting a document.</summary>
    private static SteamPageHarvestResult Conclude(RunState run, bool windowClosed, bool timedOut)
    {
        if (run.Captured.Count == 0)
        {
            if (!run.SignedIn)
            {
                return SteamPageHarvestResult.NoSession(windowClosed
                    ? "the window was closed before anyone signed in to Steam"
                    : "nobody signed in to Steam before the session ran out of time");
            }

            return SteamPageHarvestResult.Failed(windowClosed
                ? "the window was closed before either page could be read"
                : "neither account page could be read");
        }

        var pages = new SteamAccountPages
        {
            LicensesHtml = run.Captured.GetValueOrDefault(SteamAccountPageKind.Licenses),
            HistoryHtml = run.Captured.GetValueOrDefault(SteamAccountPageKind.PurchaseHistory),
            CapturedAt = DateTimeOffset.UtcNow,
            Source = SteamAccountPageSource.EmbeddedSession,
        };

        if (pages.IsComplete)
        {
            return SteamPageHarvestResult.Captured(
                pages, run.LoadMoreClicks, run.LoadMoreStop, run.LicensesPagesWalked, run.LicensesStop);
        }

        var why = windowClosed
            ? "the window was closed before both pages were read"
            : timedOut
                ? "the session ran out of time before both pages were read"
                : "one of the two pages could not be read";

        return SteamPageHarvestResult.Partial(
            pages, why, run.LoadMoreClicks, run.LoadMoreStop, run.LicensesPagesWalked, run.LicensesStop);
    }

    /// <summary>Wires the navigation gate and the page pipeline onto one browser session.</summary>
    private void Arm(CoreWebView2 browser, RunState run, CancellationToken ct)
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
                "Could not turn off browser autofill for the Steam session ({ExceptionType}).",
                ex.GetType().Name);
        }

        browser.NavigationStarting += (sender, e) =>
            OnNavigationStarting((CoreWebView2)sender!, run, e);

        browser.NewWindowRequested += (sender, e) =>
            OnNewWindowRequested((CoreWebView2)sender!, run, e);

        browser.NavigationCompleted += async (sender, e) =>
            await OnNavigationCompletedAsync((CoreWebView2)sender!, run, e, ct);
    }

    /// <summary>The navigation gate. Identical in shape to the sign-in prompt's, and for the same reason.</summary>
    private void OnNavigationStarting(
        CoreWebView2 browser, RunState run, CoreWebView2NavigationStartingEventArgs e)
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
            "Refused to send the Steam account-page window to {Origin}: it is not an approved origin "
            + "for this session.",
            AuthFlowPolicy.OriginOf(uri) ?? "a non-web address");
    }

    /// <summary>Handles a window the page asked to open.</summary>
    private void OnNewWindowRequested(
        CoreWebView2 browser, RunState run, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Handled on every path. Leaving it unhandled lets WebView2 open its own
        // popup, which is the one outcome with no policy applied to it.
        e.Handled = true;

        Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

        switch (run.Policy.ClassifyPopup(uri))
        {
            case AuthNavigationDecision.Allow:
                browser.Navigate(uri!.ToString());
                return;

            case AuthNavigationDecision.OpenExternally:
                _log.LogInformation(
                    "The Steam page asked to open {Origin}, which is not part of this session. Handing it "
                    + "to the default browser rather than hosting it here.",
                    AuthFlowPolicy.OriginOf(uri) ?? "another site");
                OpenExternally(uri!);
                return;

            default:
                _log.LogWarning(
                    "Refused a window the Steam page asked to open: it is not a web address.");
                return;
        }
    }

    /// <summary>
    /// The page pipeline: decide what this document is, and either read it, wait
    /// for the user, or steer on.
    /// </summary>
    private async Task OnNavigationCompletedAsync(
        CoreWebView2 browser, RunState run, CoreWebView2NavigationCompletedEventArgs e, CancellationToken ct)
    {
        if (run.Done || !e.IsSuccess || run.Busy)
        {
            return;
        }

        if (!Uri.TryCreate(browser.Source, UriKind.Absolute, out var current))
        {
            return;
        }

        // THE HARVEST GATE. Not "is this origin trusted", but "is this document
        // one of the two pages this session exists to read". Everything below runs a
        // script; nothing above it does.
        if (run.Policy.PageOf(current) is not { } kind)
        {
            OnUnrelatedPage(browser, run, current);
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
            await HarvestPageAsync(browser, run, kind, ct);
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
        }
        finally
        {
            run.Busy = false;
        }
    }

    /// <summary>
    /// Handles landing somewhere that is not one of the two pages.
    ///
    /// <para>Decided from the URL alone, deliberately: this is not a document the
    /// session may run a script in, so there is nothing to ask it. While the user
    /// is still on a sign-in page the flow waits. Once an account page has been
    /// seen signed in, a landing anywhere else means Steam's own redirect went
    /// somewhere unexpected, and the flow steers back.</para>
    /// </summary>
    private void OnUnrelatedPage(CoreWebView2 browser, RunState run, Uri current)
    {
        if (IsSignInJourney(current))
        {
            run.Say("Sign in to Steam. Winnow never sees your password. You are typing it into Steam.");
            return;
        }

        if (run.SignedIn && run.NextMissing is not null)
        {
            Advance(browser, run);
        }
    }

    /// <summary>Reads one page: check the session, exhaust the history list, capture the HTML.</summary>
    private async Task HarvestPageAsync(
        CoreWebView2 browser, RunState run, SteamAccountPageKind kind, CancellationToken ct)
    {
        if (!await IsSignedInAsync(browser))
        {
            run.SawSignedOut = true;
            run.Say("Sign in to Steam to continue.");
            return;
        }

        run.SignedIn = true;

        if (kind == SteamAccountPageKind.PurchaseHistory)
        {
            run.Say("Loading your purchase history…");
            await LoadEverythingAsync(browser, run, ct);
        }
        else
        {
            run.Say("Reading your licenses page…");
            await GatherLicensesPagesAsync(browser, run, ct);
        }

        var html = await CaptureAsync(browser, run);

        if (html is null)
        {
            _log.LogWarning(
                "The Steam {Page} page at {Origin} could not be read in full, so it was discarded.",
                kind, run.Policy.HarvestOrigin);
        }
        else
        {
            run.Captured[kind] = html;

            // Origin and size. The only two facts about a captured document that
            // are safe to write down.
            _log.LogInformation(
                "Captured the Steam {Page} page from {Origin} ({Bytes} bytes).",
                kind, run.Policy.HarvestOrigin, Encoding.UTF8.GetByteCount(html));
        }

        Advance(browser, run);
    }

    /// <summary>Moves to the next page still needed, or finishes.</summary>
    private void Advance(CoreWebView2 browser, RunState run)
    {
        if (run.NextMissing is not { } next)
        {
            run.Say("Done. Both pages have been read.");
            run.Finished.TrySetResult(true);
            return;
        }

        if (run.DeliberateNavigations >= MaxDeliberateNavigations)
        {
            _log.LogWarning(
                "Stopping the Steam account-page session after {Navigations} navigations without reaching "
                + "every page.",
                run.DeliberateNavigations);

            run.Finished.TrySetResult(true);
            return;
        }

        Steer(browser, run, next);
    }

    /// <summary>Navigates to one of the two pages. The only address this flow ever chooses.</summary>
    private void Steer(CoreWebView2 browser, RunState run, SteamAccountPageKind kind)
    {
        run.DeliberateNavigations++;
        browser.Navigate(SteamAccountPagePolicy.PageUrl(kind).ToString());
    }

    /// <summary>
    /// Clicks the purchase-history load-more control until the list is exhausted,
    /// the cap is reached, or a click stops producing rows.
    /// </summary>
    private async Task LoadEverythingAsync(CoreWebView2 browser, RunState run, CancellationToken ct)
    {
        await TryExecuteAsync(browser, SteamHarvestScripts.DefineHelpers);

        var rowsBefore = -1;

        while (!run.Done && !ct.IsCancellationRequested)
        {
            var (present, rows) = await ReadLoadMoreStateAsync(browser);
            var decision = run.Policy.ClassifyLoadMore(run.LoadMoreClicks, rowsBefore, rows, present);

            if (decision != SteamLoadMoreDecision.Continue)
            {
                run.LoadMoreStop = decision;
                break;
            }

            rowsBefore = rows;

            if (!await ClickLoadMoreAsync(browser))
            {
                run.LoadMoreStop = SteamLoadMoreDecision.Exhausted;
                break;
            }

            run.LoadMoreClicks++;
            run.Say(string.Create(
                CultureInfo.CurrentCulture,
                $"Loading your purchase history ({run.LoadMoreClicks} of at most {run.Policy.MaxLoadMoreClicks} pages)…"));

            await WaitForMoreRowsAsync(browser, rowsBefore, ct);
        }

        _log.LogInformation(
            "Expanded the Steam purchase history with {Clicks} load-more clicks; stopped because: {Reason}.",
            run.LoadMoreClicks,
            run.LoadMoreStop?.ToString() ?? "the session ended");
    }

    /// <summary>
    /// Follows the licences paginator, merging each page's rows into the live
    /// document, until the paginator runs out, the cap is reached or a page
    /// stops adding rows.
    ///
    /// <para>Verified 2026-08-29: the licences page shows a hundred licences at a
    /// time. Without this, a 979-licence account is captured as 100 licences and
    /// the parser correctly reports a truncated document, which is an honest
    /// answer to the wrong question.</para>
    /// </summary>
    private async Task GatherLicensesPagesAsync(CoreWebView2 browser, RunState run, CancellationToken ct)
    {
        await TryExecuteAsync(browser, SteamHarvestScripts.DefineHelpers);
        await TryExecuteAsync(browser, SteamHarvestScripts.LicensesWalkHelpers);

        var rowsBefore = -1;

        while (!run.Done && !ct.IsCancellationRequested)
        {
            var (hasNext, rows) = await ReadLicensesStateAsync(browser);
            var decision = run.Policy.ClassifyLicensesPage(run.LicensesPagesWalked, rowsBefore, rows, hasNext);

            if (decision != SteamLoadMoreDecision.Continue)
            {
                run.LicensesStop = decision;
                break;
            }

            rowsBefore = rows;

            if (!await StartLicensesFetchAsync(browser))
            {
                run.LicensesStop = SteamLoadMoreDecision.Exhausted;
                break;
            }

            run.LicensesPagesWalked++;
            run.Say(string.Create(
                CultureInfo.CurrentCulture,
                $"Reading your licenses page ({run.LicensesPagesWalked + 1} of at most {run.Policy.MaxLicensesPages + 1})…"));

            await WaitForLicensesFetchAsync(browser, rowsBefore, ct);
        }

        _log.LogInformation(
            "Followed the Steam licences paginator for {Pages} further pages; stopped because: {Reason}.",
            run.LicensesPagesWalked,
            run.LicensesStop?.ToString() ?? "the session ended");
    }

    /// <summary>
    /// Waits for a licences fetch to finish and its rows to land.
    ///
    /// <para>Two conditions rather than one: the fetch reporting itself done says
    /// the network is finished, and the row count says the merge actually
    /// happened. A fetch that succeeded but merged nothing is caught by the next
    /// probe as a stall.</para>
    /// </summary>
    private static async Task WaitForLicensesFetchAsync(
        CoreWebView2 browser, int rowsBefore, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + LoadMoreGrowthTimeout;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(LoadMorePollInterval, ct);

            var raw = await TryExecuteAsync(browser, SteamHarvestScripts.LicensesWalkState);
            var pending = ReadObject(raw) is { } state
                && state.TryGetProperty("pending", out var flag)
                && flag.ValueKind == JsonValueKind.True;

            if (pending)
            {
                continue;
            }

            var (_, rows) = await ReadLicensesStateAsync(browser);

            if (rows > rowsBefore)
            {
                return;
            }

            // The fetch is over and produced nothing. Waiting out the rest of the
            // deadline would only delay the stall the policy is about to declare.
            return;
        }
    }

    /// <summary>Reads whether the licences paginator offers another page, and how many rows are rendered.</summary>
    private static async Task<(bool HasNext, int Rows)> ReadLicensesStateAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.LicensesPaginatorProbe);

        if (ReadObject(raw) is not { } root)
        {
            return (false, 0);
        }

        var hasNext = root.TryGetProperty("nextUrl", out var next)
            && next.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(next.GetString());

        var rows = root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Number
            && r.TryGetInt32(out var count)
                ? count
                : 0;

        return (hasNext, rows);
    }

    private static async Task<bool> StartLicensesFetchAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.FetchNextLicensesPage);
        return string.Equals(raw?.Trim(), "true", StringComparison.Ordinal);
    }

    /// <summary>Waits for a click to actually add rows, so the next probe measures a settled page.</summary>
    private static async Task WaitForMoreRowsAsync(CoreWebView2 browser, int rowsBefore, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + LoadMoreGrowthTimeout;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(LoadMorePollInterval, ct);

            var (_, rows) = await ReadLoadMoreStateAsync(browser);

            if (rows > rowsBefore)
            {
                return;
            }
        }

        // Falling out is not a failure here. The next probe sees an unchanged row
        // count and the policy calls it stalled, which is where that judgement
        // belongs.
    }

    /// <summary>
    /// Takes the rendered document out of the page in slices.
    ///
    /// <para>An incomplete read is discarded rather than returned. A truncated
    /// HTML document parses perfectly well and silently omits whatever was cut
    /// off, which would show up as an account that owns fewer games than it
    /// does; a wrong answer is worse here than a missing one.</para>
    /// </summary>
    private async Task<string?> CaptureAsync(CoreWebView2 browser, RunState run)
    {
        var length = await ReadNumberAsync(browser, SteamHarvestScripts.BeginCapture);

        try
        {
            if (length is null or <= 0)
            {
                return null;
            }

            var builder = new StringBuilder(length.Value);

            for (var offset = 0; offset < length.Value; offset += CaptureChunkChars)
            {
                var slice = await ReadStringAsync(
                    browser, SteamHarvestScripts.Chunk(offset, CaptureChunkChars));

                if (string.IsNullOrEmpty(slice))
                {
                    break;
                }

                builder.Append(slice);
            }

            if (builder.Length != length.Value)
            {
                _log.LogWarning(
                    "Read {Read} of {Total} characters from {Origin} before the page changed underneath the "
                    + "capture.",
                    builder.Length, length.Value, run.Policy.HarvestOrigin);

                return null;
            }

            return builder.ToString();
        }
        finally
        {
            // Leaves no copy of the user's purchase history sitting in a global
            // the page's own script could reach.
            await TryExecuteAsync(browser, SteamHarvestScripts.EndCapture);
        }
    }

    /// <summary>Asks the page whether it is being rendered for a signed-in account.</summary>
    private static async Task<bool> IsSignedInAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.SignedInProbe);

        return ReadObject(raw) is { } root
            && root.TryGetProperty("signedIn", out var signedIn)
            && signedIn.ValueKind == JsonValueKind.True;
    }

    /// <summary>Reads whether a load-more control is on the page, and how much is rendered.</summary>
    private static async Task<(bool Present, int Rows)> ReadLoadMoreStateAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.LoadMoreProbe);

        if (ReadObject(raw) is not { } root)
        {
            return (false, 0);
        }

        var present = root.TryGetProperty("present", out var p) && p.ValueKind == JsonValueKind.True;
        var rows = root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Number
            && r.TryGetInt32(out var count)
                ? count
                : 0;

        return (present, rows);
    }

    private static async Task<bool> ClickLoadMoreAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.ClickLoadMore);
        return string.Equals(raw?.Trim(), "true", StringComparison.Ordinal);
    }

    /// <summary>A script result that is a JSON number.</summary>
    private static async Task<int?> ReadNumberAsync(CoreWebView2 browser, string script)
    {
        var raw = await TryExecuteAsync(browser, script);

        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Number
                && document.RootElement.TryGetInt32(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A script result that is a JSON string.</summary>
    private static async Task<string?> ReadStringAsync(CoreWebView2 browser, string script)
    {
        var raw = await TryExecuteAsync(browser, script);

        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
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
    /// A script result that is a JSON object, cloned out of the document so the
    /// element outlives the <c>using</c>.
    /// </summary>
    private static JsonElement? ReadObject(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a script and returns its raw JSON result, or null when the browser
    /// went away.
    ///
    /// <para>Callers are all in the middle of a page that may navigate, close or
    /// crash under them, and none of them has anything useful to do about it
    /// beyond stopping.</para>
    /// </summary>
    private static async Task<string?> TryExecuteAsync(CoreWebView2 browser, string script)
    {
        try
        {
            var result = await browser.ExecuteScriptAsync(script);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this address is part of signing in, as opposed to somewhere the
    /// flow should steer away from.
    ///
    /// <para>URL only. A sign-in page is not a document this session may run a
    /// script in, so there is nothing to ask it. Everything off the store origin
    /// is by construction one of Valve's login or support origins, because
    /// nothing else is navigable in the first place.</para>
    /// </summary>
    private static bool IsSignInJourney(Uri uri)
    {
        if (!string.Equals(uri.Host, "store.steampowered.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();

        return path.Length == 0
            || path.StartsWith("login", StringComparison.Ordinal)
            || path.StartsWith("join", StringComparison.Ordinal)
            || path.StartsWith("password", StringComparison.Ordinal)
            || path.StartsWith("twofactor", StringComparison.Ordinal)
            || path.StartsWith("mobilelogin", StringComparison.Ordinal)
            || path.StartsWith("account/security", StringComparison.Ordinal);
    }

    /// <summary>
    /// Hands a URL to the user's own browser. Best effort and deliberately silent
    /// on failure: the session itself is unaffected either way.
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

    private const double BrowserWidth = 1100;
    private const double BrowserHeight = 860;

    /// <summary>
    /// The window the session runs in: the browser, and one line saying what is
    /// happening.
    ///
    /// <para>Not a presentation surface. It exists because the amendment requires
    /// the user to see the browser throughout, and because a window that shows a
    /// Steam login with no explanation is a window nobody should trust. The
    /// status line carries progress and never carries page content.</para>
    /// </summary>
    private static Window BuildWindow(WebView2Host host, TextBlock status)
    {
        DockPanel.SetDock(status, Dock.Bottom);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(status);
        root.Children.Add(host);

        return new Window
        {
            Title = "Steam account pages",

            // The host application's icon, for the reason the sign-in window
            // takes it: a window showing a Steam login should say which
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
