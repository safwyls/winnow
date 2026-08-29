namespace Winnow.Enrich.Igdb.Credentials;

/// <summary>
/// A Twitch application's client id and secret, supplied by the user.
/// <see cref="ToString"/> is overridden to prevent accidental secret logging.
/// </summary>
public sealed record IgdbCredentials(string ClientId, string ClientSecret)
{
    /// <summary>Where the credentials came from, for diagnostics. Never contains a value.</summary>
    public string Source { get; init; } = "unknown";

    public override string ToString() => $"IgdbCredentials(source={Source}, values redacted)";

    /// <summary>Blank or whitespace-only halves count as unset, not as credentials.</summary>
    public static IgdbCredentials? TryCreate(string? clientId, string? clientSecret, string source)
        => string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new IgdbCredentials(clientId.Trim(), clientSecret.Trim()) { Source = source };
}
