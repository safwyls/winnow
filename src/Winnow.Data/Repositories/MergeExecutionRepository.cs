using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.Core.Merging;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// The only code that collapses identity. Closes review findings F09 (P1) and
/// F20 (P2): the soft-match queue has always proposed cross-store duplicate pairs
/// and stored the user's confirmations, and nothing applied them until now.
///
/// <para><b>The cascade hazard.</b> Every foreign key to works, releases and
/// ownerships is ON DELETE CASCADE. Deleting a row and letting cascades run would
/// silently destroy play records, sessions, achievements and the user's own merge
/// decisions. Rebuilding a dozen tables to swap CASCADE for RESTRICT was rejected
/// as a large append-only cost for a guarantee obtainable in-transaction for free.
/// Instead, every dependent is repointed by an explicit statement, and before each
/// DELETE a tripwire counts what still references the row. Non-zero throws, the
/// transaction rolls back, and the database is untouched. A table that gains a
/// foreign key and is not added to the tripwire statement fails loudly instead of
/// losing rows.</para>
///
/// <para><b>Achievements never move between releases.</b> The release survivor
/// rule prefers the side that has them, and a collapse is refused outright when
/// both sides do, because <c>achievements</c> has no provider column and two
/// stores' sets under one <c>release_id</c> would make section 6.2's never-blend
/// rule unenforceable at query time. There is a second, independent reason:
/// <c>achievement_unlocks</c> references <c>achievements(release_id,
/// provider_key)</c> with no ON UPDATE clause, so repointing the parent's
/// <c>release_id</c> would fail the foreign key anyway.</para>
///
/// <para><see cref="MergeRepointCounts"/> is the dependent-table inventory: one
/// field per table with a foreign key to works, releases or ownerships.</para>
/// </summary>
public sealed class MergeExecutionRepository : IMergeExecutionRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public MergeExecutionRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<MergePlan> PlanAsync(MergeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var lease = _factory.Lease();
        return await BuildPlanAsync(lease, request, ct);
    }

    // The prospective read path. Calls BuildPlanAsync with admitPending: true
    // and takes no transaction, so it reads a pending or confirmed pair without
    // writing anything. The review card calls this to state what an answer
    // would do before the answer is given.
    public async Task<MergePlan> PreviewAsync(MergeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var lease = _factory.Lease();
        return await BuildPlanAsync(lease, request, ct, admitPending: true);
    }

    public async Task<MergeOutcome> ApplyAsync(MergeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // One transaction for the whole merge. Nothing here is safe to leave
        // half-done, so there is no batching and no partial commit.
        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        var plan = await BuildPlanAsync(lease, request, ct);
        if (plan.Mode == MergeMode.NothingToDo)
        {
            return MergeOutcome.NotApplied(plan);
        }

        var outcome = await ApplyPlanAsync(lease, plan, ct);
        scope.Commit();
        return outcome;
    }

    public async Task<IReadOnlyList<long>> GetConfirmedUnappliedCandidateIdsAsync(
        CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // A confirmed pair whose two releases already share a work has nothing
        // left for a work-only merge to do; it is filtered here so the caller
        // does not have to plan every historical decision to find the live ones.
        var rows = await lease.Connection.QueryAsync<long>(new CommandDefinition("""
            SELECT c.id
            FROM merge_candidates c
            JOIN releases l ON l.id = c.left_release_id
            JOIN releases r ON r.id = c.right_release_id
            WHERE c.status = 'confirmed'
              AND l.work_id <> r.work_id
            ORDER BY c.id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    // ── Planning ─────────────────────────────────────────────────────────────

    // The write path's pair statement. The status = 'confirmed' predicate
    // lives in the SQL, not in a caller's if, so no ordering of C# can make
    // ApplyAsync merge an unanswered or rejected pair (§5.3).
    private const string ConfirmedPairSql = """
        SELECT c.id                AS CandidateId,
               c.left_release_id   AS LeftId,
               c.right_release_id  AS RightId,
               l.work_id           AS LeftWorkId,
               r.work_id           AS RightWorkId,
               l.platform          AS LeftPlatform,
               r.platform          AS RightPlatform,
               l.edition_note      AS LeftEditionNote,
               r.edition_note      AS RightEditionNote,
               l.igdb_version_id   AS LeftIgdbVersionId,
               r.igdb_version_id   AS RightIgdbVersionId
        FROM merge_candidates c
        JOIN releases l ON l.id = c.left_release_id
        JOIN releases r ON r.id = c.right_release_id
        WHERE c.id = @CandidateId
          AND c.status = 'confirmed';
        """;

    // The read-only pair statement. Admits pending as well as confirmed, so
    // the review card can state what an answer would do before it is given.
    // Rejected and undone are terminal and stay out.
    private const string ProspectivePairSql = """
        SELECT c.id                AS CandidateId,
               c.left_release_id   AS LeftId,
               c.right_release_id  AS RightId,
               l.work_id           AS LeftWorkId,
               r.work_id           AS RightWorkId,
               l.platform          AS LeftPlatform,
               r.platform          AS RightPlatform,
               l.edition_note      AS LeftEditionNote,
               r.edition_note      AS RightEditionNote,
               l.igdb_version_id   AS LeftIgdbVersionId,
               r.igdb_version_id   AS RightIgdbVersionId
        FROM merge_candidates c
        JOIN releases l ON l.id = c.left_release_id
        JOIN releases r ON r.id = c.right_release_id
        WHERE c.id = @CandidateId
          AND c.status IN ('pending', 'confirmed');
        """;

    private static async Task<MergePlan> BuildPlanAsync(
        DbLease lease, MergeRequest request, CancellationToken ct, bool admitPending = false)
    {
        // Two literal SQL constants, never one assembled by string
        // concatenation. A concatenated predicate could be reached by a
        // caller passing an unexpected value; the point of the whole
        // arrangement is that it cannot. admitPending defaults to false,
        // and both write-path call sites take the default.
        var pair = await lease.Connection.QueryFirstOrDefaultAsync<PairRow>(new CommandDefinition(
            admitPending ? ProspectivePairSql : ConfirmedPairSql,
            new { request.CandidateId }, lease.Transaction, cancellationToken: ct));

        if (pair is null)
        {
            var status = await lease.Connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
                "SELECT status FROM merge_candidates WHERE id = @CandidateId;",
                new { request.CandidateId }, lease.Transaction, cancellationToken: ct));

            return MergePlan.Nothing(
                request.CandidateId,
                status is null ? MergeBlocker.CandidateNotFound : MergeBlocker.CandidateNotConfirmed);
        }

        var works = (await lease.Connection.QueryAsync<WorkRow>(new CommandDefinition("""
            SELECT w.id                   AS Id,
                   w.igdb_id              AS IgdbId,
                   w.name_is_provisional  AS NameIsProvisional,
                   (SELECT COUNT(*) FROM releases r WHERE r.work_id = w.id) AS ReleaseCount
            FROM works w
            WHERE w.id IN (@LeftWorkId, @RightWorkId);
            """, new { pair.LeftWorkId, pair.RightWorkId }, lease.Transaction, cancellationToken: ct)))
            .ToDictionary(static w => w.Id);

        var evidence = (await lease.Connection.QueryAsync<ReleaseEvidenceRow>(new CommandDefinition("""
            SELECT r.id AS Id,
                   (SELECT COUNT(*) FROM achievements  a WHERE a.release_id = r.id) AS AchievementCount,
                   (SELECT COUNT(*) FROM external_ids  e WHERE e.release_id = r.id) AS ExternalIdCount
            FROM releases r
            WHERE r.id IN (@LeftId, @RightId);
            """, new { pair.LeftId, pair.RightId }, lease.Transaction, cancellationToken: ct)))
            .ToDictionary(static r => r.Id);

        var (survivingWorkId, absorbedWorkId) = ChooseWork(works[pair.LeftWorkId], works[pair.RightWorkId]);

        var left = new ReleaseRow(pair.LeftId, pair.LeftIgdbVersionId, evidence[pair.LeftId]);
        var right = new ReleaseRow(pair.RightId, pair.RightIgdbVersionId, evidence[pair.RightId]);
        var (survivingReleaseId, absorbedReleaseId) = ChooseRelease(left, right);

        var blocker = CollapseBlocker(pair, left, right, request.AllowReleaseCollapse);
        if (blocker == MergeBlocker.None)
        {
            blocker = await UpdateEventConflictAsync(
                lease, survivingReleaseId, absorbedReleaseId, ct);
        }

        var collapses = blocker == MergeBlocker.None;

        // Idempotency, decided from state rather than from the application log:
        // a database merged by an older build, or restored from a backup, must
        // still read as merged if its rows say so.
        if (!collapses && absorbedWorkId is null)
        {
            return MergePlan.Nothing(
                pair.CandidateId,
                blocker == MergeBlocker.None ? MergeBlocker.AlreadyApplied : blocker);
        }

        return new MergePlan
        {
            CandidateId = pair.CandidateId,
            Mode = collapses ? MergeMode.ReleaseCollapse : MergeMode.WorkOnly,
            Blocker = blocker,
            LeftReleaseId = pair.LeftId,
            RightReleaseId = pair.RightId,
            SurvivingWorkId = survivingWorkId,
            AbsorbedWorkId = absorbedWorkId,
            SurvivingReleaseId = collapses ? survivingReleaseId : null,
            AbsorbedReleaseId = collapses ? absorbedReleaseId : null,
        };
    }

    private static MergeBlocker CollapseBlocker(
        PairRow pair, ReleaseRow left, ReleaseRow right, bool callerAllowsCollapse)
    {
        // The caller's flag is a ceiling. It can withhold a collapse the data
        // would permit; it can never authorise one the data forbids, because
        // every other arm of this method is re-derived from the stored rows.
        if (!callerAllowsCollapse)
        {
            return MergeBlocker.DistinctEditions;
        }

        if (!SameOrBothBlank(pair.LeftPlatform, pair.RightPlatform)
            || !SameOrBothBlank(pair.LeftEditionNote, pair.RightEditionNote)
            || !SameOrBothNull(pair.LeftIgdbVersionId, pair.RightIgdbVersionId))
        {
            return MergeBlocker.DistinctEditions;
        }

        if (left.AchievementCount > 0 && right.AchievementCount > 0)
        {
            return MergeBlocker.AchievementsOnBothSides;
        }

        return MergeBlocker.None;
    }

    private static async Task<MergeBlocker> UpdateEventConflictAsync(
        DbLease lease, long survivingReleaseId, long absorbedReleaseId, CancellationToken ct)
    {
        // ux_update_events_identity is (release_id, kind, occurred_at), but the
        // row carries build_id, title, url and raw_json besides. Two sides that
        // agree on the key and disagree on the rest are two facts, and a merge
        // that drops one is worse than no merge at all.
        var conflicts = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*)
            FROM update_events a
            JOIN update_events s
              ON s.kind = a.kind
             AND s.occurred_at = a.occurred_at
            WHERE a.release_id = @absorbedReleaseId
              AND s.release_id = @survivingReleaseId
              AND (COALESCE(a.build_id, '') <> COALESCE(s.build_id, '')
                OR COALESCE(a.title,    '') <> COALESCE(s.title,    '')
                OR COALESCE(a.url,      '') <> COALESCE(s.url,      '')
                OR COALESCE(a.raw_json, '') <> COALESCE(s.raw_json, ''));
            """, new { survivingReleaseId, absorbedReleaseId }, lease.Transaction, cancellationToken: ct));

        return conflicts > 0 ? MergeBlocker.ConflictingUpdateEvents : MergeBlocker.None;
    }

    // ── Surviving identity ───────────────────────────────────────────────────

    /// <summary>
    /// Picks the surviving work. First test that discriminates wins:
    /// (1) holds an igdb_id, because <c>works.igdb_id</c> is UNIQUE and therefore
    /// the one fact that cannot be copied onto the other row, so preferring its
    /// holder is the only way to keep it;
    /// (2) name is not provisional;
    /// (3) more releases already hang off it;
    /// (4) lowest id (oldest row, stable across re-runs).
    /// Returns <c>null</c> for the absorbed side when both releases already share
    /// a work.
    /// </summary>
    private static (long Surviving, long? Absorbed) ChooseWork(WorkRow a, WorkRow b)
    {
        if (a.Id == b.Id)
        {
            return (a.Id, null);
        }

        var aWins =
            (a.IgdbId is not null) != (b.IgdbId is not null) ? a.IgdbId is not null
            : a.NameIsProvisional != b.NameIsProvisional ? !a.NameIsProvisional
            : a.ReleaseCount != b.ReleaseCount ? a.ReleaseCount > b.ReleaseCount
            : a.Id < b.Id;

        return aWins ? (a.Id, b.Id) : (b.Id, a.Id);
    }

    /// <summary>
    /// Picks the surviving release. First test that discriminates wins:
    /// (1) holds achievement rows, because a collapse is refused when both sides
    /// do, so at most one can, and preferring it means achievements never move
    /// between releases at all, the strongest reading of section 6.2;
    /// (2) holds an igdb_version_id;
    /// (3) carries more external ids;
    /// (4) lowest id.
    /// </summary>
    private static (long Surviving, long Absorbed) ChooseRelease(ReleaseRow a, ReleaseRow b)
    {
        var aWins =
            (a.AchievementCount > 0) != (b.AchievementCount > 0) ? a.AchievementCount > 0
            : (a.IgdbVersionId is not null) != (b.IgdbVersionId is not null) ? a.IgdbVersionId is not null
            : a.ExternalIdCount != b.ExternalIdCount ? a.ExternalIdCount > b.ExternalIdCount
            : a.Id < b.Id;

        return aWins ? (a.Id, b.Id) : (b.Id, a.Id);
    }

    // ── Application ──────────────────────────────────────────────────────────

    // The merge_applications row is written first, with a placeholder summary,
    // because merge_undo_rows has a hard foreign key to it and every capture
    // below needs an application id to hang on. summary_json is rewritten with
    // the real counts at the end. The whole merge is one transaction, so nothing
    // is ever visible half-written.
    private async Task<MergeOutcome> ApplyPlanAsync(DbLease lease, MergePlan plan, CancellationToken ct)
    {
        var applicationId = await RecordAsync(lease, plan, ct);
        var journal = new MergeUndoJournalWriter(lease, applicationId);

        var counts = new MergeRepointCounts();

        if (plan.AbsorbedWorkId is { } absorbedWorkId)
        {
            counts = await UnifyWorksAsync(
                lease, journal, plan.SurvivingWorkId!.Value, absorbedWorkId, counts, ct);
        }

        if (plan.Mode == MergeMode.ReleaseCollapse)
        {
            counts = await CollapseReleasesAsync(
                lease, journal, plan.SurvivingReleaseId!.Value, plan.AbsorbedReleaseId!.Value, counts, ct);
        }

        await SummariseAsync(lease, applicationId, counts, ct);

        return new MergeOutcome
        {
            Plan = plan,
            Applied = true,
            ApplicationId = applicationId,
            Repointed = counts,
        };
    }

    private static async Task<MergeRepointCounts> UnifyWorksAsync(
        DbLease lease,
        MergeUndoJournalWriter journal,
        long survivingWorkId,
        long absorbedWorkId,
        MergeRepointCounts counts,
        CancellationToken ct)
    {
        var ids = new { survivingWorkId, absorbedWorkId };

        // Capture the surviving row's prior values for the eight COALESCE-filled
        // columns and the name/name_is_provisional promotion. One of the two
        // operations the 0016 audit recorded nothing about at all.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureUpdate(MergeUndoJournal.Works, "a.\"id\" = @survivingWorkId"),
            ids, ct);

        // Fill-only, never overwrite: the same semantics enrichment settled on
        // (F03). The absorbed work's own igdb_id, if it holds a different one,
        // dies with the row - works.igdb_id is UNIQUE, and the user's "same
        // game" answer has already contradicted IGDB's claim that these are two
        // games. It is a refetchable enrichment pointer, not user data.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works
            SET igdb_id            = COALESCE(igdb_id,            (SELECT a.igdb_id            FROM works a WHERE a.id = @absorbedWorkId)),
                sort_name          = COALESCE(sort_name,          (SELECT a.sort_name          FROM works a WHERE a.id = @absorbedWorkId)),
                first_release_year = COALESCE(first_release_year, (SELECT a.first_release_year FROM works a WHERE a.id = @absorbedWorkId)),
                summary            = COALESCE(summary,            (SELECT a.summary            FROM works a WHERE a.id = @absorbedWorkId)),
                cover_url          = COALESCE(cover_url,          (SELECT a.cover_url          FROM works a WHERE a.id = @absorbedWorkId)),
                publisher          = COALESCE(publisher,          (SELECT a.publisher          FROM works a WHERE a.id = @absorbedWorkId)),
                steam_app_type     = COALESCE(steam_app_type,     (SELECT a.steam_app_type     FROM works a WHERE a.id = @absorbedWorkId)),
                epic_categories    = COALESCE(epic_categories,    (SELECT a.epic_categories    FROM works a WHERE a.id = @absorbedWorkId)),
                name = CASE
                    WHEN name_is_provisional = 1
                     AND (SELECT a.name_is_provisional FROM works a WHERE a.id = @absorbedWorkId) = 0
                    THEN (SELECT a.name FROM works a WHERE a.id = @absorbedWorkId)
                    ELSE name END,
                name_is_provisional = CASE
                    WHEN name_is_provisional = 1
                     AND (SELECT a.name_is_provisional FROM works a WHERE a.id = @absorbedWorkId) = 0
                    THEN 0
                    ELSE name_is_provisional END
            WHERE id = @survivingWorkId;
            """, ids, lease.Transaction, cancellationToken: ct));

        // Every release of the absorbed work moves, not just the paired one:
        // work membership is already an equivalence assertion, so merging two
        // works is the union of two equivalence classes.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.Releases, "@survivingWorkId", "@absorbedWorkId"),
            ids, ct);

        var releases = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE releases SET work_id = @survivingWorkId WHERE work_id = @absorbedWorkId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.WorkFacets, "@survivingWorkId", "@absorbedWorkId"),
            ids, ct);

        var workFacets = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE OR IGNORE work_facets SET work_id = @survivingWorkId WHERE work_id = @absorbedWorkId;
            """, ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.WorkFacets, "a.\"work_id\" = @absorbedWorkId"),
            ids, ct);

        var facetDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM work_facets WHERE work_id = @absorbedWorkId;",
            ids, lease.Transaction, cancellationToken: ct));

        await AssertDrainedAsync(lease, "works", absorbedWorkId, WorkDependents, ct);

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(MergeUndoJournal.Works, "a.\"id\" = @absorbedWorkId"),
            ids, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM works WHERE id = @absorbedWorkId;",
            ids, lease.Transaction, cancellationToken: ct));

        return counts with
        {
            Releases = counts.Releases + releases,
            WorkFacets = counts.WorkFacets + workFacets,
            DuplicateRowsDropped = counts.DuplicateRowsDropped + facetDuplicates,
        };
    }

    private static async Task<MergeRepointCounts> CollapseReleasesAsync(
        DbLease lease,
        MergeUndoJournalWriter journal,
        long survivingReleaseId,
        long absorbedReleaseId,
        MergeRepointCounts counts,
        CancellationToken ct)
    {
        var ids = new { survivingReleaseId, absorbedReleaseId };

        // external_ids is keyed (provider, provider_id); release_id is not in
        // the key, so repointing can never collide. Both sides' store anchors
        // survive on the one release - that is AC #1.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.ExternalIds, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var externalIds = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE external_ids SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        counts = counts with { ExternalIds = counts.ExternalIds + externalIds };
        counts = await FoldOwnershipsAsync(
            lease, journal, survivingReleaseId, absorbedReleaseId, counts, ct);

        // Residue here is equivalent by construction: UpdateEventConflictAsync
        // already refused the collapse if any colliding pair disagreed on
        // build_id, title, url or raw_json.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.UpdateEvents, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var updateEvents = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE update_events SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.UpdateEvents, "a.\"release_id\" = @absorbedReleaseId"),
            ids, ct);

        var updateEventDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM update_events WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.UpdateAcknowledgements, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var acknowledgements = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE update_acknowledgements SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.FeedVerdicts, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var verdicts = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE feed_verdicts SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        // The remaining four are set-membership rows keyed on release_id. A
        // collision means the surviving release is already in that list, carries
        // that facet, or was already logged as surfaced that day, so the
        // absorbed row states nothing the survivor does not.
        //
        // Three of these four drop rows that carry payload the survivor does not
        // have: list_items.position, release_facets.rank,
        // feed_surfacings.shelf_id. The dropped row is redundant as a membership
        // statement and not redundant as a row, which is why the journal captures
        // every column of it rather than a count.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.ListItems, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var listItems = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE list_items SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.ListItems, "a.\"release_id\" = @absorbedReleaseId"),
            ids, ct);

        var listItemDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM list_items WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.ReleaseFacets, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var releaseFacets = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE release_facets SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.ReleaseFacets, "a.\"release_id\" = @absorbedReleaseId"),
            ids, ct);

        var releaseFacetDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM release_facets WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.FeedSurfacings, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var surfacings = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE feed_surfacings SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.FeedSurfacings, "a.\"release_id\" = @absorbedReleaseId"),
            ids, ct);

        var surfacingDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM feed_surfacings WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        // Achievements must already be zero on the absorbed side: the survivor
        // rule prefers the release that has them, and a collapse is refused when
        // both do. Nothing moves, so nothing can be blended. Asserted rather than
        // assumed, because the alternative is a silent cross-platform blend, and
        // because achievement_unlocks references achievements(release_id,
        // provider_key) with no ON UPDATE, so repointing the parent would fail
        // the foreign key anyway.
        var strandedAchievements = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM achievements WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));
        if (strandedAchievements > 0)
        {
            throw new InvalidOperationException(
                $"Release {absorbedReleaseId} still holds {strandedAchievements} achievement row(s); "
                + "the surviving-release rule should have made it the survivor. Merge aborted.");
        }

        counts = await RepointMergeCandidatesAsync(
            lease, journal, survivingReleaseId, absorbedReleaseId, counts, ct);

        await AssertDrainedAsync(lease, "releases", absorbedReleaseId, ReleaseDependents, ct);

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(MergeUndoJournal.Releases, "a.\"id\" = @absorbedReleaseId"),
            ids, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM releases WHERE id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        return counts with
        {
            UpdateEvents = counts.UpdateEvents + updateEvents,
            UpdateAcknowledgements = counts.UpdateAcknowledgements + acknowledgements,
            FeedVerdicts = counts.FeedVerdicts + verdicts,
            ListItems = counts.ListItems + listItems,
            ReleaseFacets = counts.ReleaseFacets + releaseFacets,
            FeedSurfacings = counts.FeedSurfacings + surfacings,
            DuplicateRowsDropped = counts.DuplicateRowsDropped
                + updateEventDuplicates + listItemDuplicates
                + releaseFacetDuplicates + surfacingDuplicates,
        };
    }

    private static async Task<MergeRepointCounts> FoldOwnershipsAsync(
        DbLease lease,
        MergeUndoJournalWriter journal,
        long survivingReleaseId,
        long absorbedReleaseId,
        MergeRepointCounts counts,
        CancellationToken ct)
    {
        var ids = new { survivingReleaseId, absorbedReleaseId };

        // ux_ownerships_release_store permits one ownership per (release, store),
        // so a side that owns the game on a store the survivor already owns it on
        // cannot simply be repointed. Its play history is folded onto the
        // survivor's ownership first; nothing is cascade-deleted.
        var collisions = (await lease.Connection.QueryAsync<OwnershipFold>(new CommandDefinition("""
            SELECT a.id AS AbsorbedId,
                   s.id AS SurvivingId
            FROM ownerships a
            JOIN ownerships s
              ON s.release_id = @survivingReleaseId
             AND s.store = a.store
            WHERE a.release_id = @absorbedReleaseId
            ORDER BY a.id;
            """, ids, lease.Transaction, cancellationToken: ct))).AsList();

        foreach (var fold in collisions)
        {
            counts = await FoldOwnershipAsync(lease, journal, fold, counts, ct);
        }

        // Captured after the folds, so it names exactly the ownerships the
        // statement below moves: the folded ones are already gone.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.Ownerships, "@survivingReleaseId", "@absorbedReleaseId"),
            ids, ct);

        var ownerships = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ownerships SET release_id = @survivingReleaseId WHERE release_id = @absorbedReleaseId;",
            ids, lease.Transaction, cancellationToken: ct));

        return counts with
        {
            Ownerships = counts.Ownerships + ownerships,
            OwnershipsFolded = counts.OwnershipsFolded + collisions.Count,
        };
    }

    private static async Task<MergeRepointCounts> FoldOwnershipAsync(
        DbLease lease,
        MergeUndoJournalWriter journal,
        OwnershipFold fold,
        MergeRepointCounts counts,
        CancellationToken ct)
    {
        // ux_play_records_observation and ux_playtime_snapshots_observation cover
        // every column of their tables except the id, so a row that survives the
        // repoint and then collides is a byte-identical observation already
        // present on the survivor. Dropping it is the same deduplication
        // migration 0013 established, not a lost fact.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(MergeUndoJournal.PlayRecords, "@SurvivingId", "@AbsorbedId"),
            fold, ct);

        var playRecords = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE play_records SET ownership_id = @SurvivingId WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.PlayRecords, "a.\"ownership_id\" = @AbsorbedId"),
            fold, ct);

        var playRecordDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM play_records WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.PlaytimeSnapshots, "@SurvivingId", "@AbsorbedId"),
            fold, ct);

        var snapshots = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE playtime_snapshots SET ownership_id = @SurvivingId WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.PlaytimeSnapshots, "a.\"ownership_id\" = @AbsorbedId"),
            fold, ct);

        var snapshotDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM playtime_snapshots WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        // Sessions have no uniqueness constraint, so every one moves, and
        // session_notes ride along on session_id without being touched.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(MergeUndoJournal.Sessions, "@SurvivingId", "@AbsorbedId"),
            fold, ct);

        var sessions = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE sessions SET ownership_id = @SurvivingId WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        // Two rows for one account: first_seen/last_seen is genuinely a range and
        // is widened, but playtime and last_played are one observed tuple and are
        // taken whole from whichever row was seen more recently. Recombining them
        // field by field would manufacture an observation no source reported -
        // F10's lesson.
        //
        // The second of the two operations the 0016 audit recorded nothing about.
        // The capture's WHERE is the UPDATE's own: the surviving ownership's rows
        // that have a counterpart on the absorbed one.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureUpdate(MergeUndoJournal.OwnershipAccounts, """
                a."ownership_id" = @SurvivingId
                  AND EXISTS (
                      SELECT 1 FROM ownership_accounts absorbed
                      WHERE absorbed."ownership_id" = @AbsorbedId
                        AND absorbed."account_ref" = a."account_ref")
                """),
            fold, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ownership_accounts AS s
            SET playtime_minutes = CASE WHEN Absorbed.last_seen_at > s.last_seen_at
                                        THEN Absorbed.playtime_minutes ELSE s.playtime_minutes END,
                last_played_at   = CASE WHEN Absorbed.last_seen_at > s.last_seen_at
                                        THEN Absorbed.last_played_at   ELSE s.last_played_at   END,
                source           = CASE WHEN Absorbed.last_seen_at > s.last_seen_at
                                        THEN Absorbed.source           ELSE s.source           END,
                last_seen_at     = MAX(s.last_seen_at,  Absorbed.last_seen_at),
                first_seen_at    = MIN(s.first_seen_at, Absorbed.first_seen_at)
            FROM (SELECT * FROM ownership_accounts WHERE ownership_id = @AbsorbedId) AS Absorbed
            WHERE s.ownership_id = @SurvivingId
              AND s.account_ref = Absorbed.account_ref;
            """, fold, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureRepoint(
                MergeUndoJournal.OwnershipAccounts, "@SurvivingId", "@AbsorbedId"),
            fold, ct);

        var accounts = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE OR IGNORE ownership_accounts SET ownership_id = @SurvivingId WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(
                MergeUndoJournal.OwnershipAccounts, "a.\"ownership_id\" = @AbsorbedId"),
            fold, ct);

        var accountDuplicates = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ownership_accounts WHERE ownership_id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        await AssertDrainedAsync(lease, "ownerships", fold.AbsorbedId, OwnershipDependents, ct);

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(MergeUndoJournal.Ownerships, "a.\"id\" = @AbsorbedId"),
            fold, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ownerships WHERE id = @AbsorbedId;",
            fold, lease.Transaction, cancellationToken: ct));

        return counts with
        {
            PlayRecords = counts.PlayRecords + playRecords,
            PlaytimeSnapshots = counts.PlaytimeSnapshots + snapshots,
            Sessions = counts.Sessions + sessions,
            OwnershipAccounts = counts.OwnershipAccounts + accounts,
            DuplicateRowsDropped = counts.DuplicateRowsDropped
                + playRecordDuplicates + snapshotDuplicates + accountDuplicates,
        };
    }

    private static async Task<MergeRepointCounts> RepointMergeCandidatesAsync(
        DbLease lease,
        MergeUndoJournalWriter journal,
        long survivingReleaseId,
        long absorbedReleaseId,
        MergeRepointCounts counts,
        CancellationToken ct)
    {
        var ids = new { survivingReleaseId, absorbedReleaseId };

        // The pair being merged, in either orientation. Its decision is not lost:
        // merge_applications records it, and no sweep can propose it again once
        // one of its releases no longer exists.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(MergeUndoJournal.MergeCandidates, """
                (a."left_release_id" = @absorbedReleaseId AND a."right_release_id" = @survivingReleaseId)
                   OR (a."left_release_id" = @survivingReleaseId AND a."right_release_id" = @absorbedReleaseId)
                """),
            ids, ct);

        var answered = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM merge_candidates
            WHERE (left_release_id = @absorbedReleaseId AND right_release_id = @survivingReleaseId)
               OR (left_release_id = @survivingReleaseId AND right_release_id = @absorbedReleaseId);
            """, ids, lease.Transaction, cancellationToken: ct));

        // A pair the absorbed release had with some third release becomes a pair
        // the survivor has with it. Where that pair already exists and the row
        // being moved is a decision while the sitting row is only a proposal, the
        // proposal gives way: losing a rejection would let a sweep re-ask a
        // question the user has already answered.
        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(MergeUndoJournal.MergeCandidates, """
                a."status" = 'pending'
                  AND a."id" IN (
                      SELECT sitting.id
                      FROM merge_candidates sitting
                      JOIN merge_candidates moving
                        ON sitting.left_release_id = MIN(
                               CASE WHEN moving.left_release_id  = @absorbedReleaseId
                                    THEN @survivingReleaseId ELSE moving.left_release_id  END,
                               CASE WHEN moving.right_release_id = @absorbedReleaseId
                                    THEN @survivingReleaseId ELSE moving.right_release_id END)
                       AND sitting.right_release_id = MAX(
                               CASE WHEN moving.left_release_id  = @absorbedReleaseId
                                    THEN @survivingReleaseId ELSE moving.left_release_id  END,
                               CASE WHEN moving.right_release_id = @absorbedReleaseId
                                    THEN @survivingReleaseId ELSE moving.right_release_id END)
                      WHERE (moving.left_release_id = @absorbedReleaseId
                          OR moving.right_release_id = @absorbedReleaseId)
                        AND moving.status <> 'pending')
                """),
            ids, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM merge_candidates
            WHERE status = 'pending'
              AND id IN (
                  SELECT sitting.id
                  FROM merge_candidates sitting
                  JOIN merge_candidates moving
                    ON sitting.left_release_id = MIN(
                           CASE WHEN moving.left_release_id  = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE moving.left_release_id  END,
                           CASE WHEN moving.right_release_id = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE moving.right_release_id END)
                   AND sitting.right_release_id = MAX(
                           CASE WHEN moving.left_release_id  = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE moving.left_release_id  END,
                           CASE WHEN moving.right_release_id = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE moving.right_release_id END)
                  WHERE (moving.left_release_id = @absorbedReleaseId
                      OR moving.right_release_id = @absorbedReleaseId)
                    AND moving.status <> 'pending');
            """, ids, lease.Transaction, cancellationToken: ct));

        // The journal records the pre-merge pair whole rather than a substitution
        // rule, so restoring it is exact. MIN/MAX is applied on the way back
        // anyway, because an absorbed release restored at a fresh id may sort on
        // the other side of its partner and 0016's CHECK (left < right) admits
        // only one orientation.
        await journal.CaptureAsync("""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            SELECT @applicationId,
                   @seqBase + ROW_NUMBER() OVER (ORDER BY a.id),
                   'merge_candidates',
                   'repoint',
                   json_object(
                       'left_release_id',  MIN(
                           CASE WHEN a.left_release_id  = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE a.left_release_id  END,
                           CASE WHEN a.right_release_id = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE a.right_release_id END),
                       'right_release_id', MAX(
                           CASE WHEN a.left_release_id  = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE a.left_release_id  END,
                           CASE WHEN a.right_release_id = @absorbedReleaseId
                                THEN @survivingReleaseId ELSE a.right_release_id END)),
                   json_object(
                       'left_release_id',  a.left_release_id,
                       'right_release_id', a.right_release_id)
            FROM merge_candidates a
            WHERE (a.left_release_id = @absorbedReleaseId OR a.right_release_id = @absorbedReleaseId)
              AND NOT EXISTS (
                  SELECT 1
                  FROM merge_candidates s
                  WHERE s.left_release_id = MIN(
                            CASE WHEN a.left_release_id  = @absorbedReleaseId
                                 THEN @survivingReleaseId ELSE a.left_release_id  END,
                            CASE WHEN a.right_release_id = @absorbedReleaseId
                                 THEN @survivingReleaseId ELSE a.right_release_id END)
                    AND s.right_release_id = MAX(
                            CASE WHEN a.left_release_id  = @absorbedReleaseId
                                 THEN @survivingReleaseId ELSE a.left_release_id  END,
                            CASE WHEN a.right_release_id = @absorbedReleaseId
                                 THEN @survivingReleaseId ELSE a.right_release_id END));
            """, ids, ct);

        var moved = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE OR IGNORE merge_candidates
            SET left_release_id = MIN(
                    CASE WHEN left_release_id  = @absorbedReleaseId
                         THEN @survivingReleaseId ELSE left_release_id  END,
                    CASE WHEN right_release_id = @absorbedReleaseId
                         THEN @survivingReleaseId ELSE right_release_id END),
                right_release_id = MAX(
                    CASE WHEN left_release_id  = @absorbedReleaseId
                         THEN @survivingReleaseId ELSE left_release_id  END,
                    CASE WHEN right_release_id = @absorbedReleaseId
                         THEN @survivingReleaseId ELSE right_release_id END)
            WHERE left_release_id = @absorbedReleaseId
               OR right_release_id = @absorbedReleaseId;
            """, ids, lease.Transaction, cancellationToken: ct));

        await journal.CaptureAsync(
            MergeUndoJournal.CaptureDelete(MergeUndoJournal.MergeCandidates, """
                a."left_release_id" = @absorbedReleaseId
                   OR a."right_release_id" = @absorbedReleaseId
                """),
            ids, ct);

        var duplicates = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM merge_candidates
            WHERE left_release_id = @absorbedReleaseId
               OR right_release_id = @absorbedReleaseId;
            """, ids, lease.Transaction, cancellationToken: ct));

        // `answered` is the pair being merged and is deliberately not counted as
        // a dropped duplicate: it is the decision being carried out, recorded in
        // merge_applications, not a redundant row.
        _ = answered;

        return counts with
        {
            MergeCandidates = counts.MergeCandidates + moved,
            DuplicateRowsDropped = counts.DuplicateRowsDropped + duplicates,
        };
    }

    private async Task<long> RecordAsync(DbLease lease, MergePlan plan, CancellationToken ct)
    {
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO merge_applications (
                candidate_id, left_release_id, right_release_id, mode,
                surviving_work_id, absorbed_work_id,
                surviving_release_id, absorbed_release_id,
                applied_at, summary_json, undo_journal_version)
            VALUES (
                @CandidateId, @LeftReleaseId, @RightReleaseId, @Mode,
                @SurvivingWorkId, @AbsorbedWorkId,
                @SurvivingReleaseId, @AbsorbedReleaseId,
                @AppliedAt, @SummaryJson, @UndoJournalVersion)
            RETURNING id;
            """,
            new
            {
                plan.CandidateId,
                plan.LeftReleaseId,
                plan.RightReleaseId,
                Mode = MergeModes.ToStorage(plan.Mode),
                plan.SurvivingWorkId,
                plan.AbsorbedWorkId,
                plan.SurvivingReleaseId,
                plan.AbsorbedReleaseId,
                AppliedAt = _clock.GetUtcNow().UtcDateTime,
                SummaryJson = JsonSerializer.Serialize(
                    new MergeRepointCounts(), MergeJsonContext.Default.MergeRepointCounts),
                UndoJournalVersion = MergeUndoJournal.Version,
            },
            lease.Transaction, cancellationToken: ct));
    }

    private static async Task SummariseAsync(
        DbLease lease, long applicationId, MergeRepointCounts counts, CancellationToken ct)
    {
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE merge_applications SET summary_json = @SummaryJson WHERE id = @applicationId;",
            new
            {
                applicationId,
                SummaryJson = JsonSerializer.Serialize(counts, MergeJsonContext.Default.MergeRepointCounts),
            },
            lease.Transaction, cancellationToken: ct));
    }

    // ── The cascade tripwire ─────────────────────────────────────────────────

    // The cascade tripwire. Each statement counts every dependent of the row
    // about to be deleted. A non-zero count throws and rolls the whole merge
    // back. This is the guarantee that a new table with a foreign key to works,
    // releases or ownerships fails loudly if it is not added here, rather than
    // losing rows to ON DELETE CASCADE.
    private const string WorkDependents = """
        SELECT (SELECT COUNT(*) FROM releases    WHERE work_id = @id) AS Releases,
               (SELECT COUNT(*) FROM work_facets WHERE work_id = @id) AS WorkFacets;
        """;

    private const string ReleaseDependents = """
        SELECT (SELECT COUNT(*) FROM external_ids            WHERE release_id = @id) AS ExternalIds,
               (SELECT COUNT(*) FROM ownerships              WHERE release_id = @id) AS Ownerships,
               (SELECT COUNT(*) FROM achievements            WHERE release_id = @id) AS Achievements,
               (SELECT COUNT(*) FROM achievement_unlocks     WHERE release_id = @id) AS AchievementUnlocks,
               (SELECT COUNT(*) FROM update_events           WHERE release_id = @id) AS UpdateEvents,
               (SELECT COUNT(*) FROM update_acknowledgements WHERE release_id = @id) AS UpdateAcknowledgements,
               (SELECT COUNT(*) FROM list_items              WHERE release_id = @id) AS ListItems,
               (SELECT COUNT(*) FROM release_facets          WHERE release_id = @id) AS ReleaseFacets,
               (SELECT COUNT(*) FROM feed_verdicts           WHERE release_id = @id) AS FeedVerdicts,
               (SELECT COUNT(*) FROM feed_surfacings         WHERE release_id = @id) AS FeedSurfacings,
               (SELECT COUNT(*) FROM merge_candidates
                 WHERE left_release_id = @id OR right_release_id = @id)              AS MergeCandidates;
        """;

    private const string OwnershipDependents = """
        SELECT (SELECT COUNT(*) FROM play_records       WHERE ownership_id = @id) AS PlayRecords,
               (SELECT COUNT(*) FROM playtime_snapshots WHERE ownership_id = @id) AS PlaytimeSnapshots,
               (SELECT COUNT(*) FROM sessions           WHERE ownership_id = @id) AS Sessions,
               (SELECT COUNT(*) FROM ownership_accounts WHERE ownership_id = @id) AS OwnershipAccounts;
        """;

    // The three tripwire statements read as a list of table names, which is the
    // same inventory the undo journal has to cover.
    // MergeUndoTests compares the two, so a table added to one and not
    // the other fails loudly instead of being silently unrecoverable.
    internal static IReadOnlyList<string> DependentTables { get; } =
        System.Text.RegularExpressions.Regex
            .Matches(
                string.Join('\n', WorkDependents, ReleaseDependents, OwnershipDependents),
                @"FROM\s+(\w+)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static async Task AssertDrainedAsync(
        DbLease lease, string table, long id, string statement, CancellationToken ct)
    {
        var row = await lease.Connection.QueryFirstAsync(
            new CommandDefinition(statement, new { id }, lease.Transaction, cancellationToken: ct));

        var stranded = ((IDictionary<string, object>)row)
            .Select(static entry => (entry.Key, Count: Convert.ToInt64(
                entry.Value ?? 0L, System.Globalization.CultureInfo.InvariantCulture)))
            .Where(static entry => entry.Count > 0)
            .Select(static entry => $"{entry.Key}={entry.Count}")
            .ToList();

        if (stranded.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to delete {table} row {id}: {string.Join(", ", stranded)} still reference it. "
                + "Deleting it would let ON DELETE CASCADE remove rows the merge was supposed to repoint. "
                + "Merge aborted.");
        }
    }

    // ── Row shapes ───────────────────────────────────────────────────────────

    private static bool SameOrBothBlank(string? a, string? b)
    {
        var left = a?.Trim();
        var right = b?.Trim();
        return string.IsNullOrEmpty(left)
            ? string.IsNullOrEmpty(right)
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameOrBothNull(long? a, long? b) => a == b;

    private sealed record PairRow
    {
        public long CandidateId { get; init; }
        public long LeftId { get; init; }
        public long RightId { get; init; }
        public long LeftWorkId { get; init; }
        public long RightWorkId { get; init; }
        public string? LeftPlatform { get; init; }
        public string? RightPlatform { get; init; }
        public string? LeftEditionNote { get; init; }
        public string? RightEditionNote { get; init; }
        public long? LeftIgdbVersionId { get; init; }
        public long? RightIgdbVersionId { get; init; }
    }

    private sealed record WorkRow
    {
        public long Id { get; init; }
        public long? IgdbId { get; init; }
        public bool NameIsProvisional { get; init; }
        public int ReleaseCount { get; init; }
    }

    private sealed record ReleaseEvidenceRow
    {
        public long Id { get; init; }
        public int AchievementCount { get; init; }
        public int ExternalIdCount { get; init; }
    }

    private sealed record ReleaseRow(long Id, long? IgdbVersionId, ReleaseEvidenceRow Evidence)
    {
        public int AchievementCount => Evidence.AchievementCount;
        public int ExternalIdCount => Evidence.ExternalIdCount;
    }

    private sealed record OwnershipFold
    {
        public long AbsorbedId { get; init; }
        public long SurvivingId { get; init; }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false)]
[JsonSerializable(typeof(MergeRepointCounts))]
internal sealed partial class MergeJsonContext : JsonSerializerContext;
