namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Which kind of secret a <see cref="SteamCredential"/> carries.
/// </summary>
public enum SteamCredentialKind
{
    /// <summary>
    /// A key the user registers on Steam's developer site and pastes into
    /// settings. Never expires; unattended schedulers prefer it for that
    /// reason.
    /// </summary>
    ApiKey,

    /// <summary>
    /// A JWT minted by a WebView sign-in. Measured lifetime 24 h 22 m
    /// (docs/spikes/steam-web-session-auth.md, 2026-08-30). Also carries the
    /// account it belongs to, which the key does not.
    /// </summary>
    SessionToken,
}

/// <summary>
/// What the caller intends to do with the credential it asks for. The
/// <see cref="SteamCredentialSelector"/> uses this to pick the right kind.
/// </summary>
public enum SteamCredentialPurpose
{
    /// <summary>
    /// Scheduled background work (the 15-minute local pass, the 6-hour
    /// remote pass) with nobody watching and nobody able to intervene when
    /// a credential fails.
    /// </summary>
    Unattended,

    /// <summary>
    /// Work a person just asked for and is waiting on. A renewal that needs
    /// the user is cheap when the user is present, so the fuller credential
    /// is preferred.
    /// </summary>
    UserInitiated,
}

/// <summary>
/// A secret that authenticates a Steam Web API request. Replaces the
/// hand-concatenated <c>key=</c> that three call sites used before: a second
/// credential kind travelling in a different parameter name would have meant a
/// second concatenation at each site, and three chances to forget escaping or
/// to send both names at once. <see cref="AppendTo"/> is the only method
/// allowed to put a credential into a URI. <see cref="ToString"/> is redacted,
/// for the same reason <see cref="SteamApiKey.ToString"/> is.
/// </summary>
public sealed record SteamCredential
{
    /// <summary>The query parameter name an API key travels in. Valve's name,
    /// verified against their shipped store bundle
    /// (docs/spikes/steam-web-session-auth.md).</summary>
    public const string ApiKeyParameter = "key";

    /// <summary>The query parameter name a session token travels in. Valve's
    /// name, verified against Valve's store bundle and Playnite's source
    /// (docs/spikes/steam-web-session-auth.md).</summary>
    public const string SessionTokenParameter = "access_token";

    /// <summary>
    /// Clock-skew allowance for expiry checks: a token that expires inside the
    /// next two minutes is treated as already dead, because it would very
    /// likely die between the moment it is chosen and the moment the request
    /// reaches Valve.
    /// </summary>
    public static readonly TimeSpan DefaultSkew = TimeSpan.FromMinutes(2);

    private SteamCredential(
        SteamCredentialKind kind,
        string value,
        DateTimeOffset? expiresAt,
        string provenance,
        SteamId? steamId)
    {
        Kind = kind;
        Value = value;
        ExpiresAt = expiresAt;
        Provenance = provenance;
        SteamId = steamId;
    }

    /// <summary>Which kind of credential this is. Determines <see cref="ParameterName"/>.</summary>
    public SteamCredentialKind Kind { get; }

    /// <summary>The secret. Goes into a query string through <see cref="AppendTo"/> and nowhere else. Never logged, never stored by this type, never put in an exception message.</summary>
    public string Value { get; }

    /// <summary>When this credential stops being accepted. Always null for an API key.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Where it came from, for diagnostics. Never contains the value. Same role <see cref="SteamApiKey.Source"/> plays.</summary>
    public string Provenance { get; }

    /// <summary>The account this credential identifies. Non-null only for a session token; an API key can only learn its account by making a separate disclosure call.</summary>
    public SteamId? SteamId { get; }

    /// <summary>Length of the value, for diagnostics that want to say "a credential is present" credibly without saying what it is.</summary>
    public int Length => Value.Length;

    /// <summary>The URI parameter name, derived from <see cref="Kind"/>. Never chosen by a call site.</summary>
    public string ParameterName => Kind switch
    {
        SteamCredentialKind.SessionToken => SessionTokenParameter,
        _ => ApiKeyParameter,
    };

    public override string ToString()
        => $"SteamCredential(kind={Kind}, source={Provenance}, value redacted)";

    /// <summary>
    /// Whether this credential is still good at <paramref name="now"/>,
    /// allowing <paramref name="skew"/> for clock drift and network transit.
    /// An API key has no expiry and is always usable.
    /// </summary>
    public bool IsUsableAt(DateTimeOffset now, TimeSpan skew)
        => ExpiresAt is not { } expiry || now + skew < expiry;

    /// <summary>
    /// The only place a credential enters a URI. Picks <c>?</c> or
    /// <c>&amp;</c> for itself, appends exactly one parameter, and escapes
    /// the value. Escaping is not optional: a JWT carries <c>.</c>, <c>-</c>
    /// and <c>_</c>, other encodings carry <c>+</c> and <c>/</c>, and an
    /// unescaped value could smuggle a second parameter into the query.
    /// </summary>
    public string AppendTo(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return uri + separator + ParameterName + "=" + Uri.EscapeDataString(Value);
    }

    /// <summary>Blank or whitespace-only counts as unset rather than as a credential, the same rule <see cref="SteamApiKey.TryCreate"/> has always applied. Surrounding whitespace is trimmed.</summary>
    public static SteamCredential? TryCreateApiKey(string? value, string provenance)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : new SteamCredential(SteamCredentialKind.ApiKey, value.Trim(), null, provenance, null);

    /// <summary>
    /// Same blank-or-whitespace rule as <see cref="TryCreateApiKey"/>. A
    /// token also carries an expiry and the account it was minted for.
    /// </summary>
    public static SteamCredential? TryCreateSessionToken(
        string? value,
        string provenance,
        DateTimeOffset? expiresAt,
        SteamId? steamId)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : new SteamCredential(
                SteamCredentialKind.SessionToken, value.Trim(), expiresAt, provenance, steamId);

    /// <summary>
    /// Lifts an existing <see cref="SteamApiKey"/> onto this type, carrying
    /// <see cref="SteamApiKey.Source"/> across as <see cref="Provenance"/>.
    /// Null in, null out.
    /// </summary>
    public static SteamCredential? FromApiKey(SteamApiKey? key)
        => key is null ? null : TryCreateApiKey(key.Value, key.Source);
}
