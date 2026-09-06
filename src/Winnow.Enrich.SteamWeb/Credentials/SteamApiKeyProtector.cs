using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Protection for the stored Web API key — the one credential the key chain
/// persists that is not a session. Same contract as
/// <see cref="ISteamSecretProtector"/> (DPAPI, <see cref="DataProtectionScope.CurrentUser"/>,
/// refuse rather than degrade to plaintext), kept as an interface of its own so
/// the container can hand each store its own protector, and so two credentials
/// that grant access to different accounts are never interchangeable.
/// </summary>
public interface ISteamApiKeyProtector
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
    /// credential that has to be re-entered, never an exception.
    /// </summary>
    string? Unprotect(string? protectedBase64);
}

/// <summary>
/// Windows DPAPI (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>)
/// protection for the stored Steam Web API key.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSteamApiKeyProtector : ISteamApiKeyProtector
{
    /// <summary>
    /// Application-scoped entropy. Fixed, not secret, and deliberately
    /// different from the session protector's: the API key reads the owned-games
    /// list and the session is the account itself, and the two must not be
    /// interchangeable ciphertexts even by mistake. Changing it makes every
    /// stored key unreadable, which degrades to re-entering the key, not to a
    /// crash.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Winnow.Steam.WebApiKey.v1");

    private readonly ILogger<DpapiSteamApiKeyProtector> _log;

    public DpapiSteamApiKeyProtector(ILogger<DpapiSteamApiKeyProtector>? log = null)
        => _log = log ?? NullLogger<DpapiSteamApiKeyProtector>.Instance;

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
                "The Steam Web API key could not be encrypted with DPAPI; it will not be persisted. "
                + "The key entered works for this run and has to be re-entered after a restart.");
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
            // before the key moved behind the protector. The store's migration
            // path handles that case; null here just means "not this shape".
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
            // or a truncated value. Nothing to recover: the key is re-entered.
            _log.LogInformation(
                "The stored Steam Web API key could not be decrypted by this Windows user; it has to be re-entered.");
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
/// plaintext, exactly as <see cref="UnavailableSteamSecretProtector"/> does for
/// the session: §4.7's second amendment binds every secret Winnow keeps.
/// </summary>
public sealed class UnavailableSteamApiKeyProtector : ISteamApiKeyProtector
{
    public bool IsAvailable => false;

    public string Name => "unavailable";

    public string? Protect(string plaintext) => null;

    public string? Unprotect(string? protectedBase64) => null;
}
