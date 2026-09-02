using System.Diagnostics;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// Fills in what the local Steam files could not know: real titles and the
/// metadata columns (igdb_id, first_release_year, summary, cover_url, publisher)
/// from IGDB, the Steam store, steamcmd.net and Epic's catalog service, in slices.
/// </summary>
public sealed class EnrichmentSyncService
{
    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly IBuildInfoClient _steamCmd;
    private readonly IEpicCatalogClient? _epicCatalog;
    private readonly EnrichmentLookupPlanner _lookups;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ILogger<EnrichmentSyncService> _logger;

    /// <param name="works">Work repository.</param>
    /// <param name="releases">Release repository — names move with the work.</param>
    /// <param name="igdb">The metadata backbone (§4.4).</param>
    /// <param name="steamStore">Keyless Steam store fallback, titles only.</param>
    /// <param name="steamCmd">The PICS mirror: last-resort Steam names, and Valve's app type.</param>
    /// <param name="lookups">Works out how each store reaches IGDB.</param>
    /// <param name="unitOfWork">Transaction scope factory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="epicCatalog">
    /// Epic's catalog service, or null on a host that did not register the
    /// opt-in Epic API module. Optional in exactly the way
    /// <see cref="IEpicCatalogClient"/> describes: an install with no Epic
    /// session, or none at all, simply has no step 3b, and every Epic work keeps
    /// whatever name and classification its local files gave it.
    /// </param>
    public EnrichmentSyncService(
        IWorkRepository works,
        IReleaseRepository releases,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        IBuildInfoClient steamCmd,
        EnrichmentLookupPlanner lookups,
        IUnitOfWorkFactory unitOfWork,
        ILogger<EnrichmentSyncService> logger,
        IEpicCatalogClient? epicCatalog = null)
    {
        _works = works;
        _releases = releases;
        _igdb = igdb;
        _steamStore = steamStore;
        _steamCmd = steamCmd;
        _lookups = lookups;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _epicCatalog = epicCatalog;
    }

    /// <summary>
    /// How many targets one pass of plan-ask-write covers before committing and
    /// starting the next.
    ///
    /// <para>40 is chosen against the shape of a real run rather than a round
    /// number. The query interleaves the stores, so the first slice of 40 on the
    /// author's library holds every one of GOG's 14 outstanding rows beside a
    /// dozen each of Epic and Steam — the smallest store is finished before the
    /// second slice begins, which is exactly the property that failed. Smaller
    /// slices would commit sooner but multiply the per-slice IGDB round trips;
    /// much larger ones drift back towards the all-or-nothing pass this
    /// replaced.</para>
    /// </summary>
    public const int DefaultSliceSize = 40;

    /// <summary>
    /// Overridable so a test can bound a run without waiting on wall-clock time.
    /// <c>init</c> rather than a constructor parameter so DI keeps resolving the
    /// single public constructor unchanged.
    /// </summary>
    public int SliceSize { get; init; } = DefaultSliceSize;

    /// <summary>
    /// Names every work still carrying a placeholder and back-fills metadata
    /// columns for every work missing any of them. Idempotent and free once warm.
    /// </summary>
    public async Task<EnrichmentReport> EnrichAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var targets = await _works.GetEnrichmentTargetsAsync(ct);
        var outstandingNames = targets.Count(t => t.NameIsProvisional);
        if (targets.Count == 0)
        {
            _logger.LogInformation(
                "Every work is named and fully enriched; enrichment has nothing to do.");
            return new EnrichmentReport(0, 0, 0, stopwatch.Elapsed);
        }

        var run = new RunState(targets);
        var sliceSize = Math.Max(1, SliceSize);

