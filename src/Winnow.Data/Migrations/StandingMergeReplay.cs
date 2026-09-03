using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Identity;
using Winnow.Core.Merging;

namespace Winnow.Data.Migrations;

/// <summary>
/// Counters from one <see cref="StandingMergeReplay.Run"/> pass,
/// written once and read only by tests and by the trace log.
/// </summary>
internal sealed record StandingMergeReplayReport(
    int ApplicationsReplayed,
    int RowsRestored,
    int LinksWritten,
    int ConfirmedPairsLinked)
{
    public static StandingMergeReplayReport Nothing { get; } = new(0, 0, 0, 0);

    public bool DidNothing
        => ApplicationsReplayed == 0 && RowsRestored == 0
        && LinksWritten == 0 && ConfirmedPairsLinked == 0;
}

/// <summary>
/// Thrown when a standing merge cannot be replayed into an identity link.
/// The message names the application id, the absorbed title and both
/// release ids so the person reading the crash knows which merge to
/// deal with and how to recover.
/// </summary>
internal sealed class StandingMergeReplayRefusedException : InvalidOperationException
{
    public StandingMergeReplayRefusedException(string message)
        : base(message)
    {
    }

    public StandingMergeReplayRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public StandingMergeReplayRefusedException()
        : base("A standing merge cannot be replayed into an identity link.")
    {
    }
}

/// <summary>
/// One-shot that runs between migrations 0018 and 0019. Replays every
/// merge still standing into a restored work plus a live identity link,
/// and writes a link for every confirmed pair whose two releases sit
/// under different works. Runs in one transaction; refuses rather than
/// guesses when the journal does not describe the database. After this
/// runs, 0019's guard finds zero standing applications and proceeds.
/// </summary>
internal static class StandingMergeReplay
{
    /// <summary>
    /// The sixteen tables migration 0017's journal could name. Table names
    /// in generated statements are checked against this list so a journal
    /// row cannot address anything the schema does not hold.
    /// </summary>
    private static readonly string[] JournalledTables =
    [
        "works", "releases", "work_facets", "external_ids", "ownerships",
        "play_records", "playtime_snapshots", "sessions", "ownership_accounts",
        "update_events", "update_acknowledgements", "list_items", "release_facets",
        "feed_verdicts", "feed_surfacings", "merge_candidates",
    ];

    /// <summary>
    /// Identity tables restored before anything else, in the order a
    /// foreign key needs them: a re-inserted release needs its work, and a
    /// re-inserted ownership needs its release.
    /// </summary>
    private static readonly string[] IdentityOrder = ["works", "releases", "ownerships"];

    public static StandingMergeReplayReport Run(SqliteConnection connection, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(clock);

        if (!TableExists(connection, "merge_applications"))
        {
            return StandingMergeReplayReport.Nothing;
        }

        RequireTable(connection, "merge_undo_rows");
        RequireTable(connection, "identity_links");
        RequireTable(connection, "identity_acts");

        using var transaction = connection.BeginTransaction();

        var now = clock.GetUtcNow().UtcDateTime;
        var replayed = 0;
        var restored = 0;
        var links = 0;

        foreach (var application in Standing(connection, transaction))
        {
            restored += Replay(connection, transaction, application, now, ref links);
            replayed++;
        }

        var confirmed = LinkConfirmedPairs(connection, transaction, now);

        transaction.Commit();

        return new StandingMergeReplayReport(replayed, restored, links, confirmed);
    }

    // ── The standing applications ────────────────────────────────────────────

    private static List<ApplicationRow> Standing(
        SqliteConnection connection, SqliteTransaction transaction)
        => connection.Query<ApplicationRow>(
            """
            SELECT a.id                    AS Id,
                   a.candidate_id          AS CandidateId,
                   a.left_release_id       AS LeftReleaseId,
                   a.right_release_id      AS RightReleaseId,
                   a.surviving_work_id     AS SurvivingWorkId,
                   a.absorbed_work_id      AS AbsorbedWorkId,
                   a.applied_at            AS AppliedAt,
                   a.undo_journal_version  AS UndoJournalVersion,
                   (SELECT json_extract(j.before_json, '$.name')
                      FROM merge_undo_rows j
                     WHERE j.application_id = a.id
                       AND j.table_name = 'works'
                       AND j.op = 'delete'
                     LIMIT 1)              AS AbsorbedTitle
            FROM merge_applications a
            WHERE a.undone_at IS NULL
            ORDER BY a.id DESC;
            """,
            transaction: transaction).AsList();

