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
        steam_app_type     AS SteamAppType,
        name_is_provisional AS NameIsProvisional
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Work work, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, summary, cover_url, publisher, steam_app_type, name_is_provisional)
            VALUES (@IgdbId, @Name, @SortName, @FirstReleaseYear, @Summary, @CoverUrl, @Publisher, @SteamAppType, @NameIsProvisional)
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
    ///
    /// <para><b>Every store provider, not one.</b> The <c>provider</c> parameter
    /// this method used to take was answered <c>steam</c> by its only caller,
    /// which is why the author's 67 Epic and 14 GOG releases had zero metadata
    /// of any kind — they were never in a result set. <c>ExternalIdProviders.Stores</c>
    /// is expanded by Dapper into the <c>IN</c> list, so adding a store to that
    /// constant is all it takes for this sweep to see it. <c>igdb</c> is excluded
    /// by not being in that list: it is Hoard's own canonical id, not a
    /// storefront's, and using it as a lookup key would be asking IGDB to
    /// resolve an id IGDB gave us.</para>
    /// </summary>
    public async Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(
        CancellationToken ct = default)
    {
        var providers = ExternalIdProviders.Stores;
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
                   (w.publisher          IS NOT NULL) AS HasPublisher,
                   (w.steam_app_type     IS NOT NULL) AS HasSteamAppType,
                   COALESCE(NULLIF(TRIM(r.name), ''), w.name) AS Title
            FROM works w
            JOIN releases     r ON r.work_id = w.id
            JOIN external_ids e ON e.release_id = r.id AND e.provider IN @providers
            WHERE w.name_is_provisional = 1
               OR w.igdb_id            IS NULL
               OR w.first_release_year IS NULL
               OR w.summary            IS NULL
               OR w.cover_url          IS NULL
               OR w.publisher          IS NULL
               -- migration 0006. NOT "every untyped work": that would return the
               -- whole library forever and invite 616 requests to a volunteer
               -- service to learn `Game` six hundred times. Valve's type only
               -- ever changes an outcome for a row DemoConsolidation reasons
               -- about, so the predicate narrows to rows whose title already
               -- looks like a handout. This LIKE is a cheap PREFILTER only —
               -- it over-selects ("Demonologist", "The Turing Test") and the
               -- caller applies DemoConsolidation.IsVariantTitle, the real
               -- tokenised gate, to what it returns. SQLite cannot run the
               -- normaliser, and a second opinion about titles written in SQL
               -- is exactly what §5.3 says must not exist.
               OR (w.steam_app_type IS NULL
                   AND (LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%demo%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%beta%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%test%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%alpha%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%trial%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%weekend%'))
            ORDER BY w.id, e.provider;
            """, new { providers }, transaction: lease.Transaction, cancellationToken: ct));
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
                publisher          = COALESCE(@Publisher,        publisher),

                -- Migration 0006. Same one-way rule: Valve saying nothing about
                -- an appid (the `_missing_token` shape) must not erase a type an
                -- earlier, luckier fetch already recorded.
                steam_app_type     = COALESCE(@SteamAppType,     steam_app_type)
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
                SteamAppType = Trimmed(enrichment.SteamAppType),
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
