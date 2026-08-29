namespace Winnow.Core.Domain;

/// <summary>
/// Steam's epoch conventions for timestamps. Shared in Core so all readers
/// (VDF, Web API) apply the same placeholder rules and emit consistent values.
/// </summary>
public static class SteamTime
{
    /// <summary>
    /// 1980-01-01T00:00:00Z as Unix epoch seconds. Values below this are placeholders
    /// (known: <c>0</c> for never-launched, <c>86400</c> for pre-tracking installs).
    /// </summary>
    public const long MinValidEpochSeconds = 315_532_800;

    /// <summary>1980-01-01T00:00:00Z — <see cref="MinValidEpochSeconds"/> as a UTC instant.</summary>
    public static readonly DateTime MinValidUtc = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Converts Steam epoch seconds to UTC, or null if absent or a placeholder.</summary>
    public static DateTime? FromEpochSeconds(long? seconds)
        => seconds is { } s && s >= MinValidEpochSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
            : null;

    /// <summary>Returns null for any DateTime below <see cref="MinValidUtc"/>.</summary>
    public static DateTime? Sanitize(DateTime? value)
        => value is { } v && v >= MinValidUtc ? value : null;
}
