using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class MergeCandidateRepository : IMergeCandidateRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public MergeCandidateRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, signals_json, status)
            VALUES (@LeftReleaseId, @RightReleaseId, @Score, @SignalsJson, @Status)
            RETURNING id;
            """, candidate, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<MergeCandidate>(new CommandDefinition("""
            SELECT id               AS Id,
                   left_release_id  AS LeftReleaseId,
                   right_release_id AS RightReleaseId,
                   score            AS Score,
                   signals_json     AS SignalsJson,
                   status           AS Status
            FROM merge_candidates
            WHERE status = 'pending'
            ORDER BY score DESC, id;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task SetStatusAsync(long id, string status, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE merge_candidates SET status = @status WHERE id = @id;",
            new { id, status }, transaction: lease.Transaction, cancellationToken: ct));
    }
}
