namespace Hoard.Ingest.Epic.Web.Credentials;

/// <summary>
/// The OAuth client Hoard authenticates <i>as</i> when it talks to Epic's
/// account and library services — an id and secret pair, supplied by the user,
/// stored locally, never logged, never committed.
///
/// <para><b>Why the user supplies these, and Hoard does not ship them.</b> This
/// is the single most consequential decision in this module, so it is written
/// down here rather than left to be rediscovered.</para>
///
/// <para>Epic has no third-party registration path that reaches the storefront
/// library. Epic Account Services will issue a real client to anyone, but its
/// consent scopes stop at <c>basic_profile</c> / <c>friends_list</c> /
/// <c>presence</c> / <c>country</c> — none of which can read entitlements. The
/// <c>library:public:items</c> and playtime permissions live only on Epic's own
/// launcher client, <c>launcherAppClient2</c>, whose id and secret were
/// extracted from the launcher binary and have circulated publicly since 2020.
/// Every tool in this space — Legendary, Heroic, Rare — authenticates as that
/// client.</para>
///
/// <para><b>Hoard could do the same, and deliberately does not.</b> Baking
/// Epic's secret into this repository would put a credential Hoard has no right
/// to into every checkout and every shipped binary, which is exactly what
/// "API keys are user-supplied, stored locally, never logged, never committed"
/// exists to prevent. It would also make Hoard, rather than the person running
/// it, the party impersonating Epic's launcher. So the pair is a setting like
/// the Steam key and the IGDB pair: absent by default, and absent means this
/// whole module is a no-op and the local readers carry on untouched
/// (<see cref="EpicLibrarySource"/>).</para>
///
/// <para>The practical consequence is honest rather than convenient: a user who
/// wants API-sourced Epic ownership has to find and enter a client id and secret
/// themselves, and in doing so decides for themselves whether to use Epic's
/// launcher credentials. See <c>docs/spikes/epic-oauth.md</c> for what that
/// choice involves.</para>
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
