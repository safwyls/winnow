using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class MergeCandidateRepository : IMergeCandidateRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public MergeCandidateRepository(ISqliteConnectionFactory factory) => _factory = factory;

    /// <summary>
    /// Canonicalises the pair to (lower id, higher id) here rather than trusting
    /// the caller to have done it. Migration 0016 makes the same rule a
    /// <c>CHECK</c> and a <c>UNIQUE</c> key, so a mirror image or a self-pair is
    /// rejected by the database whichever path reaches it; this method is the
    /// half that turns a mirror into the canonical row instead of an error.
    /// </summary>
    public async Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.LeftReleaseId == candidate.RightReleaseId)
        {
            throw new ArgumentException(
                $"A merge candidate cannot pair release {candidate.LeftReleaseId} with itself.",
                nameof(candidate));
        }

        var canonical = candidate.LeftReleaseId < candidate.RightReleaseId
            ? candidate
            : candidate with
            {
                LeftReleaseId = candidate.RightReleaseId,
                RightReleaseId = candidate.LeftReleaseId,
            };

        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, signals_json, status)
            VALUES (@LeftReleaseId, @RightReleaseId, @Score, @SignalsJson, @Status)
            RETURNING id;
            """, canonical, transaction: lease.Transaction, cancellationToken: ct));
    }

    // The l.work_id <> r.work_id predicate is the same one
    // MergeExecutionRepository.GetConfirmedUnappliedCandidateIdsAsync already
    // applies to the confirmed read, for the same reason: two releases already
    // under one work are correctly modelled as two releases of one game (§9
    // pitfall 5), and offering to merge them is offering to collapse Release
    // into Work. The row is not deleted and not answered: GetAllAsync and
    // FindByPairAsync still return it, because they are reads about the ROW,
    // while this one is the read about the QUESTION, and the question is
    // closed. The sweep's own withdrawal pass (SoftMatchAdmission.CouldPropose,
    // which already refuses such a pair) is what eventually removes it.
    public async Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<MergeCandidate>(new CommandDefinition("""
            SELECT c.id               AS Id,
                   c.left_release_id  AS LeftReleaseId,
                   c.right_release_id AS RightReleaseId,
                   c.score            AS Score,
                   c.signals_json     AS SignalsJson,
                   c.status           AS Status
            FROM merge_candidates c
            JOIN releases l ON l.id = c.left_release_id
            JOIN releases r ON r.id = c.right_release_id
            WHERE c.status = 'pending'
              AND l.work_id <> r.work_id
            ORDER BY c.score DESC, c.id;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MergeCandidate>> GetAllAsync(CancellationToken ct = default)
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
            ORDER BY id;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<MergeCandidate?> GetAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QueryFirstOrDefaultAsync<MergeCandidate>(new CommandDefinition("""
            SELECT id               AS Id,
                   left_release_id  AS LeftReleaseId,
                   right_release_id AS RightReleaseId,
                   score            AS Score,
                   signals_json     AS SignalsJson,
                   status           AS Status
            FROM merge_candidates
            WHERE id = @id;
            """, new { id }, transaction: lease.Transaction, cancellationToken: ct));
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

    /// <summary>
    /// The <c>status = 'pending'</c> predicate is the whole safety property, so
    /// it lives in the statement rather than in a caller's <c>if</c>: there is
    /// no ordering of C# that can make this rewrite an answered row.
    /// </summary>
    public async Task<bool> UpdatePendingScoreAsync(
        long id, double score, string? signalsJson, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE merge_candidates
            SET score = @score, signals_json = @signalsJson
            WHERE id = @id AND status = 'pending';
            """,
            new { id, score, signalsJson },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return rows > 0;
    }

    /// <summary>
    /// Same guard, same reason. Deleting is limited to proposals the user has
    /// not answered; <c>confirmed</c> and <c>rejected</c> rows are unreachable
    /// from this statement.
    /// </summary>
    public async Task<bool> WithdrawPendingAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM merge_candidates WHERE id = @id AND status = 'pending';",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));

        return rows > 0;
    }
}
