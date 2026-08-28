using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Windows DPAPI (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>)
/// protection for the stored Epic session.
///
/// <para><b>Why CurrentUser and not LocalMachine.</b> The two scopes differ in
/// who can decrypt, and the difference is the whole point.
/// <c>LocalMachine</c> ciphertext can be decrypted by <i>any</i> account on the
/// box, including services and other users' processes, which for a credential
/// granting full access to someone's Epic account is not protection at all.
/// <c>CurrentUser</c> keys the blob to the signed-in Windows user, so a second
/// account on the same machine — or an attacker who walks off with the SQLite
/// file — gets ciphertext they cannot open.</para>
///
/// <para><b>What this does and does not defend against.</b> It defends against
/// the database file being copied elsewhere, read by another user on the same
/// machine, or landing in a backup. It does not defend against malicious code
/// already running as this user, which can simply call <c>Unprotect</c> itself.
/// That is DPAPI's documented boundary and it is the same one every local
/// credential store on Windows lives within; it is recorded here so nobody
/// mistakes the guarantee for a stronger one.</para>
///
/// <para><b>The entropy parameter is used, and is not a secret.</b> The extra
/// entropy passed to <c>Protect</c>/<c>Unprotect</c> is a fixed application
/// string. It adds no cryptographic strength — it is in this source file — but
/// it does mean a blob written by Winnow cannot be decrypted by another
/// application that merely happens to run as the same user and call
/// <c>Unprotect</c> on bytes it found. Changing it invalidates every stored
/// session, which is why it is a constant and not a setting.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiEpicSecretProtector : IEpicSecretProtector
{
    /// <summary>
    /// Application-scoped entropy. Fixed, not secret, and never changed without
    /// accepting that every persisted session becomes unreadable (which degrades
    /// to a re-login, not to a crash).
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Winnow.Epic.OAuth.v1");

    private readonly ILogger<DpapiEpicSecretProtector> _log;

    public DpapiEpicSecretProtector(ILogger<DpapiEpicSecretProtector>? log = null)
        => _log = log ?? NullLogger<DpapiEpicSecretProtector>.Instance;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string Name => "dpapi:CurrentUser";

    public string? Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Convert.ToBase64String(
                ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // No exception detail. A CryptographicException from DPAPI carries a
            // Win32 message, not the plaintext — but this class handles a bearer
            // credential and the standing rule here is that nothing on a failure
            // path involving one gets to write free-form text to a log.
            _log.LogWarning(
                "Epic session could not be encrypted with DPAPI; it will not be persisted. "
                + "Sign-in still works for this run and is simply not remembered across restarts.");
            return null;
        }
        finally
        {
            // The plaintext held a bearer token. Nothing can be done about the
            // immutable string the caller handed over, but the copy this method
            // made is wiped rather than left for the GC to move around the heap.
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? Unprotect(string protectedBase64)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(protectedBase64))
        {
            return null;
        }

        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(protectedBase64);
        }
        catch (FormatException)
        {
            // Not base64 at all. Most likely a value written by an older or
            // different build; treat it as no session rather than as an error.
            _log.LogWarning("Stored Epic session is not readable (bad encoding); a fresh sign-in is required.");
            return null;
        }

        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // The ordinary causes are all benign and all mean the same thing:
            // a different Windows user, a profile restored onto another machine,
            // or a truncated value. There is nothing to recover and nothing to
            // report to the user beyond "sign in again".
            _log.LogInformation(
                "Stored Epic session could not be decrypted by this Windows user; a fresh sign-in is required.");
            return null;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}

/// <summary>
/// The protector used where DPAPI does not exist — every non-Windows host.
///
/// <para><b>It refuses; it does not degrade.</b> <see cref="Protect"/> returns
/// null, so the token store writes nothing at all. The alternative — falling
/// back to storing the session in the clear because encryption was unavailable —
/// is the exact failure this type exists to make impossible, and it is why
/// protection is an interface with an explicit unavailable implementation rather
/// than a nullable dependency somewhere that a null check could be forgotten
/// on.</para>
///
/// <para>The functional consequence is mild: on a non-Windows host the Epic
/// session lives in memory for the lifetime of the process and the user signs in
/// again after a restart. Winnow's Epic ingest is Windows-shaped anyway — the
/// launcher's data root is discovered through the registry — so this is a
/// theoretical host rather than a supported one.</para>
/// </summary>
public sealed class UnavailableEpicSecretProtector : IEpicSecretProtector
{
    public bool IsAvailable => false;

    public string Name => "unavailable";

    public string? Protect(string plaintext) => null;

    public string? Unprotect(string protectedBase64) => null;
}
