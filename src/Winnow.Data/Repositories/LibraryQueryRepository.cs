using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// The §6.1 derived-bucket query. Buckets are computed on read from stored
/// facts (latest play record, correlated update events) with caller-supplied
/// thresholds — never persisted, so thresholds can be retuned freely.
///
/// <para>One of those stored facts is the user's own: an
/// <c>update_acknowledgements</c> watermark (migration 0012) drops the build
/// pushes they have already read out of the <c>major_update</c> CTE, which is
/// the whole of the "dismiss the Patched since flag" feature. It lives here
/// because design-system.md §5.2 makes the badge identical to
/// <c>stale_but_patched</c> membership, so every surface that draws or counts
/// that badge inherits the dismissal from this one query.</para>
///
/// <para>Demo consolidation (<see cref="DemoConsolidation"/>) is derived here
/// for the same reason and in the same pass: a demo whose full game is also
/// owned is dropped from the result, so the library shows one entry per game
/// without the view knowing anything about demos. Nothing is written and
/// nothing is deleted — removing the base game makes the demo reappear on the
/// very next read.</para>
///
/// <para>The non-game filter (<see cref="NonGameEntries"/>) is derived here too,
/// and last: with <see cref="BucketThresholds.ShowNonGameEntries"/> off, the
/// tools, soundtracks and videos Valve typed as such never reach the caller. It
/// runs on the same rows the buckets and the counts are read from, which is the
/// whole point — the rail cannot report a total the grid does not show.</para>
///
/// <para>The account-visibility filter (<see cref="AccountScope"/>, migration
/// 0015) is the third stored user fact applied here, and it is applied for the
/// acknowledgement watermark's reason. When the user has asked to see only their
/// own Steam account's games, this query hides the rest and substitutes that
/// account's own figures for the household ones — so the grid, the rail counts,
/// the filter chips, the recommender and the feed narrow together, and no caller
/// has to learn that accounts exist.</para>
/// </summary>
public sealed class LibraryQueryRepository : ILibraryQueryRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public LibraryQueryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
        // Null override: the stored preference governs, and the caller does not
        // get a say. Only the toggle's own label reaches past this, and it does
        // so through CountHiddenByAccountScopeAsync rather than here.
        => await QueryAsync(thresholds, scopeOverride: null, ct);

    /// <inheritdoc/>
    public async Task<int> CountHiddenByAccountScopeAsync(
        BucketThresholds thresholds, CancellationToken ct = default)
    {
        // Answered by running the whole query both ways and subtracting, rather
        // than by counting membership rows. The label says "N games hidden", and
        // that has to be the number of tiles that actually disappear — after
        // demo consolidation has folded a demo into its base game and after the
        // non-game filter has removed a soundtrack that was never on screen to
        // be hidden. A count taken off ownership_accounts would be a different,
        // larger number, and the user would go looking for the difference.
        //
        // This is also the one place a scope is passed IN rather than read from
        // the settings table, and deliberately so: the question is "what would
        // the other mode show", which the stored preference cannot answer about
        // itself. Both calls still read the same stored account reference, so a
        // machine with no confirmed account answers zero either way.
        var all = await QueryAsync(thresholds, AccountScope.All, ct);
        var own = await QueryAsync(thresholds, AccountScope.Own, ct);

        // Distinct games, not rows. The grid draws one tile per resolved
        // work, so the number of tiles that actually disappear is the
        // difference between two counts of distinct games — a linked pair
        // whose Steam entry is filtered away loses a store chip and not a
        // tile, and counting rows would promise a tile back that never left.
        return Math.Max(0, Games(all) - Games(own));

        static int Games(IReadOnlyList<OwnershipBucket> rows)
        {
            var seen = new HashSet<long>();
            foreach (var row in rows)
            {
                seen.Add(row.ResolvedWorkId);
            }

            return seen.Count;
        }
    }

    private async Task<IReadOnlyList<OwnershipBucket>> QueryAsync(
        BucketThresholds thresholds, string? scopeOverride, CancellationToken ct)
    {
        // Bucket precedence now lives in LibraryBucketRules.Classify rather
        // than in the CASE this query used to carry. The query still finds
        // every stored fact the rules read — the latest play record per
        // ownership, the acknowledgement watermark, the correlated build
        // push, the account scope — and Consolidate applies the rules on
        // the way out, at both grains. Buckets are still derived on read
        // and still never stored.
        const string sql = """
            WITH latest_play AS (
                -- The newest play record per ownership. observed_at is stored to
                -- whole seconds, so two scans in one second tie; the higher id
                -- is the later write. Same rule as PlayRecordRepository
                -- .GetLatestAsync, which a bare-column MAX() would not have
                -- agreed with (SQLite would pick an arbitrary row of the tie).
                SELECT ownership_id, playtime_minutes, last_played_at
                FROM (
                    SELECT ownership_id,
                           playtime_minutes,
                           last_played_at,
                           ROW_NUMBER() OVER (
                               PARTITION BY ownership_id
                               ORDER BY observed_at DESC, id DESC) AS rn
                    FROM play_records
                )
                WHERE rn = 1
            ),
            acknowledged AS (
                -- The user's "I've seen this patch" watermark per release
                -- (migration 0012): the occurred_at of the newest build push
                -- they have dismissed and not taken back.
                --
                -- MAX() because repeated dismissals ACCUMULATE — rows are
                -- appended and revoked, never updated in place — so the latest
                -- standing one is the one in force.
                --
                -- `revoked_at IS NULL` rather than an "active" column, for
                -- 0011's reason: standing is a property of the asking moment,
                -- so it stays a query. Note it is still weaker than
                -- "suppressing": a standing row stops suppressing the instant a
                -- newer correlated push outranks it, which is decided below and
                -- costs no write.
                SELECT release_id, MAX(acknowledged_through) AS through
                FROM update_acknowledgements
                WHERE revoked_at IS NULL
                GROUP BY release_id
            ),
            major_update AS (
                -- §4.5 and pitfall 4: a "major update" is a build push AND an
                -- announcement within the same window. Neither alone qualifies —
                -- a lone depot push is a DRM bump, a localization file or a
                -- one-line hotfix, and announcing "MAJOR UPDATE" on the strength
                -- of one is the single most visible way this feature can lie.
                --
                -- Correlation happens HERE, at read time, not at ingest: §4.5
                -- stores both raw signals precisely so the heuristic can be
                -- retuned (@UpdateCorrelationWindowDays) without re-fetching.
                --
                -- The build push is the moment the user's game actually changed,
                -- so it — not the announcement, which may tease or recap — is the
                -- timestamp compared against last-played.
                --
                -- ── The acknowledgement filter ───────────────────────────────
                --
                -- A push at or before the release's standing watermark does not
                -- qualify at all: the user has already read that patch and
                -- dismissed §5.2's dot for it. The test is applied BEFORE the
                -- MAX() and before the announcement-correlation EXISTS, so a
                -- dismissed push cannot become the reported occurred_at and
                -- cannot lend its announcement to itself. A push strictly after
                -- the watermark survives, and the badge comes back with NO WRITE
                -- ANYWHERE — the whole point of storing an instant rather than a
                -- flag. Compared with datetime() on both sides, as the staleness
                -- test below is.
                --
                -- Applying it HERE, once, is the entire reach of the feature.
                -- design-system.md §5.2 states the badge IS `stale_but_patched`
                -- bucket membership, so this single exclusion makes the tile
                -- badge, the rail's "Patched since" count, the library filter
                -- chip, the recommender's bucket bonus and the feed's
                -- `patched_while_away` shelf agree at once — none of them need
                -- to learn that acknowledgements exist, and a second consumer
                -- answering the same question separately would be the beginning
                -- of them disagreeing.
                --
                -- The exclusion is UNCONDITIONAL and takes no parameter. It is a
                -- stored user fact, NOT a tunable, so it does not belong in
                -- BucketThresholds beside the floors and windows: those exist to
                -- be retuned, and no retuning may put back a badge the user
                -- personally dismissed.
                --
                -- Nothing in update_events is deleted or mutated to achieve
                -- this. §4.5's "store both raw signals so the heuristic can be
                -- retuned" still holds — the acknowledgement is a separate fact
                -- layered over untouched rows, which is also why the detail
                -- view can still list every update the user missed.
                SELECT push.release_id,
                       MAX(push.occurred_at) AS occurred_at
                FROM update_events push
                LEFT JOIN acknowledged ack ON ack.release_id = push.release_id
                WHERE push.kind = 'build_push'
                  AND (ack.through IS NULL
                       OR datetime(push.occurred_at) > datetime(ack.through))
                  AND EXISTS (
                      SELECT 1
                      FROM update_events news
                      WHERE news.release_id = push.release_id
                        AND news.kind = 'announcement'
                        AND abs(julianday(news.occurred_at) - julianday(push.occurred_at))
                            <= @UpdateCorrelationWindowDays
                  )
                GROUP BY push.release_id
            ),
            owned_account AS (
                -- The Steam account the user's own Web API key was observed to
                -- belong to, and ONLY when they have asked to be shown just that
                -- account. Empty otherwise — which is what makes every CTE below
                -- collapse to nothing and the whole query fall back to exactly
                -- the rows it returned before this feature existed.
                --
                -- Read here rather than taken as a parameter, for the
                -- acknowledgement watermark's reason two CTEs up: this is a
                -- stored user fact, not a tunable, and the surfaces that draw the
                -- library must not each be trusted to remember to ask.
                --
                -- Both halves are required. A stored `own` on a machine whose key
                -- was removed, or was never confirmed, degrades to showing
                -- everything rather than to showing nothing — a preference must
                -- not be able to empty the library it was meant to narrow.
                SELECT TRIM(ref.value) AS account_ref
                FROM (SELECT value FROM settings WHERE key = @OwnedAccountKey) ref
                WHERE TRIM(COALESCE(ref.value, '')) <> ''
                  AND COALESCE(
                          @ScopeOverride,
                          (SELECT value FROM settings WHERE key = @ScopeKey),
                          @ScopeAll) = @ScopeOwn
            ),
            mine AS (
                -- The user's own account's row for each ownership: what THEY
                -- played, as opposed to what this PC did.
                SELECT oa.ownership_id,
                       oa.playtime_minutes,
                       oa.last_played_at
                FROM ownership_accounts oa
                JOIN owned_account oc ON oc.account_ref = oa.account_ref
                JOIN ownerships    o  ON o.id = oa.ownership_id AND o.store = @Store
            ),
            owned_account_attested AS (
                -- Proof that the pass which can name the user's account has
                -- actually run at least once, anywhere in this store.
                --
                -- ── Why absence needs this before it means anything ──────────
                --
                -- The two kinds of evidence come from DIFFERENT passes, and only
                -- one of them is on the local, always-available path. The local
                -- scan reads localconfig.vdf, which records games an account has
                -- PLAYED — so it happily attests that a housemate played
                -- something while saying nothing at all about the games the user
                -- owns and has never launched. Those come only from
                -- GetOwnedGames, a network call whose failure is caught and
                -- logged so a private profile or a dead endpoint cannot cost the
                -- user the local scan.
                --
                -- That asymmetry is a trap. On a machine where the account is
                -- confirmed but the owned-list pass has not yet succeeded, every
                -- game the user owns-but-never-launched that a housemate DID
                -- play carries exactly one non-seed row — the housemate's — and
                -- the predicate below would read it as positive evidence the
                -- game is not the user's. The user would watch their own
                -- never-played backlog disappear, which is the failure
                -- acceptance criterion #2 exists to forbid, arriving by a
                -- different road than the account_ref column it names.
                --
                -- One non-seed row for the user's account, anywhere in the
                -- store, is the cheapest honest proof that the pass has run. It
                -- costs one indexed lookup and it cannot be satisfied by a seed,
                -- which is what makes it evidence about the PASS rather than
                -- about any particular game.
                SELECT 1 AS attested
                FROM ownership_accounts oa
                JOIN owned_account oc ON oc.account_ref = oa.account_ref
                JOIN ownerships    o  ON o.id = oa.ownership_id AND o.store = @Store
                WHERE oa.source <> @LegacySeedSource
                LIMIT 1
            ),
            hidden AS (
                -- ── The filter, and the exact shape of its honesty ───────────
                --
                -- A game is hidden ONLY on positive evidence that no account row
                -- names the user's account. Four conditions, each load-bearing:
                --
                --   EXISTS owned_account_attested — the user's own evidence pass
                --   has run at all. Until it has, nothing here can distinguish
                --   "not yours" from "not yet looked for", and the CTE above
                --   explains why that distinction cannot be assumed.
                --
                --   o.store = @Store — the stored account reference is a Steam3
                --   id. A GOG user id and an Epic ownership with no account at
                --   all are not evidence about a Steam account, and letting them
                --   fail the match would empty two thirds of the library.
                --
                --   EXISTS a non-seed row — evidence has to exist before absence
                --   from it means anything. A game no reader has enumerated
                --   accounts for stays VISIBLE; "not known" is not "not yours".
                --   Migration 0015's seed rows are excluded from this test by
                --   source, because a seed carries the single-winner ambiguity
                --   this whole table replaces: it names whoever played the game
                --   most, which on a shared game is routinely not the only owner.
                --   Trusting one would hide the user's own game because a
                --   housemate played it more — the exact failure the feature
                --   forbids. The first sync after the migration supplies real
                --   rows and the caveat retires itself.
                --
                --   NOT EXISTS a row for the user's account — the question
                --   itself. Note it is asked over ALL rows, seeded ones
                --   included: a seed naming the user IS proof they hold the game,
                --   even though a seed naming somebody else proves nothing about
                --   them. The asymmetry is not an oversight; evidence of
                --   presence and evidence of absence are different claims and
                --   this table can make only one of them cheaply.
                --
                -- Family Sharing needs no special case and gets none. The rows
                -- record which ACCOUNT played, not which account bought, so a
                -- title played under the user's login counts as theirs whoever
                -- paid for the licence.
                SELECT o.id AS ownership_id
                FROM ownerships o
                CROSS JOIN owned_account oc
                WHERE o.store = @Store
                  AND EXISTS (SELECT 1 FROM owned_account_attested)
                  AND EXISTS (
                      SELECT 1 FROM ownership_accounts e
                      WHERE e.ownership_id = o.id AND e.source <> @LegacySeedSource)
                  AND NOT EXISTS (
                      SELECT 1 FROM ownership_accounts m
                      WHERE m.ownership_id = o.id AND m.account_ref = oc.account_ref)
            ),
            effective_play AS (
                -- ── The substitution ────────────────────────────────────────
                --
                -- Filtered to one account, the library shows THAT ACCOUNT'S
                -- playtime and last-played, not the household total, and every
                -- bucket below is derived from the substituted pair. A user who
                -- has asked to see only their own games and is then told they
                -- have 40 hours in something they played for two would rightly
                -- read the whole feature as broken.
                --
                -- The two columns move TOGETHER, never field by field. They are
                -- one account's coherent answer — the same discipline
                -- CandidateOwnershipMerge applies to the play tuple — and a
                -- blend of one account's minutes with another's date is a fact
                -- no source ever reported. So a membership row with NULL minutes
                -- substitutes its NULL, and the row reads as "played, total
                -- unknown", which is true.
                --
                -- ── Known divergence, accepted 2026-08-30 ───────────────────
                --
                -- The recommender's episode signal reads playtime_snapshots,
                -- which is an OWNERSHIP-level series and has no per-account
                -- form. So for a game two accounts play, what the feed scores on
                -- and what the tile displays can disagree while this filter is
                -- on. Recorded rather than fixed: a yours-versus-household
                -- episode distinction is follow-up work, and the alternative —
                -- showing household figures here — was rejected because it makes
                -- every filtered tile wrong instead of a few feed cards.
                --
                -- In `all` mode `mine` is empty, so this is `latest_play`
                -- verbatim and the query returns byte-identical rows to the ones
                -- it returned before the feature existed.
                SELECT o.id AS ownership_id,
                       CASE WHEN m.ownership_id IS NOT NULL
                            THEN m.playtime_minutes ELSE lp.playtime_minutes END AS playtime_minutes,
                       CASE WHEN m.ownership_id IS NOT NULL
                            THEN m.last_played_at   ELSE lp.last_played_at   END AS last_played_at
                FROM ownerships o
                LEFT JOIN latest_play lp ON lp.ownership_id = o.id
                LEFT JOIN mine        m  ON m.ownership_id = o.id
            ),
            same_game AS (
                -- Identity links (migration 0018), resolved HERE and once, in
                -- the same pass as demo consolidation.
                --
                -- kind is filtered to @SameGameKind, bound from
                -- IdentityLinkKinds.SameGame, so an expansion_of link can never
                -- move a count, a playtime, a bucket or a recommendation — the
                -- user's decision of 2026-08-31 that expansions are titles whose
                -- playtime does not roll up, made a fact of the query rather
                -- than a convention.
                --
                -- retracted_at IS NULL is the same predicate as
                -- ux_identity_links_live, so at most one row per child and the
                -- LEFT JOIN below cannot multiply the result.
                --
                -- Depth is one (asserted by IdentityLinkRepository), so one join
                -- reaches the parent; no recursive CTE is needed.
                --
                -- With no links this CTE is empty, every ResolvedWorkId below
                -- collapses to the work's own id, and the query returns
                -- byte-identical rows to the ones it returned before links
                -- existed.
                SELECT child_work_id, parent_work_id
                FROM identity_links
                WHERE retracted_at IS NULL
                  AND kind = @SameGameKind
            ),
            variant AS (
                -- Variant links (migration 0021): demos, betas, playtests and
                -- staging branches under the game they sample. Read here,
                -- beside same_game, and applied in C# below for the same
                -- reason demo consolidation is: the rule needs to know which
                -- rows survive, and the account filter and the non-game filter
                -- both drop rows after the query.
                --
                -- A variant does not count as a title while its parent is
                -- owned, and DOES count when it is the only thing owned. That
                -- is DemoConsolidation's read-time rule with a storefront fact
                -- behind it instead of a title guess, and it keeps the same
                -- reversibility: the parent leaving the library brings the demo
                -- straight back on the next read, because nothing was written.
                --
                -- Playtime never rolls up. The variant's own hours stay on its
                -- own row and reach the details modal from there.
                SELECT child_work_id, parent_work_id
                FROM identity_links
                WHERE retracted_at IS NULL
                  AND kind = @VariantKind
            )
            SELECT o.id                                AS OwnershipId,
                   o.release_id                        AS ReleaseId,
                   w.id                                AS WorkId,
                   -- Total by construction: COALESCE makes every work resolve,
                   -- to its parent or to itself, which is the same contract
                   -- SameGameResolution.Resolve carries in C#.
                   COALESCE(sg.parent_work_id, w.id)   AS ResolvedWorkId,
                   va.parent_work_id                   AS VariantParentWorkId,
                   COALESCE(ep.playtime_minutes, 0)    AS PlaytimeMinutes,
                   ep.last_played_at                   AS LastPlayedAt,
                   -- Demo consolidation reads these three; the SQL itself takes
                   -- no view on them. Title matching is a token-level question
                   -- (sequel ordinals, edition markers) that SQLite cannot ask
                   -- and that must be asked with the SAME normaliser the soft
                   -- matcher uses, so it happens in C# below over the rows this
                   -- join already had to read.
                   COALESCE(NULLIF(TRIM(r.name), ''), w.name)  AS Title,
                   w.name_is_provisional               AS NameIsProvisional,
                   w.first_release_year                AS FirstReleaseYear,
                   -- Valve's own classification of the appid (migration 0006),
                   -- verbatim. NULL is "nobody has read it", which is common:
                   -- some appids are unreadable without a Web API key.
                   w.steam_app_type                    AS SteamAppType,
                   -- Epic's own categories[].path list (migration 0009),
                   -- comma-joined and verbatim. Same contract as the column
                   -- above: NULL is "nobody has read it", which is the state of
                   -- every Epic work named from catcache.bin.
                   w.epic_categories                   AS EpicCategories,
                   -- The raw input the rules are applied to, not the verdict.
                   -- The CASE that stood here now lives in
                   -- LibraryBucketRules.Classify so it can run at two grains
                   -- (per row and per game) without two implementations.
                   mu.occurred_at                      AS MajorUpdateAt
            FROM ownerships o
            JOIN releases            r  ON r.id = o.release_id
            JOIN works               w  ON w.id = r.work_id
            -- `effective_play`, not `latest_play`: with the account filter off
            -- the two are the same relation, and with it on this is the one join
            -- that puts the user's own figures in front of the household's.
            LEFT JOIN effective_play ep ON ep.ownership_id = o.id
            LEFT JOIN major_update   mu ON mu.release_id = o.release_id
            -- One LEFT JOIN, at most one row per child by ux_identity_links_live.
            LEFT JOIN same_game      sg ON sg.child_work_id = w.id
            -- Same shape and the same guarantee: at most one row per child.
            LEFT JOIN variant        va ON va.child_work_id = w.id
            -- Empty unless the user asked to see one account only. See the CTE
            -- for why "no evidence" is not "not yours".
            WHERE NOT EXISTS (SELECT 1 FROM hidden h WHERE h.ownership_id = o.id)
            ORDER BY o.id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<BucketRow>(new CommandDefinition(sql, new
        {
            thresholds.BouncedFloorMinutes,
            thresholds.RetiredFloorMinutes,
            thresholds.StaleWindowMonths,
            thresholds.UpdateCorrelationWindowDays,
            ScopeOverride = scopeOverride,
            ScopeKey = AccountScope.SettingKey,
            ScopeAll = AccountScope.All,
            ScopeOwn = AccountScope.Own,
            OwnedAccountKey = SteamOwnedAccount.RefSettingKey,
            Store = ExternalIdProviders.Steam,
            LegacySeedSource = OwnershipAccountSources.LegacyOwnershipColumn,
            SameGameKind = IdentityLinkKinds.SameGame,
            VariantKind = IdentityLinkKinds.VariantOf,
        }, transaction: lease.Transaction, cancellationToken: ct));

        return Consolidate(rows.AsList(), thresholds);
    }

    public async Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default)
    {
        // One row per release, carrying the id each descriptor source is keyed
        // by. The Steam appid is a correlated subquery rather than a join
        // because external_ids can in principle hold more than one row per
        // release (gog, epic, igdb alongside steam) and a join would multiply
        // the result; LIMIT 1 keeps this at exactly one row per release.
        //
        // No filter on "already has facets": see the interface's own note on why
        // the backfill re-reads everything and lets the cache and the
        // read-before-write keep it cheap.
        const string sql = """
            SELECT r.work_id  AS WorkId,
                   r.id       AS ReleaseId,
                   w.igdb_id  AS IgdbId,
                   (SELECT e.provider_id
                    FROM external_ids e
                    WHERE e.release_id = r.id AND e.provider = 'steam'
                    LIMIT 1) AS SteamAppId
            FROM releases r
            JOIN works w ON w.id = r.work_id
            ORDER BY r.id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<FacetTarget>(new CommandDefinition(
            sql, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Drops the demo rows the library already holds the full game for, and
    /// tells each surviving base row how many it absorbed.
    ///
    /// <para>Reads the rows the query returned and nothing else: the set of
    /// owned releases IS the set of releases with a row here, so a base game
    /// the user does not own cannot hide anything, and a base game removed
    /// tomorrow stops hiding its demo the moment this runs again. That is the
    /// whole reversibility guarantee, and it costs one pass over a few hundred
    /// rows.</para>
    /// </summary>
    /// <remarks>
    /// The non-game filter is applied after all of that, and only when the
    /// caller asked for it. <b>The order is load-bearing.</b> Consolidation is
    /// fed every owned row regardless of the setting, so the demo/base map it
    /// returns is identical whether non-game entries are shown or hidden — the
    /// filter can move a tool off the screen but can never change which demo is
    /// folded into which game. (The one corner: a hidden non-game row that had
    /// absorbed a variant takes that variant's suppression with it. That needs
    /// an owned entry Valve typed <c>Tool</c> whose title is exactly an owned
    /// demo's base title, which the measured library contains nothing like, and
    /// un-hiding is one toggle away.)
    /// </remarks>
    private static IReadOnlyList<OwnershipBucket> Consolidate(
        List<BucketRow> rows, BucketThresholds thresholds)
    {
        var showNonGameEntries = thresholds.ShowNonGameEntries;

        // One entry per RELEASE — a release owned on two stores is one game,
        // and normalising its title twice would only produce the same answer.
        var owned = new Dictionary<long, DemoConsolidationEntry>();
        foreach (var row in rows)
        {
            owned.TryAdd(row.ReleaseId, new DemoConsolidationEntry
            {
                ReleaseId = row.ReleaseId,
                Title = row.Title ?? string.Empty,
                NameIsProvisional = row.NameIsProvisional,
                FirstReleaseYear = row.FirstReleaseYear,
                SteamAppType = row.SteamAppType,
            });
        }

        var consolidated = DemoConsolidation.Consolidate(owned.Values);

        // The stored half of the same rule. A work owned in this result set can
        // hide its variants; a parent the user does not own hides nothing, so
        // its demo is the only copy there is and counts as a title in its own
        // right.
        var ownedWorkIds = new HashSet<long>();
        foreach (var row in rows)
        {
            ownedWorkIds.Add(row.ResolvedWorkId);
            ownedWorkIds.Add(row.WorkId);
        }

        var absorbedByVariantParent = new Dictionary<long, int>();
        var suppressedVariants = new HashSet<long>();
        foreach (var row in rows)
        {
            if (row.VariantParentWorkId is not { } parent
                || !ownedWorkIds.Contains(parent)
                || consolidated.ContainsKey(row.ReleaseId))
            {
                continue;
            }

            if (suppressedVariants.Add(row.ReleaseId))
            {
                absorbedByVariantParent[parent] =
                    absorbedByVariantParent.TryGetValue(parent, out var n) ? n + 1 : 1;
            }
        }

        var absorbedByBase = new Dictionary<long, int>();
        foreach (var baseReleaseId in consolidated.Values)
        {
            absorbedByBase[baseReleaseId] =
                absorbedByBase.TryGetValue(baseReleaseId, out var n) ? n + 1 : 1;
        }

        // A hidden variant is counted against one of its parent's surviving
        // store entries, the lowest release id, for the same reason a
        // consolidated demo is counted against one base release: the number is
        // "this entry stands in for N you are not being shown", and adding it
        // to every entry of a game owned on two stores would report one demo
        // twice.
        foreach (var (parentWorkId, count) in absorbedByVariantParent)
        {
            long? anchor = null;
            foreach (var row in rows)
            {
                if ((row.ResolvedWorkId != parentWorkId && row.WorkId != parentWorkId)
                    || consolidated.ContainsKey(row.ReleaseId)
                    || suppressedVariants.Contains(row.ReleaseId))
                {
                    continue;
                }

                if (anchor is null || row.ReleaseId < anchor)
                {
                    anchor = row.ReleaseId;
                }
            }

            if (anchor is { } releaseId)
            {
                absorbedByBase[releaseId] = absorbedByBase.GetValueOrDefault(releaseId) + count;
            }
        }

        var survivors = new List<BucketRow>(rows.Count);
        foreach (var row in rows)
        {
            if (consolidated.ContainsKey(row.ReleaseId) || suppressedVariants.Contains(row.ReleaseId))
            {
                // Suppressed from the LIBRARY VIEW only. The ownership, its
                // play records, its snapshots and its sessions are untouched
                // and still reachable through every other repository.
                continue;
            }

            if (!showNonGameEntries && NonGameEntries.IsNonGame(row.SteamAppType, row.EpicCategories))
            {
                // A tool, soundtrack, video or piece of hardware on Steam; an
                // Unreal Engine build, a marketplace asset pack or a cosmetic
                // entitlement on Epic. Either way something the user genuinely
                // owns and has not asked to see. Hidden from the LIBRARY VIEW
                // only, exactly like a consolidated demo: nothing is written and
                // nothing is deleted, so the next read with the setting on
                // returns it untouched — including its playtime, which for two
                // of the Epic rows in the author's library is not zero.
                //
                // ONE notion of "not a game", two sources of evidence: Valve
                // publishes a type string per appid and Epic a category list per
                // catalog item, neither expressible in the other's vocabulary,
                // and NonGameEntries.IsNonGame(steam, epic) is the single place
                // that reads both. The Epic half defers to
                // EpicGameFilter — the same predicate the local Epic scan
                // applies before a candidate is ever emitted — so the two halves
                // of Epic ingest cannot drift apart.
                //
                // A NULL or unrecognised value never reaches here on either
                // side: most of the library has no stored classification at all,
                // and "not known" is not "not a game".
                continue;
            }

            survivors.Add(row);
        }

        return Fold(survivors, absorbedByBase, thresholds);
    }

    /// <summary>
    /// Folds the surviving rows into games (TASK-70.6) and hands every row
    /// the <see cref="GameGrouping"/> it is one store entry of.
    ///
    /// <para>It runs here, after consolidation, and not as a window function
    /// in the SQL, because the SQL cannot know which rows survive: demo
    /// consolidation, the non-game filter and the account filter all drop
    /// rows in C# above. A sum taken in the query would include entries the
    /// grid does not show, and the details modal — which folds the rows it
    /// is given, by design — would report a different total for the same
    /// game. One fold over one set is what stops those two numbers
    /// disagreeing.</para>
    ///
    /// <para>The sum and its date come from
    /// <see cref="CoveragePlaytime.Across"/>, the same factory the modal
    /// uses, which has no constructor that can pair a sum with a foreign
    /// store's date (the F10 hazard). The game's bucket is the shared rules
    /// re-applied to that sum, not the strongest member's bucket: two entries
    /// at sixty minutes each are two Active rows and one Bounced game.</para>
    /// </summary>
    private static IReadOnlyList<OwnershipBucket> Fold(
        List<BucketRow> survivors,
        Dictionary<long, int> absorbedByBase,
        BucketThresholds thresholds)
    {
        var members = new Dictionary<long, List<BucketRow>>();
        foreach (var row in survivors)
        {
            if (!members.TryGetValue(row.ResolvedWorkId, out var list))
            {
                members[row.ResolvedWorkId] = list = [];
            }

            list.Add(row);
        }

        var games = new Dictionary<long, GameGrouping>(members.Count);
        foreach (var (resolvedWorkId, rows) in members)
        {
            DateTime? update = null;
            foreach (var row in rows)
            {
                if (row.MajorUpdateAt is { } at && (update is null || at > update))
                {
                    update = at;
                }
            }

            games[resolvedWorkId] = GameGrouping.Of(resolvedWorkId, rows, update, thresholds);
        }

        var result = new List<OwnershipBucket>(survivors.Count);
        foreach (var row in survivors)
        {
            result.Add(new OwnershipBucket
            {
                OwnershipId = row.OwnershipId,
                ReleaseId = row.ReleaseId,
                WorkId = row.WorkId,
                ResolvedWorkId = row.ResolvedWorkId,
                PlaytimeMinutes = row.PlaytimeMinutes,
                LastPlayedAt = row.LastPlayedAt,
                MajorUpdateAt = row.MajorUpdateAt,
                Bucket = LibraryBucketRules.Classify(
                    row.PlaytimeMinutes, row.LastPlayedAt, row.MajorUpdateAt, thresholds),
                Game = games[row.ResolvedWorkId],

                // Never a playtime sum — see OwnershipBucket.ConsolidatedDemoCount.
                ConsolidatedDemoCount = absorbedByBase.GetValueOrDefault(row.ReleaseId),
            });
        }

        return result;
    }

    /// <summary>
    /// The query's own row shape: an <see cref="OwnershipBucket"/> plus the
    /// three title columns consolidation needs. They stay off the public
    /// projection because they are inputs to a decision this repository has
    /// already made by the time the caller sees a row.
    /// </summary>
    private sealed record BucketRow : IPlayedEntry
    {
        public long OwnershipId { get; init; }
        public long ReleaseId { get; init; }
        public long WorkId { get; init; }
        public long ResolvedWorkId { get; init; }
        public long? VariantParentWorkId { get; init; }
        public long PlaytimeMinutes { get; init; }
        public DateTime? LastPlayedAt { get; init; }
        public DateTime? MajorUpdateAt { get; init; }
        public string? Title { get; init; }
        public bool NameIsProvisional { get; init; }
        public int? FirstReleaseYear { get; init; }
        public string? SteamAppType { get; init; }
        public string? EpicCategories { get; init; }
    }
}
