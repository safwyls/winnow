namespace Hoard.Core.Auth;

/// <summary>
/// Which OAuth grant the captured value feeds. The two are not
/// interchangeable — they go to different <c>grant_type</c>s — so the capture
/// mechanism has to say which one it produced rather than leaving the caller to
/// guess from the string's shape.
/// </summary>
public enum AuthCodeKind
{
    /// <summary>
    /// A standard OAuth authorization code, spent as
    /// <c>grant_type=authorization_code</c>. What Epic's <c>id/api/redirect</c>
    /// page prints, and what a <c>?code=</c> redirect carries.
    /// </summary>
    AuthorizationCode = 0,

    /// <summary>
    /// A launcher exchange code, spent as <c>grant_type=exchange_code</c>. What
    /// Epic's sign-in page hands to a host that implements the launcher's
    /// JavaScript bridge — a different value on a different grant, never an
    /// authorization code by another name.
    /// </summary>
    ExchangeCode = 1,
}

/// <summary>
/// How a prompt attempt ended. Every one of these is an ordinary outcome the
/// caller handles by leaving the existing local ingest exactly as it was; none
/// of them is an exception.
/// </summary>
public enum AuthPromptOutcome
{
    /// <summary>A code was captured.</summary>
    Captured = 0,

    /// <summary>
    /// The user closed the window, declined the consent step, or entered
    /// nothing. Deliberate, and never retried or escalated to another prompt.
    /// </summary>
    Cancelled = 1,

    /// <summary>
    /// This prompt cannot run here at all — no browser runtime, no window
    /// system, no console. The caller falls through to the next prompt.
    /// </summary>
    Unavailable = 2,

    /// <summary>
    /// The prompt ran and did not produce a code: the provider changed its page,
    /// the network went away, or the flow finished with nothing to capture. The
    /// caller falls through to the next prompt.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The provider's code endpoint answered, and answered that there is no
    /// authenticated session — every code field present and null.
    ///
    /// <para><b>Named separately from <see cref="Failed"/> because the remedy is
    /// the opposite one.</b> "Failed" says the capture broke and the manual flow
    /// is the way round it; this says nobody signed in, which is a thing the user
    /// can simply do. The embedded prompt handles it internally by sending the
    /// user to a login page and asking again, so seeing it come back out means
    /// even that did not work — but it must still be reported as what it is
    /// rather than flattened into a generic miss.</para>
    /// </summary>
    NoSession = 4,
}

/// <summary>
/// Which capture mechanisms a prompt may arm. A flags set rather than an ordered
/// list, because an embedded browser can arm all of them in ONE sign-in and take
/// whichever fires first — see <see cref="AuthPromptRequest.Strategies"/>.
/// </summary>
[Flags]
public enum AuthCaptureStrategies
{
    /// <summary>Nothing armed. A prompt given this can only fail.</summary>
    None = 0,

    /// <summary>
    /// Inject the launcher's JavaScript bridge and wait for the page to call it.
    /// Yields an <see cref="AuthCodeKind.ExchangeCode"/>.
    /// </summary>
    LauncherJsBridge = 1,

    /// <summary>
    /// Watch navigations for <see cref="AuthPromptRequest.RedirectUrl"/> and read
    /// <see cref="AuthPromptRequest.RedirectCodeParameter"/> out of its query
    /// string. Yields an <see cref="AuthCodeKind.AuthorizationCode"/>.
    /// </summary>
    RedirectInterception = 2,

    /// <summary>
    /// Read the completed page's body as JSON and pull
    /// <see cref="AuthPromptRequest.JsonCodeFields"/> out of it.
    /// </summary>
    JsonBodyScrape = 4,

    /// <summary>
    /// Once the browser is on the provider's own origin, ask
    /// <see cref="AuthPromptRequest.HarvestUrl"/> for the code directly instead
    /// of waiting for the provider to volunteer it.
    ///
    /// <para><b>This is what makes the flow independent of the provider's
    /// post-sign-in behaviour.</b> The other three routes all wait for the
    /// provider to DO something — call a bridge, follow a redirect, render a
    /// page. Each of those is plausible and none of them is guaranteed. This one
    /// stops hoping and goes and asks, which is the difference between a flow
    /// that works and a flow that works if a hypothesis holds.</para>
    /// </summary>
    SessionHarvest = 8,

    /// <summary>All four.</summary>
    All = LauncherJsBridge | RedirectInterception | JsonBodyScrape | SessionHarvest,
}

