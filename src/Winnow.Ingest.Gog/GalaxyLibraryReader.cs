using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Gog;

/// <summary>
/// Runs the owned-library query against a <see cref="GalaxyDatabaseSnapshot"/>
/// (docs/spikes/epic-gog-local-files.md sections 12–15). Read-only, and only ever
/// against a Winnow-owned copy — never the live file.
/// </summary>
public sealed class GalaxyLibraryReader
{
    /// <summary>
    /// The ownership query, verified against the live database (schema
    /// <c>user_version = 40</c>). Filters on the <c>gog_</c> releaseKey prefix
    /// to exclude other stores' releases Galaxy tracks.
    /// </summary>
    private const string OwnershipQuery = """
        SELECT lr.releaseKey,
               lr.userId,
               (SELECT json_extract(gp.value, '$.title')
                  FROM GamePieces gp
                 WHERE gp.releaseKey = lr.releaseKey
                   AND gp.gamePieceTypeId = (SELECT id FROM GamePieceTypes WHERE type = 'title')
                   AND (gp.userId = lr.userId OR gp.userId IS NULL)
                 ORDER BY (gp.userId IS NULL)
                 LIMIT 1)                              AS title,
               COALESCE(rp.isDlc, 0)                   AS isDlc,
               COALESCE(rp.isVisibleInLibrary, 1)      AS isVisibleInLibrary,
               gt.minutesInGame,
               lpd.lastPlayedDate,
               ppd.purchaseDate,
               ppd.addedDate,
               ibp.installationPath,
               ibp.installationDate,
               ibp.buildId
          FROM LibraryReleases lr
          JOIN LicensedReleases lic          ON lic.libraryId = lr.id AND lic.isOwned = 1
          LEFT JOIN ReleaseProperties rp     ON rp.releaseKey = lr.releaseKey
          LEFT JOIN GameTimes gt             ON gt.releaseKey = lr.releaseKey       AND gt.userId  = lr.userId
          LEFT JOIN LastPlayedDates lpd      ON lpd.gameReleaseKey = lr.releaseKey  AND lpd.userId = lr.userId
          LEFT JOIN ProductPurchaseDates ppd ON ppd.gameReleaseKey = lr.releaseKey  AND ppd.userId = lr.userId
          LEFT JOIN ProductsToReleaseKeys ptrk ON ptrk.releaseKey = lr.releaseKey
          LEFT JOIN InstalledBaseProducts ibp  ON ibp.productId = ptrk.gogId
         WHERE substr(lr.releaseKey, 1, 4) = 'gog_'
         ORDER BY lr.userId, lr.releaseKey;
        """;

    private readonly ILogger<GalaxyLibraryReader> _logger;

    /// <param name="logger">Optional logger.</param>
    public GalaxyLibraryReader(ILogger<GalaxyLibraryReader>? logger = null)
        => _logger = logger ?? NullLogger<GalaxyLibraryReader>.Instance;

    /// <summary>
    /// Reads every owned GOG-native release, for every Galaxy user in the
    /// database. DLC rows are returned with <see cref="GogLibraryEntry.IsDlc"/>
    /// set rather than dropped — the caller decides, and the flag is useful.
    /// Returns an empty list rather than throwing when the schema is not what this
    /// query expects.
    /// </summary>
    public IReadOnlyList<GogLibraryEntry> Read(GalaxyDatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            using var connection = snapshot.OpenReadOnly();
            using var command = connection.CreateCommand();
            command.CommandText = OwnershipQuery;

            var entries = new List<GogLibraryEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var releaseKey = reader.GetString(0);
                entries.Add(new GogLibraryEntry(
                    ReleaseKey: releaseKey,
                    ProductId: ProductIdOf(releaseKey),
                    UserId: reader.GetInt64(1),
                    Title: NullableString(reader, 2),
                    IsDlc: reader.GetInt64(3) != 0,
                    IsVisibleInLibrary: reader.GetInt64(4) != 0,
                    PlaytimeMinutes: NullableInt64(reader, 5),
                    LastPlayedUtc: GalaxyTime.Parse(NullableString(reader, 6)),
                    PurchasedAtUtc: GalaxyTime.Parse(NullableString(reader, 7)),
                    AddedAtUtc: GalaxyTime.Parse(NullableString(reader, 8)),
                    InstallationPath: NullableString(reader, 9),
                    InstalledAtUtc: GalaxyTime.Parse(NullableString(reader, 10)),
                    BuildId: NullableInt64(reader, 11)));
            }

            return entries;
        }
        catch (SqliteException ex)
        {
            // A schema Galaxy has migrated out from under this query costs the
            // GOG half of a sync, never the sync.
            _logger.LogWarning(
                ex, "GOG Galaxy ownership query failed; the client schema may have changed");
            return [];
        }
    }

    /// <summary>
    /// The GOG product id: everything after the first underscore of the release
    /// key. The prefix has already been constrained to <c>gog_</c> by the query.
    /// </summary>
    private static string ProductIdOf(string releaseKey)
    {
        var separator = releaseKey.IndexOf('_', StringComparison.Ordinal);
        return separator >= 0 && separator + 1 < releaseKey.Length
            ? releaseKey[(separator + 1)..]
            : releaseKey;
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? NullableInt64(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
