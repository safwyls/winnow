using System.Security.Cryptography;
using System.Text;

namespace Winnow.Core.Auth;

/// <summary>
/// The OAuth <c>state</c> parameter: minted per attempt, sent on the
/// authorization URL, and required back — unmodified — before a code is spent.
///
/// <para>State is what binds a returned authorization code to <em>this</em>
/// attempt by <em>this</em> user. Without it, any page the embedded browser can
/// be steered to — a provider subpage, a social-login hop, an iframe, a popup —
/// can hand the flow a code that belongs to someone else, and the flow will
/// spend it. That is login-CSRF: the user ends up holding a session for an
/// account that is not theirs, and every subsequent library read is against the
/// attacker's library.</para>
///
/// <para>Pure computation over the BCL, so it lives in Core with the rest of the
/// prompt contract (§5.1: no IO, no net).</para>
/// </summary>
public static class AuthState
{
    /// <summary>
    /// Bytes of entropy behind one state value. 256 bits — far past the 128 RFC
    /// 6749 §10.12 asks for, and free: this is generated once per sign-in.
    /// </summary>
    public const int EntropyBytes = 32;

    /// <summary>
    /// Mints one state value: 256 cryptographic bits, base64url-encoded so it
    /// survives a query string without escaping.
    /// </summary>
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[EntropyBytes];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    /// <summary>
    /// Whether <paramref name="actual"/> is the state that was sent, compared in
    /// time that does not depend on where the two differ.
    ///
    /// <para>Both sides are hashed first and the digests are compared with
    /// <see cref="CryptographicOperations.FixedTimeEquals"/>. Hashing is not
    /// decoration: <c>FixedTimeEquals</c> is constant-time only for equal-length
    /// inputs and short-circuits on a length mismatch, so comparing the raw
    /// strings would leak the length of the expected state to a page that can
    /// retry. Fixed-width digests remove that channel entirely.</para>
    ///
    /// <para>A null or blank value on either side is false. "No state at all" is
    /// never a match; callers that legitimately have no state to check must not
    /// call this at all — see <see cref="AuthStateVerification.NotRequired"/>.</para>
    /// </summary>
    public static bool Matches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        Span<byte> expectedDigest = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> actualDigest = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedDigest);
        SHA256.HashData(Encoding.UTF8.GetBytes(actual), actualDigest);

        return CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest);
    }

    /// <summary>
    /// RFC 4648 §5 base64url without padding. Hand-rolled rather than
    /// <c>Base64Url.EncodeToString</c> so this compiles on any target the rest of
    /// Core does, and it is three replacements on a 44-character string.
    /// </summary>
    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
