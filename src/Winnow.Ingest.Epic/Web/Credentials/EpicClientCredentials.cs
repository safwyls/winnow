namespace Winnow.Ingest.Epic.Web.Credentials;

/// <summary>
/// An Epic OAuth client id and secret pair. Stored locally, never logged.
/// See <see cref="BuiltInEpicCredentialSource"/> for the embedded pair's provenance.
/// </summary>
public sealed record EpicClientCredentials
{
    private EpicClientCredentials(string clientId, string clientSecret, string source)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Source = source;
    }

    /// <summary>OAuth client id. Goes into an HTTP Basic header and nowhere else.</summary>
    public string ClientId { get; }

    /// <summary>OAuth client secret. Goes into an HTTP Basic header and nowhere else.</summary>
    public string ClientSecret { get; }

    /// <summary>Where the pair came from, for diagnostics. Never contains either value.</summary>
    public string Source { get; }

    /// <summary>Redacted to prevent secrets from reaching log files.</summary>
    public override string ToString() => $"EpicClientCredentials(source={Source}, values redacted)";

    /// <summary>
    /// Both halves, or null. A half-configured pair is not a credential: sending
    /// a client id with an empty secret produces Epic's
    /// <c>invalid_client_credentials</c> (verified live 2026-08-26: HTTP 400,
    /// numericErrorCode 18033) rather than anything useful, so it is treated as
    /// "not configured" and never sent.
    /// </summary>
    public static EpicClientCredentials? TryCreate(string? clientId, string? clientSecret, string source)
        => string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new EpicClientCredentials(clientId.Trim(), clientSecret.Trim(), source);
}
