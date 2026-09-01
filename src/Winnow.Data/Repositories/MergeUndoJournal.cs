using System.Globalization;

namespace Winnow.Data.Repositories;

/// <summary>
/// The journal's operation names, matching migration 0017's CHECK on
/// <c>merge_undo_rows.op</c>.
/// </summary>
internal static class MergeUndoOps
{
    public const string Repoint = "repoint";
    public const string Delete = "delete";
    public const string Update = "update";
}

/// <summary>
/// Which of the three identity layers a column's value names, so a restore can
/// redirect it when an absorbed identity has to come back at a fresh id.
/// </summary>
internal enum MergeUndoIdentity
{
    None,
    Work,
    Release,
    Ownership,
}

/// <summary>
/// One journalled table. <c>Key</c> is the columns that identify a row (the
/// primary key everywhere except <c>merge_candidates</c>, where it is the
/// canonical pair, because 0016's UNIQUE on that pair is the constraint a
/// restore can collide with and the surrogate id is not). <c>Columns</c> is
/// every column, which is what a deleted row's <c>before_json</c> holds.
/// <c>Repointed</c> is the parent column(s) the merge rewrites. <c>InPlace</c>
/// is the columns a merge overwrites without moving the row; only <c>works</c>
/// (the COALESCE fill and the name promotion) and <c>ownership_accounts</c>
/// (the same-account merge) have any. <c>CollisionKey</c> is the rest of the
/// unique key that governs the executor's UPDATE OR IGNORE; empty when a
/// repoint on this table cannot collide.
/// </summary>
internal sealed record MergeUndoTable
{
    public required string Name { get; init; }

    public required string[] Key { get; init; }

    public required string[] Columns { get; init; }

    public string[] Repointed { get; init; } = [];

    public string[] InPlace { get; init; } = [];

    public string[] CollisionKey { get; init; } = [];

    /// <summary>
    /// The key a repoint journal row records: the primary key plus the
    /// repointed columns, all at their post-merge values. Matching on it
    /// answers "does the row still exist" and "is it still on the parent the
    /// merge moved it to" in one <c>COUNT(*)</c>.
    /// </summary>
    public string[] RepointKey
        => [.. Key, .. Repointed.Where(c => !Key.Contains(c, StringComparer.Ordinal))];
}

/// <summary>
/// The fifteen dependent tables a merge moves rows in, plus <c>works</c>, which
/// a merge edits in place and deletes but is nobody's dependent.
/// <c>achievements</c> and <c>achievement_unlocks</c> are absent on purpose:
/// the surviving-release rule prefers the side that holds them and a collapse
/// with achievements on both sides is refused, so they never move and there is
/// nothing to journal. <c>MergeUndoTests</c> pins that this list and
/// the executor's cascade tripwire name the same tables.
///
/// <para>One generic journal serves all of them. The legibility Dapper was
/// chosen for lives in the statements this class builds, which are ordinary SQL
/// naming ordinary columns.</para>
/// </summary>
internal static class MergeUndoJournal
{
    public const int Version = 1;

    public static readonly MergeUndoTable Works = new()
    {
        Name = "works",
        Key = ["id"],
        Columns =
        [
            "id", "igdb_id", "name", "sort_name", "first_release_year", "summary",
            "cover_url", "name_is_provisional", "publisher", "steam_app_type", "epic_categories",
        ],
        InPlace =
        [
            "igdb_id", "sort_name", "first_release_year", "summary", "cover_url",
            "publisher", "steam_app_type", "epic_categories", "name", "name_is_provisional",
        ],
    };

    public static readonly MergeUndoTable Releases = new()
    {
        Name = "releases",
        Key = ["id"],
        Columns = ["id", "work_id", "igdb_version_id", "name", "platform", "edition_note"],
        Repointed = ["work_id"],
    };

    public static readonly MergeUndoTable WorkFacets = new()
    {
        Name = "work_facets",
        Key = ["work_id", "facet_id"],
        Columns = ["work_id", "facet_id"],
        Repointed = ["work_id"],
        CollisionKey = ["{s}.\"facet_id\" = {a}.\"facet_id\""],
    };

