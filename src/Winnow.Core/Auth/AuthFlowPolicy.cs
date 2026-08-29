namespace Winnow.Core.Auth;

/// <summary>What an embedded browser should do with one navigation or popup.</summary>
public enum AuthNavigationDecision
{
    /// <summary>Host it. The destination is on an approved origin.</summary>
    Allow = 0,

    /// <summary>It is the registered redirect: cancel the navigation and read the query.</summary>
    CaptureRedirect = 1,

    /// <summary>Not approved, but a real web page: hand it to the user's own browser.</summary>
    OpenExternally = 2,

    /// <summary>Refuse it outright. Not approved and not something to hand anywhere.</summary>
    Block = 3,
}

/// <summary>How the <c>state</c> on a returned redirect compared to the one that was sent.</summary>
public enum AuthStateVerification
{
    /// <summary>No state was sent on this attempt, so there is nothing to check.</summary>
    NotRequired = 0,

    /// <summary>The redirect carried exactly the state that was sent.</summary>
    Matched = 1,

    /// <summary>A state was sent but the redirect carried none. Nothing may be spent.</summary>
    Missing = 2,

    /// <summary>The redirect carried a different state. Nothing may be spent.</summary>
    Mismatched = 3,
}

/// <summary>
/// The security model of one interactive sign-in, as pure functions over a
/// <see cref="AuthPromptRequest"/>.
///
/// <para>Extracted from the browser host on purpose: a WebView2 control cannot
/// be created in a unit test, so every rule that decides <em>whether to trust
/// something</em> lives here, where a test can ask it directly. The host is left
/// holding only the wiring.</para>
///
/// <para><b>Two tiers, and the distinction is the whole design.</b></para>
///
/// <list type="bullet">
/// <item>
/// <description><b>Trusted origins</b> — where a code may come from. Derived
/// from the request itself: the origins of the start URL, the harvest URL and
/// the registered redirect, HTTPS only. The JS bridge is injected only here, web
/// messages are accepted only from here, and a page body is scraped only here.
/// Nothing widens this set.</description>
/// </item>
/// <item>
/// <description><b>Navigable origins</b> — where the window may go. The trusted
/// set plus <see cref="AuthPromptRequest.AdditionalNavigableOrigins"/>, which is
/// how a provider's social-login hand-offs (Google, Xbox, Steam…) stay usable.
/// Being navigable buys a page nothing but the right to render: it is never
/// injected into, never listened to, and never read.</description>
/// </item>
/// </list>
///
/// <para>An origin is scheme + host + port, compared exactly. The port is not
/// decoration — <c>https://localhost/launcher/authorized</c> and
/// <c>https://localhost:8443/launcher/authorized</c> are different security
/// principals, and matching without the port is how a redirect on an attacker's
/// port is accepted as the provider's.</para>
/// </summary>
public sealed class AuthFlowPolicy
{
    private readonly AuthPromptRequest _request;
    private readonly HashSet<string> _trusted;
    private readonly HashSet<string> _navigable;

    private AuthFlowPolicy(AuthPromptRequest request, HashSet<string> trusted, HashSet<string> navigable)
    {
        _request = request;
        _trusted = trusted;
        _navigable = navigable;
    }

    /// <summary>Builds the policy for one sign-in attempt.</summary>
    public static AuthFlowPolicy For(AuthPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trusted = new HashSet<string>(StringComparer.Ordinal);

        // HTTPS only, and no exception for localhost. A code is a full-account
        // credential; there is no address it may cross plaintext.
        AddSecureOrigin(trusted, request.StartUrl);
        AddSecureOrigin(trusted, request.HarvestUrl);
        AddSecureOrigin(trusted, request.RedirectUrl);

        var navigable = new HashSet<string>(trusted, StringComparer.Ordinal);
        foreach (var origin in request.AdditionalNavigableOrigins)
        {
            AddSecureOrigin(navigable, origin);
        }

        return new AuthFlowPolicy(request, trusted, navigable);
    }

    /// <summary>The origins a code may come from. Ordinal, normalised, HTTPS only.</summary>
    public IReadOnlyCollection<string> TrustedOrigins => _trusted;

    /// <summary>The origins the window may render. A superset of <see cref="TrustedOrigins"/>.</summary>
    public IReadOnlyCollection<string> NavigableOrigins => _navigable;

