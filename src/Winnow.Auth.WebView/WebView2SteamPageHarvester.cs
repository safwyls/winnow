using System.Text;
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

    /// <summary>How long to give the browser process to let go of its profile before deleting it.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

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
        public RunState(
            SteamPageHarvestRequest request, ILogger log, Action<string> say, Action<bool> working)
        {
            Request = request;
            Policy = SteamAccountPagePolicy.For(request);
            Say = say;
            Working = working;
            Reader = new SteamAccountPageReader(Policy, log, say);
        }

        public SteamPageHarvestRequest Request { get; }

        /// <summary>Every trust decision this run makes.</summary>
        public SteamAccountPagePolicy Policy { get; }

        /// <summary>Drives the list and takes the document. Shared with the sign-in session.</summary>
        public SteamAccountPageReader Reader { get; }

        /// <summary>Updates the line of text under the browser. Never given page content.</summary>
        public Action<string> Say { get; }

        /// <summary>
        /// Raises or lowers the "please wait" banner and, with it, the block on
        /// input to the browser. True while Winnow is reading; false whenever the
        /// window is the user's again.
        /// </summary>
        public Action<bool> Working { get; }

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

                // Both, and neither is redundant. IsHitTestVisible stops
                // Avalonia routing anything into the control; the native call is
                // what actually stops Windows delivering a click to the browser
                // window inside it.
                host.IsHitTestVisible = !working;
                host.SetInputEnabled(!working);
            });

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
                pages,
                run.Reader.LoadMoreClicks,
                run.Reader.LoadMoreStop,
                run.Reader.LicensesPagesWalked,
                run.Reader.LicensesStop);
        }

        var why = windowClosed
            ? "the window was closed before both pages were read"
            : timedOut
                ? "the session ran out of time before both pages were read"
                : "one of the two pages could not be read";

        return SteamPageHarvestResult.Partial(
            pages,
            why,
            run.Reader.LoadMoreClicks,
            run.Reader.LoadMoreStop,
            run.Reader.LicensesPagesWalked,
            run.Reader.LicensesStop);
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

            // The read is over, however it ended. Leaving the window blocked
            // would strand the user in a browser they cannot use or close their
            // way out of.
            run.Working(false);
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
            run.Working(false);
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
        if (!await SteamAccountPageReader.IsSignedInAsync(browser))
        {
            run.SawSignedOut = true;

            // The window goes back to being the user's: they have a form to fill
            // in, and nothing of ours is reading the page.
            run.Working(false);
            run.Say("Sign in to Steam to continue.");
            return;
        }

        run.SignedIn = true;

        // From here until the capture is finished, a click in this window could
        // navigate the page out from under the read. The banner says so and the
        // input block enforces it.
        run.Working(true);

        var html = await run.Reader.ReadAsync(browser, kind, () => run.Done, ct);

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
    /// <summary>
    /// The "please wait" strip, hidden until Winnow starts reading.
    ///
    /// <para>It exists because the window is a real browser and looks like one:
    /// nothing about it says that clicking a link during the read would navigate
    /// the page out from under the capture. The banner appears at the moment the
    /// window stops being the user's to drive, next to the input block that
    /// enforces it, and disappears if the flow goes back to waiting for a
    /// sign-in.</para>
    ///
    /// <para>Docked above the browser rather than drawn over it. A hosted native
    /// window paints over Avalonia content whatever the z-order says, so an
    /// overlay would be invisible; this is the same airspace constraint that
    /// made the sign-in prompt swap its consent panel out rather than reveal a
    /// browser underneath it.</para>
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