    public static readonly MergeUndoTable ExternalIds = new()
    {
        Name = "external_ids",
        Key = ["provider", "provider_id"],
        Columns = ["release_id", "provider", "provider_id"],
        Repointed = ["release_id"],
    };

    public static readonly MergeUndoTable Ownerships = new()
    {
        Name = "ownerships",
        Key = ["id"],
        Columns =
        [
            "id", "release_id", "store", "account_ref", "acquired_at", "license_type",
            "price_paid_cents", "price_source", "install_path", "installed",
        ],
        Repointed = ["release_id"],
    };

    public static readonly MergeUndoTable PlayRecords = new()
    {
        Name = "play_records",
        Key = ["id"],
        Columns = ["id", "ownership_id", "playtime_minutes", "last_played_at", "source", "observed_at"],
        Repointed = ["ownership_id"],
        CollisionKey =
        [
            "{s}.\"source\" = {a}.\"source\"",
            "{s}.\"observed_at\" = {a}.\"observed_at\"",
            "{s}.\"playtime_minutes\" = {a}.\"playtime_minutes\"",
            "COALESCE({s}.\"last_played_at\", '') = COALESCE({a}.\"last_played_at\", '')",
        ],
    };

    public static readonly MergeUndoTable PlaytimeSnapshots = new()
    {
        Name = "playtime_snapshots",
        Key = ["id"],
        Columns = ["id", "ownership_id", "playtime_minutes", "observed_at"],
        Repointed = ["ownership_id"],
        CollisionKey =
        [
            "{s}.\"observed_at\" = {a}.\"observed_at\"",
            "{s}.\"playtime_minutes\" = {a}.\"playtime_minutes\"",
        ],
    };

    public static readonly MergeUndoTable Sessions = new()
    {
        Name = "sessions",
        Key = ["id"],
        Columns =
        [
            "id", "ownership_id", "started_at", "ended_at", "duration_s",
            "detection_method", "attributed_by",
        ],
        Repointed = ["ownership_id"],
    };

    public static readonly MergeUndoTable OwnershipAccounts = new()
    {
        Name = "ownership_accounts",
        Key = ["ownership_id", "account_ref"],
        Columns =
        [
            "ownership_id", "account_ref", "playtime_minutes", "last_played_at",
            "source", "first_seen_at", "last_seen_at",
        ],
        Repointed = ["ownership_id"],
        InPlace = ["playtime_minutes", "last_played_at", "source", "first_seen_at", "last_seen_at"],
        CollisionKey = ["{s}.\"account_ref\" = {a}.\"account_ref\""],
    };

    public static readonly MergeUndoTable UpdateEvents = new()
    {
        Name = "update_events",
        Key = ["id"],
        Columns = ["id", "release_id", "kind", "build_id", "occurred_at", "title", "raw_json", "url"],
        Repointed = ["release_id"],
        CollisionKey =
        [
            "{s}.\"kind\" = {a}.\"kind\"",
            "{s}.\"occurred_at\" = {a}.\"occurred_at\"",
        ],
    };

    public static readonly MergeUndoTable UpdateAcknowledgements = new()
    {
        Name = "update_acknowledgements",
        Key = ["id"],
        Columns = ["id", "release_id", "acknowledged_through", "created_at", "revoked_at"],
        Repointed = ["release_id"],
    };

    public static readonly MergeUndoTable ListItems = new()
    {
        Name = "list_items",
        Key = ["list_id", "release_id"],
        Columns = ["list_id", "release_id", "position"],
        Repointed = ["release_id"],
        CollisionKey = ["{s}.\"list_id\" = {a}.\"list_id\""],
    };

    public static readonly MergeUndoTable ReleaseFacets = new()
    {
        Name = "release_facets",
        Key = ["release_id", "facet_id"],
        Columns = ["release_id", "facet_id", "rank"],
        Repointed = ["release_id"],
        CollisionKey = ["{s}.\"facet_id\" = {a}.\"facet_id\""],
    };

