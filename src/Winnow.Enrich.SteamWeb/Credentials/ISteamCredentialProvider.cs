namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// How much a caller is willing to pay for a current token.
/// <see cref="None"/> is a plain read: it never sends a request and never
/// waits, and is what the Stores screen and the credential inventory use.
/// <see cref="WhenDue"/> lets the source renew first and wait for the answer,
/// and is chosen only where the session is the credential that will actually
/// be spent.
/// </summary>
public enum SteamSessionRenewalMode
{
    /// <summary>Plain read, never renews.</summary>
    None,

    /// <summary>Renew first if the session is due, then return.</summary>
    WhenDue,
}

/// <summary>
/// One place a Steam session token might be stored. Deliberately mirrors
/// <see cref="ISteamApiKeySource"/>.
/// </summary>
public interface ISteamSessionCredentialSource
{
    /// <summary>Name of this source, for diagnostics. Never contains a secret.</summary>
    string Name { get; }

    /// <summary>The token held here, or null when this source holds none.</summary>
    ValueTask<SteamCredential?> TryGetAsync(CancellationToken ct = default);

    /// <summary>
    /// The token held here, renewing first if <paramref name="mode"/> allows
    /// it and the session is due. Default-implemented as the plain read so a
    /// source that cannot renew needs no code.
    /// </summary>
    ValueTask<SteamCredential?> TryGetAsync(SteamSessionRenewalMode mode, CancellationToken ct = default)
        => TryGetAsync(ct);

    /// <summary>
    /// Steam answered 401 to a request carrying
    /// <paramref name="rejectedToken"/>. Returns a replacement to retry with
    /// exactly once, or null when there is none to be had.
    /// Default-implemented as "no replacement".
    /// </summary>
    ValueTask<SteamCredential?> RenewAfterUnauthorizedAsync(
        string rejectedToken, CancellationToken ct = default)
        => ValueTask.FromResult<SteamCredential?>(null);
}

/// <summary>
/// What credentials are registered, with no secret anywhere in it. The Stores
/// screen reads this to show the two connection methods and their state.
/// </summary>
public sealed record SteamCredentialInventory(
    bool HasApiKey,
    string? ApiKeySource,
    bool HasSession,
    string? SessionSource,
    DateTimeOffset? SessionExpiresAt,
    bool SessionUsable,
    SteamId? SessionAccount)
{
    /// <summary>The state of a user who has connected nothing.</summary>
    public static readonly SteamCredentialInventory Empty =
        new(false, null, false, null, null, false, null);

    /// <summary>Whether anything is registered, usable or not. A session whose renewal has failed is still registered; the distinction matters because the UI must surface that state rather than silently degrade past it (TASK-55 decision note 4).</summary>
    public bool HasAnyCredential => HasApiKey || HasSession;

    /// <summary>Whether a registered credential can actually be sent right now.</summary>
    public bool HasUsableCredential => HasApiKey || SessionUsable;
}

/// <summary>
/// Resolves the credential for one call, or reports what is configured. Null
/// from <see cref="GetAsync"/> is ordinary, not an error (§5.1: enrichment
/// must never block a user-facing path). Mirrors
/// <see cref="ISteamApiKeyProvider"/>, <c>IIgdbCredentialProvider</c> and
/// <c>IEpicCredentialProvider</c> so all four credential paths behave the
/// same way.
/// </summary>
public interface ISteamCredentialProvider
{
    /// <summary>The credential to use for <paramref name="purpose"/>, or null when none is configured or usable.</summary>
    ValueTask<SteamCredential?> GetAsync(
        SteamCredentialPurpose purpose, CancellationToken ct = default);

    /// <summary>What exists, without exposing a secret.</summary>
    ValueTask<SteamCredentialInventory> GetInventoryAsync(CancellationToken ct = default);

    /// <summary>Drops memoised state so the next call re-reads every source. Call after the user edits their key.</summary>
    void Invalidate();

    /// <summary>
    /// Steam rejected <paramref name="rejected"/> with a 401. Returns a
    /// replacement to retry the request with, exactly once, or null to give
    /// up for this pass.
    ///
    /// <para>A 401 is a clean trigger only because nothing else consumes it:
    /// <see cref="Http.SteamWebResilienceHandler"/> retries 429, 408 and 5xx
    /// and deliberately does NOT list 401, so a 401 that reaches a call site
    /// is Steam's final answer about the credential rather than a blip worth
    /// waiting out.</para>
    ///
    /// <para>Only a session token can be replaced. A rejected API key is
    /// answered with null: re-reading the settings table would return the same
    /// wrong key, and the remedy is the user pasting a new one.</para>
    /// </summary>
    ValueTask<SteamCredential?> RenewAfterUnauthorizedAsync(
        SteamCredential rejected, CancellationToken ct = default)
        => ValueTask.FromResult<SteamCredential?>(null);
}
