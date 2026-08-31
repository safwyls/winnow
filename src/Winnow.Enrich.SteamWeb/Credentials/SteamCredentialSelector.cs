namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Pure credential selection: no IO, no clock of its own, no logging, every
/// input passed in. Ships now, with its tests, even though session inputs are
/// always null today; the seam exists so later stages are small.
/// </summary>
public static class SteamCredentialSelector
{
    /// <summary>
    /// Overload that uses <see cref="SteamCredential.DefaultSkew"/>.
    /// </summary>
    public static SteamCredential? Choose(
        SteamCredentialPurpose purpose,
        SteamCredential? apiKey,
        SteamCredential? session,
        DateTimeOffset now)
        => Choose(purpose, apiKey, session, now, SteamCredential.DefaultSkew);

    /// <summary>
    /// The binding decision (TASK-55 decision note 2, 2026-08-30):
    /// <see cref="SteamCredentialPurpose.Unattended"/> prefers the key, then
    /// a usable session. The key does not expire and needs no renewal; the
    /// token lives about a day and re-minting depends on a persisted refresh
    /// token and Valve's <c>jwt/finalizelogin</c> endpoint, so a renewal
    /// failure overnight costs a sync cycle the user cannot intervene in.
    /// <see cref="SteamCredentialPurpose.UserInitiated"/> prefers a usable
    /// session, then the key, because the user is present and a renewal that
    /// needs them is cheap. An expired session is chosen for neither purpose.
    ///
    /// <para>Returning null is a normal outcome, not an error. §5.1:
    /// enrichment must never block a user-facing path, and "the module
    /// declines" is how that is honoured.</para>
    /// </summary>
    public static SteamCredential? Choose(
        SteamCredentialPurpose purpose,
        SteamCredential? apiKey,
        SteamCredential? session,
        DateTimeOffset now,
        TimeSpan skew)
    {
        var key = apiKey is { Kind: SteamCredentialKind.ApiKey } candidateKey
            && candidateKey.IsUsableAt(now, skew)
                ? candidateKey
                : null;

        var token = session is { Kind: SteamCredentialKind.SessionToken } candidateToken
            && candidateToken.IsUsableAt(now, skew)
                ? candidateToken
                : null;

        return purpose is SteamCredentialPurpose.UserInitiated
            ? token ?? key
            : key ?? token;
    }
}