    public static readonly MergeUndoTable FeedVerdicts = new()
    {
        Name = "feed_verdicts",
        Key = ["id"],
        Columns = ["id", "release_id", "kind", "created_at", "expires_at", "revoked_at"],
        Repointed = ["release_id"],
    };

    public static readonly MergeUndoTable FeedSurfacings = new()
    {
        Name = "feed_surfacings",
        Key = ["release_id", "surfaced_on"],
        Columns = ["release_id", "surfaced_on", "shelf_id"],
        Repointed = ["release_id"],
        CollisionKey = ["{s}.\"surfaced_on\" = {a}.\"surfaced_on\""],
    };

    public static readonly MergeUndoTable MergeCandidates = new()
    {
        Name = "merge_candidates",
        Key = ["left_release_id", "right_release_id"],
        Columns = ["id", "left_release_id", "right_release_id", "score", "signals_json", "status"],
        Repointed = ["left_release_id", "right_release_id"],
    };

    /// <summary>
    /// Restore order. Parents before children, so a re-inserted work exists
    /// before its releases are repointed back onto it and a re-inserted
    /// ownership exists before its play records land on it. This is the merge's
    /// order run backwards, and it is why nothing is deleted during an undo and
    /// no cascade ever fires.
    /// </summary>
    public static readonly IReadOnlyList<MergeUndoTable> IdentityOrder = [Works, Releases, Ownerships];

    public static readonly IReadOnlyList<MergeUndoTable> All =
    [
        Works, Releases, WorkFacets, ExternalIds, Ownerships,
        PlayRecords, PlaytimeSnapshots, Sessions, OwnershipAccounts,
        UpdateEvents, UpdateAcknowledgements, ListItems, ReleaseFacets,
        FeedVerdicts, FeedSurfacings, MergeCandidates,
    ];

    /// <summary>
    /// The tables whose dropped rows the executor sums into
    /// <c>summary_json</c>'s single <c>duplicate_rows_dropped</c> scalar, minus
    /// <c>merge_candidates</c> which is verified separately because its residue
    /// deletes have a special structure. Four of these carry payload the
    /// survivor does not have: <c>list_items.position</c>,
    /// <c>release_facets.rank</c>, <c>feed_surfacings.shelf_id</c>, and the
    /// folded <c>ownership_accounts</c> row's own playtime,
    /// <c>last_played_at</c>, <c>source</c> and seen-window. A count could
    /// never have restored them.
    /// </summary>
    public static readonly IReadOnlyList<MergeUndoTable> DeduplicatedByCount =
    [
        WorkFacets, UpdateEvents, ListItems, ReleaseFacets, FeedSurfacings,
        PlayRecords, PlaytimeSnapshots, OwnershipAccounts,
    ];

    /// <summary>
    /// What layer a column's value names. Used only to redirect a restored
    /// reference when an absorbed identity comes back at a fresh id.
    /// </summary>
    public static MergeUndoIdentity IdentityOf(MergeUndoTable table, string column) => column switch
    {
        "id" when table.Name == "works" => MergeUndoIdentity.Work,
        "id" when table.Name == "releases" => MergeUndoIdentity.Release,
        "id" when table.Name == "ownerships" => MergeUndoIdentity.Ownership,
        "work_id" => MergeUndoIdentity.Work,
        "release_id" or "left_release_id" or "right_release_id" => MergeUndoIdentity.Release,
        "ownership_id" => MergeUndoIdentity.Ownership,
        _ => MergeUndoIdentity.None,
    };

    // ── SQL fragments ────────────────────────────────────────────────────────

    public static string Quote(string column) => $"\"{column}\"";

    public static string JsonObject(string alias, IEnumerable<string> columns)
        => "json_object(" + string.Join(", ", columns.Select(c => $"'{c}', {alias}.{Quote(c)}")) + ")";

    public static string JsonObject(IEnumerable<KeyValuePair<string, string>> pairs)
        => "json_object(" + string.Join(", ", pairs.Select(p => $"'{p.Key}', {p.Value}")) + ")";