        try
        {
            foreach (var slice in targets.Chunk(sliceSize))
            {
                ct.ThrowIfCancellationRequested();
                await EnrichSliceAsync(slice, run, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The one outcome that used to be invisible. Everything committed so
            // far stands — each write is its own transaction — but the run did
            // NOT finish, and saying so is the whole point: a pass that dies
            // halfway used to log nothing at all and was indistinguishable from
            // one that had nothing left to do, which is precisely why a store
            // sat at zero for months without anyone noticing.
            stopwatch.Stop();
            _logger.LogWarning(
                "Enrichment CUT SHORT by shutdown after {Elapsed:n1}s. "
                + "{Attempted} of {Targets} targets attempted, {Remaining} never reached; "
                + "{Enriched} works had metadata written, {Promoted} names promoted. "
                + "Attempted per store: {Attempts}. Written per store: {Writes}. "
                + "The next run re-reads the same query and resumes where this one stopped — "
                + "and because that query interleaves the stores, the untouched rows are "
                + "spread across all of them rather than being one store's entire library.",
                stopwatch.Elapsed.TotalSeconds,
                run.Attempted, targets.Count, targets.Count - run.Attempted,
                run.EnrichedWorks.Count, run.Promoted,
                Describe(run.AttemptedByProvider), Describe(run.WrittenByProvider));
            throw;
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Enrichment COMPLETE: {Promoted} of {Outstanding} names promoted "
            + "({Igdb} from IGDB, {Steam} from the Steam store, {SteamCmd} from steamcmd.net, "
            + "{EpicCatalog} from Epic's catalog service); "
            + "{Types} Steam app types read, {Parents} storefront parent pointers read, "
            + "{EpicTypes} Epic catalog items classified; "
            + "{Enriched} of {Targets} works had metadata filled in, in {Elapsed:n1}s. "
            + "Written per store: {Writes}. Routes: {Routes}.",
            run.Promoted, outstandingNames, run.FromIgdb, run.FromSteam, run.FromSteamCmd,
            run.FromEpicCatalog,
            run.TypesRead, run.ParentsRead, run.EpicClassified, run.EnrichedWorks.Count, targets.Count,
            stopwatch.Elapsed.TotalSeconds,
            Describe(run.WrittenByProvider),
            run.Routes.Count == 0
                ? "none"
                : string.Join(", ", run.Routes.OrderBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => $"{r.Key} {r.Value}")));

        return new EnrichmentReport(
            outstandingNames, run.Promoted, run.FromIgdb, stopwatch.Elapsed,
            run.EnrichedWorks.Count, run.FromSteamCmd);
    }

