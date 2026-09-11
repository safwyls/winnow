using Dapper;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class IgdbObservationWriter(ISqliteConnectionFactory factory) : IIgdbObservationWriter
{
    public async Task<IgdbMappingVersion?> CaptureAsync(long workId, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<IgdbMappingVersion>(new CommandDefinition("""
            SELECT id AS WorkId, igdb_id AS IgdbId, igdb_mapping_revision AS Revision
            FROM works WHERE id = @workId;
            """, new { workId }, lease.Transaction, cancellationToken: ct));
    }

    public async Task<bool> TryWriteAsync(IgdbMappingVersion expected,
        Func<CancellationToken, Task> persist, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(persist);
        ct.ThrowIfCancellationRequested();

        bool hasAmbient;
        using (var current = factory.Lease()) hasAmbient = current.Transaction is not null;
        // A batch's local lease is not ambient. Publish a factory unit of work
        // first so every repository called by persist enlists in this writer.
        using var scope = hasAmbient ? null : factory.Begin();
        using var batch = new RepositoryWriteBatch(factory);
        if (!await IsCurrentAsync(batch.Lease, expected, ct)) return false;

        await persist(ct);
        ct.ThrowIfCancellationRequested();
        batch.Commit();
        scope?.Commit();
        return true;
    }

    internal static Task<bool> IsCurrentAsync(DbLease lease, IgdbMappingVersion expected, CancellationToken ct)
        => lease.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM works
                WHERE id = @WorkId AND igdb_id IS @IgdbId AND igdb_mapping_revision = @Revision);
            """, expected, lease.Transaction, cancellationToken: ct));
}
