namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Encrypts and decrypts the stored Epic session at rest.
///
/// <para>An abstraction rather than a direct <c>ProtectedData</c> call for one
/// reason: <c>ProtectedData</c> is Windows-only, and the alternative to an
/// abstraction is a <c>#if</c> or a runtime branch that ends in a plaintext
/// fallback. There must be no plaintext fallback — see
/// <see cref="UnavailableEpicSecretProtector"/>, which refuses rather than
/// degrades.</para>
/// </summary>
public interface IEpicSecretProtector
{
    /// <summary>
    /// Whether this protector can actually encrypt. False means the session will
    /// not be persisted at all; it does not mean it will be persisted in the
    /// clear.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Name of the protection scheme, for diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns it base64-encoded, or
    /// null when protection is unavailable or failed.
    /// </summary>
    string? Protect(string plaintext);

    /// <summary>
    /// Decrypts what <see cref="Protect"/> produced, or null when the input is
    /// unreadable — a different Windows user, a restored profile, a corrupted or
    /// truncated value. Null is always "start over", never an exception: an
    /// unreadable stored session is exactly the degrade-to-local case.
    /// </summary>
    string? Unprotect(string protectedBase64);
}
