using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>work_images</c> (migration 0028). One row per (work,
/// source, kind); a re-read from one source replaces its own row and never
/// clobbers the other's. Modelled on <see cref="WorkMaturityRepository"/>.
/// </summary>
public sealed class WorkImageRepository : IWorkImageRepository
{
    private const string Columns = """
        work_id     AS WorkId,
        source      AS Source,
        kind        AS Kind,
        image_ids   AS ImageIds,
        observed_at AS ObservedAt
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkImageRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task UpsertAsync(WorkImages images, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(images);

        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO work_images (work_id, source, kind, image_ids, observed_at)
            VALUES (@WorkId, @Source, @Kind, @ImageIds, @ObservedAt)
            ON CONFLICT (work_id, source, kind) DO UPDATE SET
                image_ids   = excluded.image_ids,
                observed_at = excluded.observed_at;
            """, images, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(
        long workId, string source, string kind, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM work_images WHERE work_id = @workId AND source = @source AND kind = @kind;",
            new { workId, source, kind }, transaction: lease.Transaction, cancellationToken: ct));

        return rows > 0;
    }

    public async Task<IReadOnlyList<WorkImages>> GetForWorkAsync(
        long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<WorkImages>(new CommandDefinition(
            $"SELECT {Columns} FROM work_images WHERE work_id = @workId ORDER BY source, kind;",
            new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }
}

/// <summary>
/// Dapper over <c>work_ratings</c> (migration 0028). One row per (work,
/// source); a re-read from one source replaces its own row and never
/// clobbers the other's. Modelled on <see cref="WorkMaturityRepository"/>.
/// </summary>
public sealed class WorkRatingRepository : IWorkRatingRepository
{
    private const string Columns = """
        work_id      AS WorkId,
        source       AS Source,
        score        AS Score,
        rating_count AS RatingCount,
        label        AS Label,
        observed_at  AS ObservedAt
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkRatingRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task UpsertAsync(WorkRating rating, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rating);

        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO work_ratings (work_id, source, score, rating_count, label, observed_at)
            VALUES (@WorkId, @Source, @Score, @RatingCount, @Label, @ObservedAt)
            ON CONFLICT (work_id, source) DO UPDATE SET
                score        = excluded.score,
                rating_count = excluded.rating_count,
                label        = excluded.label,
                observed_at  = excluded.observed_at;
            """, rating, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(long workId, string source, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM work_ratings WHERE work_id = @workId AND source = @source;",
            new { workId, source }, transaction: lease.Transaction, cancellationToken: ct));

        return rows > 0;
    }

    public async Task<IReadOnlyList<WorkRating>> GetForWorkAsync(
        long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<WorkRating>(new CommandDefinition(
            $"SELECT {Columns} FROM work_ratings WHERE work_id = @workId ORDER BY source;",
            new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }
}
