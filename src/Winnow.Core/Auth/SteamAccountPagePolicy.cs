using Winnow.Core.Ingest;

namespace Winnow.Core.Auth;

/// <summary>
/// Whether an account page has more rows to gather, and if not, why the
/// gathering stopped.
///
/// <para>Shared by both pages. The purchase history grows by clicking a
/// load-more control and the licences page by following a paginator, but the
/// decision is the same one and a caller reading a result should not have to
/// learn two vocabularies for it.</para>
/// </summary>
public enum SteamLoadMoreDecision
{
    /// <summary>There is another step, it is under the cap, and the last one produced rows. Take it.</summary>
    Continue = 0,

    /// <summary>No control and no next page. Everything the account has is now in the document.</summary>
    Exhausted = 1,

    /// <summary>The cap was reached. The page is truncated and the caller must be told so.</summary>
    ReachedCap = 2,

    /// <summary>A step produced no new rows. The page offers more, but gathering it no longer changes anything.</summary>
    Stalled = 3,
}

/// <summary>
/// The security model of one embedded Steam page harvest, as pure functions.
///
/// <para>Built on <see cref="AuthFlowPolicy"/> rather than beside it: the origin
/// discipline, the navigation gate and the popup rule are the same F05-hardened
/// machinery the Epic sign-in runs on, and this type adds only what is specific
/// to harvesting: which origin may be read, and which two paths on it.</para>
///
/// <para><b>Four tiers here, and each one is narrower than the last about a
/// different question.</b></para>
///
/// <list type="bullet">
/// <item><description><b>Harvestable</b>: the two exact pages. A script runs in
/// a document only when <see cref="PageOf"/> names it, so the reading surface is
/// two URLs and nothing widens it. A sibling path on the same host
/// (<c>/account/</c>, <c>/account/licenses/detail/1</c>) is not one of
/// them.</description></item>
/// <item><description><b>Mintable</b>: any store page that is not a sign-in
/// form, which is where a signed-in <c>webapi_token</c> can be read off
/// <c>application_config</c>. Wider in address than harvestable and far narrower
/// in what it takes away: one field, never a document. See
/// <see cref="AllowsMint"/> for why the store root is in this tier and out of
/// the one above.</description></item>
/// <item><description><b>Trusted</b>: the exact HTTPS origin
/// <c>store.steampowered.com</c>, which is where those two pages live. Derived
/// from the page URLs by <see cref="AuthFlowPolicy"/> and not extensible: no
/// caller supplies it, so no caller can move it.</description></item>
/// <item><description><b>Navigable</b>: the trusted origin plus Valve's login
/// and support origins, so that signing in, Steam Guard, a captcha and an
/// account-recovery detour all work. Being navigable buys a page the right to
/// render and nothing else: it is never scripted, never read, never
/// captured.</description></item>
/// </list>
///
/// <para>Everything else is refused, and a popup to it is handed to the user's
/// own browser rather than hosted next to the session cookies.</para>
/// </summary>
public sealed class SteamAccountPagePolicy
{
    /// <summary>The licenses page. Also the page the session starts on, so Steam's own login redirect does the routing.</summary>
    public static readonly Uri LicensesPage = new("https://store.steampowered.com/account/licenses/");

    /// <summary>The purchase-history page.</summary>
    public static readonly Uri PurchaseHistoryPage = new("https://store.steampowered.com/account/history/");

    /// <summary>How many times the load-more control is clicked when the request does not say.</summary>
    public const int DefaultMaxLoadMoreClicks = 100;

    /// <summary>The ceiling a request cannot raise. A cap the user can set to infinity is not a cap.</summary>
    public const int MaxAllowedLoadMoreClicks = 500;

    /// <summary>
    /// How many further licences pages are walked when the request does not say.
    ///
    /// <para>Verified 2026-08-29: the licences page does not load more, it
    /// paginates, 100 licences at a time. A 979-licence account is ten pages, so
    /// fifty is generous by a factor of five and still finite.</para>
    /// </summary>
    public const int DefaultMaxLicensesPages = 50;

    /// <summary>The ceiling a request cannot raise, for the same reason as the load-more one.</summary>
    public const int MaxAllowedLicensesPages = 200;

