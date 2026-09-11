using System.Text.Json;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class ActivityRepository(ISqliteConnectionFactory factory) : IActivityRepository
{
    public async Task<ActivityPage> GetPageAsync(IReadOnlyCollection<long> ownershipIds, DateTime fromUtc, DateTime untilUtc,
        ActivitySection section, ActivityCursor? after = null, int pageSize = 50, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 200);
        if (fromUtc >= untilUtc) throw new ArgumentException("Activity period must have a positive duration.");
        if (!Enum.IsDefined(section)) throw new ArgumentOutOfRangeException(nameof(section));
        if (ownershipIds.Count == 0) return new([], null);
        // A JSON table avoids SQLite's parameter limit on very large libraries.
        var ids = JsonSerializer.Serialize(ownershipIds.Distinct());
        const string sessionSql = """
            SELECT s.id AS Id, s.ownership_id AS OwnershipId, o.store AS Store,
                   s.started_at AS AtUtc, s.ended_at AS EndedAt, s.duration_s AS DurationSeconds,
                   s.detection_method AS DetectionMethod, s.attributed_by AS AttributedBy,
                   s.monitor_key AS MonitorKey, n.note AS Note, n.rating AS Rating
            FROM sessions s
            JOIN ownerships o ON o.id=s.ownership_id
            LEFT JOIN session_notes n ON n.session_id=s.id
            WHERE s.ownership_id IN (SELECT value FROM json_each(@ids))
              AND s.started_at >= @fromUtc AND s.started_at < @untilUtc
              AND (@journal=0 OR NULLIF(TRIM(n.note),'') IS NOT NULL OR n.rating IS NOT NULL)
              AND (@afterAt IS NULL OR s.started_at < @afterAt OR (s.started_at=@afterAt AND s.id<@afterId))
            ORDER BY s.started_at DESC, s.id DESC LIMIT @take;
            """;
        const string updateSql = """
            WITH visible AS (
                SELECT release_id,MIN(id) AS ownership_id FROM ownerships
                WHERE id IN (SELECT value FROM json_each(@ids)) GROUP BY release_id
            )
            SELECT u.id AS Id, v.ownership_id AS OwnershipId, o.store AS Store,
                   u.occurred_at AS AtUtc, u.release_id AS ReleaseId, u.kind AS Kind,
                   u.build_id AS BuildId, u.title AS Title, u.url AS Url
            FROM update_events u
            JOIN visible v ON v.release_id=u.release_id
            JOIN ownerships o ON o.id=v.ownership_id
            WHERE u.occurred_at >= @fromUtc AND u.occurred_at < @untilUtc
              AND (@afterAt IS NULL OR u.occurred_at < @afterAt OR (u.occurred_at=@afterAt AND u.id<@afterId))
            ORDER BY u.occurred_at DESC, u.id DESC LIMIT @take;
            """;
        using var lease = factory.Lease();
        var rows = (await lease.Connection.QueryAsync<Row>(new CommandDefinition(
            section == ActivitySection.Updates ? updateSql : sessionSql,
            new { ids, fromUtc, untilUtc, journal = section == ActivitySection.Journal ? 1 : 0,
                afterAt = after?.AtUtc, afterId = after?.Id, take = pageSize + 1 },
            transaction: lease.Transaction, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        ct.ThrowIfCancellationRequested();
        var page = rows.Take(pageSize).Select(row => section == ActivitySection.Updates
            ? new ActivityRow(row.OwnershipId, row.Store, row.AtUtc, null, null,
                new UpdateEvent { Id=row.Id, ReleaseId=row.ReleaseId, OccurredAt=row.AtUtc,
                    Kind=row.Kind!, BuildId=row.BuildId, Title=row.Title, Url=row.Url })
            : new ActivityRow(row.OwnershipId, row.Store, row.AtUtc,
                new Session { Id=row.Id, OwnershipId=row.OwnershipId, StartedAt=row.AtUtc,
                    EndedAt=row.EndedAt, DurationSeconds=row.DurationSeconds, DetectionMethod=row.DetectionMethod!,
                    AttributedBy=row.AttributedBy, MonitorKey=row.MonitorKey },
                row.Note is null && row.Rating is null ? null : new SessionNote { SessionId=row.Id, Note=row.Note, Rating=row.Rating }, null)).ToArray();
        return new(page, rows.Count > pageSize ? new(rows[pageSize-1].AtUtc, rows[pageSize-1].Id) : null);
    }

    private sealed class Row
    {
        public long Id { get; init; }
        public long OwnershipId { get; init; }
        public string Store { get; init; } = "";
        public DateTime AtUtc { get; init; }
        public DateTime? EndedAt { get; init; }
        public long? DurationSeconds { get; init; }
        public string? DetectionMethod { get; init; }
        public string? AttributedBy { get; init; }
        public string? MonitorKey { get; init; }
        public string? Note { get; init; }
        public int? Rating { get; init; }
        public long ReleaseId { get; init; }
        public string? Kind { get; init; }
        public string? BuildId { get; init; }
        public string? Title { get; init; }
        public string? Url { get; init; }
    }
}
