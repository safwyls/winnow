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
        cover_url          AS CoverUrl
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Work work, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, summary, cover_url)
            VALUES (@IgdbId, @Name, @SortName, @FirstReleaseYear, @Summary, @CoverUrl)
            RETURNING id;
            """, work, cancellationToken: ct));
    }

    public async Task<Work?> GetAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<Work>(new CommandDefinition(
            $"SELECT {Columns} FROM works WHERE id = @id;",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Work>(new CommandDefinition(
            $"SELECT {Columns} FROM works ORDER BY name;",
            cancellationToken: ct));
        return rows.AsList();
    }
}
