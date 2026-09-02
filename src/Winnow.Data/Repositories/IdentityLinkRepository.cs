using Dapper;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Reads and writes identity links over migration 0018's two tables. Every
/// write is one transaction and one act. Depth is fixed at one: a parent may
/// not itself be a child (refused), and a child that already has children is
/// re-parented rather than refused, inside the same act so one retraction
/// puts them all back.
///
/// <para>Retraction stamps the act's live rows with <c>retracted_at</c> and
/// <c>retracted_by_act_id</c>, then re-inserts the links it displaced as
/// fresh rows under the unlink act. A retracted row is never un-retracted:
/// append-only means the table is the journal.</para>
/// </summary>
public sealed class IdentityLinkRepository : IIdentityLinkRepository
{
    private const string LinkColumns = """
        l.id                  AS Id,
               l.act_id              AS ActId,
               l.child_work_id       AS ChildWorkId,
               l.parent_work_id      AS ParentWorkId,
               l.kind                AS Kind,
               l.source              AS Source,
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

        var rows = await lease.Connection.QueryAsync<IdentityLink>(new CommandDefinition($"""
            SELECT {LinkColumns}
            FROM identity_links l
            WHERE l.retracted_at IS NULL
            ORDER BY l.id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return IdentityResolution.FromLiveLinks(rows);
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

        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        List<long> everyWork = [request.ParentWorkId, .. children];
        await AssertWorksExistAsync(lease, everyWork, ct);

        // Depth one, half one: the chosen parent may not itself be a live child.
        // Re-parenting the whole group under its grandparent would be a decision
        // nobody made, so this is refused rather than repaired.
        var parentsParent = await LiveParentOfAsync(lease, request.ParentWorkId, ct);
        if (parentsParent is not null)
        {
            throw new IdentityLinkRefusedException(
                IdentityLinkRefusal.ParentIsAlreadyAChild,
                $"Work {request.ParentWorkId} is already linked under work {parentsParent}. "
                + "A parent may not itself be a child.");
        }

        var actId = await InsertActAsync(lease, IdentityActKinds.Link, request.Note, ct);

        // Depth one, half two: a child may not be a parent. Any work hanging off
        // a work that is becoming a child is re-parented onto the new parent
        // inside this same act, so retracting the act puts every one of them
        // back where it was.
        var displaced = await LiveLinksUnderAsync(lease, children, ct);

        var targets = new List<long>(children.Count + displaced.Count);
        foreach (var childWorkId in children)
        {
            targets.Add(childWorkId);
        }

        foreach (var link in displaced)
        {
            if (link.ChildWorkId != request.ParentWorkId && !targets.Contains(link.ChildWorkId))
            {
                targets.Add(link.ChildWorkId);
            }
        }

        foreach (var childWorkId in targets)
        {
            await RetractLiveLinkAsync(lease, childWorkId, actId, ct);
            await InsertLinkAsync(lease, actId, request, childWorkId, ct);
        }

        scope.Commit();
        return actId;
    }

    /// <inheritdoc />
    public async Task<bool> RetractActAsync(
        long actId, string? note = null, CancellationToken ct = default)
    {
        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        var exists = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM identity_acts WHERE id = @actId;",
            new { actId }, lease.Transaction, cancellationToken: ct));

        if (exists == 0)
        {
            throw new IdentityLinkRefusedException(
                IdentityLinkRefusal.ActNotFound, $"No identity act with id {actId}.");
        }

        var liveCount = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM identity_links WHERE act_id = @actId AND retracted_at IS NULL;",
            new { actId }, lease.Transaction, cancellationToken: ct));

        if (liveCount == 0)
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

        // Every link this act displaced, restored under the unlink act as a
        // fresh row. Append-only: a retracted row is never un-retracted, so the
        // journal of what happened stays the table itself.
        var displaced = (await lease.Connection.QueryAsync<IdentityLink>(new CommandDefinition($"""
            SELECT {LinkColumns}
            FROM identity_links l
            WHERE l.retracted_by_act_id = @actId
            ORDER BY l.id;
            """, new { actId }, lease.Transaction, cancellationToken: ct))).AsList();

        foreach (var prior in displaced)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO identity_links (
                    act_id, child_work_id, parent_work_id, kind, source, evidence_json, applied_at)
                VALUES (@undoActId, @childWorkId, @parentWorkId, @kind, @source, @evidenceJson, @now);
                """,
                new
                {
                    undoActId,
                    childWorkId = prior.ChildWorkId,
                    parentWorkId = prior.ParentWorkId,
                    kind = prior.Kind,
                    source = prior.Source,
                    evidenceJson = prior.EvidenceJson,
                    now,
                },
                lease.Transaction,
                cancellationToken: ct));
        }

        scope.Commit();
        return true;
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

    private static async Task<long?> LiveParentOfAsync(
        DbLease lease, long workId, CancellationToken ct)
        => await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition("""
            SELECT parent_work_id
            FROM identity_links
            WHERE child_work_id = @workId AND retracted_at IS NULL;
            """, new { workId }, lease.Transaction, cancellationToken: ct));

    private static async Task<List<IdentityLink>> LiveLinksUnderAsync(
        DbLease lease, List<long> parentWorkIds, CancellationToken ct)
    {
        var rows = await lease.Connection.QueryAsync<IdentityLink>(new CommandDefinition($"""
            SELECT {LinkColumns}
            FROM identity_links l
            WHERE l.parent_work_id IN @parentWorkIds
              AND l.retracted_at IS NULL
            ORDER BY l.id;
            """, new { parentWorkIds }, lease.Transaction, cancellationToken: ct));

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
        CancellationToken ct)
        => await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source, evidence_json, applied_at)
            VALUES (@actId, @childWorkId, @parentWorkId, @kind, @source, @evidenceJson, @appliedAt);
            """,
            new
            {
                actId,
                childWorkId,
                parentWorkId = request.ParentWorkId,
                kind = request.Kind,
                source = request.Source,
                evidenceJson = request.EvidenceJson,
                appliedAt = _clock.GetUtcNow().UtcDateTime,
            },
            lease.Transaction,
            cancellationToken: ct));
}
