namespace Winnow.Core.Auth;

/// <summary>Which OAuth grant type the captured code feeds.</summary>
public enum AuthCodeKind
{
    /// <summary>A standard OAuth authorization code (<c>grant_type=authorization_code</c>).</summary>
    AuthorizationCode = 0,

    /// <summary>A launcher exchange code (<c>grant_type=exchange_code</c>).</summary>
    ExchangeCode = 1,
}

/// <summary>How a prompt attempt ended. All outcomes are non-exceptional.</summary>
public enum AuthPromptOutcome
{
    /// <summary>A code was captured.</summary>
    Captured = 0,

    /// <summary>The user closed the window or declined. Never retried.</summary>
    Cancelled = 1,

    /// <summary>This prompt cannot run here (no browser/window/console). Caller tries the next prompt.</summary>
    Unavailable = 2,

    /// <summary>The prompt ran but produced no code. Caller tries the next prompt.</summary>
    Failed = 3,

    /// <summary>
    /// The provider answered that there is no authenticated session (all code fields present but null).
    /// Distinct from <see cref="Failed"/>: the remedy is to sign in, not to fall back to manual capture.
    /// </summary>
    NoSession = 4,
}

/// <summary>Flags for which capture mechanisms a prompt may arm simultaneously.</summary>
[Flags]
public enum AuthCaptureStrategies
{
    /// <summary>Nothing armed. A prompt given this can only fail.</summary>
    None = 0,

    /// <summary>Inject the launcher's JS bridge; yields an <see cref="AuthCodeKind.ExchangeCode"/>.</summary>
    LauncherJsBridge = 1,

    /// <summary>Intercept the redirect to <see cref="AuthPromptRequest.RedirectUrl"/> and read the code from the query string.</summary>
    RedirectInterception = 2,

    /// <summary>Read the page body as JSON and extract <see cref="AuthPromptRequest.JsonCodeFields"/>.</summary>
    JsonBodyScrape = 4,

    /// <summary>
    /// Once on the provider's origin, fetch <see cref="AuthPromptRequest.HarvestUrl"/>
    /// directly rather than waiting for the provider to volunteer the code.
    /// </summary>
    SessionHarvest = 8,

    /// <summary>All four.</summary>
    All = LauncherJsBridge | RedirectInterception | JsonBodyScrape | SessionHarvest,
}

/// <summary>A JSON field that may carry a code, and which grant it feeds.</summary>
/// <param name="FieldName">Property name on the JSON object, matched exactly.</param>
/// <param name="Kind">Which grant the value is spent on.</param>
public sealed record AuthJsonCodeField(string FieldName, AuthCodeKind Kind);

/// <summary>
/// Everything a prompt needs to run one interactive sign-in. Provider-neutral:
/// the provider identity is carried entirely as URL/field values.
/// </summary>
public sealed record AuthPromptRequest
{
    /// <summary>The storefront being signed into, for window titles and log lines. Never a secret.</summary>
    public required string ProviderName { get; init; }

    /// <summary>The page the sign-in starts on. Must render a login form for an unauthenticated browser.</summary>
    public required Uri StartUrl { get; init; }

    /// <summary>Consent text shown before anything opens. Implementations must display it and require affirmative acceptance.</summary>
    public required string ConsentNotice { get; init; }

    /// <summary>
    /// The provider's registered redirect target for <see cref="AuthCaptureStrategies.RedirectInterception"/>.
    /// Matched on scheme/host/path. No listener needed; navigation is intercepted before connection.
    /// </summary>
    public Uri? RedirectUrl { get; init; }

    /// <summary>Query parameter on <see cref="RedirectUrl"/> that carries the code.</summary>
    public string RedirectCodeParameter { get; init; } = "code";

    /// <summary>
    /// Provider-origin URL that returns the code as JSON for an already-authenticated browser,
    /// used by <see cref="AuthCaptureStrategies.SessionHarvest"/>. Not a starting page --
    /// it returns nulls without a session and never renders a login form.
    /// </summary>
    public Uri? HarvestUrl { get; init; }

    /// <summary>JSON fields to look for in a code-bearing body, in priority order.</summary>
    public IReadOnlyList<AuthJsonCodeField> JsonCodeFields { get; init; } = [];

    /// <summary>Which mechanisms this prompt may arm.</summary>
    public AuthCaptureStrategies Strategies { get; init; } = AuthCaptureStrategies.All;

    /// <summary>Browser profile directory name, isolating cookies per provider.</summary>
    public string ProfileKey { get; init; } = "default";

    /// <summary>How long to wait for the user before giving up.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>The outcome of one prompt attempt. Carries a code or a reason, never an exception.</summary>
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

    /// <summary>The captured code. Never logged or stored -- equivalent to full account access.</summary>
    public string? Code { get; }

    /// <summary>Which mechanism produced the code, for diagnostics. Never contains the code itself.</summary>
    public string? Via { get; }

    /// <summary>A safe one-line reason. Never contains a code, token or secret.</summary>
    public string? Detail { get; }

    /// <summary>A captured code, and which mechanism produced it.</summary>
    public static AuthCodeResult Captured(AuthCodeKind kind, string code, string via)
        => new(AuthPromptOutcome.Captured, kind, code, via, null);

    /// <summary>The user backed out.</summary>
    public static AuthCodeResult Cancelled(string? detail = null)
        => new(AuthPromptOutcome.Cancelled, default, null, null, detail);

    /// <summary>This prompt cannot run here.</summary>
    public static AuthCodeResult Unavailable(string? detail = null)
        => new(AuthPromptOutcome.Unavailable, default, null, null, detail);

    /// <summary>The prompt ran and produced nothing.</summary>
    public static AuthCodeResult Failed(string? detail = null)
        => new(AuthPromptOutcome.Failed, default, null, null, detail);

    /// <summary>No authenticated session. Distinct from <see cref="Failed"/>: capture worked, nobody was signed in.</summary>
    public static AuthCodeResult NoSession(string? detail = null)
        => new(AuthPromptOutcome.NoSession, default, null, null, detail);

    /// <summary>Redacted to prevent the code from leaking into log lines.</summary>
    public override string ToString()
        => Outcome == AuthPromptOutcome.Captured
            ? $"AuthCodeResult(captured {Kind} via {Via}, value redacted)"
            : $"AuthCodeResult({Outcome}{(Detail is null ? string.Empty : ": " + Detail)})";
}

/// <summary>
/// Shows a provider's sign-in page and returns a code. Contract only (no IO);
/// implementations live in Winnow.Auth.WebView and Winnow.App.
/// Expected failures return an <see cref="AuthCodeResult"/> with a reason, never throw.
/// </summary>
public interface IInteractiveAuthPrompt
{
    /// <summary>Short human name, for logs and for telling the user which route was used.</summary>
    string Name { get; }

    /// <summary>Whether this prompt can run on this machine right now. Must not open a window or do IO.</summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Shows consent, opens the sign-in page, and returns the captured code.</summary>
    Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest request, CancellationToken ct = default);
}
