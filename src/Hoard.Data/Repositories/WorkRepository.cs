using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class WorkRepository : IWorkRepository
{
    private const string Columns = """
        id                 AS Id,
        igdb_id            AS IgdbId,
        name               AS Name,
        sort_name          AS SortName,
        first_release_year AS FirstReleaseYear,
        summary            AS Summary,
        cover_url          AS CoverUrl,
        name_is_provisional AS NameIsProvisional
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Work work, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, summary, cover_url, name_is_provisional)
            VALUES (@IgdbId, @Name, @SortName, @FirstReleaseYear, @Summary, @CoverUrl, @NameIsProvisional)
            RETURNING id;
            """, work, transaction: lease.Transaction, cancellationToken: ct));
    }

    /// <summary>
    /// Renames a work and sets its provisional flag. Callers must not use this
    /// to overwrite a real title with a placeholder — see
    /// <see cref="Work.NameIsProvisional"/>.
    /// </summary>
    public async Task UpdateNameAsync(
        long id, string name, bool nameIsProvisional, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works
            SET name = @name, name_is_provisional = @nameIsProvisional
            WHERE id = @id;
            """, new { id, name, nameIsProvisional }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<Work?> GetAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<Work>(new CommandDefinition(
            $"SELECT {Columns} FROM works WHERE id = @id;",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Work>(new CommandDefinition(
            $"SELECT {Columns} FROM works ORDER BY name;",
            transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
