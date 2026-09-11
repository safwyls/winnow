namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Nonsecret identity of the credential that produced an observation. Session
/// renewal preserves its account identity; replacing an API key changes its
/// fingerprint. An unidentified session cannot supply account-scoped evidence.
/// </summary>
public sealed record SteamCredentialIdentity(
    SteamCredentialKind Kind, string Fingerprint, SteamId? Account)
{
    public static SteamCredentialIdentity? From(SteamCredential? credential)
        => credential is not null && SteamCredentialFingerprint.Of(credential) is { } fingerprint
            ? new(credential.Kind, fingerprint, credential.SteamId)
            : null;

    public override string ToString() => $"SteamCredentialIdentity(kind={Kind}, identity redacted)";
}
