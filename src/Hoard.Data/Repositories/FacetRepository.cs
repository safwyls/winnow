using Dapper;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

/// <summary>
/// Dapper over migration 0007's three tables.
///
/// <para>The read is one query per table and one join in C#, rather than one
/// clever query with two <c>UNION ALL</c> branches and a <c>GROUP_CONCAT</c>. The
/// whole point of §3.1's choice of Dapper over an ORM was SQL you can read; a
/// statement that assembles a per-release array in SQLite and parses it back out
/// in C# would be neither readable SQL nor readable C#, and the library is 926
/// rows.</para>
/// </summary>
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

        // Both layers of §6's identity model, unioned onto the release the caller
        // is going to draw a tile for.
        //
        // Branch 1 — the WORK's descriptors, reaching the release through
        // releases.work_id. Skyrim's genres are Skyrim's, whichever edition the
        // user happens to own.
        //
        // Branch 2 — the RELEASE's own. Steam tags belong to one appid and stop
        // there; that is the whole reason there are two tables.
        //
        // UNION, not UNION ALL: a facet true at both layers (a game mode, which
        // IGDB writes onto the work and Steam writes onto the release) must
        // appear once. Rank survives only from branch 2 because only Steam
        // publishes an order, and only for tags.
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
            -- Rank ascending with NULLs last, so a release's tags come back in
            -- Steam's own order (rank 1 is its top tag) and the unranked kinds
            -- trail behind by id. The spike's finding that weight is comparable
            -- only within an app is exactly why the ORDER is the only part of it
            -- worth keeping — and this is where it is kept.
            --
            -- The subquery wrapper is not decoration: SQLite restricts the
            -- ORDER BY of a compound SELECT to bare result columns, so
            -- `Rank IS NULL` cannot appear there.
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

            // ONE entry per facet id, whatever the two layers each said about
            // rank. The SQL UNION above dedupes whole ROWS, so it only collapses
            // a cross-layer facet when both branches agree on Rank — which they
            // do for the one kind written at both layers today (game modes,
            // NULL on both sides), and which is why this never fired.
            //
            // It stops being true the moment a RANKED kind is written at the
            // work layer: (release, facet, 1) and (release, facet, NULL) are
            // distinct rows, the UNION keeps both, and CountsFor increments
            // twice for one release — a filter checkbox reading "Roguelike 41"
            // beside 40 tiles. That is not hypothetical; the tag spike's own
            // recommended fallback is to write IGDB keywords when GetItems
            // yields nothing, and keywords are facts about the WORK.
            //
            // Deduping here rather than in the SQL keeps the rank that the
            // ORDER BY already chose: rank-ascending-NULLs-last means the first
            // sighting of a facet is its best-placed one, so the Steam ordering
            // survives and the work layer's rankless copy is the one dropped.
            if (!entry.Seen.Add(row.FacetId))
            {
                continue;
            }

            entry.Ids.Add(row.FacetId);

            // Game modes are also handed over as slugs, because that is what
            // LibraryFilter.GameModes matches on — the one facet two providers
            // write, so the one facet no provider's id could key.
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
    /// Replaces one scope's assignments, and writes nothing at all when the
    /// stored set already matches.
    ///
    /// <para><b>The read-first check is the feature, not an optimisation.</b> The
    /// backfill runs on every launch over every work in the library, and the
    /// answer is almost always "the same as last time". Without this, a warm
    /// re-run would rewrite roughly ten thousand rows to arrive at the state it
    /// started in, churning the WAL and touching mtimes for nothing. With it, a
    /// second run reports zero — which is also how the idempotence test states
    /// its claim, rather than by comparing table dumps.</para>
    /// </summary>
    private async Task<int> SetAsync(
        string table,
        string scopeColumn,
        long scopeId,
        IReadOnlyList<FacetAssignment> facets,
        bool ranked,
        CancellationToken ct)
    {
        // Desired state, keyed by facet id. An assignment with no usable key is
        // dropped here rather than minting a nameless row — see
        // IFacetRepository.SetWorkFacetsAsync. The key is the assignment's own
        // slug where it has one (closed vocabularies) and the folded name where
        // it does not (a provider's vocabulary).
        var desired = new Dictionary<long, int?>();

        using var lease = _factory.Lease();

        foreach (var assignment in facets)
        {
            var slug = assignment.Key;
            if (slug.Length == 0 || string.IsNullOrWhiteSpace(assignment.Kind))
            {
                continue;
            }

            // Game modes are a CLOSED vocabulary that migration 0007 seeded with
            // fixed ids, so an assignment that does not name one of the six is
            // dropped rather than minting a seventh row. Without this, one caller
            // building the assignment by hand from the display name ("Co-op",
            // which folds to `co_op`, not `co_operative`) would silently add a
            // duplicate checkbox beside the seeded one and split its count in
            // two — a bug that looks like a data problem and is a spelling one.
            // Use GameModes.Assignment to build these.
            if (assignment.Kind == FacetKinds.GameMode && !GameModes.All.Contains(slug))
            {
                continue;
            }

            var facetId = await EnsureFacetAsync(lease, assignment.Kind, slug, assignment.Name.Trim(), ct);

            // First mention wins the rank. Two assignments that collapse onto one
            // facet (Valve ships duplicate category display names — 55 and 56 are
            // both "DualShock Controller Support") keep the better-placed of the
            // two, which for a rank-ordered kind is the earlier one.
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

    /// <summary>
    /// The id of a facet, minting it on first sight.
    ///
    /// <para><b>Insert-only, never delete.</b> These ids are what a live list's
    /// <c>filter_json</c> refers to, so a facet that stops appearing anywhere in
    /// the library keeps its row: it costs one row and it keeps every saved
    /// filter that mentions it meaningful. Migration 0007 records the same
    /// promise from the schema's side.</para>
    ///
    /// <para>The <c>DO NOTHING</c> plus <c>SELECT</c> shape rather than
    /// <c>RETURNING</c>: <c>ON CONFLICT DO NOTHING ... RETURNING</c> returns no
    /// row on the conflict path, which is the common path here.</para>
    /// </summary>
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
