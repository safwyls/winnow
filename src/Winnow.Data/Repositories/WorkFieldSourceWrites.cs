using System.Data;
using Dapper;

namespace Winnow.Data.Repositories;

/// <summary>
/// The one place a field-source stamp is written (migration 0027), shared
/// by <see cref="WorkRepository"/> and <see cref="WorkIgdbPinRepository"/>
/// so both write the same INSERT … ON CONFLICT DO UPDATE shape inside their
/// own transaction. The upsert is what makes "the user owns this" and "IGDB
/// owns this" the same operation with a different value.
/// </summary>
internal static class WorkFieldSourceWrites
{
    /// <summary>Stamps one or more fields with the source that supplied them.</summary>
    internal static async Task StampAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        long workId,
        IReadOnlyDictionary<string, string> fieldSources,
        DateTime setAt,
        CancellationToken ct)
    {
        if (fieldSources.Count == 0)
        {
            return;
        }

        foreach (var (field, source) in fieldSources)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO work_field_sources (work_id, field, source, set_at)
                VALUES (@workId, @field, @source, @setAt)
                ON CONFLICT(work_id, field) DO UPDATE
                SET source = excluded.source,
                    set_at = excluded.set_at;
                """,
                new { workId, field, source, setAt },
                transaction: transaction,
                cancellationToken: ct));
        }
    }

    internal static async Task<HashSet<string>> GetUserOwnedFieldsAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        long workId,
        CancellationToken ct)
    {
        var fields = await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT field
            FROM work_field_sources
            WHERE work_id = @workId AND source = 'user';
            """,
            new { workId }, transaction: transaction, cancellationToken: ct));

        return new HashSet<string>(fields, StringComparer.Ordinal);
    }
}