    public static string Extract(string source, string column)
        => $"json_extract({source}, '$.{column}')";

    public static string OrderBy(string alias, MergeUndoTable table)
        => string.Join(", ", table.Key.Select(c => $"{alias}.{Quote(c)}"));

    /// <summary>
    /// The anti-join that UPDATE OR IGNORE applies implicitly. A row on the
    /// absorbed parent moves only if the surviving parent does not already hold
    /// a row with the same rest-of-unique-key; the ones that do not move are
    /// the residue the executor then deletes. Capturing the two sets apart is
    /// what keeps each journal row correct on its own: a capture of the whole
    /// set would journal a repoint whose key names the survivor's own row, and
    /// the undo would drag that row onto the absorbed parent.
    /// </summary>
    public static string CollisionExists(MergeUndoTable table, string survivingParam)
    {
        if (table.CollisionKey.Length == 0)
        {
            return string.Empty;
        }

        var predicates = table.CollisionKey
            .Select(p => p.Replace("{s}", "s", StringComparison.Ordinal)
                          .Replace("{a}", "a", StringComparison.Ordinal));

        return $"""
            EXISTS (
                    SELECT 1 FROM {table.Name} s
                    WHERE s.{Quote(table.Repointed[0])} = {survivingParam}
                      AND {string.Join("\n                      AND ", predicates)})
            """;
    }

    // ── Capture ──────────────────────────────────────────────────────────────

    public static string CaptureRepoint(MergeUndoTable table, string survivingParam, string absorbedParam)
    {
        var repointed = table.Repointed[0];
        var key = table.RepointKey.Select(c => new KeyValuePair<string, string>(
            c, c == repointed ? survivingParam : $"a.{Quote(c)}"));

        var collision = CollisionExists(table, survivingParam);
        var antiJoin = collision.Length == 0 ? string.Empty : $"\n  AND NOT {collision}";

        return $"""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            SELECT @applicationId,
                   @seqBase + ROW_NUMBER() OVER (ORDER BY {OrderBy("a", table)}),
                   '{table.Name}',
                   '{MergeUndoOps.Repoint}',
                   {JsonObject(key)},
                   {JsonObject("a", table.Repointed)}
            FROM {table.Name} a
            WHERE a.{Quote(repointed)} = {absorbedParam}{antiJoin};
            """;
    }

    public static string CaptureDelete(MergeUndoTable table, string where)
        => $"""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            SELECT @applicationId,
                   @seqBase + ROW_NUMBER() OVER (ORDER BY {OrderBy("a", table)}),
                   '{table.Name}',
                   '{MergeUndoOps.Delete}',
                   {JsonObject("a", table.Key)},
                   {JsonObject("a", table.Columns)}
            FROM {table.Name} a
            WHERE {where};
            """;

    public static string CaptureUpdate(MergeUndoTable table, string where)
        => $"""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            SELECT @applicationId,
                   @seqBase + ROW_NUMBER() OVER (ORDER BY {OrderBy("a", table)}),
                   '{table.Name}',
                   '{MergeUndoOps.Update}',
                   {JsonObject("a", table.Key)},
                   {JsonObject("a", table.InPlace)}
            FROM {table.Name} a
            WHERE {where};
            """;

    // ── Verification (gate two) ──────────────────────────────────────────────

    private static string JournalScope(MergeUndoTable table, string op)
        => $"""
            j.application_id = @applicationId
              AND j.table_name = '{table.Name}'
              AND j.op = '{op}'
            """;

