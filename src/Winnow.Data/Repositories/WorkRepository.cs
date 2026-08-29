using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

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
        epic_categories    AS EpicCategories,
        name_is_provisional AS NameIsProvisional
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Work work, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, summary, cover_url, publisher, steam_app_type, epic_categories, name_is_provisional)
            VALUES (@IgdbId, @Name, @SortName, @FirstReleaseYear, @Summary, @CoverUrl, @Publisher, @SteamAppType, @EpicCategories, @NameIsProvisional)
            RETURNING id;
            """, work, transaction: lease.Transaction, cancellationToken: ct));
    }

    /// <summary>Renames a work and sets its provisional flag.</summary>
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
    /// Returns works missing any metadata (name, IGDB id, year, summary, cover,
    /// publisher) or needing type classification. Ordered emptiest-first, then
    /// round-robin across store providers to prevent any store from being starved.
    /// </summary>
    public async Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(
        CancellationToken ct = default)
    {
        var providers = ExternalIdProviders.Stores;
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<EnrichmentTarget>(new CommandDefinition("""
            WITH candidate AS (
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
                   (w.epic_categories    IS NOT NULL) AS HasEpicCategories,
                   COALESCE(NULLIF(TRIM(r.name), ''), w.name) AS Title,

                   -- Count of NULL metadata columns (5 = nothing at all).
                   -- steam_app_type excluded: not user-visible metadata.
                   ((w.igdb_id            IS NULL)
                  + (w.first_release_year IS NULL)
                  + (w.summary            IS NULL)
                  + (w.cover_url          IS NULL)
                  + (w.publisher          IS NULL)) AS MissingColumns
            FROM works w
            JOIN releases     r ON r.work_id = w.id
            JOIN external_ids e ON e.release_id = r.id AND e.provider IN @providers
            WHERE w.name_is_provisional = 1
               OR w.igdb_id            IS NULL
               OR w.first_release_year IS NULL
               OR w.summary            IS NULL
               OR w.cover_url          IS NULL
               OR w.publisher          IS NULL
               -- migration 0006: cheap LIKE prefilter for demo-like titles.
               -- Over-selects; caller applies DemoConsolidation.IsVariantTitle.
               OR (w.steam_app_type IS NULL
                   AND (LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%demo%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%beta%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%test%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%alpha%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%trial%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%weekend%'))
            )
            SELECT WorkId, ReleaseId, Provider, ProviderId, NameIsProvisional,
                   HasIgdbId, HasFirstReleaseYear, HasSummary, HasCoverUrl,
                   HasPublisher, HasSteamAppType, HasEpicCategories, Title
            FROM (
                SELECT candidate.*,

                       -- Round-robin: each store numbered independently per tier.
                       ROW_NUMBER() OVER (
                           PARTITION BY Provider, MissingColumns
                           ORDER BY WorkId) AS ProviderRank
                FROM candidate
            )
            -- Tie-breakers: WorkId before Provider for stable cross-store dedup.
            ORDER BY MissingColumns DESC, ProviderRank, WorkId, Provider;
            """, new { providers }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Applies enrichment to a work. Every column is guarded so writes can only
    /// add information. Returns true if a provisional name was promoted.
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

                -- igdb_id is UNIQUE. Skip if another work already claims it.
                igdb_id = CASE
                            WHEN igdb_id IS NOT NULL THEN igdb_id
                            WHEN @IgdbId IS NULL     THEN NULL
                            WHEN EXISTS (SELECT 1 FROM works other
                                         WHERE other.igdb_id = @IgdbId
                                           AND other.id <> @WorkId) THEN NULL
                            ELSE @IgdbId
                          END,

                -- COALESCE: incoming only fills NULLs, never overwrites.
                first_release_year = COALESCE(@FirstReleaseYear, first_release_year),
                summary            = COALESCE(@Summary,          summary),
                cover_url          = COALESCE(@CoverUrl,         cover_url),
                publisher          = COALESCE(@Publisher,        publisher),

                -- Migration 0006. Same one-way rule.
                steam_app_type     = COALESCE(@SteamAppType,     steam_app_type),

                -- Migration 0009. Same one-way rule.
                epic_categories    = COALESCE(@EpicCategories,   epic_categories)
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
                EpicCategories = Trimmed(enrichment.EpicCategories),
            },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return promoteName;
    }

    /// <summary>Normalises blank/whitespace to null so empty strings never satisfy the "filled" test.</summary>
    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
