using System.Globalization;
using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

/// <summary>
/// SQLite implementation over migration 0011's two tables. Verdicts are
/// append-and-revoke (never UPDATE-in-place beyond the revocation stamp,
/// never DELETE — the history is the inspection surface); surfacings are
/// INSERT OR IGNORE against the (release, day) primary key so a same-day
/// refresh re-records nothing.
///
/// <para><c>surfaced_on</c> is a DATE ('yyyy-MM-dd') and crosses this boundary
/// as a string: Dapper's TEXT-column mapping is only dependable for
/// <see cref="DateTime"/>, so <see cref="DateOnly"/> is parsed and formatted
/// here, explicitly, rather than trusting provider version behaviour.</para>
/// </summary>
public sealed class FeedFeedbackRepository : IFeedFeedbackRepository
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ISqliteConnectionFactory _factory;

    public FeedFeedbackRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> RecordVerdictAsync(FeedVerdict verdict, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO feed_verdicts (release_id, kind, created_at, expires_at, revoked_at)
            VALUES (@ReleaseId, @Kind, @CreatedAt, @ExpiresAt, @RevokedAt)
            RETURNING id;
            """, verdict, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<int> RevokeVerdictsAsync(
        long releaseId, string kind, DateTime revokedAtUtc, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Only ACTIVE rows get the stamp: a verdict revoked last year keeps
        // its original revocation date (history stays history), and stamping
        // an already-lapsed snooze would claim the user undid something that
        // had already undone itself.
        return await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE feed_verdicts
            SET revoked_at = @revokedAtUtc
            WHERE release_id = @releaseId
              AND kind = @kind
              AND revoked_at IS NULL
              AND (expires_at IS NULL OR expires_at > @revokedAtUtc);
            """,
            new { releaseId, kind, revokedAtUtc },
            transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<FeedVerdict>> GetActiveVerdictsAsync(
        DateTime asOfUtc, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<FeedVerdict>(new CommandDefinition($"""
            SELECT {VerdictColumns}
            FROM feed_verdicts
            WHERE revoked_at IS NULL
              AND (expires_at IS NULL OR expires_at > @asOfUtc)
            ORDER BY created_at, id;
            """, new { asOfUtc }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<FeedVerdict>> GetAllVerdictsAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<FeedVerdict>(new CommandDefinition($"""
            SELECT {VerdictColumns}
            FROM feed_verdicts
            ORDER BY created_at DESC, id DESC;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task RecordSurfacedAsync(
        IReadOnlyList<FeedSurfacing> surfacings, CancellationToken ct = default)
    {
        if (surfacings.Count == 0)
        {
            return;
        }

        using var lease = _factory.Lease();
        foreach (var surfacing in surfacings)
        {
            // OR IGNORE against the (release_id, surfaced_on) primary key:
            // the first record of the day wins and a refresh is a no-op. The
            // shelf on the first record is the one kept — within a day the
            // feed is stable by design, so they cannot disagree anyway.
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT OR IGNORE INTO feed_surfacings (release_id, surfaced_on, shelf_id)
                VALUES (@ReleaseId, @SurfacedOn, @ShelfId);
                """,
                new
                {
                    surfacing.ReleaseId,
                    SurfacedOn = surfacing.SurfacedOn.ToString(DateFormat, CultureInfo.InvariantCulture),
                    surfacing.ShelfId,
                },
                transaction: lease.Transaction, cancellationToken: ct));
        }
    }

    public async Task<IReadOnlyList<FeedSurfacing>> GetSurfacedSinceAsync(
        DateOnly since, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<SurfacingRow>(new CommandDefinition("""
            SELECT release_id AS ReleaseId, surfaced_on AS SurfacedOn, shelf_id AS ShelfId
            FROM feed_surfacings
            WHERE surfaced_on >= @since
            ORDER BY surfaced_on, release_id;
            """,
            new { since = since.ToString(DateFormat, CultureInfo.InvariantCulture) },
            transaction: lease.Transaction, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<FeedEndorsement>> GetEndorsementsAsync(
        int windowDays, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // attributed_by = 'launch' ONLY: 'inferred' means the game was started
        // from Steam/Epic/Galaxy and merely detected, and NULL means "not
        // recorded" — three-valued on purpose (migration 0010), and neither of
        // those is the user answering the feed.
        //
        // GROUP BY s.id with MAX(surfaced_on): a session that falls inside the
        // window of several surfacing days is ONE endorsement, credited to the
        // nearest (latest) surfacing. SQLite's bare-column-with-aggregate rule
        // guarantees shelf_id comes from that same max row; release_id is
        // constant within the group (one session, one ownership, one release).
        var rows = await lease.Connection.QueryAsync<EndorsementRow>(new CommandDefinition("""
            SELECT o.release_id                      AS ReleaseId,
                   s.id                              AS SessionId,
                   s.started_at                      AS StartedAt,
                   CAST(MAX(fs.surfaced_on) AS TEXT) AS SurfacedOn,
                   fs.shelf_id                       AS ShelfId
            FROM sessions s
            JOIN ownerships o       ON o.id = s.ownership_id
            JOIN feed_surfacings fs ON fs.release_id = o.release_id
            WHERE s.attributed_by = 'launch'
              AND julianday(date(s.started_at)) - julianday(fs.surfaced_on)
                  BETWEEN 0 AND @windowDays
            GROUP BY s.id
            ORDER BY s.started_at, s.id;
            """, new { windowDays }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private const string VerdictColumns = """
        id         AS Id,
        release_id AS ReleaseId,
        kind       AS Kind,
        created_at AS CreatedAt,
        expires_at AS ExpiresAt,
        revoked_at AS RevokedAt
        """;

    // Property-based DTOs, not positional records: Dapper maps a positional
    // record by demanding a constructor whose parameter types exactly match
    // the reader's column types, which SQLite's loose typing cannot promise
    // (an aggregate column, for one, loses its declared type). Property
    // mapping converts per column instead — the same shape every other
    // repository here relies on.
    private sealed class SurfacingRow
    {
        public long ReleaseId { get; init; }
        public string SurfacedOn { get; init; } = string.Empty;
        public string ShelfId { get; init; } = string.Empty;

        public FeedSurfacing ToDomain() => new()
        {
            ReleaseId = ReleaseId,
            SurfacedOn = DateOnly.ParseExact(SurfacedOn, DateFormat, CultureInfo.InvariantCulture),
            ShelfId = ShelfId,
        };
    }

    private sealed class EndorsementRow
    {
        public long ReleaseId { get; init; }
        public long SessionId { get; init; }
        public DateTime StartedAt { get; init; }
        public string SurfacedOn { get; init; } = string.Empty;
        public string ShelfId { get; init; } = string.Empty;

        public FeedEndorsement ToDomain() => new()
        {
            ReleaseId = ReleaseId,
            SessionId = SessionId,
            StartedAt = StartedAt,
            SurfacedOn = DateOnly.ParseExact(SurfacedOn, DateFormat, CultureInfo.InvariantCulture),
            ShelfId = ShelfId,
        };
    }
}