    /// <summary>
    /// Valve's own login and support origins. Navigable only.
    ///
    /// <para>The sign-in form itself is served from the store origin, but the
    /// token exchange, Steam Guard, the QR sign-in and account recovery all cross
    /// these, and a blocked navigation in the middle of a login looks to the user
    /// like Steam is broken.</para>
    /// </summary>
    private static readonly Uri[] LoginSupportOrigins =
    [
        new("https://login.steampowered.com"),
        new("https://help.steampowered.com"),
        new("https://steamcommunity.com"),
        new("https://www.steamcommunity.com"),
        new("https://www.steampowered.com"),
    ];

    /// <summary>
    /// Stands in for the consent text on the synthesised sign-in request.
    ///
    /// <para><see cref="AuthFlowPolicy"/> reads only URLs off the request, so this
    /// value reaches no screen. The real consent moment belongs to whatever calls
    /// the harvester; it is recorded on
    /// <see cref="SteamPageHarvestRequest.ConsentGranted"/>, which the harvester
    /// refuses to run without.</para>
    /// </summary>
    private const string UnusedConsentNotice = "not displayed: this request exists only to derive origins";

    private readonly AuthFlowPolicy _navigation;

    private SteamAccountPagePolicy(AuthFlowPolicy navigation, int maxLoadMoreClicks, int maxLicensesPages)
    {
        _navigation = navigation;
        MaxLoadMoreClicks = maxLoadMoreClicks;
        MaxLicensesPages = maxLicensesPages;
    }

    /// <summary>Builds the policy for one harvest, clamping both of the request's caps into range.</summary>
    public static SteamAccountPagePolicy For(SteamPageHarvestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return For(request.MaxLoadMoreClicks, request.MaxLicensesPages);
    }

    /// <summary>Builds the policy with an explicit load-more cap and the default licences-page cap.</summary>
    public static SteamAccountPagePolicy For(int maxLoadMoreClicks)
        => For(maxLoadMoreClicks, DefaultMaxLicensesPages);

    /// <summary>
    /// Builds the policy with both caps explicit, each clamped into its own range.
    /// </summary>
    /// <param name="maxLoadMoreClicks">Purchase-history load-more clicks, clamped to <c>0..<see cref="MaxAllowedLoadMoreClicks"/></c>.</param>
    /// <param name="maxLicensesPages">Further licences pages, clamped to <c>0..<see cref="MaxAllowedLicensesPages"/></c>.</param>
    public static SteamAccountPagePolicy For(int maxLoadMoreClicks, int maxLicensesPages) => new(
        AuthFlowPolicy.For(new AuthPromptRequest
        {
            ProviderName = "Steam",

            // Both URLs are on the store origin, and that is the point: the
            // trusted set AuthFlowPolicy derives is exactly {store}, with the
            // login and support origins arriving one tier down as navigable.
            StartUrl = LicensesPage,
            HarvestUrl = PurchaseHistoryPage,

            // No redirect and no capture strategy. There is no code in this flow
            // to intercept, so CaptureRedirect can never be returned and no
            // injected bridge is ever asked for.
            RedirectUrl = null,
            ExpectedState = null,
            Strategies = AuthCaptureStrategies.None,
            ConsentNotice = UnusedConsentNotice,
            AdditionalNavigableOrigins = LoginSupportOrigins,
        }),
        Math.Clamp(maxLoadMoreClicks, 0, MaxAllowedLoadMoreClicks),
        Math.Clamp(maxLicensesPages, 0, MaxAllowedLicensesPages));

    /// <summary>How many times this run may click the purchase-history load-more control.</summary>
    public int MaxLoadMoreClicks { get; }

    /// <summary>
    /// How many further licences pages this run may follow past the first.
    ///
    /// <para>Zero is a complete answer for an account with under a hundred
    /// licences and a truncated one for anybody else, which is why the result
    /// says how far the walk got rather than leaving the parser to infer it.</para>
    /// </summary>
    public int MaxLicensesPages { get; }

    /// <summary>The one origin a document may be read from.</summary>
    public string HarvestOrigin => AuthFlowPolicy.OriginOf(LicensesPage)!;

    /// <summary>The origins a document may be read from. Exactly one, and it is <see cref="HarvestOrigin"/>.</summary>
    public IReadOnlyCollection<string> TrustedOrigins => _navigation.TrustedOrigins;