    /// <summary>
    /// One slice: plan, ask, write. Commits after each slice so a truncated run
    /// keeps every slice it finished.
    /// </summary>
    private async Task EnrichSliceAsync(
        IReadOnlyList<EnrichmentTarget> slice, RunState run, CancellationToken ct)
    {
        // 0. How does each store reach IGDB? Steam and GOG answer directly on
        //    their own external_game_source; Epic has no id IGDB indexes and
        //    reaches source 1 through GOG's cross-store graph instead. A target
        //    the planner has no route for is simply absent from the plan, and
        //    absent means "ask nothing, write nothing" — never "IGDB said no".
        var plan = await _lookups.PlanAsync(slice, ct);
        foreach (var (route, count) in plan.RouteCounts)
        {
            run.Routes[route] = run.Routes.GetValueOrDefault(route) + count;
        }

        // 1. IGDB first — the backbone, and the only source that carries the
        //    year/summary/cover/publisher the works columns and the soft matcher
        //    both want.
        var matches = new Dictionary<TargetKey, IgdbExternalMatch>();
        var games = new Dictionary<long, IgdbGame>();
        if (await _igdb.IsConfiguredAsync(ct))
        {
            // IsConfiguredAsync only proves credentials EXIST — it reads the
            // credential store, not the network. Minting can still fail (Twitch
            // down, credentials revoked, machine offline), and that must not
            // take the credential-free fallback down with it: the whole point
            // of step 3 is that it needs nothing from IGDB.
            try
            {
                // One batched call per external_game_source. Grouping matters:
                // a uid is only unique within its source ("1" is Fallout on GOG
                // and a plausible Steam appid), so the batches must not be
                // merged and the answers must be read back through the same
                // lookup that asked.
                foreach (var group in plan.Lookups.GroupBy(pair => pair.Value.SourceId))
                {
                    var uids = group
                        .Select(pair => pair.Value.Uid)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                    var resolved = await _igdb.ResolveByExternalIdsAsync(group.Key, uids, ct: ct);
                    foreach (var (key, lookup) in group)
                    {
                        if (resolved.TryGetValue(lookup.Uid, out var match))
                        {
                            matches[key] = match;
                        }
                    }
                }

                // 2. Second batched call for the publisher. external_games
                //    expands game.name/summary/first_release_date/cover but
                //    cannot reach involved_companies, and publisher is one of
                //    §5.3's four soft-match signals — the one that has never
                //    once fired because nothing ever fetched or stored it.
                //    Batched and cached exactly like step 1, so this is roughly
                //    one more request per slice and zero once warm.
                var igdbIds = matches.Values
                    .Select(m => m.IgdbId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToArray();

                if (igdbIds.Length > 0)
                {
                    foreach (var game in await _igdb.GetGamesAsync(igdbIds, ct: ct))
                    {
                        games[game.IgdbId] = game;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "IGDB lookup failed; continuing with the Steam store fallback.");
            }
        }
        else if (!run.IgdbUnconfiguredLogged)
        {
            // Once per run, not once per slice: the same sentence six times over
            // is how a real warning stops being read.
            run.IgdbUnconfiguredLogged = true;
            _logger.LogInformation(
                "IGDB is not configured; falling back to the Steam store for titles. "
                + "Set Igdb__ClientId / Igdb__ClientSecret to enable the metadata backbone.");
        }

        var titles = new Dictionary<TargetKey, string>();
        foreach (var (key, match) in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.Name))
            {
                titles[key] = match.Name;
            }
        }

        // 3. Steam store for the remaining NAMES only. Undocumented endpoint,
        //    soft-fails, and carries nothing the metadata columns want — so a
        //    work that has a title and only needs a year never reaches it.
        //
        //    STEAM TARGETS ONLY, and that is not a leftover from the days when
        //    this pass thought the library was all Steam: IStoreBrowseService
        //    takes appids. Handing it an Epic catalog item id or a GOG product
        //    id asks Valve about an appid that is either nonexistent or, worse,
        //    a real and unrelated game — a numeric GOG id would look exactly
        //    like an appid and come back with a confident wrong title. Epic and
        //    GOG names come from their own local files at ingest and need no
        //    fallback here.
        var unnamed = slice
            .Where(t => t.Provider == ExternalIdProviders.Steam
                        && t.NameIsProvisional
                        && !titles.ContainsKey(KeyOf(t)))
            .Select(t => t.ProviderId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var storeItems = new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);

        if (unnamed.Length > 0)
        {
            foreach (var (appId, item) in await _steamStore.GetItemsAsync(unnamed, ct: ct))
            {
                storeItems[appId] = item;
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    titles[new TargetKey(ExternalIdProviders.Steam, appId)] = item.Name;
                }
            }
        }

