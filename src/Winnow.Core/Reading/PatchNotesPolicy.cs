using Winnow.Core.Auth;

namespace Winnow.Core.Reading;

/// <summary>Where the navigation gate sent the address.</summary>
public enum PatchNotesNavigation
{
    /// <summary>The address is on an allowlisted origin and may be rendered.</summary>
    Allow = 0,

    /// <summary>The address is HTTP(S) but off the allowlist; hand it to the user's own browser.</summary>
    OpenExternally = 1,

    /// <summary>The address is null, non-web, or otherwise refused.</summary>
    Block = 2,
}

/// <summary>
/// Decides whether a URL may be read inside the embedded patch-notes panel and
/// what the panel may do once it is open. Two gates, both allowlists:
///
/// <list type="bullet">
/// <item><description><b>Entry gate</b> (<see cref="IsReadable"/> / <see cref="For"/>):
/// HTTPS only, the origin must be one of the four Steam news origins, and the path
/// must contain a news or announcements marker. A URL that fails any test is not
/// opened in the panel at all.</description></item>
/// <item><description><b>Navigation gate</b> (<see cref="ClassifyNavigation"/>,
/// <see cref="ClassifyPopup"/>, <see cref="ClassifyFrame"/>): once the panel is open,
/// every navigation is classified as <see cref="PatchNotesNavigation.Allow"/>,
/// <see cref="PatchNotesNavigation.OpenExternally"/> or
/// <see cref="PatchNotesNavigation.Block"/>. Frames are stricter than top-level
/// navigations: an off-allowlist frame is blocked rather than opened
/// externally.</description></item>
/// </list>
///
/// <para>Built on <see cref="AuthFlowPolicy"/> (the NAVIGABLE tier only, never
/// TRUSTED) rather than a second origin mechanism. The precedent is
/// <see cref="SteamAccountPagePolicy"/>, which derives its own navigation policy
/// from the same class for the same reason.</para>
/// </summary>
public sealed class PatchNotesPolicy
{
    // The four origins ISteamNews/GetNewsForApp can return:
    //   store.steampowered.com          — the store's own news hub
    //   steamstore-a.akamaihd.net       — the CDN form of the same posts
    //   steamcommunity.com              — community announcements a redirect lands on
    //   www.steamcommunity.com          — the www variant of the same community origin
    private static readonly Uri[] Origins =
    [
        new("https://store.steampowered.com"),
        new("https://steamstore-a.akamaihd.net"),
        new("https://steamcommunity.com"),
        new("https://www.steamcommunity.com"),
    ];

    private static readonly string[] PathMarkers = ["/news/", "/announcements"];

    /// <summary>
    /// <see cref="AuthPromptRequest.ProviderName"/> is required but never displayed
    /// on this surface — there is no consent screen and no sign-in window title.
    /// </summary>
    private const string UnusedProviderName = "patch-notes";

    /// <summary>
    /// <see cref="AuthPromptRequest.ConsentNotice"/> is required but never
    /// displayed: this request exists only to derive navigable origins.
    /// </summary>
    private const string UnusedConsentNotice = "not displayed: this request exists only to derive origins";

    private readonly AuthFlowPolicy _navigation;

    private PatchNotesPolicy(Uri start, AuthFlowPolicy navigation)
    {
        Start = start;
        _navigation = navigation;
    }

    /// <summary>The address the panel navigates to on open.</summary>
    public Uri Start { get; }

    /// <summary>The four Steam origins the entry gate accepts.</summary>
    public static IReadOnlyList<Uri> NewsOrigins => Origins;

    /// <summary>Path fragments (<c>/news/</c>, <c>/announcements</c>) the entry gate requires.</summary>
    public static IReadOnlyList<string> NewsPathMarkers => PathMarkers;

    /// <summary>Origins the navigation gate allows, as scheme+host+port strings.</summary>
    public IReadOnlyCollection<string> NavigableOrigins => _navigation.NavigableOrigins;

    /// <summary>
    /// Entry gate. Returns true when <paramref name="url"/> is HTTPS, on one of
    /// the four news origins, and carries a news or announcements path marker.
    /// </summary>
    public static bool IsReadable(Uri? url)
    {
        if (url is null
            || !url.IsAbsoluteUri
            || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (AuthFlowPolicy.OriginOf(url) is not { } origin
            || !Origins.Any(known => string.Equals(
                AuthFlowPolicy.OriginOf(known), origin, StringComparison.Ordinal)))
        {
            return false;
        }

        return PathMarkers.Any(marker
            => url.AbsolutePath.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc cref="IsReadable(Uri?)"/>
    public static bool IsReadable(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) && IsReadable(parsed);

    /// <summary>
    /// Builds a policy for <paramref name="url"/> if it passes the entry gate,
    /// or null if it does not. The returned policy carries the navigation gate
    /// for the panel's lifetime.
    /// </summary>
    public static PatchNotesPolicy? For(Uri? url)
    {
        if (!IsReadable(url))
        {
            return null;
        }

        return new PatchNotesPolicy(
            url!,
            AuthFlowPolicy.For(new AuthPromptRequest
            {
                ProviderName = UnusedProviderName,
                ConsentNotice = UnusedConsentNotice,
                StartUrl = url!,
                RedirectUrl = null,
                ExpectedState = null,
                HarvestUrl = null,
                Strategies = AuthCaptureStrategies.None,
                AdditionalNavigableOrigins = Origins,
            }));
    }

    /// <inheritdoc cref="For(Uri?)"/>
    public static PatchNotesPolicy? For(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? For(parsed) : null;

    /// <summary>
    /// Navigation gate (top-level). Allowlisted origins are allowed; <c>about:</c>
    /// is allowed (WebView2 starts there); a non-web scheme is blocked outright;
    /// anything else is handed to the user's own browser.
    /// </summary>
    public PatchNotesNavigation ClassifyNavigation(Uri? uri)
    {
        if (uri is null)
        {
            return PatchNotesNavigation.Block;
        }

        if (string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            return PatchNotesNavigation.Allow;
        }

        if (AuthFlowPolicy.OriginOf(uri) is null)
        {
            return PatchNotesNavigation.Block;
        }

        return _navigation.IsNavigableOrigin(uri)
            ? PatchNotesNavigation.Allow
            : PatchNotesNavigation.OpenExternally;
    }

    /// <inheritdoc cref="ClassifyNavigation(Uri?)"/>
    public PatchNotesNavigation ClassifyNavigation(string? uri)
        => ClassifyNavigation(Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed : null);

    /// <summary>Same decision as <see cref="ClassifyNavigation(Uri?)"/>: a popup is a navigation the page requested.</summary>
    public PatchNotesNavigation ClassifyPopup(Uri? uri) => ClassifyNavigation(uri);

    /// <summary>
    /// Navigation gate (sub-frame). Stricter than the top-level gate: an
    /// off-allowlist frame is blocked rather than opened externally, so a
    /// third-party embed cannot load inside the panel.
    /// </summary>
    public PatchNotesNavigation ClassifyFrame(Uri? uri)
    {
        var decision = ClassifyNavigation(uri);

        return decision == PatchNotesNavigation.OpenExternally
            ? PatchNotesNavigation.Block
            : decision;
    }

    public PatchNotesNavigation ClassifyFrame(string? uri)
        => ClassifyFrame(Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed : null);
}
