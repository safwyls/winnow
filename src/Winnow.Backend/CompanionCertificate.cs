using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Winnow.Core.Repositories;

namespace Winnow.Backend;

/// <summary>Encrypts the phone-sync certificate's private key at rest.</summary>
public interface ICompanionSecretProtector
{
    bool IsAvailable { get; }
    string? Protect(byte[] plaintext);
    byte[]? Unprotect(string protectedBase64);
}

/// <summary>DPAPI for the current Windows user. Elsewhere it is unavailable and phone sync
/// refuses to start rather than store the key in the clear.</summary>
public sealed class DpapiCompanionSecretProtector : ICompanionSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Winnow.Companion.Certificate.v1");

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string? Protect(byte[] plaintext) => OperatingSystem.IsWindows() ? ProtectWindows(plaintext) : null;

    public byte[]? Unprotect(string protectedBase64) => OperatingSystem.IsWindows() ? UnprotectWindows(protectedBase64) : null;

    [SupportedOSPlatform("windows")]
    private static string? ProtectWindows(byte[] plaintext)
    {
        try { return Convert.ToBase64String(ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? UnprotectWindows(string protectedBase64)
    {
        try { return ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }
}

/// <summary>
/// The listener's self-signed certificate. Phones trust it by the SHA-256 fingerprint in the
/// pairing QR code, not by a certificate authority, so it never needs renewing for trust;
/// it is valid for ten years and recreated if its stored copy cannot be read.
/// </summary>
public sealed class CompanionCertificate(ISettingsRepository settings, ICompanionSecretProtector protector)
{
    public const string CertificateKey = "companion.certificate.v1";

    /// <summary>The certificate, or null with the reason when it cannot be kept safely.</summary>
    public async Task<(X509Certificate2? Certificate, string? Problem)> LoadOrCreateAsync(CancellationToken ct = default)
    {
        if (!protector.IsAvailable)
            return (null, "Phone sync needs Windows to encrypt its certificate key, and that is not available here.");
        var stored = await settings.GetAsync(CertificateKey, ct);
        if (stored is not null && protector.Unprotect(stored) is { } pfx)
        {
            try { return (X509CertificateLoader.LoadPkcs12(pfx, null), null); }
            catch (CryptographicException) { }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
        var created = Create();
        var protectedPfx = protector.Protect(created);
        try
        {
            if (protectedPfx is null) return (null, "Windows could not encrypt the phone-sync certificate key, so phone sync stays off.");
            await settings.SetAsync(CertificateKey, protectedPfx, ct);
            // Reloading from PFX gives Windows' TLS stack a key it can use; a freshly
            // created key is ephemeral and refused there.
            return (X509CertificateLoader.LoadPkcs12(created, null), null);
        }
        finally { CryptographicOperations.ZeroMemory(created); }
    }

    public static string Fingerprint(X509Certificate2 certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();

    private static byte[] Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Winnow phone sync", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
        return certificate.Export(X509ContentType.Pfx);
    }
}