        // 3a. The relation facts, from the store bodies ALREADY ON DISK.
        //     `type` and `related_items` arrive with the query
        //     BuildGetItemsQuery has always sent -- there is no include_ flag
        //     for either -- so every body the cache holds already carries them
        //     and this costs no HTTP request at all. That is the whole reason
        //     the Steam half of TASK-70.10 could ship before the IGDB half:
        //     the data was already paid for.
        var relationAppIds = slice
            .Where(t => t.Provider == ExternalIdProviders.Steam && !storeItems.ContainsKey(t.ProviderId))
            .Select(t => t.ProviderId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (relationAppIds.Length > 0)
        {
            foreach (var (appId, item) in await _steamStore.GetCachedItemsAsync(relationAppIds, ct))
            {
                storeItems[appId] = item;
            }
        }

        // 3b. Epic's catalog service, for the same two jobs the Steam steps do
        //     — a name for a work that has none, and the storefront's own
        //     classification — but for Epic ids, which neither Steam endpoint can
        //     be asked about.
        var epic = await ReadEpicCatalogAsync(slice, titles, ct);
        run.EpicClassified += epic.Count;

        // 4. steamcmd.net, last. See the class remarks for why it is last and
        //    what it is worth.
        var storeParents = storeItems
            .Where(pair => pair.Value.Related.ParentAppId is not null)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        var steamCmd = await ReadSteamCmdAsync(slice, titles, storeParents, ct);
        run.TypesRead += steamCmd.Types.Count;
        run.ParentsRead += steamCmd.Parents.Count
                           + storeItems.Values.Count(i => i.Related.ParentAppId is not null);

        // 5. Write. Work and release move together, in ONE transaction each:
        //    clearing name_is_provisional is what removes the work from the
        //    name half of this query, so a crash between the two writes would
        //    strand a work named "Portal 2" beside a release still named
        //    "App 620" that no future run would ever revisit.
        foreach (var target in slice)
        {
            ct.ThrowIfCancellationRequested();

            // Counted here rather than at the top of the slice: "attempted"
            // must mean this row got as far as a write decision, or the
            // truncation line would claim credit for a slice cancelled while it
            // was still on the network.
            run.Attempted++;
            run.AttemptedByProvider[target.Provider] =
                run.AttemptedByProvider.GetValueOrDefault(target.Provider) + 1;

            var key = KeyOf(target);
            var patch = BuildPatch(
                target, key, titles, matches, games, steamCmd.Types, steamCmd.Parents, storeItems, epic);
            if (patch.IsEmpty)
            {
                continue;
            }

            bool namePromoted;
            using (var scope = _unitOfWork.Begin())
            {
                namePromoted = await _works.ApplyEnrichmentAsync(patch, ct);
                if (namePromoted)
                {
                    await _releases.UpdateNameAsync(target.ReleaseId, patch.Name!, ct);
                }

                scope.Commit();
            }

            if (namePromoted)
            {
                run.Promoted++;
                if (matches.ContainsKey(key))
                {
                    run.FromIgdb++;
                }
                else if (steamCmd.Named.Contains(target.ProviderId))
                {
                    run.FromSteamCmd++;
                }
                else if (epic.ContainsKey(target.ProviderId))
                {
                    // Epic's catalog service. Attributed explicitly rather than
                    // falling through to the Steam store, which is what the
                    // final `else` used to do — and did, for all 29 Epic names
                    // on the author's first real run, reporting them as "29 from
                    // the Steam store" for an endpoint that was never asked
                    // about a single one of them. A run's own account of where
                    // its titles came from is the thing that tells you a source
                    // has stopped working, so it has to be true.
                    run.FromEpicCatalog++;
                }
                else
                {
                    run.FromSteam++;
                }
            }

            // Counted per WORK, not per target row: a work reachable under two
            // providers yields two rows, and the second one's patch is a no-op
            // the writer's COALESCE guards absorb. Counting rows would report
            // more works enriched than the library contains.
            if (run.EnrichedWorks.Add(target.WorkId))
            {
                run.WrittenByProvider[target.Provider] =
                    run.WrittenByProvider.GetValueOrDefault(target.Provider) + 1;
            }
        }
    }

    /// <summary>
    /// Everything one run accumulates across its slices. A mutable bag rather
    /// than a returned record because the truncation path needs these numbers
    /// too, and a cancelled slice must not take the totals of the slices before
    /// it with it.
    /// </summary>
    private sealed class RunState
    {
        public RunState(IReadOnlyList<EnrichmentTarget> targets) => Targets = targets;

        public IReadOnlyList<EnrichmentTarget> Targets { get; }

        /// <summary>Targets that reached a write decision. The honest measure of how far a run got.</summary>
        public int Attempted { get; set; }

        public int Promoted { get; set; }

        public int FromIgdb { get; set; }

        public int FromSteam { get; set; }

        public int FromSteamCmd { get; set; }

        /// <summary>
        /// Names that came from Epic's catalog service — the only source that
        /// can name an Epic catalog item id. Counted separately because neither
        /// Steam endpoint can be asked about one, so a promotion credited to
        /// them would be a claim about a request that never happened.
        /// </summary>
        public int FromEpicCatalog { get; set; }