/// <summary>
/// A JSON field a rendered response body may carry a code in, and which grant
/// that code feeds.
/// </summary>
/// <param name="FieldName">Property name on the JSON object, matched exactly.</param>
/// <param name="Kind">Which grant the value is spent on.</param>
public sealed record AuthJsonCodeField(string FieldName, AuthCodeKind Kind);

/// <summary>
/// Everything a prompt needs to run one interactive sign-in, and nothing about
/// tokens, grants or storage.
///
/// <para><b>Provider-neutral on purpose.</b> The strings that make this Epic —
/// the start URL, the registered redirect, the JSON field names — are values,
/// not code, so <c>Hoard.Core</c> and the browser host both stay ignorant of
/// which storefront is being signed into. A GOG request would differ only in
/// these fields.</para>
/// </summary>
public sealed record AuthPromptRequest
{
    /// <summary>The storefront being signed into, for window titles and log lines. Never a secret.</summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// The page the sign-in starts on. <b>Must render a login form for an
    /// unauthenticated browser</b> — see <see cref="HarvestUrl"/> for the trap
    /// this requirement exists because of. An embedded browser opens an isolated
    /// profile with no cookies, so "unauthenticated" is the state of every
    /// first-time run.
    /// </summary>
    public required Uri StartUrl { get; init; }

    /// <summary>
    /// What the user is told, in plain words, BEFORE anything opens.
    ///
    /// <para><b>Every implementation must display this and take a deliberate
    /// affirmative act before navigating.</b> This is not a courtesy string. The
    /// console flow used to show Epic's own warning at the moment the user copied
    /// the code — "Do not share this code with any 3rd party service. It allows
    /// full access to your Epic account." — and an embedded browser makes the
    /// code invisible, which removes the one moment the user could have
    /// reconsidered. Moving that moment earlier is the whole reason this field
    /// exists; a prompt that navigates without showing it has silently dropped
    /// the consent step because the flow got smoother.</para>
    /// </summary>
    public required string ConsentNotice { get; init; }

    /// <summary>
    /// The provider's registered redirect target, watched by
    /// <see cref="AuthCaptureStrategies.RedirectInterception"/>. Matched on
    /// scheme, host and path; the query is what carries the code.
    ///
    /// <para>Nothing needs to listen on this address. A navigation is observable
    /// before the connection is attempted, so an unroutable host works fine —
    /// which is what makes an https loopback redirect interceptable with no
    /// listener and no certificate.</para>
    /// </summary>
    public Uri? RedirectUrl { get; init; }

    /// <summary>Query parameter on <see cref="RedirectUrl"/> that carries the code.</summary>
    public string RedirectCodeParameter { get; init; } = "code";

    /// <summary>
    /// A URL on the provider's own origin that returns the code as JSON <i>for a
    /// browser that already has a session</i>, used by
    /// <see cref="AuthCaptureStrategies.SessionHarvest"/>.
    ///
    /// <para><b>This is NOT a starting page, and the distinction is the whole
    /// lesson of this flow's first real run.</b> An endpoint that answers with a
    /// code for an authenticated browser answers a cold one with every code field
    /// null — it never renders a login form, because it is an API, not a page.
    /// Starting there means every first-time sign-in lands on a JSON body full of
    /// nulls and no user ever sees a password box. <see cref="StartUrl"/> has to
    /// be somewhere that renders a login form; this is where the code is
    /// collected from afterwards.</para>
    /// </summary>
    public Uri? HarvestUrl { get; init; }

    /// <summary>
    /// JSON fields to look for when the flow ends on a rendered JSON body, in
    /// priority order.
    /// </summary>
    public IReadOnlyList<AuthJsonCodeField> JsonCodeFields { get; init; } = [];

    /// <summary>Which mechanisms this prompt may arm.</summary>
    public AuthCaptureStrategies Strategies { get; init; } = AuthCaptureStrategies.All;

    /// <summary>
    /// Names the browser profile directory, so two providers do not share cookies
    /// and a signed-in session survives between attempts.
    /// </summary>
    public string ProfileKey { get; init; } = "default";

    /// <summary>
    /// How long to wait for the user before giving up with
    /// <see cref="AuthPromptOutcome.Cancelled"/>. Generous: it has to cover
    /// finding a password manager and a 2FA prompt on a phone.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// The outcome of one prompt attempt. Carries a code or a reason, never an
/// exception.
/// </summary>
public sealed record AuthCodeResult
{
    private AuthCodeResult(AuthPromptOutcome outcome, AuthCodeKind kind, string? code, string? via, string? detail)
    {
        Outcome = outcome;
        Kind = kind;
        Code = code;
        Via = via;
        Detail = detail;
    }

