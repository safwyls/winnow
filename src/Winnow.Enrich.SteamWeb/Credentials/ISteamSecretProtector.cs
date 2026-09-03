namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Encrypts and decrypts the stored Steam session at rest.
///
/// <para>An abstraction rather than a direct <c>ProtectedData</c> call for one
/// reason: <c>ProtectedData</c> is Windows-only, and the alternative to an
/// abstraction is a <c>#if</c> or a runtime branch that ends in a plaintext
/// fallback. There must be no plaintext fallback; see
/// <see cref="UnavailableSteamSecretProtector"/>, which refuses rather than
/// degrades. This is the same contract
/// <c>Winnow.Ingest.Epic.Web.Auth.IEpicSecretProtector</c> states, restated here
/// rather than shared because the two modules do not reference one another and
/// the entropy each uses must stay distinct.</para>
/// </summary>
public interface ISteamSecretProtector
{
    /// <summary>
    /// Whether this protector can actually encrypt. False means the session will
    /// not be persisted at all; it does not mean it will be persisted in the
    /// clear.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Name of the protection scheme, for diagnostics. Never contains a secret.</summary>
    string Name { get; }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns it base64-encoded, or
    /// null when protection is unavailable or failed.
    /// </summary>
    string? Protect(string plaintext);

    /// <summary>
    /// Decrypts what <see cref="Protect"/> produced, or null when the input is
    /// unreadable: a different Windows user, a restored profile, a corrupted or
    /// truncated value. Null is always "sign in again", never an exception; the
    /// key path and the local readers are unaffected.
    /// </summary>
    string? Unprotect(string protectedBase64);
}
