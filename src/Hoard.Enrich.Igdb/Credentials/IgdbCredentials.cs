namespace Hoard.Enrich.Igdb.Credentials;

/// <summary>
/// A Twitch application's client id and secret, supplied by the user (§4.2:
/// keys are user-supplied and stored locally).
///
/// <para><b>Never logged.</b> <see cref="ToString"/> is overridden precisely
/// because the compiler-generated record <c>ToString</c> would print the
/// secret the first time anyone interpolated one of these into a log line.</para>
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