    /// <summary>
    /// Counts the repoint rows that are not where the merge left them. Zero is
    /// the only acceptable answer. A repoint row whose primary key also appears
    /// as a delete row for the same table in the same application is exempt: the
    /// work unify repoints every release of the absorbed work, and a release
    /// collapse then deletes one of those same rows, so the row is legitimately
    /// gone. The restore puts deleted rows back before it reverses any repoint,
    /// so nothing is lost by the exemption.
    /// </summary>
    public static string VerifyRepoint(MergeUndoTable table)
    {
        var superseded = string.Join(
            "\n                        AND ",
            table.Key.Select(c => $"{Extract("d.key_json", c)} = {Extract("j.key_json", c)}"));

        var stillThere = string.Join(
            "\n                        AND ",
            table.RepointKey.Select(c => $"t.{Quote(c)} = {Extract("j.key_json", c)}"));

        return $"""
            SELECT COUNT(*)
            FROM merge_undo_rows j
            WHERE {JournalScope(table, MergeUndoOps.Repoint)}
              AND NOT EXISTS (
                    SELECT 1 FROM merge_undo_rows d
                    WHERE d.application_id = j.application_id
                      AND d.table_name = j.table_name
                      AND d.op = '{MergeUndoOps.Delete}'
                      AND {superseded})
              AND NOT EXISTS (
                    SELECT 1 FROM {table.Name} t
                    WHERE {stillThere});
            """;
    }

    /// <summary>
    /// Counts the delete rows whose restore key is already occupied. Zero is the
    /// only acceptable answer. Skipped for <c>works</c>, <c>releases</c> and
    /// <c>ownerships</c>: SQLite allocates rowids as max+1 without
    /// AUTOINCREMENT, so a later insert can hold an absorbed identity's id, and
    /// that case is handled by restoring the identity at a fresh id rather than
    /// by refusing.
    /// </summary>
    public static string VerifyDeleteKeyFree(MergeUndoTable table, MergeUndoIdMap map)
    {
        var occupied = string.Join(
            "\n                        AND ",
            table.Key.Select(c => $"t.{Quote(c)} = {map.Redirect(IdentityOf(table, c), Extract("j.key_json", c))}"));

        return $"""
            SELECT COUNT(*)
            FROM merge_undo_rows j
            WHERE {JournalScope(table, MergeUndoOps.Delete)}
              AND EXISTS (
                    SELECT 1 FROM {table.Name} t
                    WHERE {occupied});
            """;
    }

    public static string VerifyUpdate(MergeUndoTable table)
    {
        var present = string.Join(
            "\n                        AND ",
            table.Key.Select(c => $"t.{Quote(c)} = {Extract("j.key_json", c)}"));

        return $"""
            SELECT COUNT(*)
            FROM merge_undo_rows j
            WHERE {JournalScope(table, MergeUndoOps.Update)}
              AND NOT EXISTS (
                    SELECT 1 FROM {table.Name} t
                    WHERE {present});
            """;
    }

    public static string CountRows(MergeUndoTable table, string op)
        => $"SELECT COUNT(*) FROM merge_undo_rows j WHERE {JournalScope(table, op)};";

    // ── Restore ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-inserts every row the merge deleted, reading each column out of
    /// <c>before_json</c>. Ordered by <c>seq</c> so the insert order is the
    /// capture order. <c>merge_candidates</c> is re-canonicalised on the way
    /// in; see <see cref="ReinsertMergeCandidates"/>.
    /// </summary>
    public static string Reinsert(MergeUndoTable table, MergeUndoIdMap map)
    {
        var values = table.Columns.Select(
            c => map.Redirect(IdentityOf(table, c), Extract("j.before_json", c)));

        return $"""
            INSERT INTO {table.Name} ({string.Join(", ", table.Columns.Select(Quote))})
            SELECT {string.Join(",\n                   ", values)}
            FROM merge_undo_rows j
            WHERE {JournalScope(table, MergeUndoOps.Delete)}
            ORDER BY j.seq;
            """;
    }

