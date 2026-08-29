using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Windows DPAPI (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>)
/// protection for the stored Epic session.
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
/// Protector for non-Windows hosts. Refuses to store rather than degrading to
/// plaintext; the session lives in memory only.
/// </summary>
public sealed class UnavailableEpicSecretProtector : IEpicSecretProtector
{
    public bool IsAvailable => false;

    public string Name => "unavailable";

    public string? Protect(string plaintext) => null;

    public string? Unprotect(string protectedBase64) => null;
}
