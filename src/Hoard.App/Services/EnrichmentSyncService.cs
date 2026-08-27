using System.Diagnostics;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Model;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Updates;
using Hoard.Enrich.Updates.Model;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

/// <summary>
/// Fills in what the local Steam files could not know: real titles, and the
/// metadata §6 gives <c>works</c> columns for — <c>igdb_id</c>,
/// <c>first_release_year</c>, <c>summary</c>, <c>cover_url</c> and (migration
/// 0005) <c>publisher</c>.
///
/// <para><b>Three sources, deliberately ordered.</b> IGDB is the designed
/// metadata backbone (§4.4) and wins any disagreement — it carries the
/// canonical title plus the year, summary, cover and publisher. Steam's keyless
/// store endpoint fills whatever IGDB does not answer for, and covers the case
/// where no IGDB credentials are configured at all, so the library is never
/// stuck showing appids because of a missing API key. The Steam endpoint is
/// asked about <b>titles only</b>: it is undocumented, it is strictly a
/// fallback, and it has nothing to say about the columns the matcher needs.</para>
///
/// <para><b>Third, and last: api.steamcmd.net.</b> The unofficial PICS mirror
/// this project already polls for build signals carries <c>common.name</c>
/// beside the <c>depots</c> block, and it names appids the first two refuse.
/// Measured on the author's library: of 18 works still showing <c>App
/// &lt;appid&gt;</c> after IGDB and <c>IStoreBrowseService/GetItems</c>, it
/// names <b>11</b> — including 4028270 "Everwind Demo", 2614110 "Enshrouded
/// Demo" and 202480 "Skyrim Creation Kit". The remaining seven answer with no
/// <c>common</c> block at all and cannot be named without a Steam Web API key.
/// It is LAST because of what it is: unofficial,
/// unaffiliated with Valve, volunteer-run and explicitly without an SLA (§4.4
/// keeps IGDB the backbone; <c>docs/spikes/update-signals.md</c> §1 records the
/// terms). It is asked one appid at a time, only about works that still have no
/// name, and its answers are cached for
/// <see cref="UpdateSignalOptions.AppInfoCacheTtl"/> in the same
/// <c>metadata_cache</c> row the build poller uses — so a name and a build
/// signal for one appid cost one request between them, not two.</para>
///
/// <para>The same response also carries <c>common.type</c>, Valve's own
/// classification (<c>Game</c>, <c>Demo</c>, <c>Tool</c>), which migration 0006
/// stores and <see cref="DemoConsolidation"/> reads as its first gate. That is
/// why a handful of already-named works reach step 4 as well: an entry whose
/// title looks like a handout is worth one request to learn what Steam says it
/// actually is.</para>
///
/// <para><b>Two IGDB calls, not one.</b> <c>external_games</c> is the
/// high-precision Steam-appid join (§4.4) and its expanded <c>game.*</c> fields
/// carry name, year, summary and cover — but not publisher, which lives on
/// <c>involved_companies</c> and is only reachable through <c>/games</c>. Both
/// batch 400 ids per request, so a 616-game library costs about four requests
/// on a cold cache and none on a warm one.</para>
///
/// <para><b>Why metadata matters beyond display.</b> §5.3 scores a soft match on
/// title, release year, publisher and cover hash. Until this pass stored the
/// year and the publisher, two of those four signals could never fire on a
/// library-internal pair, and every candidate in the merge queue was scored on
/// title similarity alone — the one thing §5.3 says must never be trusted by
/// itself. Persisting the metadata IGDB already returns is therefore a precision
/// change, not a cosmetic one.</para>
///
/// <para><b>Every store, not just Steam.</b> This pass spent its life asking
/// the repository for <c>steam</c> targets and asking IGDB with Steam's
/// <c>external_game_source</c>. The consequence was measurable and total: on the
/// author's library, 67 Epic works and 14 GOG works had zero <c>igdb_id</c>,
/// zero covers, zero years and zero summaries between them, while 946 Steam
/// works had ~865 of each. Exactly zero rather than a small number is the
/// signature of a population no query ever selected. The target query now
/// returns every store provider and
/// <see cref="EnrichmentLookupPlanner"/> works out how each one reaches IGDB —
/// GOG directly on source 5, Epic through GOG's cross-store identity graph.
/// Steps 3 and 4 below stay Steam-only, because a Steam appid is the only thing
/// either endpoint can be asked about.</para>
///
/// <para>§5.1: this composes ingest-adjacent sources and the repositories, and
/// the UI never calls it — Program sequences it, the view models read the
/// database afterwards.</para>
/// </summary>
public sealed class EnrichmentSyncService
{
    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly IBuildInfoClient _steamCmd;
    private readonly EnrichmentLookupPlanner _lookups;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ILogger<EnrichmentSyncService> _logger;

