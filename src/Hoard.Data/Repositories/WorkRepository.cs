using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
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
        publisher          AS Publisher,
        name_is_provisional AS NameIsProvisional
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Work work, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, summary, cover_url, publisher, name_is_provisional)
            VALUES (@IgdbId, @Name, @SortName, @FirstReleaseYear, @Summary, @CoverUrl, @Publisher, @NameIsProvisional)
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

    public async Task<IReadOnlyList<ProvisionalNameTarget>> GetProvisionalNameTargetsAsync(
        string provider, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<ProvisionalNameTarget>(new CommandDefinition("""
            SELECT w.id  AS WorkId,
                   r.id  AS ReleaseId,
                   e.provider    AS Provider,
                   e.provider_id AS ProviderId
            FROM works w
            JOIN releases     r ON r.work_id = w.id
            JOIN external_ids e ON e.release_id = r.id AND e.provider = @provider
            WHERE w.name_is_provisional = 1
            ORDER BY w.id;
            """, new { provider }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// "Which works are still missing anything?" — a placeholder name, or any
    /// empty metadata column. The disjunction is the point: a library named by
    /// an earlier build has no provisional rows left, and asking only about
    /// those would back-fill nothing.
    ///
    /// <para>No index serves an OR across five columns, and none is wanted: the
    /// projection is six flags over a few hundred rows, and the answer is the
    /// empty set once the backlog drains.</para>
    /// </summary>
    public async Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(
        string provider, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<EnrichmentTarget>(new CommandDefinition("""
            SELECT w.id  AS WorkId,
                   r.id  AS ReleaseId,
                   e.provider    AS Provider,
                   e.provider_id AS ProviderId,
                   w.name_is_provisional              AS NameIsProvisional,
                   (w.igdb_id            IS NOT NULL) AS HasIgdbId,
                   (w.first_release_year IS NOT NULL) AS HasFirstReleaseYear,
                   (w.summary            IS NOT NULL) AS HasSummary,
                   (w.cover_url          IS NOT NULL) AS HasCoverUrl,
                   (w.publisher          IS NOT NULL) AS HasPublisher
            FROM works w
            JOIN releases     r ON r.work_id = w.id
            JOIN external_ids e ON e.release_id = r.id AND e.provider = @provider
            WHERE w.name_is_provisional = 1
               OR w.igdb_id            IS NULL
               OR w.first_release_year IS NULL
               OR w.summary            IS NULL
               OR w.cover_url          IS NULL
               OR w.publisher          IS NULL
            ORDER BY w.id;
            """, new { provider }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// One statement per work, every column guarded so the write can only ever
    /// add information.
    ///
    /// <para>The pre-read of <c>name_is_provisional</c> is what lets the caller
    /// move the release name in the same transaction: SQLite's RETURNING clause
    /// reports the row AFTER the update, which cannot distinguish "was already
    /// named" from "was named by this statement". The UPDATE re-checks the flag
    /// itself rather than trusting the read, so a title that became real
    /// between the two statements still wins.</para>
    /// </summary>
    public async Task<bool> ApplyEnrichmentAsync(
        WorkEnrichment enrichment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(enrichment);

        using var lease = _factory.Lease();

        var name = Trimmed(enrichment.Name);
        var wasProvisional = await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT name_is_provisional FROM works WHERE id = @WorkId;",
            new { enrichment.WorkId }, transaction: lease.Transaction, cancellationToken: ct));

        var promoteName = name is not null && wasProvisional == 1;

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works
            SET name = CASE WHEN @PromoteName = 1 AND name_is_provisional = 1
                            THEN @Name ELSE name END,

                name_is_provisional = CASE WHEN @PromoteName = 1 AND name_is_provisional = 1
                            THEN 0 ELSE name_is_provisional END,

                -- igdb_id is the canonical identity and UNIQUE. Filled only when
                -- absent, and only when no OTHER work already claims it: two
                -- Steam appids resolving to one IGDB game is a duplicate in the
                -- user's library, and the answer to that is a merge candidate
                -- for a human (§5.3), never a silent identity steal or a failed
                -- transaction that rolls back the whole enrichment pass.
                igdb_id = CASE
                            WHEN igdb_id IS NOT NULL THEN igdb_id
                            WHEN @IgdbId IS NULL     THEN NULL
                            WHEN EXISTS (SELECT 1 FROM works other
                                         WHERE other.igdb_id = @IgdbId
                                           AND other.id <> @WorkId) THEN NULL
                            ELSE @IgdbId
                          END,

                -- COALESCE(incoming, stored): a source that said nothing cannot
                -- erase what a source that did say something already wrote.
                first_release_year = COALESCE(@FirstReleaseYear, first_release_year),
                summary            = COALESCE(@Summary,          summary),
                cover_url          = COALESCE(@CoverUrl,         cover_url),
                publisher          = COALESCE(@Publisher,        publisher)
            WHERE id = @WorkId;
            """,
            new
            {
                enrichment.WorkId,
                Name = name,
                PromoteName = promoteName ? 1 : 0,
                enrichment.IgdbId,
                enrichment.FirstReleaseYear,
                Summary = Trimmed(enrichment.Summary),
                CoverUrl = Trimmed(enrichment.CoverUrl),
                Publisher = Trimmed(enrichment.Publisher),
            },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return promoteName;
    }

    /// <summary>
    /// Blank is not an answer. A source returning <c>""</c> means "I do not
    /// know", and storing it would satisfy the "column is filled" test forever
    /// while showing the user nothing.
    /// </summary>
    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