    /// <summary>How the attempt ended.</summary>
    public AuthPromptOutcome Outcome { get; }

    /// <summary>Which grant <see cref="Code"/> is spent on. Meaningless unless captured.</summary>
    public AuthCodeKind Kind { get; }

    /// <summary>
    /// The captured code. Single-use, short-lived, and equivalent to full account
    /// access — so it is never logged, never stored, never placed in a URI, and
    /// never included in <see cref="ToString"/>.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Which mechanism produced the code, for diagnostics. Free of the code
    /// itself, and the only way to find out which of the three capture routes a
    /// real sign-in actually takes.
    /// </summary>
    public string? Via { get; }

    /// <summary>A safe one-line reason. Never contains a code, token or secret.</summary>
    public string? Detail { get; }

    /// <summary>A captured code, and which mechanism produced it.</summary>
    public static AuthCodeResult Captured(AuthCodeKind kind, string code, string via)
        => new(AuthPromptOutcome.Captured, kind, code, via, null);

    /// <summary>The user backed out. Not retried, and not escalated to another prompt.</summary>
    public static AuthCodeResult Cancelled(string? detail = null)
        => new(AuthPromptOutcome.Cancelled, default, null, null, detail);

    /// <summary>This prompt cannot run here. The caller tries the next one.</summary>
    public static AuthCodeResult Unavailable(string? detail = null)
        => new(AuthPromptOutcome.Unavailable, default, null, null, detail);

    /// <summary>The prompt ran and produced nothing. The caller tries the next one.</summary>
    public static AuthCodeResult Failed(string? detail = null)
        => new(AuthPromptOutcome.Failed, default, null, null, detail);

    /// <summary>
    /// The provider answered that there is no authenticated session. Distinct
    /// from <see cref="Failed"/>: the capture worked, there was just nobody
    /// signed in.
    /// </summary>
    public static AuthCodeResult NoSession(string? detail = null)
        => new(AuthPromptOutcome.NoSession, default, null, null, detail);

    /// <summary>
    /// Redacted. The compiler-generated record <c>ToString</c> would print
    /// <see cref="Code"/> the first time anyone interpolated one of these into a
    /// log line — which is exactly how a full-account credential reaches a log
    /// file. Same reasoning as <c>EpicClientCredentials.ToString</c>.
    /// </summary>
    public override string ToString()
        => Outcome == AuthPromptOutcome.Captured
            ? $"AuthCodeResult(captured {Kind} via {Via}, value redacted)"
            : $"AuthCodeResult({Outcome}{(Detail is null ? string.Empty : ": " + Detail)})";
}

/// <summary>
/// One way of putting a provider's own sign-in page in front of the user and
/// getting a code back.
///
/// <para><b>This is a contract only.</b> It lives in <c>Hoard.Core</c>, which
/// does no IO and references neither Avalonia nor WebView2, so that
/// <c>Hoard.Ingest.*</c> can depend on the SEAM without ever depending on a UI
/// framework (§5.1). The embedded-browser implementation lives in
/// <c>Hoard.Auth.WebView</c>; the console implementation lives in
/// <c>Hoard.App</c>. Neither is the "real" one and neither is a legacy path — a
/// headless machine and a machine with no WebView2 runtime both need the console
/// peer, and a provider breaking its sign-in page needs it too.</para>
///
/// <para><b>Nothing here throws for an expected condition.</b> No runtime, no
/// window, no console, a user who closed the window, a provider that changed its
/// page — all of them are an <see cref="AuthCodeResult"/> carrying a reason. The
/// caller's response is always the same and always safe: no session this time,
/// and the local readers carry on untouched.</para>
/// </summary>
public interface IInteractiveAuthPrompt
{
    /// <summary>Short human name, for logs and for telling the user which route was used.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this prompt can run on this machine right now. False is ordinary —
    /// a headless host has no window and a service has no console — and the
    /// caller falls through to the next prompt.
    ///
    /// <para>Must not open a window, print anything, or make a network call.</para>
    /// </summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Shows <see cref="AuthPromptRequest.ConsentNotice"/>, waits for the user to
    /// accept it, puts the provider's own page in front of them, and returns the
    /// code the provider issues.
    ///
    /// <para>Hoard never sees the user's password: authentication happens on the
    /// provider's own page, and only the resulting code crosses this
    /// boundary.</para>
    /// </summary>
    Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest request, CancellationToken ct = default);
}
