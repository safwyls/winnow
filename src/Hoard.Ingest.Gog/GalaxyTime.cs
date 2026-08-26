using System.Globalization;

namespace Hoard.Ingest.Gog;

/// <summary>
/// Galaxy's timestamp convention (docs/spikes/epic-gog-local-files.md
/// section 14).
///
/// <para><b>Galaxy's database is UTC. GOG's registry is local time.</b> Both were
/// measured against the same install: <c>InstalledBaseProducts.installationDate</c>
/// read <c>2026-08-26 06:17:36</c> while the registry's <c>INSTALLDATE</c> for the
/// same install read <c>2026-08-25 23:17:36</c> — seven hours apart on a UTC−7
/// machine. Mixing the two shifts every GOG date by the user's offset. Only the
/// database's form is parsed here; the registry's local-time value deliberately
/// has no parser, because nothing in the candidate feed should carry it.</para>
///
/// <para>The UTC reading was confirmed independently: GWENT's
/// <c>LastPlayedDates</c> row is <c>2017-07-01 03:32:16</c> and the
/// <c>myFriendsActivity</c> GamePiece for the same release carries
/// <c>last_played_date: 1498879936</c>, which is that instant in UTC to the
/// second.</para>
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
