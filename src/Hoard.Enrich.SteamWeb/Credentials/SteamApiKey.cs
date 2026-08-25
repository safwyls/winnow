namespace Hoard.Enrich.SteamWeb.Credentials;

/// <summary>
/// A user-supplied Steam Web API key (§4.2: keys are user-supplied and stored
/// locally, never logged, never committed).
///
/// <para><b>Never logged.</b> <see cref="ToString"/> is overridden precisely
/// because the compiler-generated record <c>ToString</c> would print the key the
/// first time anyone interpolated one of these into a log line, a structured
/// logging argument, or an exception message. The key itself is reachable only
/// through <see cref="Value"/>, which exists for exactly one caller — the code
/// that builds the query string — and is never handed to a logger.</para>
/// </summary>
public sealed record SteamApiKey
{
    private SteamApiKey(string value, string source)
    {
        Value = value;
        Source = source;
    }

    /// <summary>The key. Goes into a query string and nowhere else.</summary>
    public string Value { get; }

    /// <summary>Where the key came from, for diagnostics. Never contains the key.</summary>
    public string Source { get; }

    /// <summary>Length of the key, for diagnostics that want to say "a key is present" credibly.</summary>
    public int Length => Value.Length;

    public override string ToString() => $"SteamApiKey(source={Source}, value redacted)";

    /// <summary>Blank or whitespace-only counts as unset, not as a key.</summary>
    public static SteamApiKey? TryCreate(string? value, string source)
        => string.IsNullOrWhiteSpace(value) ? null : new SteamApiKey(value.Trim(), source);
}