    public EnrichmentSyncService(
        IWorkRepository works,
        IReleaseRepository releases,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        IBuildInfoClient steamCmd,
        EnrichmentLookupPlanner lookups,
        IUnitOfWorkFactory unitOfWork,
        ILogger<EnrichmentSyncService> logger)
    {
        _works = works;
        _releases = releases;
        _igdb = igdb;
        _steamStore = steamStore;
        _steamCmd = steamCmd;
        _lookups = lookups;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Names every work still carrying a placeholder, and back-fills the
    /// metadata columns of every work missing any of them.
    ///
    /// <para><b>Why the target set is wider than "provisional".</b> On the
    /// author's real library 616 works already have real names and not one has a
    /// year, summary, cover or publisher — they were named by a build that threw
    /// the rest of the IGDB answer away. Keyed on <c>name_is_provisional</c>
    /// alone this pass would look at nothing and back-fill nothing, forever. So
    /// the query asks the wider question: which works are missing <i>anything</i>
    /// (<see cref="IWorkRepository.GetEnrichmentTargetsAsync"/>).</para>
    ///
    /// <para><b>Idempotent, and free once warm.</b> Three separate mechanisms,
    /// each covering a different cost:</para>
    /// <list type="bullet">
    ///   <item><b>No re-fetch.</b> Both IGDB calls read <c>metadata_cache</c>
    ///     first, with a 30-day TTL and cached misses recorded as such, so a
    ///     second launch spends no requests — not even on the appids IGDB has
    ///     never heard of. This, not a stored watermark, is what keeps a
    ///     re-run off the network; a watermark would also permanently suppress
    ///     works IGDB only learns about later.</item>
    ///   <item><b>No re-write.</b> A work whose every column is filled is not
    ///     returned by the query at all, and a target the sources say nothing
    ///     new about never opens a transaction.</item>
    ///   <item><b>No fallback flood.</b> The Steam store is asked only about
    ///     works that still need a <i>name</i>. Without that split, a
    ///     credential-free machine would hit an undocumented endpoint 616 times
    ///     on every launch to re-learn titles it already has. steamcmd.net is
    ///     held to a stricter version of the same rule — see
    ///     <c>ReadSteamCmdAsync</c> — because it is a volunteer service.
    ///     The handful of appids it can never answer for (the
    ///     <c>_missing_token</c> set) stay in the target list forever, and it is
    ///     the client's own cached miss, not this query, that keeps them off the
    ///     wire: one request per appid per <c>AppInfoCacheTtl</c>.</item>
    /// </list>
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

        // 0. How does each store reach IGDB? Steam and GOG answer directly on
        //    their own external_game_source; Epic has no id IGDB indexes and
        //    reaches source 1 through GOG's cross-store graph instead. A target
        //    the planner has no route for is simply absent from the plan, and
        //    absent means "ask nothing, write nothing" — never "IGDB said no".
        var plan = await _lookups.PlanAsync(targets, ct);

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
                //    two more requests for a 616-game library and zero
                //    thereafter.
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
        else
        {
            _logger.LogInformation(
                "IGDB is not configured; falling back to the Steam store for titles. "
                + "Set Igdb__ClientId / Igdb__ClientSecret to enable the metadata backbone.");
        }

        var fromIgdb = 0;
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
        var unnamed = targets
            .Where(t => t.Provider == ExternalIdProviders.Steam
                        && t.NameIsProvisional
                        && !titles.ContainsKey(KeyOf(t)))
            .Select(t => t.ProviderId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var fromSteam = 0;
        if (unnamed.Length > 0)
        {
            foreach (var (appId, item) in await _steamStore.GetItemsAsync(unnamed, ct: ct))
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    titles[new TargetKey(ExternalIdProviders.Steam, appId)] = item.Name;
                }
            }
        }

