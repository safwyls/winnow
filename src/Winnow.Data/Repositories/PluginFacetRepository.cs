using Dapper;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class PluginFacetRepository(ISqliteConnectionFactory factory) : IPluginFacetRepository
{
    public async Task SetAsync(long workId, string source, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default)
    {
        if (!source.StartsWith("plugin:", StringComparison.Ordinal)) throw new ArgumentException("A plugin source is required.", nameof(source));
        using var batch = new RepositoryWriteBatch(factory);
        var lease = batch.Lease;
        var ids = new HashSet<long>();
        foreach (var facet in facets.Where(f => f.Kind is FacetKinds.Genre or FacetKinds.Tag && f.Key.Length > 0))
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO facets(kind,slug,name) VALUES(@Kind,@Key,@Name)
                ON CONFLICT(kind,slug) DO NOTHING;
                """, facet, lease.Transaction, cancellationToken: ct));
            ids.Add(await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT id FROM facets WHERE kind=@Kind AND slug=@Key;", facet, lease.Transaction, cancellationToken: ct)));
        }
        var existing = (await lease.Connection.QueryAsync<long>(new CommandDefinition(
            "SELECT facet_id FROM plugin_work_facets WHERE work_id=@workId AND source=@source;",
            new { workId, source }, lease.Transaction, cancellationToken: ct))).ToHashSet();
        if (!existing.SetEquals(ids))
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM plugin_work_facets WHERE work_id=@workId AND source=@source;",
                new { workId, source }, lease.Transaction, cancellationToken: ct));
            foreach (var id in ids)
                await lease.Connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO plugin_work_facets(work_id,source,facet_id) VALUES(@workId,@source,@id);",
                    new { workId, source, id }, lease.Transaction, cancellationToken: ct));
        }
        batch.Commit();
    }
}
