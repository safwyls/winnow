namespace Hoard.Ingest.Epic.Web.Credentials;

/// <summary>
/// The OAuth client Hoard authenticates <i>as</i> when it talks to Epic's
/// account and library services — an id and secret pair, stored locally, never
/// logged.
///
/// <para><b>Where the pair comes from, and how that changed.</b> Epic has no
/// third-party registration path that reaches the storefront library. Epic
/// Account Services will issue a real client to anyone, but its consent scopes
/// stop at <c>basic_profile</c> / <c>friends_list</c> / <c>presence</c> /
/// <c>country</c> — none of which can read entitlements. The
/// <c>library:public:items</c> and playtime permissions live only on Epic's own
/// launcher client, <c>launcherAppClient2</c>, whose id and secret were
/// extracted from the launcher binary and have circulated publicly since 2020.
/// Every tool in this space — Legendary, Heroic, Rare — authenticates as that
/// client.</para>
///
/// <para>This module originally refused to ship that pair, and required the user
/// to supply it. <b>That was reversed on 2026-08-26</b> when the sign-in button
/// was built: a button cannot ask for an OAuth client secret, and there is no
/// client the user could supply instead, so the alternative to embedding was the
/// feature not existing. The reasoning, the cost and the precedence rule are all
/// recorded on <see cref="BuiltInEpicCredentialSource"/>, which is the LAST
/// source consulted — a user-supplied pair still wins.</para>
///
/// <para>The credential is still never committed to a log, never printed, and
/// never leaves this machine except in the HTTP Basic header of a request to
/// Epic's own token endpoint. See <c>docs/spikes/epic-oauth.md</c> §12 for the
/// risks a user accepts by signing in.</para>
/// </summary>
public sealed record EpicClientCredentials
{
    private EpicClientCredentials(string clientId, string clientSecret, string source)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Source = source;
    }

    /// <summary>OAuth client id. Goes into an HTTP Basic header and nowhere else.</summary>
    public string ClientId { get; }

    /// <summary>OAuth client secret. Goes into an HTTP Basic header and nowhere else.</summary>
    public string ClientSecret { get; }

    /// <summary>Where the pair came from, for diagnostics. Never contains either value.</summary>
    public string Source { get; }

    /// <summary>
    /// Redacted. The compiler-generated record <c>ToString</c> would print both
    /// halves the first time anyone interpolated one of these into a log line, a
    /// structured logging argument or an exception message — which is precisely
    /// how secrets reach log files.
    ///
    /// <para>The client id is redacted too, even though it is not secret on its
    /// own. It identifies <i>which</i> Epic client is being impersonated, and a
    /// log that names it is a log that documents the impersonation.</para>
    /// </summary>
    public override string ToString() => $"EpicClientCredentials(source={Source}, values redacted)";

    /// <summary>
    /// Both halves, or null. A half-configured pair is not a credential: sending
    /// a client id with an empty secret produces Epic's
    /// <c>invalid_client_credentials</c> (verified live 2026-08-26: HTTP 400,
    /// numericErrorCode 18033) rather than anything useful, so it is treated as
    /// "not configured" and never sent.
    /// </summary>
    public static EpicClientCredentials? TryCreate(string? clientId, string? clientSecret, string source)
        => string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new EpicClientCredentials(clientId.Trim(), clientSecret.Trim(), source);
}