    /// <summary>
    /// Puts every repointed row back on the parent it came from. The WHERE
    /// matches on the post-merge key, which is exactly what the journal
    /// recorded, so the survivor's own rows (which have a different key) are
    /// never touched.
    /// </summary>
    public static string RepointBack(MergeUndoTable table, MergeUndoIdMap map)
    {
        var sets = table.Repointed.Select(
            c => $"{Quote(c)} = {map.Redirect(IdentityOf(table, c), Extract("j.before_json", c))}");

        // The key is redirected too, not just the value being written back. A
        // release repointed by the work unify and then deleted by the release
        // collapse is restored first, possibly at a fresh id; without the
        // redirection its repoint reversal would look for the row at an id
        // nothing occupies, and the release would keep the surviving work rather
        // than going back to the absorbed one. Every other repoint key names a
        // surviving parent, which is never remapped, so the redirection is a
        // no-op there.
        var match = string.Join(
            "\n              AND ",
            table.RepointKey.Select(
                c => $"t.{Quote(c)} = {map.Redirect(IdentityOf(table, c), Extract("j.key_json", c))}"));

        return $"""
            UPDATE {table.Name} AS t
            SET {string.Join(",\n                ", sets)}
            FROM merge_undo_rows j
            WHERE {JournalScope(table, MergeUndoOps.Repoint)}
              AND {match};
            """;
    }

    /// <summary>
    /// <c>merge_candidates</c> has to be restored by hand rather than by
    /// <see cref="Reinsert"/>, for two reasons. 0016's CHECK admits only
    /// <c>left &lt; right</c>, and a pair whose absorbed release comes back at
    /// a fresh id may sort on the other side of its partner, so the pair is
    /// re-canonicalised rather than merely un-substituted. The surrogate id is
    /// preserved only when it is still free; nothing inside the journal
    /// references it, and <c>merge_applications.candidate_id</c> has no foreign
    /// key precisely because the row it names may be gone.
    /// </summary>
    public static string ReinsertMergeCandidates(MergeUndoIdMap map)
    {
        var (low, high) = CanonicalPair(map, "j.before_json");

        return $"""
            INSERT INTO merge_candidates (id, left_release_id, right_release_id, score, signals_json, status)
            SELECT CASE
                       WHEN EXISTS (SELECT 1 FROM merge_candidates c
                                    WHERE c.id = {Extract("j.before_json", "id")})
                       THEN NULL
                       ELSE {Extract("j.before_json", "id")}
                   END,
                   {low},
                   {high},
                   {Extract("j.before_json", "score")},
                   {Extract("j.before_json", "signals_json")},
                   {Extract("j.before_json", "status")}
            FROM merge_undo_rows j
            WHERE {JournalScope(MergeCandidates, MergeUndoOps.Delete)}
            ORDER BY j.seq;
            """;
    }

    public static string RepointBackMergeCandidates(MergeUndoIdMap map)
    {
        var (low, high) = CanonicalPair(map, "j.before_json");

        return $"""
            UPDATE merge_candidates AS t
            SET left_release_id  = {low},
                right_release_id = {high}
            FROM merge_undo_rows j
            WHERE {JournalScope(MergeCandidates, MergeUndoOps.Repoint)}
              AND t.left_release_id  = {Extract("j.key_json", "left_release_id")}
              AND t.right_release_id = {Extract("j.key_json", "right_release_id")};
            """;
    }

    // merge_candidates is the one table where a deleted row's key can
    // legitimately be occupied at undo time, and it is occupied by design. The
    // executor deletes a pending proposal precisely because a decision about the
    // absorbed release is moving onto its pair, so the deleted row's key is the
    // moving row's post-merge key. That pair is vacated by this same undo, one
    // statement earlier, which is why the restore reverses merge_candidates
    // repoints before it re-inserts merge_candidates deletes. A pair occupied by
    // anything else is drift.
    public static string VerifyMergeCandidateKeysFree(MergeUndoIdMap map)
    {
        var (low, high) = CanonicalPair(map, "j.key_json");

        return $"""
            SELECT COUNT(*)
            FROM merge_undo_rows j
            WHERE {JournalScope(MergeCandidates, MergeUndoOps.Delete)}
              AND EXISTS (
                    SELECT 1 FROM merge_candidates t
                    WHERE t.left_release_id = {low}
                      AND t.right_release_id = {high})
              AND NOT EXISTS (
                    SELECT 1 FROM merge_undo_rows moving
                    WHERE moving.application_id = j.application_id
                      AND moving.table_name = 'merge_candidates'
                      AND moving.op = '{MergeUndoOps.Repoint}'
                      AND {Extract("moving.key_json", "left_release_id")} = {low}
                      AND {Extract("moving.key_json", "right_release_id")} = {high});
            """;
    }

