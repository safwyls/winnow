namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// One place a Steam session token might be stored. Deliberately mirrors
/// <see cref="ISteamApiKeySource"/>. Nothing implements this yet; it exists so
/// the WebView path plugs in without the provider changing.
/// </summary>
public interface ISteamSessionCredentialSource
{
    /// <summary>Name of this source, for diagnostics. Never contains a secret.</summary>
    string Name { get; }

    /// <summary>The token held here, or null when this source holds none.</summary>
    ValueTask<SteamCredential?> TryGetAsync(CancellationToken ct = default);
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
}
