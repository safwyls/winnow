using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.Igdb.Credentials;

/// <summary>
/// Protection for the stored IGDB client secret and the cached Twitch access
/// token. Same contract as the Epic and Steam session protectors (DPAPI,
/// <see cref="DataProtectionScope.CurrentUser"/>, refuse rather than degrade to
/// plaintext), with its own module entropy: the Twitch application credential
/// is a different credential from a Steam or Epic account and must not be an
/// interchangeable ciphertext with either.
/// </summary>
public interface IIgdbSecretProtector
{
    /// <summary>Whether this host can protect anything at all.</summary>
    bool IsAvailable { get; }

    /// <summary>What the logs call this protector. Never a secret.</summary>
    string Name { get; }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> for the current user. Null means
    /// "could not": the caller refuses to store, it does not fall back to
    /// plaintext.
    /// </summary>
    string? Protect(string plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Null means "not readable here" — a
    /// different Windows user, a profile restored onto another machine, or a
    /// value that was never a protected blob — and is the ordinary shape of a
    /// credential that has to be re-supplied, never an exception.
    /// </summary>
    string? Unprotect(string? protectedBase64);
}

/// <summary>
/// Windows DPAPI (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>)
/// protection for the stored IGDB credentials.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiIgdbSecretProtector : IIgdbSecretProtector
{
    /// <summary>
    /// Application-scoped entropy. Fixed, not secret, and deliberately different
    /// from the Epic and Steam protectors' — and from the Steam Web API key's.
    /// Changing it makes every stored secret unreadable, which degrades to
    /// re-entering the client secret and re-minting the token, not to a crash.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Winnow.Igdb.Secret.v1");

    private readonly ILogger<DpapiIgdbSecretProtector> _log;

    public DpapiIgdbSecretProtector(ILogger<DpapiIgdbSecretProtector>? log = null)
        => _log = log ?? NullLogger<DpapiIgdbSecretProtector>.Instance;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string Name => "dpapi:CurrentUser";

    public string? Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

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
            // No exception detail. The standing rule for a failure path that
            // handled a bearer credential: no free-form text in the log.
            _log.LogWarning(
                "The IGDB client secret could not be encrypted with DPAPI; it will not be persisted. "
                + "The credentials work for this run and have to be re-supplied after a restart.");
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? Unprotect(string? protectedBase64)
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
            // Not base64 at all — most likely a value written by an older build
            // before these rows moved behind the protector. The migration paths
            // handle that case; null here just means "not this shape".
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
            // Different Windows user, a profile restored onto another machine,
            // or a truncated value. Nothing to recover: the credentials are
            // re-supplied.
            _log.LogInformation(
                "The stored IGDB credentials could not be decrypted by this Windows user; they have to be re-supplied.");
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
/// plaintext, exactly as the Epic and Steam modules do: a host that cannot
/// encrypt does not keep the secret.
/// </summary>
public sealed class UnavailableIgdbSecretProtector : IIgdbSecretProtector
{
    public bool IsAvailable => false;

    public string Name => "unavailable";

    public string? Protect(string plaintext) => null;

    public string? Unprotect(string? protectedBase64) => null;
}