    /// <summary>The origins the window may render. A superset of <see cref="TrustedOrigins"/>.</summary>
    public IReadOnlyCollection<string> NavigableOrigins => _navigation.NavigableOrigins;

    /// <summary>The two pages, in the order they are visited.</summary>
    public static IReadOnlyList<SteamAccountPageKind> Pages { get; } =
    [
        SteamAccountPageKind.Licenses,
        SteamAccountPageKind.PurchaseHistory,
    ];

    /// <summary>The URL of one page. The only addresses this flow ever navigates to deliberately.</summary>
    public static Uri PageUrl(SteamAccountPageKind kind) => kind switch
    {
        SteamAccountPageKind.Licenses => LicensesPage,
        SteamAccountPageKind.PurchaseHistory => PurchaseHistoryPage,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Whether this is a trusted origin. Delegated unchanged.</summary>
    public bool IsTrustedOrigin(Uri? uri) => _navigation.IsTrustedOrigin(uri);

    /// <summary>Whether the window may render this address at all. Delegated unchanged.</summary>
    public bool IsNavigableOrigin(Uri? uri) => _navigation.IsNavigableOrigin(uri);

    /// <summary>What to do with a top-level navigation. Delegated unchanged.</summary>
    public AuthNavigationDecision ClassifyNavigation(Uri? uri) => _navigation.ClassifyNavigation(uri);

    /// <summary>What to do with a window the page asked to open. Delegated unchanged.</summary>
    public AuthNavigationDecision ClassifyPopup(Uri? uri) => _navigation.ClassifyPopup(uri);

    /// <summary>
    /// Which of the two pages this URL is, or null for anything else.
    ///
    /// <para>Origin first, exactly, then the path with case and trailing slashes
    /// normalised. The query is ignored (it is not part of a page's identity)
    /// and a longer path is a different page, not this one with something after
    /// it.</para>
    /// </summary>
    public SteamAccountPageKind? PageOf(Uri? uri)
    {
        if (uri is null
            || AuthFlowPolicy.OriginOf(uri) is not { } origin
            || !string.Equals(origin, HarvestOrigin, StringComparison.Ordinal))
        {
            return null;
        }

        var path = NormalisePath(uri.AbsolutePath);

        if (string.Equals(path, NormalisePath(LicensesPage.AbsolutePath), StringComparison.Ordinal))
        {
            return SteamAccountPageKind.Licenses;
        }

        if (string.Equals(path, NormalisePath(PurchaseHistoryPage.AbsolutePath), StringComparison.Ordinal))
        {
            return SteamAccountPageKind.PurchaseHistory;
        }

        return null;
    }

    /// <summary>
    /// Whether a script may run in this document, and therefore whether its HTML
    /// may be read.
    ///
    /// <para>The two-paths-only rule, and the only gate the harvester consults
    /// before calling into a page. The login form is on the trusted origin and
    /// still fails this: being trusted is what lets the window go somewhere, not
    /// what lets Winnow read it.</para>
    /// </summary>
    public bool AllowsHarvest(Uri? uri) => PageOf(uri) is not null;

    /// <summary>
    /// Whether a document may be asked for a minted <c>webapi_token</c>, and
    /// therefore whether the sign-in session may run its one small script in it.
    ///
    /// <para><b>A third tier, narrower than trusted and wider than harvestable,
    /// and it is deliberately neither of them.</b> It is a strict subset of
    /// <see cref="TrustedOrigins"/>, so a mint can never read a document the
    /// account-page session would not already have been allowed to navigate to;
    /// and it is disjoint from <see cref="AllowsHarvest"/> in what it permits the
    /// caller to take away, because a mint reads one field Valve puts on every
    /// store page rather than a whole document.</para>
    ///
    /// <para>Three clauses, all of them load-bearing. HTTPS and the exact store
    /// origin, because a token is a bearer credential and the community origin
    /// would mint one too (spike section 1, route 2) and is still refused: the
    /// session reads one origin. And not a sign-in-form path, because the pages
    /// a user types a password into are never scripted and never read, whatever
    /// tier they are on.</para>
    ///
    /// <para><b>The store root is in scope here and out of scope for
    /// <see cref="AllowsHarvest"/>, and that divergence is the point.</b>
    /// The harvester treats an empty path as part of signing in, correctly: the
    /// root is a waypoint in Steam's post-login redirect and it has no interest
    /// in reading it. The sign-in session's situation is the exact inverse. The
    /// root is where Steam lands the user after Steam Guard and it carries
    /// <c>application_config</c>, so it is precisely the document a mint most
    /// needs. Verified live against the user's own account on 2026-08-30
    /// (docs/spikes/steam-web-session-auth.md section 7.1): the token came from
    /// the store root, via <c>application_config/data-store_user_config</c>.
    /// The first version of the probe copied the harvester's clause across, and
    /// consequently refused the only page it was ever shown.</para>
    /// </summary>
    public bool AllowsMint(Uri? uri)
        => uri is not null
            && AuthFlowPolicy.OriginOf(uri) is { } origin
            && string.Equals(origin, HarvestOrigin, StringComparison.Ordinal)
            && !IsSignInFormPath(uri);

    /// <summary>
    /// Whether this address is a page the user types credentials into.
    ///
    /// <para>The named paths, and only those, are what keeps the login form out
    /// of <see cref="AllowsMint"/>. The sign-in form is served from the trusted
    /// origin: being trusted is what lets the window go somewhere, never what
    /// lets Winnow read it.</para>
    /// </summary>
    public static bool IsSignInFormPath(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();

        return path.StartsWith("login", StringComparison.Ordinal)
            || path.StartsWith("join", StringComparison.Ordinal)
            || path.StartsWith("password", StringComparison.Ordinal)
            || path.StartsWith("twofactor", StringComparison.Ordinal)
            || path.StartsWith("mobilelogin", StringComparison.Ordinal)
            || path.StartsWith("account/security", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether to click the purchase-history load-more control once more.
    /// </summary>
    /// <param name="clicksCompleted">Clicks already made in this run.</param>
    /// <param name="rowsBefore">Rows counted before the last click, or a negative number before the first.</param>
    /// <param name="rowsAfter">Rows counted now.</param>
    /// <param name="controlPresent">Whether a load-more control is on the page and visible.</param>
    public SteamLoadMoreDecision ClassifyLoadMore(
        int clicksCompleted, int rowsBefore, int rowsAfter, bool controlPresent)
        => Classify(clicksCompleted, rowsBefore, rowsAfter, controlPresent, MaxLoadMoreClicks);

    /// <summary>
    /// Whether to follow the licences paginator to another page.
    ///
    /// <para>The same question as <see cref="ClassifyLoadMore"/> and deliberately
    /// the same answer type: the two pages grow by different mechanisms, but
    /// "is there another step, is it under the cap, did the last one produce
    /// rows" is one rule and is worth having in one place. Only the control and
    /// the cap differ.</para>
    /// </summary>
    /// <param name="pagesWalked">Pages already followed past the first.</param>
    /// <param name="rowsBefore">Licence rows counted before the last page was merged, or a negative number before the first.</param>
    /// <param name="rowsAfter">Licence rows counted now.</param>
    /// <param name="nextLinkPresent">Whether the paginator still offers a next page.</param>
    public SteamLoadMoreDecision ClassifyLicensesPage(
        int pagesWalked, int rowsBefore, int rowsAfter, bool nextLinkPresent)
        => Classify(pagesWalked, rowsBefore, rowsAfter, nextLinkPresent, MaxLicensesPages);

    private static SteamLoadMoreDecision Classify(
        int stepsTaken, int rowsBefore, int rowsAfter, bool controlPresent, int cap)
    {
        if (!controlPresent)
        {
            return SteamLoadMoreDecision.Exhausted;
        }

        if (stepsTaken >= cap)
        {
            return SteamLoadMoreDecision.ReachedCap;
        }

        // Only meaningful once a step has been taken: before the first there is
        // no "before" to have failed to grow past.
        if (stepsTaken > 0 && rowsAfter <= rowsBefore)
        {
            return SteamLoadMoreDecision.Stalled;
        }

        return SteamLoadMoreDecision.Continue;
    }

    private static string NormalisePath(string path)
        => "/" + path.Trim('/').ToLowerInvariant();
}
