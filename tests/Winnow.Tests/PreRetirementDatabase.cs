using System.Data;
using Dapper;
using Winnow.Data;

namespace Winnow.Tests;

/// <summary>
/// Puts a migrated database back in its pre-0019 shape (the 0017 status
/// set, the application log and the undo journal) so that 0019 is the
/// one pending script and the C# replay runs on the way to it. Also
/// seeds the state a destructive merge used to leave, because the
/// executor that left it has been deleted. The journal is the contract,
/// not the executor: a database upgrading from an older build arrives
/// carrying exactly these rows and nothing else.
/// </summary>
internal static class PreRetirementDatabase
{

    /// <summary>
    /// Puts a migrated database back in its pre-0019 shape: the 0017
    /// status set, the application log and the journal. 0019 becomes the
    /// one pending script and the replay runs on the way to it. Existing
    /// merge_candidates rows are carried across, statuses included.
    /// </summary>
    internal static void Rewind(TempDatabase db)
    {
        using var connection = db.Factory.Open();

        connection.Execute("""
            CREATE TABLE merge_candidates_rewound (
                id                INTEGER PRIMARY KEY,
                left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
                right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
                score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
                signals_json      TEXT,
                status            TEXT NOT NULL DEFAULT 'pending'
                                  CHECK (status IN ('pending', 'confirmed', 'rejected', 'undone')),
                CHECK (left_release_id < right_release_id),
                UNIQUE (left_release_id, right_release_id)
            );

            INSERT INTO merge_candidates_rewound (
                id, left_release_id, right_release_id, score, signals_json, status)
            SELECT id, left_release_id, right_release_id, score, signals_json, status
            FROM merge_candidates;

            DROP TABLE merge_candidates;
            ALTER TABLE merge_candidates_rewound RENAME TO merge_candidates;
            CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);

            CREATE TABLE merge_applications (
                id                    INTEGER PRIMARY KEY,
                candidate_id          INTEGER NOT NULL,
                left_release_id       INTEGER NOT NULL,
                right_release_id      INTEGER NOT NULL,
                mode                  TEXT NOT NULL CHECK (mode IN ('work_only', 'release_collapse')),
                surviving_work_id     INTEGER NOT NULL,
                absorbed_work_id      INTEGER,
                surviving_release_id  INTEGER,
                absorbed_release_id   INTEGER,
                applied_at            TEXT NOT NULL,
                summary_json          TEXT,
                undone_at             TEXT,
                undo_journal_version  INTEGER
            );

            CREATE TABLE merge_undo_rows (
                id              INTEGER PRIMARY KEY,
                application_id  INTEGER NOT NULL REFERENCES merge_applications(id) ON DELETE CASCADE,
                seq             INTEGER NOT NULL,
                table_name      TEXT NOT NULL,
                op              TEXT NOT NULL CHECK (op IN ('repoint', 'delete', 'update')),
                key_json        TEXT NOT NULL,
                before_json     TEXT NOT NULL,
                UNIQUE (application_id, seq)
            );
            """);

        connection.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0019%';");
    }

