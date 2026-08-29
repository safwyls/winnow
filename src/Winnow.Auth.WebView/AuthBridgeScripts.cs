using System.Globalization;
using System.Text.Json;
using Winnow.Core.Auth;

namespace Winnow.Auth.WebView;

/// <summary>
/// The scripts the sign-in browser puts into a page, and the origin guard every
/// one of them opens with.
///
/// <para>Public, and for one reason: a WebView2 control cannot be created in a
/// unit test, so the only way to assert that the bridge refuses to define itself
/// in an iframe or on an unapproved origin is to build the script and read
/// it.</para>
///
/// <para><b>Why the guard is inside the script.</b> WebView2's
/// <c>AddScriptToExecuteOnDocumentCreated</c> is the only injection point that
/// wins the race against a page probing for the launcher bridge during load —
/// Epic's does, 21 times — and it has no per-frame or per-origin filter: it runs
/// in every document and every iframe of the session. So the filter has to
/// travel with the script. Each one refuses to do anything unless it is the top
/// frame of a document on a trusted origin, and fails closed if it cannot
/// determine either.</para>
/// </summary>
public static class AuthBridgeScripts
{
    /// <summary>
    /// The origin guard, shared by both injected scripts.
    ///
    /// <para><c>%ORIGINS%</c> is replaced with a JSON array of normalised
    /// <c>scheme://host:port</c> origins — the same normalisation
    /// <see cref="AuthFlowPolicy.OriginOf(Uri)"/> produces, ports written out
    /// explicitly on both sides so a default port cannot be the thing that makes
    /// a comparison pass.</para>
    /// </summary>
    private const string GuardPrelude = """
        var __winnowAllowed = %ORIGINS%;
        function __winnowTrusted() {
            try {
                // Never inside a frame. An iframe on a trusted origin is still a
                // document this flow did not put there, and the bridge is the one
                // thing that must not be reachable from one.
                if (window.top !== window.self) { return false; }
                var loc = window.location;
                var port = loc.port;
                if (!port) { port = loc.protocol === 'https:' ? '443' : '80'; }
                var origin = loc.protocol + '//' + loc.hostname.toLowerCase() + ':' + port;
                for (var i = 0; i < __winnowAllowed.length; i++) {
                    if (__winnowAllowed[i] === origin) { return true; }
                }
                return false;
            } catch (e) {
                // A sandboxed or opaque-origin document cannot be identified, so
                // it does not get the bridge. Fail closed.
                return false;
            }
        }
        if (!__winnowTrusted()) { return; }
        """;

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
    /// <para>Defensive in three ways that matter. It defines nothing outside a
    /// trusted top-level document (see <see cref="GuardPrelude"/>); it never
    /// throws into the page — a bridge that raises inside Epic's own handler
    /// could take the sign-in down with it — and it posts a structured object
    /// rather than a bare string, so the host is never guessing what a message
    /// means.</para>
    /// </summary>
    public static string Bridge(IReadOnlyCollection<string> trustedOrigins)
    {
        ArgumentNullException.ThrowIfNull(trustedOrigins);

        return $$"""
            (function () {
                {{Guard(trustedOrigins)}}
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
    }

    /// <summary>
    /// The in-page harvester: same-origin fetch on a timer, guarded to one
    /// instance per document, stops on the first populated answer.
    /// </summary>
    /// <param name="harvestUrl">The provider endpoint that issues a code to a browser that holds a session.</param>
    /// <param name="trustedOrigins">Origins this may run on. The harvest URL's own origin is necessarily one of them.</param>
    /// <param name="interval">How often to ask. Bounded to avoid throttling the provider's own origin.</param>
    /// <param name="maxAttempts">A ceiling, so a page left open in a background window cannot poll forever.</param>
    public static string Harvester(
        Uri harvestUrl, IReadOnlyCollection<string> trustedOrigins, TimeSpan interval, int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(harvestUrl);
        ArgumentNullException.ThrowIfNull(trustedOrigins);

        // The URL is substituted as a JSON literal, which is also the escaping:
        // a JSON string literal is a JavaScript string literal.
        var url = JsonSerializer.Serialize(harvestUrl.ToString());
        var milliseconds = ((int)interval.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        var attempts = maxAttempts.ToString(CultureInfo.InvariantCulture);

        return $$"""
            (function () {
                {{Guard(trustedOrigins)}}
                if (window.__winnowHarvesting) { return; }
                window.__winnowHarvesting = true;
                var url = {{url}};
                var remaining = {{attempts}};
                var timer = null;
                function ask() {
                    if (window.__winnowHarvested || remaining <= 0) {
                        if (timer) { clearInterval(timer); }
                        return;
                    }
                    remaining--;
                    try {
                        fetch(url, { credentials: 'include', cache: 'no-store' })
                            .then(function (r) { return r.text(); })
                            .then(function (body) {
                                if (window.__winnowHarvested) { return; }
                                window.chrome.webview.postMessage({ kind: 'harvest', value: body });
                            })
                            .catch(function () { });
                    } catch (e) { }
                }
                // Immediately, then on a slow timer. The immediate call is what
                // makes a completed sign-in visible on the very next navigation
                // rather than up to one interval later.
                ask();
                timer = setInterval(ask, {{milliseconds}});
            })();
            """;
    }

    /// <summary>
    /// Reads a rendered JSON body out of the page, or null when the document is
    /// not JSON.
    ///
    /// <para>The <c>contentType</c> test is what makes this provider-neutral and
    /// precise rather than "scrape every page and hope". Chromium reports
    /// <c>application/json</c> for these responses while still building a DOM
    /// around them, which is exactly the pair of facts this depends on and both
    /// were confirmed by the spike.</para>
    ///
    /// <para>No origin guard here, and it does not need one: this runs through
    /// <c>ExecuteScriptAsync</c>, which the host calls only after checking the
    /// document it is about to read. Unlike the injected pair, it is never
    /// installed into a document the host did not choose.</para>
    /// </summary>
    public const string ReadJsonBody = """
        (function () {
            try {
                if (document.contentType !== 'application/json') { return null; }
                return document.body ? document.body.innerText : null;
            } catch (e) { return null; }
        })();
        """;

    /// <summary>Stops the in-page harvester so no second code is ever minted.</summary>
    public const string StopHarvesting = "window.__winnowHarvested = true;";

    /// <summary>The guard, with this attempt's trusted origins baked in as a JSON array.</summary>
    private static string Guard(IReadOnlyCollection<string> trustedOrigins)
        => GuardPrelude.Replace(
            "%ORIGINS%",
            JsonSerializer.Serialize(trustedOrigins.ToArray()),
            StringComparison.Ordinal);
}
