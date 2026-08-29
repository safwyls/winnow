namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// A user-supplied Steam Web API key. <see cref="ToString"/> is redacted so
/// accidental interpolation cannot leak the key.
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
