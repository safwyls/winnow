using System.Text.Json;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Merging;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Reverses an applied merge from the row-level journal migration 0017
/// introduced, or refuses and says why.
///
/// <para><b>Gate one</b> is cheap, read-only and identity-scoped: the surviving
/// identities still exist, this application has not already been reversed, it was
/// recorded with a journal, and no later application that still stands names any
/// of its four identities. Scoping to identities rather than to position in the
/// log is what lets two merges on unrelated games be undone in either order.</para>
///
/// <para><b>Gate two</b> runs inside the undo's own transaction and is the proof
/// rather than the estimate: every journalled repoint row still sits on the
/// parent the merge moved it to, every key a restore needs is free, every
/// journalled in-place edit is still there, and the counts
/// <c>summary_json</c> recorded still hold. Any drift throws and the transaction
/// rolls back, so the database is untouched. Same shape as the executor's
/// <c>AssertDrainedAsync</c> tripwire.</para>
///
/// <para>Nothing is deleted during an undo, so no ON DELETE CASCADE ever
/// fires.</para>
/// </summary>
public sealed class MergeUndoRepository : IMergeUndoRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public MergeUndoRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<MergeUndoPlan> PlanUndoAsync(long applicationId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var log = await LoadLogAsync(lease, ct);
        var row = log.FirstOrDefault(entry => entry.Id == applicationId);

        return row is null
            ? MergeUndoPlan.Refused(applicationId, MergeUndoBlocker.ApplicationNotFound)
            : BuildPlan(row, log);
    }

    public async Task<IReadOnlyList<MergeUndoPlan>> ListUndoPlansAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var log = await LoadLogAsync(lease, ct);
        return log
            .OrderByDescending(entry => entry.Id)
            .Select(entry => BuildPlan(entry, log))
            .ToList();
    }

    public async Task<MergeUndoResult> UndoAsync(long applicationId, CancellationToken ct = default)
    {
        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        var log = await LoadLogAsync(lease, ct);
        var row = log.FirstOrDefault(entry => entry.Id == applicationId);

        var plan = row is null
            ? MergeUndoPlan.Refused(applicationId, MergeUndoBlocker.ApplicationNotFound)
            : BuildPlan(row, log);

        if (!plan.Reversible)
        {
            throw new MergeUndoRefusedException(plan, Refusal(plan));
        }

        var result = await ReverseAsync(lease, row!, plan, ct);
        scope.Commit();
        return result;
    }

    // ── Gate one ─────────────────────────────────────────────────────────────

    private static MergeUndoPlan BuildPlan(LogRow row, IReadOnlyList<LogRow> log)
    {
        var blockers = new List<MergeUndoBlocker>();

        if (row.UndoneAt is not null)
        {
            blockers.Add(MergeUndoBlocker.AlreadyUndone);
        }

        if (row.UndoJournalVersion is null)
        {
            blockers.Add(MergeUndoBlocker.PredatesUndoSupport);
        }

        if (!row.SurvivingWorkExists || !row.SurvivingReleaseExists)
        {
            blockers.Add(MergeUndoBlocker.GameNoLongerExists);
        }

        // Work ids are compared against work ids and release ids against release
        // ids. The two are separate id spaces; treating "any of the four identity
        // columns" literally would block an undo because a work happened to share
        // a number with an unrelated release.
        var blocking = log
            .Where(later => later.Id > row.Id
                         && later.UndoneAt is null
                         && (later.Works().Overlaps(row.Works())
                          || later.Releases().Overlaps(row.Releases())))
            .Select(later => (long?)later.Id)
            .Min();

        if (blocking is not null)
        {
            blockers.Add(MergeUndoBlocker.LaterMergeConsumedIdentity);
        }

        return new MergeUndoPlan
        {
            ApplicationId = row.Id,
            Application = row.ToRecord(),
            Blockers = blockers,
            BlockingApplicationId = blocking,
        };
    }

    private static string Refusal(MergeUndoPlan plan) => plan.PrimaryBlocker switch
    {
        MergeUndoBlocker.ApplicationNotFound =>
            $"No merge application {plan.ApplicationId}.",
        MergeUndoBlocker.AlreadyUndone =>
            $"Merge application {plan.ApplicationId} has already been undone.",
        MergeUndoBlocker.PredatesUndoSupport =>
            $"Merge application {plan.ApplicationId} was applied before the undo journal existed, "
            + "so nothing records which rows it moved. It cannot be reversed.",
        MergeUndoBlocker.GameNoLongerExists =>
            $"A game merge application {plan.ApplicationId} touched no longer exists.",
        MergeUndoBlocker.LaterMergeConsumedIdentity =>
            $"Merge application {plan.BlockingApplicationId} absorbed one of the games merge "
            + $"application {plan.ApplicationId} produced. Undo it first.",
        _ => $"Merge application {plan.ApplicationId} cannot be undone.",
    };

    // ── Gate two, then the reversal ──────────────────────────────────────────

    private async Task<MergeUndoResult> ReverseAsync(
        DbLease lease, LogRow row, MergeUndoPlan plan, CancellationToken ct)
    {
        var map = await BuildIdMapAsync(lease, row.Id, ct);

        await VerifyAsync(lease, row, map, ct);

        var reinserted = await RestoreDeletedAsync(lease, row.Id, map, ct);
        var repointed = await ReverseRepointsAsync(lease, row.Id, map, ct);

        // merge_candidates is re-inserted after its own repoints are reversed,
        // not before. The executor deletes a pending proposal because a decision
        // is moving onto its pair, so the proposal's key is only free once that
        // decision has moved back.
        reinserted += await lease.Connection.ExecuteAsync(new CommandDefinition(
            MergeUndoJournal.ReinsertMergeCandidates(map),
            new { applicationId = row.Id }, lease.Transaction, cancellationToken: ct));

        var inPlace = await RestoreInPlaceAsync(lease, row.Id, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE merge_applications SET undone_at = @undoneAt WHERE id = @applicationId;",
            new { applicationId = row.Id, undoneAt = _clock.GetUtcNow().UtcDateTime },
            lease.Transaction, cancellationToken: ct));

        await MarkCandidateUndoneAsync(lease, row, map, ct);

        return new MergeUndoResult
        {
            ApplicationId = row.Id,
            RestoredWorkId = row.AbsorbedWorkId is { } work
                ? map.Resolve(MergeUndoIdentity.Work, work)
                : null,
            RestoredReleaseId = row.AbsorbedReleaseId is { } release
                ? map.Resolve(MergeUndoIdentity.Release, release)
                : null,
            IdentityIdsReused = map.Any,
            RowsReinserted = reinserted,
            RowsRepointedBack = repointed,
            RowsRestoredInPlace = inPlace,
        };
    }

    // Decides where each absorbed identity comes back. Its original id when that
    // is still free, a fresh one when a later insert took it. SQLite allocates
    // rowids as max+1 without AUTOINCREMENT, so reuse is reachable. Rebuilding
    // works, releases and ownerships onto AUTOINCREMENT was considered and
    // rejected: three table rebuilds and every dependent foreign key, to prevent
    // something the undo handles directly. Release, work and ownership ids never
    // leave the database, so the user cannot observe the difference. Runs before
    // any write, so gate two below sees the ids the restore will actually use.
    private static async Task<MergeUndoIdMap> BuildIdMapAsync(
        DbLease lease, long applicationId, CancellationToken ct)
    {
        var map = new MergeUndoIdMap();

        foreach (var table in MergeUndoJournal.IdentityOrder)
        {
            var journalled = (await lease.Connection.QueryAsync<long>(new CommandDefinition($"""
                SELECT json_extract(j.key_json, '$.id')
                FROM merge_undo_rows j
                WHERE j.application_id = @applicationId
                  AND j.table_name = '{table.Name}'
                  AND j.op = '{MergeUndoOps.Delete}'
                ORDER BY j.seq;
                """, new { applicationId }, lease.Transaction, cancellationToken: ct))).AsList();

            if (journalled.Count == 0)
            {
                continue;
            }

            var taken = (await lease.Connection.QueryAsync<long>(new CommandDefinition(
                $"SELECT id FROM {table.Name} WHERE id IN @journalled;",
                new { journalled }, lease.Transaction, cancellationToken: ct))).ToHashSet();

            if (taken.Count == 0)
            {
                continue;
            }

            var ceiling = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COALESCE(MAX(id), 0) FROM {table.Name};",
                transaction: lease.Transaction, cancellationToken: ct));

            var next = Math.Max(ceiling, journalled.Max()) + 1;

            foreach (var id in journalled.Where(taken.Contains))
            {
                map.Add(IdentityOf(table), id, next++);
            }
        }

        return map;
    }

    private static async Task VerifyAsync(
        DbLease lease, LogRow row, MergeUndoIdMap map, CancellationToken ct)
    {
        var drift = new List<string>();

        foreach (var table in MergeUndoJournal.All)
        {
            if (table.Repointed.Length > 0)
            {
                await CheckAsync(
                    MergeUndoJournal.VerifyRepoint(table),
                    $"{table.Name}: journalled row(s) are no longer where the merge left them");
            }

            if (table.InPlace.Length > 0)
            {
                await CheckAsync(
                    MergeUndoJournal.VerifyUpdate(table),
                    $"{table.Name}: journalled row(s) edited in place no longer exist");
            }

            // works, releases and ownerships are exempt: a reused id is handled
            // by restoring the identity at a fresh one, not by refusing.
            if (MergeUndoJournal.IdentityOrder.Contains(table))
            {
                continue;
            }

            await CheckAsync(
                table == MergeUndoJournal.MergeCandidates
                    ? MergeUndoJournal.VerifyMergeCandidateKeysFree(map)
                    : MergeUndoJournal.VerifyDeleteKeyFree(table, map),
                $"{table.Name}: the key of a deleted row is already occupied");
        }

        await VerifyCountsAsync(lease, row, drift, ct);

        if (drift.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to undo merge application {row.Id}: {string.Join("; ", drift)}. "
                + "The database has moved since the merge was applied, so the reversal would be "
                + "partial. Undo aborted.");
        }

        async Task CheckAsync(string statement, string complaint)
        {
            var offenders = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                statement, new { applicationId = row.Id }, lease.Transaction, cancellationToken: ct));

            if (offenders > 0)
            {
                drift.Add($"{complaint} ({offenders})");
            }
        }
    }

    // summary_json's counts, checked against the journal. Every per-table field
    // is the number of repoint rows for that table; ownerships_folded is the
    // number of ownership rows deleted; achievements and achievement_unlocks are
    // structurally zero because achievements never move; and
    // duplicate_rows_dropped is one scalar over nine tables, so it is checked as
    // the sum the executor actually added up. A mismatch means the journal and
    // the receipt disagree about the same merge, which is drift by definition.
    private static async Task VerifyCountsAsync(
        DbLease lease, LogRow row, List<string> drift, CancellationToken ct)
    {
        if (row.SummaryJson is null)
        {
            drift.Add("merge_applications.summary_json is missing");
            return;
        }

        var summary = JsonSerializer.Deserialize(
            row.SummaryJson, MergeJsonContext.Default.MergeRepointCounts);

        if (summary is null)
        {
            drift.Add("merge_applications.summary_json is not a repoint summary");
            return;
        }

        (MergeUndoTable Table, int Expected)[] repoints =
        [
            (MergeUndoJournal.Releases, summary.Releases),
            (MergeUndoJournal.WorkFacets, summary.WorkFacets),
            (MergeUndoJournal.ExternalIds, summary.ExternalIds),
            (MergeUndoJournal.Ownerships, summary.Ownerships),
            (MergeUndoJournal.UpdateEvents, summary.UpdateEvents),
            (MergeUndoJournal.UpdateAcknowledgements, summary.UpdateAcknowledgements),
            (MergeUndoJournal.ListItems, summary.ListItems),
            (MergeUndoJournal.ReleaseFacets, summary.ReleaseFacets),
            (MergeUndoJournal.FeedVerdicts, summary.FeedVerdicts),
            (MergeUndoJournal.FeedSurfacings, summary.FeedSurfacings),
            (MergeUndoJournal.MergeCandidates, summary.MergeCandidates),
            (MergeUndoJournal.PlayRecords, summary.PlayRecords),
            (MergeUndoJournal.PlaytimeSnapshots, summary.PlaytimeSnapshots),
            (MergeUndoJournal.Sessions, summary.Sessions),
            (MergeUndoJournal.OwnershipAccounts, summary.OwnershipAccounts),
        ];

        foreach (var (table, expected) in repoints)
        {
            var actual = await CountAsync(MergeUndoJournal.CountRows(table, MergeUndoOps.Repoint));
            if (actual != expected)
            {
                drift.Add($"{table.Name}: summary says {expected} repointed, the journal holds {actual}");
            }
        }

        var folded = await CountAsync(
            MergeUndoJournal.CountRows(MergeUndoJournal.Ownerships, MergeUndoOps.Delete));
        if (folded != summary.OwnershipsFolded)
        {
            drift.Add(
                $"ownerships: summary says {summary.OwnershipsFolded} folded, "
                + $"the journal holds {folded}");
        }

        if (summary.Achievements != 0 || summary.AchievementUnlocks != 0)
        {
            drift.Add("achievements moved, which the surviving-release rule forbids");
        }

        var dropped = 0L;
        foreach (var table in MergeUndoJournal.DeduplicatedByCount)
        {
            dropped += await CountAsync(MergeUndoJournal.CountRows(table, MergeUndoOps.Delete));
        }

        if (row.AbsorbedReleaseId is not null)
        {
            dropped += await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                MergeUndoJournal.CountMergeCandidateResidue,
                new
                {
                    applicationId = row.Id,
                    absorbedReleaseId = row.AbsorbedReleaseId,
                    survivingReleaseId = row.SurvivingReleaseId,
                },
                lease.Transaction, cancellationToken: ct));
        }

        if (dropped != summary.DuplicateRowsDropped)
        {
            drift.Add(
                $"summary says {summary.DuplicateRowsDropped} redundant row(s) dropped, "
                + $"the journal holds {dropped}");
        }

        async Task<long> CountAsync(string statement)
            => await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                statement, new { applicationId = row.Id }, lease.Transaction, cancellationToken: ct));
    }

    // ── Restore ──────────────────────────────────────────────────────────────

    // Parents before children: works, then releases, then ownerships, so a
    // re-inserted release has its work and a re-inserted play record has its
    // ownership. Everything else follows in the inventory's order, which cannot
    // violate a foreign key because the three identities are already back.
    private static async Task<int> RestoreDeletedAsync(
        DbLease lease, long applicationId, MergeUndoIdMap map, CancellationToken ct)
    {
        var restored = 0;

        foreach (var table in MergeUndoJournal.IdentityOrder)
        {
            restored += await RunAsync(MergeUndoJournal.Reinsert(table, map));
        }

        foreach (var table in MergeUndoJournal.All)
        {
            // merge_candidates is re-inserted later, after its own repoints have
            // been reversed; see ReverseAsync.
            if (MergeUndoJournal.IdentityOrder.Contains(table)
                || table == MergeUndoJournal.MergeCandidates)
            {
                continue;
            }

            restored += await RunAsync(MergeUndoJournal.Reinsert(table, map));
        }

        return restored;

        async Task<int> RunAsync(string statement)
            => await lease.Connection.ExecuteAsync(new CommandDefinition(
                statement, new { applicationId }, lease.Transaction, cancellationToken: ct));
    }

    private static async Task<int> ReverseRepointsAsync(
        DbLease lease, long applicationId, MergeUndoIdMap map, CancellationToken ct)
    {
        var reversed = 0;

        foreach (var table in MergeUndoJournal.All.Where(t => t.Repointed.Length > 0))
        {
            reversed += await lease.Connection.ExecuteAsync(new CommandDefinition(
                table == MergeUndoJournal.MergeCandidates
                    ? MergeUndoJournal.RepointBackMergeCandidates(map)
                    : MergeUndoJournal.RepointBack(table, map),
                new { applicationId }, lease.Transaction, cancellationToken: ct));
        }

        return reversed;
    }

    private static async Task<int> RestoreInPlaceAsync(
        DbLease lease, long applicationId, CancellationToken ct)
    {
        var restored = 0;

        foreach (var table in MergeUndoJournal.All.Where(t => t.InPlace.Length > 0))
        {
            restored += await lease.Connection.ExecuteAsync(new CommandDefinition(
                MergeUndoJournal.RestoreInPlace(table),
                new { applicationId }, lease.Transaction, cancellationToken: ct));
        }

        return restored;
    }

    // The pair is located by its two release ids rather than by candidate_id,
    // because a restored row may have taken a fresh surrogate id and because
    // merge_applications.candidate_id has never had a foreign key. Status is
    // set to 'undone', not 'confirmed': confirmed would let the next
    // ApplyAllConfirmedAsync pass re-merge the pair, which is a loop.
    private static async Task MarkCandidateUndoneAsync(
        DbLease lease, LogRow row, MergeUndoIdMap map, CancellationToken ct)
    {
        var left = map.Resolve(MergeUndoIdentity.Release, row.LeftReleaseId);
        var right = map.Resolve(MergeUndoIdentity.Release, row.RightReleaseId);

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE merge_candidates
            SET status = @undone
            WHERE left_release_id = MIN(@left, @right)
              AND right_release_id = MAX(@left, @right);
            """,
            new { left, right, undone = MergeCandidateStatuses.Undone },
            lease.Transaction, cancellationToken: ct));
    }

    // ── The log ──────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<LogRow>> LoadLogAsync(DbLease lease, CancellationToken ct)
    {
        var rows = await lease.Connection.QueryAsync<LogRow>(new CommandDefinition("""
            SELECT a.id                                    AS Id,
                   a.candidate_id                          AS CandidateId,
                   a.left_release_id                       AS LeftReleaseId,
                   a.right_release_id                      AS RightReleaseId,
                   a.mode                                  AS Mode,
                   a.surviving_work_id                     AS SurvivingWorkId,
                   a.absorbed_work_id                      AS AbsorbedWorkId,
                   a.surviving_release_id                  AS SurvivingReleaseId,
                   a.absorbed_release_id                   AS AbsorbedReleaseId,
                   a.applied_at                            AS AppliedAt,
                   a.undone_at                             AS UndoneAt,
                   a.undo_journal_version                  AS UndoJournalVersion,
                   a.summary_json                          AS SummaryJson,
                   EXISTS (SELECT 1 FROM works w
                            WHERE w.id = a.surviving_work_id)       AS SurvivingWorkExists,
                   (a.surviving_release_id IS NULL
                    OR EXISTS (SELECT 1 FROM releases r
                                WHERE r.id = a.surviving_release_id)) AS SurvivingReleaseExists
            FROM merge_applications a
            ORDER BY a.id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    private static MergeUndoIdentity IdentityOf(MergeUndoTable table) => table.Name switch
    {
        "works" => MergeUndoIdentity.Work,
        "releases" => MergeUndoIdentity.Release,
        "ownerships" => MergeUndoIdentity.Ownership,
        _ => MergeUndoIdentity.None,
    };

    private sealed record LogRow
    {
        public long Id { get; init; }
        public long CandidateId { get; init; }
        public long LeftReleaseId { get; init; }
        public long RightReleaseId { get; init; }
        public string Mode { get; init; } = MergeModes.WorkOnly;
        public long SurvivingWorkId { get; init; }
        public long? AbsorbedWorkId { get; init; }
        public long? SurvivingReleaseId { get; init; }
        public long? AbsorbedReleaseId { get; init; }
        public DateTime AppliedAt { get; init; }
        public DateTime? UndoneAt { get; init; }
        public int? UndoJournalVersion { get; init; }
        public string? SummaryJson { get; init; }
        public bool SurvivingWorkExists { get; init; }
        public bool SurvivingReleaseExists { get; init; }

        public HashSet<long> Works()
            => AbsorbedWorkId is { } absorbed ? [SurvivingWorkId, absorbed] : [SurvivingWorkId];

        public HashSet<long> Releases()
        {
            var ids = new HashSet<long>();
            if (SurvivingReleaseId is { } surviving)
            {
                ids.Add(surviving);
            }

            if (AbsorbedReleaseId is { } absorbed)
            {
                ids.Add(absorbed);
            }

            return ids;
        }

        public MergeApplicationRecord ToRecord() => new()
        {
            Id = Id,
            CandidateId = CandidateId,
            LeftReleaseId = LeftReleaseId,
            RightReleaseId = RightReleaseId,
            Mode = MergeModes.FromStorage(Mode),
            SurvivingWorkId = SurvivingWorkId,
            AbsorbedWorkId = AbsorbedWorkId,
            SurvivingReleaseId = SurvivingReleaseId,
            AbsorbedReleaseId = AbsorbedReleaseId,
            AppliedAt = AppliedAt,
            UndoneAt = UndoneAt,
            UndoJournalVersion = UndoJournalVersion,
            SummaryJson = SummaryJson,
        };
    }
}
