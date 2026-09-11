using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class WorkRepository : IWorkRepository
{
    internal const string Columns = """
        id                 AS Id,
        igdb_id            AS IgdbId,
        igdb_mapping_revision AS IgdbMappingRevision,
        name               AS Name,
        sort_name          AS SortName,
        first_release_year AS FirstReleaseYear,
        summary            AS Summary,
        cover_url          AS CoverUrl,
        background_url     AS BackgroundUrl,
        publisher          AS Publisher,
        steam_app_type     AS SteamAppType,
        epic_categories    AS EpicCategories,
        name_is_provisional AS NameIsProvisional,
        steam_store_type       AS SteamStoreType,
        steam_parent_app_id    AS SteamParentAppId,
        igdb_game_type         AS IgdbGameType,
        igdb_parent_id         AS IgdbParentId,
        igdb_version_parent_id AS IgdbVersionParentId
        """;

    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public WorkRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<long> InsertAsync(Work work, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, summary, cover_url, background_url, publisher, steam_app_type, epic_categories, name_is_provisional, steam_store_type, steam_parent_app_id, igdb_game_type, igdb_parent_id, igdb_version_parent_id)
            VALUES (@IgdbId, @Name, @SortName, @FirstReleaseYear, @Summary, @CoverUrl, @BackgroundUrl, @Publisher, @SteamAppType, @EpicCategories, @NameIsProvisional, @SteamStoreType, @SteamParentAppId, @IgdbGameType, @IgdbParentId, @IgdbVersionParentId)
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

    public async Task<Work?> GetByIgdbIdAsync(long igdbId, CancellationToken ct = default)
    {
        if (igdbId <= 0)
        {
            return null;
        }

        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<Work>(new CommandDefinition(
            $"SELECT {Columns} FROM works WHERE igdb_id = @igdbId;",
            new { igdbId }, transaction: lease.Transaction, cancellationToken: ct));
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
    /// publisher) or needing type classification. A field the user owns is
    /// never missing, so it is never a reason to spend a request, and a work
    /// whose only empty column the user deliberately emptied stops being a
    /// target instead of being refilled forever. Ordered emptiest-first, then
    /// round-robin across store providers to prevent any store from being starved.
    /// </summary>
    public async Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(
        CancellationToken ct = default)
    {
        var providers = ExternalIdProviders.Stores;
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<EnrichmentTarget>(new CommandDefinition("""
            -- user_owned: which metadata fields the user has claimed. A
            -- user-owned field is never empty even when its column is NULL
            -- (the user deliberately emptied it), so it must not count as
            -- missing. Hits the partial index ix_work_field_sources_user.
            WITH user_owned AS (
                SELECT work_id,
                       MAX(field = 'name')               AS OwnsName,
                       MAX(field = 'first_release_year') AS OwnsFirstReleaseYear,
                       MAX(field = 'summary')            AS OwnsSummary,
                       MAX(field = 'cover_url')          AS OwnsCoverUrl,
                       MAX(field = 'publisher')          AS OwnsPublisher
                FROM work_field_sources
                WHERE source = 'user'
                GROUP BY work_id
            ),
            candidate AS (
            SELECT w.id  AS WorkId,
                   r.id  AS ReleaseId,
                   e.provider    AS Provider,
                   e.provider_id AS ProviderId,
                   (w.name_is_provisional = 1
                        AND COALESCE(u.OwnsName, 0) = 0)   AS NameIsProvisional,
                   (w.igdb_id            IS NOT NULL)      AS HasIgdbId,
                   (w.first_release_year IS NOT NULL
                        OR COALESCE(u.OwnsFirstReleaseYear, 0) = 1) AS HasFirstReleaseYear,
                   (w.summary            IS NOT NULL
                        OR COALESCE(u.OwnsSummary, 0) = 1)  AS HasSummary,
                   (w.cover_url          IS NOT NULL
                        OR COALESCE(u.OwnsCoverUrl, 0) = 1) AS HasCoverUrl,
                   (w.publisher          IS NOT NULL
                        OR COALESCE(u.OwnsPublisher, 0) = 1) AS HasPublisher,
                   (w.steam_app_type     IS NOT NULL) AS HasSteamAppType,
                   (w.epic_categories    IS NOT NULL) AS HasEpicCategories,
                   (w.igdb_game_type     IS NOT NULL) AS HasIgdbGameType,
                   -- Storefront title for the demo-like prefilter below and
                   -- DemoConsolidation.IsVariantTitle in the caller. Not a
                   -- display title; does not consult work_field_sources.
                   COALESCE(NULLIF(TRIM(r.name), ''), w.name) AS Title,

                   -- Count of NULL metadata columns (5 = nothing at all).
                   -- steam_app_type excluded: not user-visible metadata.
                   ((w.igdb_id IS NULL)
                  + (w.first_release_year IS NULL AND COALESCE(u.OwnsFirstReleaseYear, 0) = 0)
                  + (w.summary            IS NULL AND COALESCE(u.OwnsSummary, 0) = 0)
                  + (w.cover_url          IS NULL AND COALESCE(u.OwnsCoverUrl, 0) = 0)
                  + (w.publisher          IS NULL AND COALESCE(u.OwnsPublisher, 0) = 0))
                       AS MissingColumns
            FROM works w
            JOIN releases     r ON r.work_id = w.id
            JOIN external_ids e ON e.release_id = r.id AND e.provider IN @providers
            LEFT JOIN user_owned u ON u.work_id = w.id
            -- A pinned work is not a target. The automatic pass resolves
            -- identity from the store id via IGDB's external_games, so on a
            -- pinned work everything it would write is metadata about a game
            -- the user has already said this is not.
            WHERE NOT EXISTS (SELECT 1 FROM work_igdb_pins p
                              WHERE p.work_id = w.id AND p.cleared_at IS NULL)
              AND (
                  (w.name_is_provisional = 1 AND COALESCE(u.OwnsName, 0) = 0)
               -- igdb_id is identity — the pin's question — not a field.
               -- Tested unconditionally because user_owned tracks fields the
               -- editor exposes, and igdb_id is not one of them.
               OR w.igdb_id            IS NULL
               OR (w.first_release_year IS NULL AND COALESCE(u.OwnsFirstReleaseYear, 0) = 0)
               OR (w.summary            IS NULL AND COALESCE(u.OwnsSummary, 0) = 0)
               OR (w.cover_url          IS NULL AND COALESCE(u.OwnsCoverUrl, 0) = 0)
               OR (w.publisher          IS NULL AND COALESCE(u.OwnsPublisher, 0) = 0)
               -- migration 0006: cheap LIKE prefilter for demo-like titles.
               -- Over-selects; caller applies DemoConsolidation.IsVariantTitle.
               -- migration 0022: a work that already carries an igdb_id but no
               -- game_type was enriched before the relation fields were asked
               -- for. It rides the same batched /games call the pass already
               -- makes, so it costs no extra request.
               OR (w.igdb_id IS NOT NULL AND w.igdb_game_type IS NULL)
               OR (w.steam_app_type IS NULL
                   AND (LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%demo%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%beta%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%test%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%alpha%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%trial%'
                     OR LOWER(COALESCE(NULLIF(TRIM(r.name), ''), w.name)) LIKE '%weekend%')))
            )
            SELECT WorkId, ReleaseId, Provider, ProviderId, NameIsProvisional,
                   HasIgdbId, HasFirstReleaseYear, HasSummary, HasCoverUrl,
                   HasPublisher, HasSteamAppType, HasEpicCategories, HasIgdbGameType, Title
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
    /// Applies enrichment to a work: writes a field whose source is a service
    /// it can speak for, and leaves a field the user owns. Returns true if a
    /// provisional name was promoted. Stamps every field it actually fills.
    /// </summary>
    public async Task<bool> ApplyEnrichmentAsync(
        WorkEnrichment enrichment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(enrichment);

        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;

        // Defence in depth: a pinned work returns NULL here, so
        // promoteName stays false and the UPDATE below is a no-op. The
        // primary enforcement is the target query, which never produces a
        // pinned work at all.
        var before = await lease.Connection.QuerySingleOrDefaultAsync<EnrichmentWriteState>(
            new CommandDefinition("""
                SELECT name_is_provisional          AS NameIsProvisional,
                       (first_release_year IS NULL) AS FirstReleaseYearIsEmpty,
                       (summary            IS NULL) AS SummaryIsEmpty,
                       (cover_url          IS NULL) AS CoverUrlIsEmpty,
                       (publisher          IS NULL) AS PublisherIsEmpty
                FROM works
                WHERE id = @WorkId
                  AND NOT EXISTS (SELECT 1 FROM work_igdb_pins p
                                  WHERE p.work_id = @WorkId AND p.cleared_at IS NULL);
                """,
                new { enrichment.WorkId }, transaction: lease.Transaction, cancellationToken: ct));

        if (before is null)
        {
            return false;
        }

        // Read the user-owned set once and replace the incoming value with
        // NULL for each field in it, so the existing COALESCE in the UPDATE
        // leaves the stored value alone.
        var userOwned = await WorkFieldSourceWrites.GetUserOwnedFieldsAsync(
            lease.Connection, lease.Transaction, enrichment.WorkId, ct);

        var name = userOwned.Contains(WorkFields.Name) ? null : Trimmed(enrichment.Name);
        var firstReleaseYear = userOwned.Contains(WorkFields.FirstReleaseYear)
            ? null
            : enrichment.FirstReleaseYear;
        var summary = userOwned.Contains(WorkFields.Summary) ? null : Trimmed(enrichment.Summary);
        var coverUrl = userOwned.Contains(WorkFields.CoverUrl) ? null : Trimmed(enrichment.CoverUrl);
        var publisher = userOwned.Contains(WorkFields.Publisher) ? null : Trimmed(enrichment.Publisher);

        var promoteName = name is not null && before.NameIsProvisional;

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

                -- COALESCE means "already answered, leave it": the first
                -- service to answer keeps the field, not a precedence tower
                -- composing several sources. A field the user owns never
                -- reaches this UPDATE with a value: the incoming was replaced
                -- by NULL above, so COALESCE leaves the stored value alone.
                first_release_year = COALESCE(first_release_year, @FirstReleaseYear),
                summary            = COALESCE(summary,            @Summary),
                cover_url          = COALESCE(cover_url,          @CoverUrl),
                publisher          = COALESCE(publisher,          @Publisher),

                -- Migration 0006. Same one-way rule.
                steam_app_type     = COALESCE(steam_app_type,     @SteamAppType),

                -- Migration 0009. Same one-way rule.
                epic_categories    = COALESCE(epic_categories,    @EpicCategories),

                -- Migration 0022: the storefront relation facts. Same one-way
                -- rule, for the same reason -- these are observed facts, and the
                -- first source to answer is the one that saw the app.
                steam_store_type       = COALESCE(steam_store_type,       @SteamStoreType),
                steam_parent_app_id    = COALESCE(steam_parent_app_id,    @SteamParentAppId),
                igdb_game_type         = COALESCE(igdb_game_type,         @IgdbGameType),
                igdb_parent_id         = COALESCE(igdb_parent_id,         @IgdbParentId),
                igdb_version_parent_id = COALESCE(igdb_version_parent_id, @IgdbVersionParentId)
            WHERE id = @WorkId
              AND NOT EXISTS (SELECT 1 FROM work_igdb_pins p
                              WHERE p.work_id = @WorkId AND p.cleared_at IS NULL);
            """,
            new
            {
                enrichment.WorkId,
                Name = name,
                PromoteName = promoteName ? 1 : 0,
                enrichment.IgdbId,
                FirstReleaseYear = firstReleaseYear,
                Summary = summary,
                CoverUrl = coverUrl,
                Publisher = publisher,
                SteamAppType = Trimmed(enrichment.SteamAppType),
                EpicCategories = Trimmed(enrichment.EpicCategories),
                enrichment.SteamStoreType,
                SteamParentAppId = Trimmed(enrichment.SteamParentAppId),
                IgdbGameType = Trimmed(enrichment.IgdbGameType),
                enrichment.IgdbParentId,
                enrichment.IgdbVersionParentId,
            },
            transaction: lease.Transaction,
            cancellationToken: ct));

        // Stamp every field this pass actually filled. The "before" state
        // is read above so that a COALESCE that changed nothing does not
        // claim a source — only a field that was empty and is now answered
        // gets stamped.
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        var source = string.IsNullOrWhiteSpace(enrichment.Source)
            ? FieldSources.Igdb
            : enrichment.Source;

        // A title may come from four different steps; the metadata columns
        // come from IGDB and nowhere else.
        if (promoteName)
        {
            stamps[WorkFields.Name] = string.IsNullOrWhiteSpace(enrichment.NameSource)
                ? source
                : enrichment.NameSource;
        }

        if (before.FirstReleaseYearIsEmpty && firstReleaseYear is not null)
        {
            stamps[WorkFields.FirstReleaseYear] = source;
        }

        if (before.SummaryIsEmpty && summary is not null)
        {
            stamps[WorkFields.Summary] = source;
        }

        if (before.CoverUrlIsEmpty && coverUrl is not null)
        {
            stamps[WorkFields.CoverUrl] = source;
        }

        if (before.PublisherIsEmpty && publisher is not null)
        {
            stamps[WorkFields.Publisher] = source;
        }

        await WorkFieldSourceWrites.StampAsync(
            lease.Connection,
            lease.Transaction,
            enrichment.WorkId,
            stamps,
            _clock.GetUtcNow().UtcDateTime,
            ct);

        batch.Commit();
        return promoteName;
    }

    /// <summary>Normalises blank/whitespace to null so empty strings never satisfy the "filled" test.</summary>
    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class EnrichmentWriteState
    {
        public bool NameIsProvisional { get; init; }

        public bool FirstReleaseYearIsEmpty { get; init; }

        public bool SummaryIsEmpty { get; init; }

        public bool CoverUrlIsEmpty { get; init; }

        public bool PublisherIsEmpty { get; init; }
    }
}