    /// <summary>
    /// <c>scheme://host:port</c> for an absolute HTTP(S) URI, or null for
    /// anything else — <c>about:</c>, <c>data:</c>, <c>blob:</c>, a custom scheme,
    /// a relative reference. A null origin is never trusted and never navigable.
    /// </summary>
    public static string? OriginOf(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Uri.Port is the scheme default when none was written, so this is an
        // exact port comparison whether or not the URL spelled one out.
        return string.Concat(
            uri.Scheme.ToLowerInvariant(),
            "://",
            uri.Host.ToLowerInvariant(),
            ":",
            uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>As <see cref="OriginOf(Uri)"/>, for a URL that arrived as a string.</summary>
    public static string? OriginOf(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? OriginOf(parsed) : null;

    /// <summary>Whether a code may come from this address.</summary>
    public bool IsTrustedOrigin(Uri? uri) => OriginOf(uri) is { } origin && _trusted.Contains(origin);

    /// <summary>Whether a code may come from this address.</summary>
    public bool IsTrustedOrigin(string? uri) => OriginOf(uri) is { } origin && _trusted.Contains(origin);

    /// <summary>Whether the embedded window may render this address at all.</summary>
    public bool IsNavigableOrigin(Uri? uri) => OriginOf(uri) is { } origin && _navigable.Contains(origin);

    /// <summary>
    /// Whether the launcher JS bridge and the in-page harvester may be defined in
    /// this document. Trusted origins only — the bridge is the thing that turns a
    /// page into something that can hand Winnow a credential.
    /// </summary>
    public bool AllowsBridge(Uri? uri) => IsTrustedOrigin(uri);

    /// <summary>
    /// Whether a <c>WebMessageReceived</c> from this source may be read.
    ///
    /// <para>WebView2 reports the posting document's URL, iframes included, and
    /// that is the only identity available on the message. A page believing it is
    /// inside the launcher can post whatever it likes; this is what stops a
    /// third-party frame's <c>{"kind":"exchange"}</c> from being spent.</para>
    /// </summary>
    public bool AcceptsMessageFrom(string? source) => IsTrustedOrigin(source);

    /// <summary>
    /// Whether a navigation is heading for the registered redirect: scheme, host,
    /// <b>port</b> and path, all four. The query is deliberately excluded — it is
    /// the payload, not the identity.
    /// </summary>
    public bool IsRedirectTarget(Uri? candidate)
    {
        if (candidate is null || _request.RedirectUrl is not { } redirect)
        {
            return false;
        }

        return OriginOf(candidate) is { } candidateOrigin
            && OriginOf(redirect) is { } redirectOrigin
            && string.Equals(candidateOrigin, redirectOrigin, StringComparison.Ordinal)
            && string.Equals(
                candidate.AbsolutePath.TrimEnd('/'),
                redirect.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What to do with a top-level navigation the browser is starting.</summary>
    public AuthNavigationDecision ClassifyNavigation(Uri? uri)
    {
        if (uri is null)
        {
            return AuthNavigationDecision.Block;
        }

        // WebView2 starts on about:blank and returns there between documents.
        // It carries no origin and can host nothing, so it is neither a threat
        // nor something to cancel.
        if (string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            return AuthNavigationDecision.Allow;
        }

        if (OriginOf(uri) is null)
        {
            // data:, blob:, file:, a custom scheme a page asked to launch.
            return AuthNavigationDecision.Block;
        }

        if (IsRedirectTarget(uri)
            && _request.Strategies.HasFlag(AuthCaptureStrategies.RedirectInterception))
        {
            return AuthNavigationDecision.CaptureRedirect;
        }

        return IsNavigableOrigin(uri) ? AuthNavigationDecision.Allow : AuthNavigationDecision.Block;
    }

    /// <summary>
    /// What to do with a window the page asked to open.
    ///
    /// <para>Differs from <see cref="ClassifyNavigation"/> in one place, and it is
    /// the place the finding was about: an unapproved destination is handed to the
    /// user's own browser rather than folded into the window that carries the
    /// bridge. The user still gets where they were going; the sign-in surface
    /// does not host it.</para>
    /// </summary>
    public AuthNavigationDecision ClassifyPopup(Uri? uri)
    {
        var decision = ClassifyNavigation(uri);

        return decision == AuthNavigationDecision.Block && OriginOf(uri) is not null
            ? AuthNavigationDecision.OpenExternally
            : decision;
    }

    /// <summary>
    /// Compares the <c>state</c> on a returned redirect against the one minted for
    /// this attempt.
    ///
    /// <para><see cref="AuthStateVerification.NotRequired"/> is returned only when
    /// no state was sent — a flow that starts on the provider's code endpoint
    /// rather than its authorize endpoint never issues one, and demanding it back
    /// would break a route that has no CSRF surface to begin with.</para>
    /// </summary>
    public AuthStateVerification VerifyState(Uri? redirect)
    {
        if (string.IsNullOrWhiteSpace(_request.ExpectedState))
        {
            return AuthStateVerification.NotRequired;
        }

        if (redirect is null)
        {
            return AuthStateVerification.Missing;
        }

        var returned = ReadQueryParameter(redirect, _request.StateParameter);

        if (string.IsNullOrWhiteSpace(returned))
        {
            return AuthStateVerification.Missing;
        }

        return AuthState.Matches(_request.ExpectedState, returned)
            ? AuthStateVerification.Matched
            : AuthStateVerification.Mismatched;
    }

    /// <summary>
    /// One decoded query parameter, or null.
    ///
    /// <para>Hand-parsed rather than through a helper so nothing constructs an
    /// intermediate collection that a debugger, a log sink or a crash dump would
    /// show the code in.</para>
    /// </summary>
    public static string? ReadQueryParameter(Uri uri, string name)
    {
        ArgumentNullException.ThrowIfNull(uri);

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

    private static void AddSecureOrigin(HashSet<string> set, Uri? uri)
    {
        if (uri is null
            || !uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (OriginOf(uri) is { } origin)
        {
            set.Add(origin);
        }
    }
}
