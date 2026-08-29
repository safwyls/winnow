using System.Globalization;

namespace Winnow.Ingest.Gog;

/// <summary>
/// Galaxy's timestamp convention. Database columns are UTC; the registry is
/// local time and deliberately has no parser here.
/// </summary>
public static class GalaxyTime
{
    /// <summary>The exact format Galaxy writes into its TEXT date columns.</summary>
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// A sanity floor. GOG.com launched in 2008 and Galaxy in 2015, so a
    /// timestamp before 1980 is a placeholder or a corrupted row, not a session.
    /// The same reasoning as <c>SteamTime.MinValidUtc</c>, kept local because
    /// nothing about Steam's epoch conventions applies to a text column.
    /// </summary>
    public static readonly DateTime MinValidUtc = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Parses one of Galaxy's TEXT timestamps as UTC, or null when it is absent,
    /// malformed, or below <see cref="MinValidUtc"/>. Null means <b>unknown</b> —
    /// never the epoch, never a zero date.
    /// </summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                value,
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed >= MinValidUtc ? parsed : null;
    }
}
