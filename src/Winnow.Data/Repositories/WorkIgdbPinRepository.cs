using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>work_igdb_pins</c> (migration 0026). Append-and-stamp:
/// pinning inserts, clearing stamps <c>cleared_at</c>, and the history is the
/// table. The pin write overwrites the work's IGDB-sourced metadata
/// unconditionally, unlike the automatic enrichment write which only fills.
///
/// <para>The pin answers WHICH GAME this is; field sources (migration 0027)
/// answer WHERE EACH VALUE CAME FROM. A manual edit on a pinned work sets
/// that one field to <c>user</c> and leaves the pin live — changing the
/// summary does not un-say which game it is. Clearing the pin returns the
/// work to the automatic pass; the stamps stay, and the pass fills what is
/// empty and not user-owned.</para>
/// </summary>
public sealed class WorkIgdbPinRepository : IWorkIgdbPinRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public WorkIgdbPinRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<WorkIgdbPinOutcome> PinAsync(
        WorkIgdbPinAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;

        // The work must exist: a pin is meaningless without the row it pins.
        var revision = await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT igdb_mapping_revision FROM works WHERE id = @WorkId;",
            new { assignment.WorkId }, transaction: lease.Transaction, cancellationToken: ct));

        if (revision is null)
        {
            return WorkIgdbPinOutcome.WorkNotFound;
        }

        // works.igdb_id is UNIQUE. If another work already holds this id the
        // pin would violate that constraint, so it is refused rather than
        // allowed to collide.
        var claimed = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM works WHERE igdb_id = @IgdbId AND id <> @WorkId;",
            new { assignment.WorkId, assignment.IgdbId },
            transaction: lease.Transaction, cancellationToken: ct));

        if (claimed > 0)
        {
            return WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork;
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await WorkIgdbMappingWrites.TransitionAsync(lease, assignment.WorkId, assignment.IgdbId,
                pin: true, assignment.ExpectedIgdbMappingRevision ?? revision.Value, now, ct);
        }
        catch (ManualEntryConflictException conflict) when (conflict.Reason == ManualEntryConflictReason.ClaimedByAnotherGame)
        {
            return WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork;
        }

        // COALESCE/NULLIF on the name: works.name is NOT NULL, so a blank
        // name keeps the existing title. A real name also clears
        // name_is_provisional, which stops PromoteProvisionalNameAsync
        // renaming the work later. Every other column is written
        // unconditionally — including back to NULL — because the stored
        // values belonged to the wrong IGDB game.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works
            SET name = COALESCE(NULLIF(TRIM(@Name), ''), name),
                name_is_provisional = CASE
                        WHEN NULLIF(TRIM(@Name), '') IS NOT NULL THEN 0
                        ELSE name_is_provisional
                    END,

                first_release_year     = @FirstReleaseYear,
                summary                = NULLIF(TRIM(@Summary),  ''),
                cover_url              = NULLIF(TRIM(@CoverUrl), ''),
                publisher              = NULLIF(TRIM(@Publisher), ''),
                igdb_game_type         = NULLIF(TRIM(@IgdbGameType), ''),
                igdb_parent_id         = @IgdbParentId,
                igdb_version_parent_id = @IgdbVersionParentId
            WHERE id = @WorkId;
            """,
            new
            {
                assignment.WorkId,
                assignment.IgdbId,
                assignment.Name,
                assignment.FirstReleaseYear,
                assignment.Summary,
                assignment.CoverUrl,
                assignment.Publisher,
                assignment.IgdbGameType,
                assignment.IgdbParentId,
                assignment.IgdbVersionParentId,
            },
            transaction: lease.Transaction, cancellationToken: ct));

        // Pinning is the "take it all from this record" gesture, so it
        // stamps every field it rewrote as igdb — INCLUDING fields the user
        // previously owned. That is not the pin overriding the user; it is
        // the user, in the same act, saying take it all from this record.
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkFields.FirstReleaseYear] = FieldSources.Igdb,
            [WorkFields.Summary] = FieldSources.Igdb,
            [WorkFields.CoverUrl] = FieldSources.Igdb,
            [WorkFields.Publisher] = FieldSources.Igdb,
        };

        // The name is stamped only when the pin actually wrote one, because
        // works.name is NOT NULL and the pin COALESCEs over blank.
        if (!string.IsNullOrWhiteSpace(assignment.Name))
        {
            stamps[WorkFields.Name] = FieldSources.Igdb;
        }

        await WorkFieldSourceWrites.StampAsync(
            lease.Connection, lease.Transaction, assignment.WorkId, stamps, now, ct);

        batch.Commit();
        return WorkIgdbPinOutcome.Pinned;
    }

    public async Task<bool> ClearAsync(long workId, CancellationToken ct = default)
    {
        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE work_igdb_pins
            SET cleared_at = @now
            WHERE work_id = @workId AND cleared_at IS NULL;
            """,
            new { workId, now = _clock.GetUtcNow().UtcDateTime },
            transaction: lease.Transaction, cancellationToken: ct));

        if (rows > 0)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE works SET igdb_mapping_revision = igdb_mapping_revision + 1 WHERE id = @workId;",
                new { workId }, lease.Transaction, cancellationToken: ct));
        }

        batch.Commit();
        return rows > 0;
    }

    public async Task<WorkIgdbPin?> GetAsync(long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<WorkIgdbPin>(new CommandDefinition("""
            SELECT work_id   AS WorkId,
                   igdb_id   AS IgdbId,
                   pinned_at AS PinnedAt
            FROM work_igdb_pins
            WHERE work_id = @workId AND cleared_at IS NULL;
            """, new { workId }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var workIds = await lease.Connection.QueryAsync<long>(new CommandDefinition("""
            SELECT work_id
            FROM work_igdb_pins
            WHERE cleared_at IS NULL;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return new HashSet<long>(workIds);
    }
}
