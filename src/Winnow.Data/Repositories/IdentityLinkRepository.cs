using Dapper;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Reads and writes identity links over migration 0018's two tables. Every
/// write is one transaction and one act. Depth is fixed at one: a parent may
/// not itself be a child (refused), and a child that already has children is
/// re-parented rather than refused. Undo restores prior membership only where
/// it remains valid without changing later decisions.
///
/// <para>Retraction stamps the act's live rows with <c>retracted_at</c> and
/// <c>retracted_by_act_id</c>, then re-inserts the links it displaced as
/// fresh rows under the unlink act when the child still belongs to the selected
/// act and the prior membership is structurally valid. A retracted row is never un-retracted:
/// append-only means the table is the journal.</para>
/// </summary>
public sealed class IdentityLinkRepository : IIdentityLinkRepository
{
    internal const string LinkColumns = """
        l.id                  AS Id,
               l.act_id              AS ActId,
               l.child_work_id       AS ChildWorkId,
               l.parent_work_id      AS ParentWorkId,
               l.kind                AS Kind,
               l.source              AS Source,
               l.relation_label      AS RelationLabel,
               l.evidence_json       AS EvidenceJson,
               l.applied_at          AS AppliedAt,
               l.retracted_at        AS RetractedAt,
               l.retracted_by_act_id AS RetractedByActId
        """;

    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    /// <summary>Creates the repository. <paramref name="clock"/> defaults to system time; tests inject a fixed clock.</summary>
    public IdentityLinkRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IdentityResolution> GetResolutionAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        return IdentityResolution.FromLiveLinks(await LiveLinksAsync(lease, ct));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityLink>> GetHistoryAsync(
        long? workId = null, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var rows = await lease.Connection.QueryAsync<IdentityLink>(new CommandDefinition($"""
            SELECT {LinkColumns}
            FROM identity_links l
            WHERE @workId IS NULL
               OR l.child_work_id = @workId
               OR l.parent_work_id = @workId
            ORDER BY l.id;
            """, new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityAct>> GetActsAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var rows = await lease.Connection.QueryAsync<IdentityAct>(new CommandDefinition("""
            SELECT id           AS Id,
                   kind         AS Kind,
                   performed_at AS PerformedAt,
                   note         AS Note
            FROM identity_acts
            ORDER BY id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<long> LinkAsync(IdentityLinkRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var children = Validate(request);

        using var scope = new RepositoryWriteBatch(_factory);
        var lease = scope.Lease;

        List<long> everyWork = [request.ParentWorkId, .. children];
        await AssertWorksExistAsync(lease, everyWork, ct);

        var live = await LiveLinksAsync(lease, ct);
        var resolution = IdentityResolution.FromLiveLinks(live);
        foreach (var childWorkId in children)
        {
            var refusal = IdentityLinkRules.GetRefusal(
                request.Kind, request.ParentWorkId, childWorkId, resolution);
            if (refusal != IdentityLinkRefusal.None)
            {
                throw new IdentityLinkRefusedException(refusal,
                    refusal == IdentityLinkRefusal.ParentIsAlreadyAChild
                        ? $"Work {request.ParentWorkId} already has a parent and cannot hold children."
                        : $"Work {childWorkId} has children and cannot be grouped as an expansion or variant.");
            }
        }

        var actId = await InsertActAsync(lease, IdentityActKinds.Link, request.Note, ct);

        // Depth one, half two: a child may not be a parent. Any work hanging off
        // a work that is becoming a child is re-parented onto the new parent
        // inside this same act, so retracting the act puts every one of them
        // back where it was.
        var childSet = children.ToHashSet();
        var displaced = live.Where(link => childSet.Contains(link.ParentWorkId)).ToList();

        // Each target carries the KIND it must be written back under. The
        // children the request names take the request's kind. A displaced
        // grandchild keeps its OWN kind, and that distinction is load-bearing
        // from TASK-70.5 onward: re-parenting Civilization IV's expansions
        // because Civilization IV was itself linked to its GOG twin must move
        // them, not convert them. Writing them under the request's kind would
        // turn six expansions into six same-game links, which folds six
        // playtimes into one and moves numbers the user's decision of
        // 2026-08-31 says must never move.
        var targets =
            new List<(long ChildWorkId, string Kind, string? Label)>(children.Count + displaced.Count);
        foreach (var childWorkId in children)
        {
            targets.Add((childWorkId, request.Kind, request.RelationLabel));
        }

        foreach (var link in displaced)
        {
            if (link.ChildWorkId == request.ParentWorkId
                || targets.Exists(t => t.ChildWorkId == link.ChildWorkId))
            {
                continue;
            }

            targets.Add((link.ChildWorkId, link.Kind, link.RelationLabel));
        }

        foreach (var (childWorkId, kind, label) in targets)
        {
            await RetractLiveLinkAsync(lease, childWorkId, actId, ct);
            await InsertLinkAsync(lease, actId, request, childWorkId, kind, label, ct);
        }

        scope.Commit();
        return actId;
    }

    /// <inheritdoc />
    public async Task<bool> RetractActAsync(
        long actId, string? note = null, CancellationToken ct = default)
    {
        using var scope = new RepositoryWriteBatch(_factory);
        var lease = scope.Lease;

        var exists = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM identity_acts WHERE id = @actId;",
            new { actId }, lease.Transaction, cancellationToken: ct));

        if (exists == 0)
        {
            throw new IdentityLinkRefusedException(
                IdentityLinkRefusal.ActNotFound, $"No identity act with id {actId}.");
        }

        var children = (await lease.Connection.QueryAsync<long>(new CommandDefinition(
            "SELECT child_work_id FROM identity_links WHERE act_id = @actId AND retracted_at IS NULL;",
            new { actId }, lease.Transaction, cancellationToken: ct))).AsList();

        if (children.Count == 0)
        {
            return false;
        }

        var undoActId = await InsertActAsync(lease, IdentityActKinds.Unlink, note, ct);
        var now = _clock.GetUtcNow().UtcDateTime;

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE identity_links
            SET retracted_at = @now, retracted_by_act_id = @undoActId
            WHERE act_id = @actId AND retracted_at IS NULL;
            """, new { now, undoActId, actId }, lease.Transaction, cancellationToken: ct));

        await RestoreDisplacedLinksAsync(lease, actId, undoActId, children, now, ct);

        scope.Commit();
        return true;
    }

    public async Task<bool> RetractLinkAsync(
        long childWorkId, string? note = null, CancellationToken ct = default)
    {
        using var scope = new RepositoryWriteBatch(_factory);
        var lease = scope.Lease;

        // The act retraction narrowed to one child, not a different mechanism.
        // The rest of the act stays standing — the user is separating the one
        // title they noticed, from the place they noticed it, and did not ask
        // about its siblings. ux_identity_links_live guarantees at most one
        // live row here, so the singular read is a schema fact.
        var live = await lease.Connection.QuerySingleOrDefaultAsync<IdentityLink>(
            new CommandDefinition($"""
                SELECT {LinkColumns}
                FROM identity_links l
                WHERE l.child_work_id = @childWorkId AND l.retracted_at IS NULL;
                """, new { childWorkId }, lease.Transaction, cancellationToken: ct));

        if (live is null)
        {
            // Retracting twice is a no-op rather than an error, exactly as
            // RetractActAsync is: idempotent undo is the fix for the user's
            // complaint that undo made a pair permanently unmergeable.
            return false;
        }

        var undoActId = await InsertActAsync(lease, IdentityActKinds.Unlink, note, ct);
        var now = _clock.GetUtcNow().UtcDateTime;

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE identity_links
            SET retracted_at = @now, retracted_by_act_id = @undoActId
            WHERE id = @linkId;
            """, new { now, undoActId, linkId = live.Id }, lease.Transaction, cancellationToken: ct));

        await RestoreDisplacedLinksAsync(lease, live.ActId, undoActId, [childWorkId], now, ct);

        scope.Commit();
        return true;
    }

    private static async Task RestoreDisplacedLinksAsync(
        DbLease lease, long actId, long undoActId, List<long> children, DateTime now, CancellationToken ct)
    {
        // Only children whose current link was just removed belong to this undo.
        // A child moved by a later act keeps that later decision, even when the
        // selected act once displaced an older link for it.
        var displaced = (await lease.Connection.QueryAsync<IdentityLink>(new CommandDefinition($"""
            SELECT {LinkColumns}
            FROM identity_links l
            WHERE l.retracted_by_act_id = @actId AND l.child_work_id IN @children
            ORDER BY l.id;
            """,
            new { actId, children },
            lease.Transaction, cancellationToken: ct))).AsList();

        foreach (var prior in displaced)
        {
            // Restoration must not reparent anyone. If the old parent now has
            // a parent, or the child now holds children, leave it separate. The
            // conditional insert also protects against ambiguous old history;
            // it never overrides a standing membership to make the past fit.
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO identity_links (
                    act_id, child_work_id, parent_work_id, kind, source,
                    relation_label, evidence_json, applied_at)
                SELECT @undoActId, @childWorkId, @parentWorkId, @kind, @source,
                       @relationLabel, @evidenceJson, @now
                WHERE NOT EXISTS (
                    SELECT 1 FROM identity_links
                    WHERE retracted_at IS NULL
                      AND (child_work_id IN (@childWorkId, @parentWorkId)
                           OR parent_work_id = @childWorkId)
                );
                """,
                new
                {
                    undoActId,
                    childWorkId = prior.ChildWorkId,
                    parentWorkId = prior.ParentWorkId,
                    kind = prior.Kind,
                    source = prior.Source,
                    relationLabel = prior.RelationLabel,
                    evidenceJson = prior.EvidenceJson,
                    now,
                },
                lease.Transaction,
                cancellationToken: ct));
        }

    }

    // ── Validation ───────────────────────────────────────────────────────────

    private static List<long> Validate(IdentityLinkRequest request)
    {
        if (!IdentityLinkKinds.All.Contains(request.Kind))
        {
            throw new IdentityLinkRefusedException(
                IdentityLinkRefusal.UnknownKind, $"Unknown identity link kind '{request.Kind}'.");
        }

        if (request.Source is not (IdentityLinkSources.User or IdentityLinkSources.HardId))
        {
            throw new IdentityLinkRefusedException(
                IdentityLinkRefusal.UnknownSource,
                $"Unknown identity link source '{request.Source}'.");
        }

        ArgumentNullException.ThrowIfNull(request.ChildWorkIds);

        var children = new List<long>(request.ChildWorkIds.Count);
        foreach (var childWorkId in request.ChildWorkIds)
        {
            if (childWorkId == request.ParentWorkId)
            {
                throw new IdentityLinkRefusedException(
                    IdentityLinkRefusal.SelfLink,
                    $"Work {childWorkId} cannot be linked to itself.");
            }

            if (!children.Contains(childWorkId))
            {
                children.Add(childWorkId);
            }
        }

        if (children.Count == 0)
        {
            throw new IdentityLinkRefusedException(
                IdentityLinkRefusal.NoChildren,
                "An identity link act needs at least one child work.");
        }

        return children;
    }

    // identity_links foreign-keys works with ON DELETE CASCADE, so a
    // missing work id would be a rollback with an opaque SQLite message
    // rather than a named refusal.
    private static async Task AssertWorksExistAsync(
        DbLease lease, List<long> workIds, CancellationToken ct)
    {
        var found = await lease.Connection.QueryAsync<long>(new CommandDefinition(
            "SELECT id FROM works WHERE id IN @workIds;",
            new { workIds }, lease.Transaction, cancellationToken: ct));

        var present = new HashSet<long>(found);
        foreach (var workId in workIds)
        {
            if (!present.Contains(workId))
            {
                throw new IdentityLinkRefusedException(
                    IdentityLinkRefusal.UnknownWork, $"No work with id {workId}.");
            }
        }
    }

    // ── Statements ───────────────────────────────────────────────────────────

    private async Task<long> InsertActAsync(
        DbLease lease, string kind, string? note, CancellationToken ct)
        => await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO identity_acts (kind, performed_at, note)
            VALUES (@kind, @performedAt, @note)
            RETURNING id;
            """,
            new { kind, performedAt = _clock.GetUtcNow().UtcDateTime, note },
            lease.Transaction,
            cancellationToken: ct));

    private static async Task<List<IdentityLink>> LiveLinksAsync(DbLease lease, CancellationToken ct)
    {
        var rows = await lease.Connection.QueryAsync<IdentityLink>(new CommandDefinition($"""
            SELECT {LinkColumns}
            FROM identity_links l
            WHERE l.retracted_at IS NULL
            ORDER BY l.id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    private async Task RetractLiveLinkAsync(
        DbLease lease, long childWorkId, long actId, CancellationToken ct)
        => await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE identity_links
            SET retracted_at = @now, retracted_by_act_id = @actId
            WHERE child_work_id = @childWorkId AND retracted_at IS NULL;
            """,
            new { now = _clock.GetUtcNow().UtcDateTime, actId, childWorkId },
            lease.Transaction,
            cancellationToken: ct));

    private async Task InsertLinkAsync(
        DbLease lease,
        long actId,
        IdentityLinkRequest request,
        long childWorkId,
        string kind,
        string? relationLabel,
        CancellationToken ct)
        => await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source,
                relation_label, evidence_json, applied_at)
            VALUES (@actId, @childWorkId, @parentWorkId, @kind, @source,
                    @relationLabel, @evidenceJson, @appliedAt);
            """,
            new
            {
                actId,
                childWorkId,
                parentWorkId = request.ParentWorkId,
                kind,
                source = request.Source,
                relationLabel,
                evidenceJson = request.EvidenceJson,
                appliedAt = _clock.GetUtcNow().UtcDateTime,
            },
            lease.Transaction,
            cancellationToken: ct));
}
