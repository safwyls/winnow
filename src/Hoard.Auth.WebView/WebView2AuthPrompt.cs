using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
/// <para><b>Three capture routes, all armed at once.</b> Their evidential
/// standing differs and the difference is deliberately preserved here:</para>
/// <list type="number">
///   <item><description><b>The launcher JS bridge — CONFIRMED live.</b> Epic's
///   sign-in page reads <c>window.ue</c> 21 times of its own accord (measured,
///   2026-08-26, identically with and without a spoofed launcher user-agent),
///   and the injected bridge was driven end to end. Yields an
///   <see cref="AuthCodeKind.ExchangeCode"/>. That the page CALLS it after a
///   successful sign-in is the part no unauthenticated probe could
///   settle.</description></item>
///   <item><description><b>Redirect interception — mechanism CONFIRMED,
///   premise UNVERIFIED.</b> <c>NavigationStarting</c> delivers the full URL
///   including the query before any connection is attempted, so an unroutable
///   https redirect needs no listener and no certificate — that half is proven.
///   That the authenticated flow actually 302s there carrying <c>?code=</c> is a
///   hypothesis. It is armed because arming it is free, not because it is known
///   to fire.</description></item>
///   <item><description><b>DOM read — CONFIRMED.</b> The JSON body renders into
///   a <c>&lt;pre&gt;</c> even at <c>application/json</c>, and
///   <c>ExecuteScriptAsync</c> reads it.</description></item>
/// </list>
///
/// <para><b>The user-agent is deliberately NOT spoofed.</b> Legendary sends a
/// launcher string and the obvious move is to copy it, but the spike measured
/// identical behaviour with and without — same 21 probes, same page, same form —
/// so it is cargo cult until something authenticated says otherwise. It also
/// makes the browser more fingerprintable to Epic's bot detection, not less.
/// (<c>EpicWebOptions.UserAgent</c> still sends a launcher string on the API
/// client; that is a separate, older decision about the launcher services and is
/// left alone.)</para>
///
/// <para><b>Nothing here throws.</b> Runtime missing, window closed, page
/// changed, network gone, browser process dead — every one is an
/// <see cref="AuthCodeResult"/> with a reason, and every one leaves the existing
/// local ingest exactly as it was.</para>
/// </summary>
public sealed class WebView2AuthPrompt : IInteractiveAuthPrompt
{
    /// <summary>
    /// The launcher bridge, injected before any of the page's own script runs.
    ///
    /// <para>Shaped after <c>legendary/utils/webview_login.py</c>, which is the
    /// reference implementation of this mechanism. Epic's page probes for
    /// <c>window.ue.signinprompt</c> and, believing it is inside the launcher,
    /// hands the exchange code out through it.</para>
    ///
    /// <para>Defensive in two ways that matter. It never throws into the page —
    /// a bridge that raises inside Epic's own handler could take the sign-in down
    /// with it — and it posts a structured object rather than a bare string, so
    /// the host is never guessing what a message means.</para>
    /// </summary>
    private const string BridgeScript = """
        (function () {
            function post(kind, value) {
                try { window.chrome.webview.postMessage({ kind: kind, code: value }); } catch (e) { }
            }
            window.ue = {
                signinprompt: {
                    requestexchangecodesignin: function (code) { post('exchange', code); },
                    registersignincompletecallback: function () { post('ready', null); }
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

    private async Task<AuthCodeResult> RunOnUiThreadAsync(AuthPromptRequest request, CancellationToken ct)
    {
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new TaskCompletionSource<AuthCodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            // regardless of z-order — the classic airspace problem — so a
            // browser sharing a Panel with the notice would sit on top of the
            // thing the user has to read. Swapping the content sidesteps it
            // entirely, and has the useful side effect that the browser is not
            // even created until consent is given.
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

            await ArmCaptureAsync(controller.CoreWebView2, request, captured);

            controller.CoreWebView2.Navigate(request.StartUrl.ToString());

            var finished = await Task.WhenAny(
                captured.Task,
                closed.Task,
                Task.Delay(request.Timeout, ct));

            if (finished == captured.Task)
            {
                return await captured.Task;
            }

            return finished == closed.Task
                ? AuthCodeResult.Cancelled("the sign-in window was closed")
                : AuthCodeResult.Cancelled("the sign-in was not completed in time");
        }
        finally
        {
            // Always, on every path. A left-open browser window with a
            // half-finished sign-in in it is worse than no window.
            window.Close();
        }
    }

    /// <summary>
    /// Wires all three capture routes onto one browser session.
    ///
    /// <para>Together rather than in sequence: each route needs a whole
    /// interactive sign-in to test, and codes are single-use, so trying them one
    /// at a time would make the user sign in up to three times and burn a code on
    /// each miss. Armed together, whichever route the provider actually exercises
    /// fires first and the rest never do — and the result records which one it
    /// was, which is how the spike's open question finally gets an answer.</para>
    /// </summary>
    private async Task ArmCaptureAsync(
        CoreWebView2 browser, AuthPromptRequest request, TaskCompletionSource<AuthCodeResult> captured)
    {
        if (request.Strategies.HasFlag(AuthCaptureStrategies.LauncherJsBridge))
        {
            browser.WebMessageReceived += (_, e) =>
            {
                if (TryReadBridgeMessage(e) is { } code)
                {
                    captured.TrySetResult(
                        AuthCodeResult.Captured(AuthCodeKind.ExchangeCode, code, "launcher JS bridge"));
                }
            };

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
                    captured.TrySetResult(
                        AuthCodeResult.Captured(AuthCodeKind.AuthorizationCode, code, "redirect interception"));
                }
                else
                {
                    // Worth a line: it means the redirect half of the hypothesis
                    // is right and only the parameter name is wrong, which is a
                    // much smaller thing to fix than it looks from a silent
                    // failure. The URI is NOT logged — it is the object that
                    // would carry a code.
                    _log.LogWarning(
                        "Reached the {Provider} redirect target with no '{Parameter}' parameter on it.",
                        request.ProviderName, request.RedirectCodeParameter);
                }
            };
        }

        if (request.Strategies.HasFlag(AuthCaptureStrategies.JsonBodyScrape) && request.JsonCodeFields.Count > 0)
        {
            browser.NavigationCompleted += async (sender, e) =>
            {
                if (!e.IsSuccess || captured.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    var raw = await ((CoreWebView2)sender!).ExecuteScriptAsync(ReadJsonBodyScript);
                    if (TryReadJsonCode(raw, request.JsonCodeFields) is { } found)
                    {
                        captured.TrySetResult(
                            AuthCodeResult.Captured(found.Kind, found.Code, "JSON body"));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                    or System.Runtime.InteropServices.COMException)
                {
                    // The browser went away mid-script, or the navigation was
                    // superseded. Another route may still fire; either way this
                    // is an event handler and must not throw.
                    _log.LogDebug("Could not read the page body ({ExceptionType}).", ex.GetType().Name);
                }
            };
        }

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

    /// <summary>
    /// The window, showing the notice and nothing else until the user accepts.
    ///
    /// <para>Deliberately plain — no design tokens, no theme, no styling. This is
    /// the auth machinery; where a sign-in is offered and what it looks like is a
    /// UI decision made elsewhere.</para>
    /// </summary>
    private static Window BuildConsentWindow(AuthPromptRequest request, TaskCompletionSource<bool> consent)
    {
        var notice = new TextBlock
        {
            Text = request.ConsentNotice,
            TextWrapping = TextWrapping.Wrap,

            // Monospace because the notice quotes the provider's own warning as
            // an indented block and proportional text mangles the indentation.
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var accept = new Button { Content = "Continue to " + request.ProviderName, IsDefault = true };
        accept.Click += (_, _) => consent.TrySetResult(true);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => consent.TrySetResult(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 0 };
        panel.Children.Add(notice);
        panel.Children.Add(buttons);

        return new Window
        {
            Title = "Sign in to " + request.ProviderName,
            Width = 1024,
            Height = 820,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel },
        };
    }

    /// <summary>
    /// Pulls an exchange code out of a bridge message, or null.
    ///
    /// <para>Only <c>kind: "exchange"</c> carries one. <c>ready</c> is the page
    /// announcing the bridge is wired and <c>external</c> is it asking for a URL
    /// to be opened outside — both are noise here, and treating any message with
    /// a string in it as a code would spend a URL on a token endpoint.</para>
    /// </summary>
    private static string? TryReadBridgeMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || !string.Equals(kind.GetString(), "exchange", StringComparison.Ordinal))
            {
                return null;
            }

            return root.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(code.GetString())
                    ? code.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // A page can post anything it likes to a host it thinks is the
            // launcher. Unparseable is not an error condition.
            return null;
        }
    }

    /// <summary>
    /// Reads a code out of the JSON the page rendered, or null.
    ///
    /// <para>Doubly encoded, and it has to be: <c>ExecuteScriptAsync</c> returns
    /// its result as JSON, so a script that returns a JSON document returns a
    /// JSON STRING containing that document. Parsing once yields the text;
    /// parsing that text yields the object.</para>
    /// </summary>
    private static (AuthCodeKind Kind, string Code)? TryReadJsonCode(
        string? executeScriptResult, IReadOnlyList<AuthJsonCodeField> fields)
    {
        if (string.IsNullOrWhiteSpace(executeScriptResult))
        {
            return null;
        }

        try
        {
            using var outer = JsonDocument.Parse(executeScriptResult);
            if (outer.RootElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var body = outer.RootElement.GetString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var inner = JsonDocument.Parse(body);
            if (inner.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var field in fields)
            {
                // Null is the ordinary unauthenticated value for both of Epic's
                // code fields, so "present" is not "populated" and only a
                // non-empty string counts.
                if (inner.RootElement.TryGetProperty(field.FieldName, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } code
                    && !string.IsNullOrWhiteSpace(code))
                {
                    return (field.Kind, code);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
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
    /// helper so nothing constructs an intermediate collection that a debugger,
    /// a log sink or a crash dump would show the code in.
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