        public int TypesRead { get; set; }

        /// <summary>Parent pointers read this pass, from both Steam sources (store and PICS mirror) combined.</summary>
        public int ParentsRead { get; set; }

        /// <summary>
        /// Epic catalog items this run learned a classification for. Counted
        /// separately from <see cref="TypesRead"/> because the two come from
        /// different storefronts with incompatible vocabularies, and a run that
        /// classifies Steam apps while classifying no Epic ones is the shape of
        /// an Epic session that has quietly lapsed.
        /// </summary>
        public int EpicClassified { get; set; }

        public bool IgdbUnconfiguredLogged { get; set; }

        public HashSet<long> EnrichedWorks { get; } = [];

        public Dictionary<string, int> Routes { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Per-store attempt and write counts, so the truncation line can say
        /// WHICH stores a short run served. A run that reports "steam 40" and
        /// nothing else is the starvation bug happening again, and that is a
        /// sentence somebody can read in a log rather than a shortfall only
        /// visible by counting rows in the database months later.
        /// </summary>
        public Dictionary<string, int> AttemptedByProvider { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> WrittenByProvider { get; } = new(StringComparer.Ordinal);
    }

    private static string Describe(IReadOnlyDictionary<string, int> counts)
        => counts.Count == 0
            ? "none"
            : string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => $"{c.Key} {c.Value}"));

    private static TargetKey KeyOf(EnrichmentTarget target)
        => new(target.Provider, target.ProviderId);

    /// <summary>
    /// Step 4: asks steamcmd.net about appids the first two sources left
    /// unfinished. Adds names to <paramref name="titles"/> and returns
    /// Valve's <c>common.type</c> for every appid it answered about. Never throws.
    /// </summary>
    private async Task<SteamCmdResult> ReadSteamCmdAsync(
        IReadOnlyList<EnrichmentTarget> targets,
        Dictionary<TargetKey, string> titles,
        IReadOnlySet<string> storeParents,
        CancellationToken ct)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        var named = new HashSet<string>(StringComparer.Ordinal);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            // Steam only, and for the same reason step 3 is: this endpoint is a
            // PICS mirror keyed on appids. A GOG product id is numeric and would
            // be accepted as an appid, answering about a completely different
            // game — the one failure mode worse than answering about none.
            if (target.Provider != ExternalIdProviders.Steam)
            {
                continue;
            }

            if (!asked.Add(target.ProviderId))
            {
                continue;
            }

            var needsName = target.NameIsProvisional && !titles.ContainsKey(KeyOf(target));
            // The type is asked for when the title looks like a variant (the
            // gate migration 0006 shipped), and also when the store cache
            // carries a parent pointer with no type to explain it. The second
            // condition is the point: a storefront fact gated behind the title
            // heuristic it was meant to replace can never correct it.
            var needsType = !target.HasSteamAppType
                            && !target.NameIsProvisional
                            && (DemoConsolidation.IsVariantTitle(target.Title)
                                || storeParents.Contains(target.ProviderId));

            // Everything else gets the free read: answered if some other pass
            // already paid for this appid's body, skipped otherwise.
            var cachedOnly = !needsName && !needsType;

            AppInfoFetch fetch;
            try
            {
                fetch = await _steamCmd.GetAppInfoAsync(
                    target.ProviderId, cachedOnly: cachedOnly, ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The client soft-fails internally, so reaching here means
                // something unforeseen. It still must not abort the pass.
                _logger.LogWarning(
                    ex, "steamcmd.net lookup for appid {AppId} failed; continuing.", target.ProviderId);
                continue;
            }

            if (fetch.Outcome != AppInfoOutcome.Ok || fetch.Info is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(fetch.Info.Type))
            {
                types[target.ProviderId] = fetch.Info.Type;
            }

            // common.parent, which this client has always parsed and this
            // service has always dropped on the floor. It is the appid a demo
            // or a tool belongs to, and it is the second Steam source for the
            // parent pointer -- the one that still answers for an app the
            // store has delisted.
            if (!string.IsNullOrWhiteSpace(fetch.Info.ParentAppId))
            {
                parents[target.ProviderId] = fetch.Info.ParentAppId;
            }

            // Third in line: only offered for appids the first two sources left
            // without a title, and only while the work is still provisional.
            if (needsName && !string.IsNullOrWhiteSpace(fetch.Info.Name))
            {
                titles[KeyOf(target)] = fetch.Info.Name;
                named.Add(target.ProviderId);
            }
        }

