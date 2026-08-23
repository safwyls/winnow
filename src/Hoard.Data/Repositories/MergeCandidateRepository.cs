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

    public async Task<MergeCandidate?> FindByPairAsync(
        long leftReleaseId, long rightReleaseId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Either orientation: the soft matcher canonicalises to (min, max)
        // before writing, but a row inserted by hand — or by an earlier build
        // that did not canonicalise — must still be found, or a re-scan would
        // insert its mirror image and the user gets asked the same question
        // twice.
        return await lease.Connection.QueryFirstOrDefaultAsync<MergeCandidate>(new CommandDefinition("""
            SELECT id               AS Id,
                   left_release_id  AS LeftReleaseId,
                   right_release_id AS RightReleaseId,
                   score            AS Score,
                   signals_json     AS SignalsJson,
                   status           AS Status
            FROM merge_candidates
            WHERE (left_release_id = @a AND right_release_id = @b)
               OR (left_release_id = @b AND right_release_id = @a)
            ORDER BY id
            LIMIT 1;
            """,
            new { a = leftReleaseId, b = rightReleaseId },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    public async Task SetStatusAsync(long id, string status, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE merge_candidates SET status = @status WHERE id = @id;",
            new { id, status }, transaction: lease.Transaction, cancellationToken: ct));
    }
}