    /// <summary>
    /// The residue deletes: <c>merge_candidates</c> rows the executor dropped
    /// because they still named the absorbed release after the repoint, which
    /// are the only <c>merge_candidates</c> deletes it counts toward
    /// <c>duplicate_rows_dropped</c>. The other two deletes are told apart by
    /// their pair, not by their position: the answered pair is exactly
    /// (absorbed, surviving), and a pending proposal displaced by a decision
    /// moving onto it names neither.
    /// </summary>
    public const string CountMergeCandidateResidue = """
        SELECT COUNT(*)
        FROM merge_undo_rows j
        WHERE j.application_id = @applicationId
          AND j.table_name = 'merge_candidates'
          AND j.op = 'delete'
          AND (json_extract(j.before_json, '$.left_release_id')  = @absorbedReleaseId
            OR json_extract(j.before_json, '$.right_release_id') = @absorbedReleaseId)
          AND NOT (json_extract(j.before_json, '$.left_release_id')
                       = MIN(@absorbedReleaseId, @survivingReleaseId)
               AND json_extract(j.before_json, '$.right_release_id')
                       = MAX(@absorbedReleaseId, @survivingReleaseId));
        """;

    private static (string Low, string High) CanonicalPair(MergeUndoIdMap map, string source)
    {
        var left = map.Redirect(MergeUndoIdentity.Release, Extract(source, "left_release_id"));
        var right = map.Redirect(MergeUndoIdentity.Release, Extract(source, "right_release_id"));
        return ($"MIN({left}, {right})", $"MAX({left}, {right})");
    }

    /// <summary>
    /// Puts back the columns a merge overwrote without moving the row, the two
    /// operations 0016's audit left no trace of at all.
    /// </summary>
    public static string RestoreInPlace(MergeUndoTable table)
    {
        var sets = table.InPlace.Select(c => $"{Quote(c)} = {Extract("j.before_json", c)}");

        var match = string.Join(
            "\n              AND ",
            table.Key.Select(c => $"t.{Quote(c)} = {Extract("j.key_json", c)}"));

        return $"""
            UPDATE {table.Name} AS t
            SET {string.Join(",\n                ", sets)}
            FROM merge_undo_rows j
            WHERE {JournalScope(table, MergeUndoOps.Update)}
              AND {match};
            """;
    }
}

/// <summary>
/// The redirections a restore has to apply because an absorbed work, release or
/// ownership could not come back at its original id. Empty in the ordinary case,
/// in which every <see cref="Redirect"/> call returns its argument unchanged and
/// the generated SQL is exactly what it would have been without the map. Nothing
/// outside the database persists one of these ids (the cover cache keys on
/// provider ids), so the user cannot tell the difference.
/// </summary>
internal sealed class MergeUndoIdMap
{
    private readonly Dictionary<MergeUndoIdentity, List<(long Old, long New)>> _moves = [];

    public bool Any { get; private set; }

    public void Add(MergeUndoIdentity kind, long oldId, long newId)
    {
        if (oldId == newId)
        {
            return;
        }

        if (!_moves.TryGetValue(kind, out var list))
        {
            list = [];
            _moves[kind] = list;
        }

        list.Add((oldId, newId));
        Any = true;
    }

    public long Resolve(MergeUndoIdentity kind, long id)
        => _moves.TryGetValue(kind, out var list)
            ? list.FirstOrDefault(m => m.Old == id, (Old: id, New: id)).New
            : id;

    public string Redirect(MergeUndoIdentity kind, string expression)
    {
        if (kind == MergeUndoIdentity.None
            || !_moves.TryGetValue(kind, out var list)
            || list.Count == 0)
        {
            return expression;
        }

        var arms = list.Select(m => string.Format(
            CultureInfo.InvariantCulture,
            "WHEN {0} THEN {1}",
            m.Old,
            m.New));

        return $"CASE {expression} {string.Join(" ", arms)} ELSE {expression} END";
    }
}