        return new SteamCmdResult(types, parents, named);
    }

    private readonly record struct SteamCmdResult(
        IReadOnlyDictionary<string, string> Types,
        IReadOnlyDictionary<string, string> Parents,
        IReadOnlySet<string> Named);

    /// <summary>
    /// Step 3b: asks Epic's catalog service about Epic catalog item ids in this
    /// slice. Adds titles to <paramref name="titles"/> and returns the full
    /// answer per id so the writer can store categories. Never throws.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, EpicCatalogItemInfo>> ReadEpicCatalogAsync(
        IReadOnlyList<EnrichmentTarget> targets,
        Dictionary<TargetKey, string> titles,
        CancellationToken ct)
    {
        if (_epicCatalog is null)
        {
            return EmptyEpicCatalog;
        }

        var wanted = targets
            .Where(static t => t.Provider == ExternalIdProviders.Epic
                               && (t.NameIsProvisional || !t.HasEpicCategories))
            .Select(static t => t.ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (wanted.Length == 0)
        {
            return EmptyEpicCatalog;
        }

        IReadOnlyDictionary<string, EpicCatalogItemInfo> answers;
        try
        {
            answers = await _epicCatalog.GetItemsAsync(wanted, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The client soft-fails internally, so reaching here means something
            // unforeseen. It still must not abort the pass (§5.1).
            _logger.LogWarning(ex, "Epic catalog lookup failed; continuing without it.");
            return EmptyEpicCatalog;
        }

        foreach (var target in targets)
        {
            if (target.Provider != ExternalIdProviders.Epic
                || !target.NameIsProvisional
                || !answers.TryGetValue(target.ProviderId, out var item)
                || string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            // Only offered, never forced. BuildPatch still refuses to hand a
            // title to a work whose name is real, and the repository refuses
            // again — so an Epic title that came from catcache.bin cannot be
            // replaced by this, which is the "never overwrite a good local title"
            // rule holding at all three layers.
            titles.TryAdd(KeyOf(target), item.Title!);
        }

        return answers;
    }

    /// <summary>Shared empty result, so the no-Epic path allocates nothing.</summary>
    private static readonly IReadOnlyDictionary<string, EpicCatalogItemInfo> EmptyEpicCatalog =
        new Dictionary<string, EpicCatalogItemInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What this run is entitled to write for one work: everything a source
    /// supplied that the database does not already hold.
    ///
    /// <para>Columns already filled are left out of the patch entirely rather
    /// than re-sent. The repository would refuse to clobber them anyway (its
    /// COALESCE guards are the real safety net), but omitting them is what makes
    /// <see cref="WorkEnrichment.IsEmpty"/> answer "there is nothing to do here"
    /// truthfully — and an empty patch is a transaction not opened.</para>
    ///
    /// <para>Fields are taken from the <c>external_games</c> match first because
    /// that is the row IGDB published against this exact appid; <c>/games</c>
    /// fills whatever it did not carry, and is the only source of the
    /// publisher.</para>
    /// </summary>
    private static WorkEnrichment BuildPatch(
        EnrichmentTarget target,
        TargetKey key,
        IReadOnlyDictionary<TargetKey, string> titles,
        IReadOnlyDictionary<TargetKey, IgdbExternalMatch> matches,
        IReadOnlyDictionary<long, IgdbGame> games,
        IReadOnlyDictionary<string, string> appTypes,
        IReadOnlyDictionary<string, string> appParents,
        IReadOnlyDictionary<string, SteamStoreItem> storeItems,
        IReadOnlyDictionary<string, EpicCatalogItemInfo> epicCatalog)
    {
        var match = matches.GetValueOrDefault(key);
        var game = match is not null ? games.GetValueOrDefault(match.IgdbId) : null;

        // A title is only ever offered to a work still holding a placeholder.
        // A real title — from an earlier run, from the store, or edited by the
        // user — is never overwritten, which is the failure that would rename a
        // library back to appids.
        var name = target.NameIsProvisional ? titles.GetValueOrDefault(key) : null;

        var isSteam = key.Provider == ExternalIdProviders.Steam;
        var storeItem = isSteam ? storeItems.GetValueOrDefault(target.ProviderId) : null;

        return new WorkEnrichment(
            target.WorkId,
            Name: name,
            IgdbId: target.HasIgdbId ? null : match?.IgdbId,
            FirstReleaseYear: target.HasFirstReleaseYear
                ? null
                : match?.FirstReleaseYear ?? game?.FirstReleaseYear,
            Summary: target.HasSummary ? null : Prefer(match?.Summary, game?.Summary),
            CoverUrl: target.HasCoverUrl ? null : Prefer(match?.CoverUrl, game?.CoverUrl),
            Publisher: target.HasPublisher ? null : PrimaryPublisher(game),

            // Steam's own classification, so only ever read for a Steam target.
            // `appTypes` is keyed by appid, and a GOG product id like "1" is a
            // perfectly good appid string — without this guard a GOG work would
            // inherit whatever Valve says about an unrelated app.
            SteamAppType: target.HasSteamAppType || key.Provider != ExternalIdProviders.Steam
                ? null
                : appTypes.GetValueOrDefault(target.ProviderId),

            // Epic's own classification, and guarded the same way and for the
            // same reason: `epicCatalog` is keyed by catalog item id, and while a
            // Steam appid could not plausibly collide with a 32-hex catalog id,
            // the guard is what makes the intent local rather than a fact about
            // today's id shapes.
            //
            // CategoriesValue is null when Epic sent no categories, so an entry
            // that answered with a title and nothing else fills the name and
            // leaves the classification unknown — which is correct, and which the
            // repository's COALESCE would enforce regardless.
            EpicCategories: target.HasEpicCategories || key.Provider != ExternalIdProviders.Epic
                ? null
                : epicCatalog.GetValueOrDefault(target.ProviderId)?.CategoriesValue)
        {
            // Migration 0022. Steam ids only, for the reason SteamAppType
            // states: both dictionaries are keyed by appid and a GOG product id
            // is a perfectly good appid string.
            SteamStoreType = isSteam ? storeItem?.StoreType : null,

            // Two Steam sources for one column. The store's related_items wins
            // because it is the endpoint that still names a playtest's parent;
            // the PICS mirror fills in for an app the store has delisted, which
            // is precisely the population the store cannot answer about.
            SteamParentAppId = isSteam
                ? storeItem?.Related.ParentAppId ?? appParents.GetValueOrDefault(target.ProviderId)
                : null,

            // IGDB's relation fields ride the /games response the publisher
            // already needed, so they add no request.
            IgdbGameType = game?.GameType,
            IgdbParentId = game?.ParentGameId,
            IgdbVersionParentId = game?.VersionParentId,
        };
    }

    private static string? Prefer(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    /// <summary>
    /// Reduces IGDB's publisher list to one deterministic name (ordinal-first)
    /// for migration 0005's single publisher column.
    /// </summary>
    internal static string? PrimaryPublisher(IgdbGame? game)
    {
        if (game is null)
        {
            return null;
        }

        string? best = null;
        foreach (var publisher in game.Publishers)
        {
            if (string.IsNullOrWhiteSpace(publisher))
            {
                continue;
            }

            var candidate = publisher.Trim();
            if (best is null || string.Compare(candidate, best, StringComparison.OrdinalIgnoreCase) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }
}

/// <summary>Results of one enrichment run. A truncated run rethrows rather than returning this.</summary>
public sealed record EnrichmentReport(
    int Outstanding,
    int Promoted,
    int FromIgdb,
    TimeSpan Elapsed,
    int MetadataFilled = 0,
    int FromSteamCmd = 0);
