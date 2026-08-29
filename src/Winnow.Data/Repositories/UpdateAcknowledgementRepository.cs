using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// SQLite implementation over migration 0012's single table. Rows are appended
/// and revoked — never UPDATEd beyond the revocation stamp, never DELETEd — so
/// the whole history of what the user dismissed stays inspectable, exactly as
/// <see cref="FeedFeedbackRepository"/> keeps verdicts.
///
/// <para>Nothing here decides whether a badge is drawn. The watermark is
/// applied once, inside <see cref="LibraryQueryRepository"/>'s
/// <c>major_update</c> CTE, because design-system.md §5.2 makes the badge
/// identical to <c>stale_but_patched</c> membership. This repository only
/// records and reports what the user said.</para>
/// </summary>
public sealed class UpdateAcknowledgementRepository : IUpdateAcknowledgementRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public UpdateAcknowledgementRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> RecordAsync(UpdateAcknowledgement ack, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Plain INSERT, never an upsert: a later patch dismissed is a second
        // row, and the query's MAX(acknowledged_through) makes it win without
        // the first having to be overwritten to say so.
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO update_acknowledgements
                (release_id, acknowledged_through, created_at, revoked_at)
            VALUES (@ReleaseId, @AcknowledgedThrough, @CreatedAt, @RevokedAt)
            RETURNING id;
            """, ack, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<int> RevokeAsync(
        long releaseId, DateTime revokedAtUtc, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Only STANDING rows get the stamp: a row revoked last month keeps its
        // original revocation date, because history stays history.
        //
        // EVERY standing row on the release, not just the newest. Repeated
        // dismissals accumulate, and stamping only the latest would fall back
        // to an older watermark that still suppresses part of what the user
        // just asked to see again.
        //
        // No expiry clause here, unlike the feed's verdicts — an acknowledgement
        // does not lapse (migration 0012), so there is no "already undone
        // itself" case to avoid claiming credit for.
        return await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE update_acknowledgements
            SET revoked_at = @revokedAtUtc
            WHERE release_id = @releaseId
              AND revoked_at IS NULL;
            """,
            new { releaseId, revokedAtUtc },
            transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<UpdateAcknowledgement?> GetStandingAsync(
        long releaseId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Ordered by the WATERMARK, not by created_at, so this reports the same
        // row the bucket query's MAX(acknowledged_through) obeys. The two
        // orderings coincide in every ordinary sequence; where they could
        // diverge, the row that suppresses the badge is the one the UI must be
        // offering to undo. Tie-broken by id — the later write of two identical
        // watermarks.
        //
        // datetime() on the ORDER BY for the same reason the bucket query uses
        // it: the column is TEXT, and comparing the instants rather than the
        // spellings survives a row written with a 'T' separator or a fractional
        // second by some future writer.
        var rows = await lease.Connection.QueryAsync<UpdateAcknowledgement>(new CommandDefinition("""
            SELECT id                   AS Id,
                   release_id           AS ReleaseId,
                   acknowledged_through AS AcknowledgedThrough,
                   created_at           AS CreatedAt,
                   revoked_at           AS RevokedAt
            FROM update_acknowledgements
            WHERE release_id = @releaseId
              AND revoked_at IS NULL
            ORDER BY datetime(acknowledged_through) DESC, id DESC
            LIMIT 1;
            """, new { releaseId }, transaction: lease.Transaction, cancellationToken: ct));

        return rows.FirstOrDefault();
    }
}