    private static int Replay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApplicationRow application,
        DateTime now,
        ref int links)
    {
        if (application.UndoJournalVersion is null)
        {
            throw Refuse(
                application,
                "it was applied before migration 0017 added the undo journal, so nothing records "
                + "which rows moved or what the overwritten columns held. Its absorbed game cannot "
                + "be brought back, and this migration will not guess");
        }

        if (!RowExists(connection, transaction, "works", "id", application.SurvivingWorkId))
        {
            throw Refuse(
                application,
                $"its surviving game (work {application.SurvivingWorkId.ToString(CultureInfo.InvariantCulture)}) "
                + "no longer exists, so there is nothing left to move the absorbed rows back off");
        }

        var journal = connection.Query<JournalRow>(
            """
            SELECT seq        AS Seq,
                   table_name AS TableName,
                   op         AS Op,
                   key_json   AS KeyJson,
                   before_json AS BeforeJson
            FROM merge_undo_rows
            WHERE application_id = @applicationId
            ORDER BY seq;
            """,
            new { applicationId = application.Id },
            transaction).AsList();

        var restored = 0;

        // Identity rows first, in the order a foreign key needs them.
        foreach (var table in IdentityOrder)
        {
            foreach (var row in journal.Where(r => r.Op == "delete" && r.TableName == table))
            {
                restored += Reinsert(connection, transaction, application, row);
            }
        }

        foreach (var row in journal.Where(r => r.Op is "repoint" or "update"))
        {
            restored += RestoreInPlace(connection, transaction, application, row);
        }

        foreach (var row in journal.Where(
            r => r.Op == "delete" && !IdentityOrder.Contains(r.TableName, StringComparer.Ordinal)))
        {
            restored += Reinsert(connection, transaction, application, row);
        }

        if (application.AbsorbedWorkId is { } childWorkId)
        {
            WriteLink(connection, transaction, application, childWorkId, now);
            links++;
        }

        connection.Execute(
            "UPDATE merge_applications SET undone_at = @now WHERE id = @applicationId;",
            new { now, applicationId = application.Id },
            transaction);

        return restored;
    }

    // ── Restoring one journalled row ─────────────────────────────────────────

    private static int Reinsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApplicationRow application,
        JournalRow row)
    {
        var table = Table(application, row.TableName);
        var columns = Columns(connection, table);

        var values = Fields(application, table, columns, row.BeforeJson);
        var key = Fields(application, table, columns, row.KeyJson);

        if (Occupied(connection, transaction, table, key))
        {
            throw Refuse(
                application,
                $"the {table} row it must put back is occupied by a row written since the merge "
                + $"({Describe(key)}). Restoring the absorbed game at a different id would leave "
                + "the database consistent and the history wrong, so this migration refuses "
                + "instead");
        }

        var names = string.Join(", ", values.Select(f => Quote(f.Column)));
        var parameters = string.Join(", ", values.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)));

        return connection.Execute(
            $"INSERT INTO {Quote(table)} ({names}) VALUES ({parameters});",
            Bind(values, "p"),
            transaction);
    }

    private static int RestoreInPlace(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApplicationRow application,
        JournalRow row)
    {
        var table = Table(application, row.TableName);
        var columns = Columns(connection, table);

        var values = Fields(application, table, columns, row.BeforeJson);
        var key = Fields(application, table, columns, row.KeyJson);

        var assignments = string.Join(
            ", ",
            values.Select((f, i) => $"{Quote(f.Column)} = @v{i.ToString(CultureInfo.InvariantCulture)}"));

        var affected = connection.Execute(
            $"UPDATE {Quote(table)} SET {assignments} WHERE {Predicate(key, "k")};",
            Bind(values, "v", key, "k"),
            transaction);

        if (affected != 1)
        {
            throw Refuse(
                application,
                $"the {table} row it must move back is no longer where the merge left it "
                + $"({Describe(key)} matched {affected.ToString(CultureInfo.InvariantCulture)} row(s), "
                + "not one). Something has edited it since, so the journal no longer describes the "
                + "database and this migration refuses to apply it");
        }

        return affected;
    }

    // ── Writing the link the merge stood for ─────────────────────────────────

    private static void WriteLink(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApplicationRow application,
        long childWorkId,
        DateTime now)
    {
        if (LiveParentOf(connection, transaction, childWorkId) is not null)
        {
            throw Refuse(
                application,
                $"the restored game (work {childWorkId.ToString(CultureInfo.InvariantCulture)}) "
                + "already has a live identity link, which cannot happen for a row that was "
                + "deleted until a moment ago");
        }

        var parentWorkId = LiveParentOf(connection, transaction, application.SurvivingWorkId)
            ?? application.SurvivingWorkId;

        if (parentWorkId == childWorkId)
        {
            return;
        }

        var evidence = JsonSerializer.Serialize(
            new ReplayEvidence(
                application.Id,
                application.CandidateId,
                application.LeftReleaseId,
                application.RightReleaseId,
                application.AppliedAt),
            ReplayEvidenceJsonContext.Default.ReplayEvidence);

        Link(connection, transaction, parentWorkId, childWorkId, evidence, now);
    }

    private static int LinkConfirmedPairs(
        SqliteConnection connection, SqliteTransaction transaction, DateTime now)
    {
        var pairs = connection.Query<ConfirmedPairRow>(
            """
            SELECT c.id       AS CandidateId,
                   l.work_id  AS LeftWorkId,
                   r.work_id  AS RightWorkId
            FROM merge_candidates c
            JOIN releases l ON l.id = c.left_release_id
            JOIN releases r ON r.id = c.right_release_id
            WHERE c.status = @confirmed
              AND l.work_id <> r.work_id
            ORDER BY c.id;
            """,
            new { confirmed = "confirmed" },
            transaction).AsList();

        if (pairs.Count == 0)
        {
            return 0;
        }

        var facts = connection.Query<SurvivorFactsRow>(
            """
            SELECT w.id                                                   AS WorkId,
                   CASE WHEN w.igdb_id IS NULL THEN 0 ELSE 1 END          AS HasIgdbId,
                   w.name_is_provisional                                  AS NameIsProvisional,
                   (SELECT COUNT(*) FROM releases r WHERE r.work_id = w.id) AS ReleaseCount
            FROM works w;
            """,
            transaction: transaction)
            .ToDictionary(row => row.WorkId);

        var written = 0;

        foreach (var pair in pairs)
        {
            if (!facts.TryGetValue(pair.LeftWorkId, out var left)
                || !facts.TryGetValue(pair.RightWorkId, out var right))
            {
                continue;
            }

            var decision = SurvivorLadder.Choose(left.ToCandidate(), right.ToCandidate());
            if (decision.AbsorbedWorkId is not { } childWorkId)
            {
                continue;
            }

            if (LiveParentOf(connection, transaction, childWorkId) is not null)
            {
                continue;
            }

            var parentWorkId = LiveParentOf(connection, transaction, decision.SurvivingWorkId)
                ?? decision.SurvivingWorkId;

            if (parentWorkId == childWorkId
                || HasLiveChildren(connection, transaction, childWorkId))
            {
                continue;
            }

            var evidence = JsonSerializer.Serialize(
                new ReplayEvidence(null, pair.CandidateId, null, null, null),
                ReplayEvidenceJsonContext.Default.ReplayEvidence);

            Link(connection, transaction, parentWorkId, childWorkId, evidence, now);
            written++;
        }

        return written;
    }

    private static void Link(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long parentWorkId,
        long childWorkId,
        string evidenceJson,
        DateTime now)
    {
        var actId = connection.ExecuteScalar<long>(
            """
            INSERT INTO identity_acts (kind, performed_at, note)
            VALUES (@kind, @now, @note)
            RETURNING id;
            """,
            new { kind = IdentityActKinds.Link, now, note = ActNote },
            transaction);

        connection.Execute(
            """
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source, evidence_json, applied_at)
            VALUES (@actId, @childWorkId, @parentWorkId, @kind, @source, @evidenceJson, @now);
            """,
            new
            {
                actId,
                childWorkId,
                parentWorkId,
                kind = IdentityLinkKinds.SameGame,
                source = IdentityLinkSources.User,
                evidenceJson,
                now,
            },
            transaction);
    }

    /// <summary>The note stamped on every identity act the replay writes, so the history screen shows provenance.</summary>
    private const string ActNote = "Carried over from a merge applied before identity links.";

    private static long? LiveParentOf(
        SqliteConnection connection, SqliteTransaction transaction, long workId)
        => connection.ExecuteScalar<long?>(
            """
            SELECT parent_work_id
            FROM identity_links
            WHERE child_work_id = @workId AND retracted_at IS NULL
            LIMIT 1;
            """,
            new { workId },
            transaction);

    private static bool HasLiveChildren(
        SqliteConnection connection, SqliteTransaction transaction, long workId)
        => connection.ExecuteScalar<long>(
            """
            SELECT COUNT(*)
            FROM identity_links
            WHERE parent_work_id = @workId AND retracted_at IS NULL;
            """,
            new { workId },
            transaction) > 0;

    // ── Statement building ───────────────────────────────────────────────────

    private static string Table(ApplicationRow application, string name)
    {
        if (!JournalledTables.Contains(name, StringComparer.Ordinal))
        {
            throw Refuse(
                application,
                $"its journal names a table the merge never touched ('{name}')");
        }

        return name;
    }

    private static IReadOnlyList<Field> Fields(
        ApplicationRow application,
        string table,
        IReadOnlySet<string> columns,
        string json)
    {
        using var document = JsonDocument.Parse(json);

        var fields = new List<Field>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!columns.Contains(property.Name))
            {
                throw Refuse(
                    application,
                    $"its journal names a column '{property.Name}' that {table} does not have");
            }

            fields.Add(new Field(property.Name, Value(property.Value)));
        }

        if (fields.Count == 0)
        {
            throw Refuse(application, $"its journal holds an empty {table} row");
        }

        return fields;
    }

    private static object? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
        _ => element.GetRawText(),
    };

    private static bool Occupied(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<Field> key)
        => connection.ExecuteScalar<long>(
            $"SELECT COUNT(*) FROM {Quote(table)} WHERE {Predicate(key, "k")};",
            Bind(key, "k"),
            transaction) > 0;

    private static bool RowExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        long id)
        => connection.ExecuteScalar<long>(
            $"SELECT COUNT(*) FROM {Quote(table)} WHERE {Quote(column)} = @id;",
            new { id },
            transaction) > 0;

    private static string Predicate(IReadOnlyList<Field> key, string prefix)
        => string.Join(
            " AND ",
            key.Select((field, i) => field.Value is null
                ? $"{Quote(field.Column)} IS NULL"
                : $"{Quote(field.Column)} = @{prefix}{i.ToString(CultureInfo.InvariantCulture)}"));

    private static DynamicParameters Bind(
        IReadOnlyList<Field> first,
        string firstPrefix,
        IReadOnlyList<Field>? second = null,
        string? secondPrefix = null)
    {
        var parameters = new DynamicParameters();
        Add(first, firstPrefix);

        if (second is not null && secondPrefix is not null)
        {
            Add(second, secondPrefix);
        }

        return parameters;

        void Add(IReadOnlyList<Field> fields, string prefix)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                parameters.Add(prefix + i.ToString(CultureInfo.InvariantCulture), fields[i].Value);
            }
        }
    }

    private static string Describe(IReadOnlyList<Field> key)
        => string.Join(", ", key.Select(field => $"{field.Column} = {field.Value ?? "NULL"}"));

    /// <summary>
    /// Double-quotes an identifier for use in a generated statement. The
    /// name has already been checked against <see cref="JournalledTables"/>
    /// or <c>PRAGMA table_info</c> by the time it reaches here, so
    /// quoting is a precaution, not the only guard.
    /// </summary>
    private static string Quote(string identifier) => $"\"{identifier}\"";

    private static IReadOnlySet<string> Columns(SqliteConnection connection, string table)
        => connection
            .Query<string>($"SELECT name FROM pragma_table_info(@table);", new { table })
            .ToHashSet(StringComparer.Ordinal);

    private static bool TableExists(SqliteConnection connection, string table)
        => connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;",
            new { table }) > 0;

    private static void RequireTable(SqliteConnection connection, string table)
    {
        if (!TableExists(connection, table))
        {
            throw new StandingMergeReplayRefusedException(
                $"Cannot replay standing merges into identity links: this database has "
                + $"merge_applications but no {table}. The replay runs between migrations 0018 "
                + "and 0019 and needs both the journal it reads and the link tables it writes.");
        }
    }

    private static StandingMergeReplayRefusedException Refuse(
        ApplicationRow application, string because)
        => new(
            $"Refusing to retire the destructive merge: merge application "
            + $"{application.Id.ToString(CultureInfo.InvariantCulture)} "
            + $"({application.AbsorbedTitle ?? "an untitled game"} folded into "
            + $"{application.SurvivingWorkId.ToString(CultureInfo.InvariantCulture)}, releases "
            + $"{application.LeftReleaseId.ToString(CultureInfo.InvariantCulture)} and "
            + $"{application.RightReleaseId.ToString(CultureInfo.InvariantCulture)}) still stands "
            + $"and cannot be replayed into an identity link because {because}. Nothing has been "
            + "changed. Restore the pre-upgrade copy of the database and run the previous version "
            + "of Winnow, which can still undo this merge from its history screen.");

    // ── Rows ─────────────────────────────────────────────────────────────────

    private readonly record struct Field(string Column, object? Value);

    private sealed record ApplicationRow
    {
        public long Id { get; init; }

        public long CandidateId { get; init; }

        public long LeftReleaseId { get; init; }

        public long RightReleaseId { get; init; }

        public long SurvivingWorkId { get; init; }

        public long? AbsorbedWorkId { get; init; }

        public DateTime AppliedAt { get; init; }

        public int? UndoJournalVersion { get; init; }

        public string? AbsorbedTitle { get; init; }
    }

    private sealed record JournalRow
    {
        public long Seq { get; init; }

        public string TableName { get; init; } = string.Empty;

        public string Op { get; init; } = string.Empty;

        public string KeyJson { get; init; } = string.Empty;

        public string BeforeJson { get; init; } = string.Empty;
    }

    private sealed record ConfirmedPairRow
    {
        public long CandidateId { get; init; }

        public long LeftWorkId { get; init; }

        public long RightWorkId { get; init; }
    }

    private sealed record SurvivorFactsRow
    {
        public long WorkId { get; init; }

        public bool HasIgdbId { get; init; }

        public bool NameIsProvisional { get; init; }

        public int ReleaseCount { get; init; }

        public SurvivorCandidate ToCandidate() => new()
        {
            WorkId = WorkId,
            HasIgdbId = HasIgdbId,
            NameIsProvisional = NameIsProvisional,
            ReleaseCount = ReleaseCount,
        };
    }
}

/// <summary>
/// The evidence payload written on a link the replay created, recording
/// the merge application id and candidate id it was derived from so the
/// provenance is traceable from the identity_links table alone.
/// </summary>
internal sealed record ReplayEvidence(
    long? ApplicationId,
    long CandidateId,
    long? LeftReleaseId,
    long? RightReleaseId,
    DateTime? AppliedAt);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReplayEvidence))]
internal sealed partial class ReplayEvidenceJsonContext : JsonSerializerContext;