    internal static (long WorkId, long ReleaseId) SeedGame(
        IDbConnection connection, string name, bool provisional = false)
    {
        var workId = connection.ExecuteScalar<long>(
            "INSERT INTO works (name, name_is_provisional) VALUES (@name, @provisional) RETURNING id;",
            new { name, provisional });

        var releaseId = connection.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, @name) RETURNING id;",
            new { workId, name });

        return (workId, releaseId);
    }

    internal static void SeedCandidate(
        IDbConnection connection, long a, long b, string status)
        => connection.Execute("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (MIN(@a, @b), MAX(@a, @b), 0.92, @status);
            """,
            new { a, b, status });

    /// <summary>
    /// Performs a work-only merge the way the deleted executor performed
    /// one: the absorbed work's releases repointed onto the survivor, the
    /// absorbed works row deleted, and the journal migration 0017
    /// specified for it written. The journal is the only thing the replay
    /// reads, so this method is the whole contract.
    /// </summary>
    internal static long ApplyMergeByHand(
        IDbConnection connection,
        long survivingWork,
        long absorbedWork,
        long survivingRelease,
        long absorbedRelease,
        int? journalVersion)
    {
        var absorbed = connection.QuerySingle<AbsorbedWork>(
            """
            SELECT id AS Id, igdb_id AS IgdbId, name AS Name, sort_name AS SortName,
                   first_release_year AS FirstReleaseYear, summary AS Summary,
                   cover_url AS CoverUrl, name_is_provisional AS NameIsProvisional,
                   publisher AS Publisher, steam_app_type AS SteamAppType,
                   epic_categories AS EpicCategories
            FROM works WHERE id = @absorbedWork;
            """,
            new { absorbedWork });

        var applicationId = connection.ExecuteScalar<long>("""
            INSERT INTO merge_applications (
                candidate_id, left_release_id, right_release_id, mode,
                surviving_work_id, absorbed_work_id, applied_at, undo_journal_version)
            VALUES (1, MIN(@survivingRelease, @absorbedRelease),
                       MAX(@survivingRelease, @absorbedRelease),
                    'work_only', @survivingWork, @absorbedWork,
                    '2026-08-30 10:00:00', @journalVersion)
            RETURNING id;
            """,
            new { survivingRelease, absorbedRelease, survivingWork, absorbedWork, journalVersion });

        // The absorbed work's row, every column, before it is deleted.
        connection.Execute("""
            INSERT INTO merge_undo_rows (
                application_id, seq, table_name, op, key_json, before_json)
            VALUES (@applicationId, 1, 'works', 'delete',
                    json_object('id', @Id),
                    json_object(
                        'id', @Id, 'igdb_id', @IgdbId, 'name', @Name,
                        'sort_name', @SortName, 'first_release_year', @FirstReleaseYear,
                        'summary', @Summary, 'cover_url', @CoverUrl,
                        'name_is_provisional', @NameIsProvisional, 'publisher', @Publisher,
                        'steam_app_type', @SteamAppType, 'epic_categories', @EpicCategories));
            """,
            new
            {
                applicationId,
                absorbed.Id,
                absorbed.IgdbId,
                absorbed.Name,
                absorbed.SortName,
                absorbed.FirstReleaseYear,
                absorbed.Summary,
                absorbed.CoverUrl,
                absorbed.NameIsProvisional,
                absorbed.Publisher,
                absorbed.SteamAppType,
                absorbed.EpicCategories,
            });

        // The repoint: the key is the row plus the parent the merge moved it
        // to, so matching on it answers "still there" and "still moved" at once.
        connection.Execute("""
            INSERT INTO merge_undo_rows (
                application_id, seq, table_name, op, key_json, before_json)
            VALUES (@applicationId, 2, 'releases', 'repoint',
                    json_object('id', @absorbedRelease, 'work_id', @survivingWork),
                    json_object('work_id', @absorbedWork));
            """,
            new { applicationId, absorbedRelease, survivingWork, absorbedWork });

        connection.Execute(
            "UPDATE releases SET work_id = @survivingWork WHERE id = @absorbedRelease;",
            new { survivingWork, absorbedRelease });

        connection.Execute(
            "DELETE FROM works WHERE id = @absorbedWork;", new { absorbedWork });

        return applicationId;
    }

    private sealed record AbsorbedWork
    {
        public long Id { get; init; }

        public long? IgdbId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? SortName { get; init; }

        public int? FirstReleaseYear { get; init; }

        public string? Summary { get; init; }

        public string? CoverUrl { get; init; }

        public bool NameIsProvisional { get; init; }

        public string? Publisher { get; init; }

        public string? SteamAppType { get; init; }

        public string? EpicCategories { get; init; }
    }
}