        // 4. steamcmd.net, last. See the class remarks for why it is last and
        //    what it is worth.
        var fromSteamCmd = 0;
        var steamCmd = await ReadSteamCmdAsync(targets, titles, ct);

        // 5. Write. Work and release move together, in ONE transaction each:
        //    clearing name_is_provisional is what removes the work from the
        //    name half of this query, so a crash between the two writes would
        //    strand a work named "Portal 2" beside a release still named
        //    "App 620" that no future run would ever revisit.
        var promoted = 0;
        var enrichedWorks = new HashSet<long>();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            var key = KeyOf(target);
            var patch = BuildPatch(target, key, titles, matches, games, steamCmd.Types);
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
                promoted++;
                if (matches.ContainsKey(key))
                {
                    fromIgdb++;
                }
                else if (steamCmd.Named.Contains(target.ProviderId))
                {
                    fromSteamCmd++;
                }
                else
                {
                    fromSteam++;
                }
            }

            // Counted per WORK, not per target row: a work reachable under two
            // providers yields two rows, and the second one's patch is a no-op
            // the writer's COALESCE guards absorb. Counting rows would report
            // more works enriched than the library contains.
            enrichedWorks.Add(target.WorkId);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Enrichment: {Promoted} of {Outstanding} names promoted "
            + "({Igdb} from IGDB, {Steam} from the Steam store, {SteamCmd} from steamcmd.net); "
            + "{Types} app types read; "
            + "{Enriched} of {Targets} works had metadata filled in, in {Elapsed:n1}s. "
            + "Routes: {Routes}.",
            promoted, outstandingNames, fromIgdb, fromSteam, fromSteamCmd, steamCmd.Types.Count,
            enrichedWorks.Count, targets.Count, stopwatch.Elapsed.TotalSeconds,
            plan.RouteCounts.Count == 0
                ? "none"
                : string.Join(", ", plan.RouteCounts.OrderBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => $"{r.Key} {r.Value}")));

        return new EnrichmentReport(
            outstandingNames, promoted, fromIgdb, stopwatch.Elapsed, enrichedWorks.Count, fromSteamCmd);
    }

    private static TargetKey KeyOf(EnrichmentTarget target)
        => new(target.Provider, target.ProviderId);

    /// <summary>
    /// Step 4: what api.steamcmd.net can say about the appids the first two
    /// sources left unfinished. Adds any name it supplies to
    /// <paramref name="titles"/> and returns Valve's <c>common.type</c> for
    /// every appid it answered about.
    ///
    /// <para><b>Two disjoint reasons to ask, and both are narrow.</b> A work
    /// still carrying a placeholder is asked outright — that is the name
    /// fallback, and it is bounded by how many appids IGDB and the store both
    /// missed (18 on the author's 616-game library). A work that already has a
    /// name is asked only when its title reads like a handout
    /// (<see cref="DemoConsolidation.IsVariantTitle"/>) and no type is stored,
    /// because that is the only shape whose type can change what the library
    /// shows. Everything else is offered the cache and nothing more: if the
    /// update poller already fetched that appid the type is free, and if it did
    /// not, no request is made.</para>
    ///
    /// <para><b>Never throws.</b> Every outcome other than a name is a
    /// no-op — a dead volunteer service degrades to "the work keeps its
    /// placeholder and is asked again next launch", exactly as an IGDB failure
    /// does, and never to an exception that would take the write phase down with
    /// it (§5.1: enrichment must never block a user-facing path).</para>
    /// </summary>
    private async Task<SteamCmdResult> ReadSteamCmdAsync(
        IReadOnlyList<EnrichmentTarget> targets,
        Dictionary<TargetKey, string> titles,
        CancellationToken ct)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
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
            var needsType = !target.HasSteamAppType
                            && !target.NameIsProvisional
                            && DemoConsolidation.IsVariantTitle(target.Title);

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

            // Third in line: only offered for appids the first two sources left
            // without a title, and only while the work is still provisional.
            if (needsName && !string.IsNullOrWhiteSpace(fetch.Info.Name))
            {
                titles[KeyOf(target)] = fetch.Info.Name;
                named.Add(target.ProviderId);
            }
        }

        return new SteamCmdResult(types, named);
    }

    private readonly record struct SteamCmdResult(
        IReadOnlyDictionary<string, string> Types, IReadOnlySet<string> Named);

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
        IReadOnlyDictionary<string, string> appTypes)
    {
        var match = matches.GetValueOrDefault(key);
        var game = match is not null ? games.GetValueOrDefault(match.IgdbId) : null;

        // A title is only ever offered to a work still holding a placeholder.
        // A real title — from an earlier run, from the store, or edited by the
        // user — is never overwritten, which is the failure that would rename a
        // library back to appids.
        var name = target.NameIsProvisional ? titles.GetValueOrDefault(key) : null;

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
                : appTypes.GetValueOrDefault(target.ProviderId));
    }

    private static string? Prefer(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    /// <summary>
    /// Reduces IGDB's publisher list to the one name migration 0005 stores.
    ///
    /// <para><b>Ordinal-first, not "whatever IGDB listed first".</b> The pair
    /// this signal exists to score is two library rows for the SAME game under
    /// different store ids. Both resolve to the same IGDB game and therefore see
    /// the same set of publishers, so any order-independent choice makes the two
    /// sides agree and the signal fire. IGDB's own row order is not guaranteed
    /// stable between fetches, so picking the first element would let a
    /// re-fetch months apart write two different names for one game and turn a
    /// corroborating signal into a -0.15 mismatch penalty. Multi-publisher games
    /// are the norm rather than the exception — The Witcher 3 lists four, in
    /// regional order — so this is not a hypothetical.</para>
    ///
    /// <para>Comparison is ordinal and case-insensitive so the pick does not
    /// drift with the machine's culture; the stored string keeps IGDB's own
    /// casing.</para>
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

/// <param name="Outstanding">Works carrying a placeholder name when the run began.</param>
/// <param name="Promoted">Works given a real title this run.</param>
/// <param name="FromIgdb">How many of the promotions came from IGDB rather than the fallback.</param>
/// <param name="Elapsed">Wall-clock time for the whole pass.</param>
/// <param name="MetadataFilled">
/// Works that had at least one column written — the back-fill count. Larger than
/// <paramref name="Promoted"/> on any library whose titles were already real,
/// which after the first run is every library.
/// </param>
/// <param name="FromSteamCmd">
/// How many promotions came from api.steamcmd.net, the third and last source.
/// Reported separately from <paramref name="FromIgdb"/> because it is the one
/// name source with no SLA: a number that starts climbing while the other two
/// flatline is the signal that the library has come to depend on a volunteer
/// service.
/// </param>
public sealed record EnrichmentReport(
    int Outstanding,
    int Promoted,
    int FromIgdb,
    TimeSpan Elapsed,
    int MetadataFilled = 0,
    int FromSteamCmd = 0);
