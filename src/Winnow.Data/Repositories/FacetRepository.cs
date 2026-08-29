using Dapper;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>Dapper repository for facets, work_facets and release_facets (migration 0007).</summary>
public sealed class FacetRepository : IFacetRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public FacetRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Facet>> GetVocabularyAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Facet>(new CommandDefinition("""
            SELECT id AS Id, kind AS Kind, slug AS Slug, name AS Name
            FROM facets
            ORDER BY kind, name;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<FacetSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var facets = (await lease.Connection.QueryAsync<Facet>(new CommandDefinition("""
            SELECT id AS Id, kind AS Kind, slug AS Slug, name AS Name
            FROM facets
            ORDER BY kind, name;
            """, transaction: lease.Transaction, cancellationToken: ct))).AsList();

        // UNION of work_facets and release_facets. UNION (not ALL) deduplicates
        // facets present at both layers.
        var assignments = (await lease.Connection.QueryAsync<AssignmentRow>(new CommandDefinition("""
            SELECT ReleaseId, FacetId, Rank
            FROM (
                SELECT r.id        AS ReleaseId,
                       wf.facet_id AS FacetId,
                       NULL        AS Rank
                FROM releases r
                JOIN work_facets wf ON wf.work_id = r.work_id

                UNION

                SELECT rf.release_id AS ReleaseId,
                       rf.facet_id   AS FacetId,
                       rf.rank       AS Rank
                FROM release_facets rf
            )
            -- Rank ascending, NULLs last. Subquery wrapper needed because SQLite
            -- restricts ORDER BY of a compound SELECT to bare result columns.
            ORDER BY ReleaseId, Rank IS NULL, Rank, FacetId;
            """, transaction: lease.Transaction, cancellationToken: ct))).AsList();

        var byRelease = new Dictionary<long, (List<long> Ids, List<string> Modes, HashSet<long> Seen)>();
        var gameModeSlugs = facets
            .Where(f => f.Kind == FacetKinds.GameMode)
            .ToDictionary(f => f.Id, f => f.Slug);

        foreach (var row in assignments)
        {
            if (!byRelease.TryGetValue(row.ReleaseId, out var entry))
            {
                entry = ([], [], []);
                byRelease[row.ReleaseId] = entry;
            }

            // Deduplicate cross-layer facets, keeping the best-ranked sighting.
            if (!entry.Seen.Add(row.FacetId))
            {
                continue;
            }

            entry.Ids.Add(row.FacetId);

            // Game modes are matched by slug in LibraryFilter.
            if (gameModeSlugs.TryGetValue(row.FacetId, out var slug))
            {
                entry.Modes.Add(slug);
            }
        }

        return new FacetSnapshot
        {
            Facets = facets,
            Releases = byRelease
                .Select(kv => new ReleaseFacets(kv.Key, kv.Value.Ids, kv.Value.Modes))
                .OrderBy(r => r.ReleaseId)
                .ToArray(),
        };
    }

    public Task<int> SetWorkFacetsAsync(
        long workId, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default)
        => SetAsync("work_facets", "work_id", workId, facets, ranked: false, ct);

    public Task<int> SetReleaseFacetsAsync(
        long releaseId, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default)
        => SetAsync("release_facets", "release_id", releaseId, facets, ranked: true, ct);

    /// <summary>
    /// Replaces one scope's facet assignments, writing nothing when the stored
    /// set already matches (idempotent on re-runs).
    /// </summary>
    private async Task<int> SetAsync(
        string table,
        string scopeColumn,
        long scopeId,
        IReadOnlyList<FacetAssignment> facets,
        bool ranked,
        CancellationToken ct)
    {
        // Desired state, keyed by facet id. Assignments with no usable key are dropped.
        var desired = new Dictionary<long, int?>();

        using var lease = _factory.Lease();

        foreach (var assignment in facets)
        {
            var slug = assignment.Key;
            if (slug.Length == 0 || string.IsNullOrWhiteSpace(assignment.Kind))
            {
                continue;
            }

            // Game modes are a closed vocabulary; reject unknown slugs.
            if (assignment.Kind == FacetKinds.GameMode && !GameModes.All.Contains(slug))
            {
                continue;
            }

            var facetId = await EnsureFacetAsync(lease, assignment.Kind, slug, assignment.Name.Trim(), ct);

            // First mention wins the rank (handles duplicate display names).
            if (!desired.ContainsKey(facetId))
            {
                desired[facetId] = ranked ? assignment.Rank : null;
            }
        }

        var existing = (await lease.Connection.QueryAsync<StoredAssignment>(new CommandDefinition(
            $"SELECT facet_id AS FacetId, {(ranked ? "rank" : "NULL")} AS Rank FROM {table} WHERE {scopeColumn} = @scopeId;",
            new { scopeId }, transaction: lease.Transaction, cancellationToken: ct)))
            .ToDictionary(r => r.FacetId, r => r.Rank);

        if (existing.Count == desired.Count
            && desired.All(kv => existing.TryGetValue(kv.Key, out var rank) && rank == kv.Value))
        {
            return 0;
        }

        var written = 0;

        var stale = existing.Keys.Where(id => !desired.ContainsKey(id)).ToArray();
        if (stale.Length > 0)
        {
            written += await lease.Connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {table} WHERE {scopeColumn} = @scopeId AND facet_id IN @stale;",
                new { scopeId, stale }, transaction: lease.Transaction, cancellationToken: ct));
        }

        foreach (var (facetId, rank) in desired)
        {
            written += await lease.Connection.ExecuteAsync(new CommandDefinition(
                ranked
                    ? $"""
                       INSERT INTO {table} ({scopeColumn}, facet_id, rank)
                       VALUES (@scopeId, @facetId, @rank)
                       ON CONFLICT ({scopeColumn}, facet_id) DO UPDATE SET rank = excluded.rank;
                       """
                    : $"""
                       INSERT INTO {table} ({scopeColumn}, facet_id)
                       VALUES (@scopeId, @facetId)
                       ON CONFLICT ({scopeColumn}, facet_id) DO NOTHING;
                       """,
                new { scopeId, facetId, rank }, transaction: lease.Transaction, cancellationToken: ct));
        }

        return written;
    }

    /// <summary>Returns the id of a facet, inserting it on first sight. Insert-only, never deleted.</summary>
    private static async Task<long> EnsureFacetAsync(
        DbLease lease, string kind, string slug, string name, CancellationToken ct)
    {
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO facets (kind, slug, name)
            VALUES (@kind, @slug, @name)
            ON CONFLICT (kind, slug) DO NOTHING;
            """, new { kind, slug, name }, transaction: lease.Transaction, cancellationToken: ct));

        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT id FROM facets WHERE kind = @kind AND slug = @slug;",
            new { kind, slug }, transaction: lease.Transaction, cancellationToken: ct));
    }

    private sealed record AssignmentRow
    {
        public long ReleaseId { get; init; }

        public long FacetId { get; init; }

        public int? Rank { get; init; }
    }

    private sealed record StoredAssignment
    {
        public long FacetId { get; init; }

        public int? Rank { get; init; }
    }
}
