using System.Security.Cryptography;
using System.Text;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// A one-way digest naming the credential that earned an account confirmation,
/// so a later pass can ask "is that credential still present and unchanged?"
///
/// <para><b>Never a secret.</b> The digest is written to the settings table,
/// which lives in the same file as the library and is copied by every backup.
/// The only question asked of it is the sameness one, which a digest answers and
/// a stored secret would answer no better.</para>
///
/// <para><b>The API key digest is frozen.</b> It is
/// <c>SHA256(key)</c> rendered lower-case hex, byte for byte what
/// <c>SteamPlaytimeBackfillService</c> wrote before this type existed. Changing
/// it would make every existing install's stored fingerprint stop matching its
/// own unchanged key, clearing a confirmation that was never in doubt and
/// charging the user a TASK-54 disclosure refetch to earn it back.</para>
///
/// <para><b>A session is digested over its account, not its token.</b> The
/// access token is replaced roughly daily and S6 will rotate the refresh token
/// too, so a digest over either would report "the credential changed" every
/// renewal and clear a confirmation nothing was wrong with. The account is what
/// actually has to stay the same, and it is the fact the confirmation is about
/// in the first place.</para>
/// </summary>
public static class SteamCredentialFingerprint
{
    /// <summary>
    /// Domain separator mixed into a session digest. Two purposes: a session
    /// fingerprint can never collide with an API key digest, so a stored value
    /// cannot be satisfied by the wrong kind of credential; and the version
    /// suffix leaves room for the input to change later without a new settings
    /// key.
    /// </summary>
    public const string SessionScope = "steam.session.v1|";

    /// <summary>
    /// The fingerprint of whichever kind <paramref name="credential"/> is, or
    /// null when there is no credential or when a session names no account.
    /// </summary>
    public static string? Of(SteamCredential? credential)
    {
        if (credential is null)
        {
            return null;
        }

        return credential.Kind == SteamCredentialKind.SessionToken
            ? OfSession(credential.SteamId)
            : OfApiKey(credential.Value);
    }

    /// <summary>
    /// <c>SHA256(key)</c> as lower-case hex. Frozen; see the type comment.
    /// </summary>
    public static string OfApiKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Digest(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// A session-scoped digest over the account the session is for, or null when
    /// it names none — an unidentified session has earned nothing and must not be
    /// able to satisfy a stored fingerprint.
    /// </summary>
    public static string? OfSession(SteamId? steamId)
        => steamId is not { } id ? null : Digest(Encoding.UTF8.GetBytes(SessionScope + id.ToString()));

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
