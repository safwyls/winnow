using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Hoard.Core.Queries;
using Hoard.Enrich.GamesDb;
using Hoard.Enrich.GamesDb.Model;
using Hoard.Enrich.Igdb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.App.Services;

/// <summary>Identifies one enrichment target by the external id it was found under.</summary>
/// <param name="Provider">An <see cref="ExternalIdProviders"/> value.</param>
/// <param name="ProviderId">That store's id, as stored in <c>external_ids</c>.</param>
public readonly record struct TargetKey(string Provider, string ProviderId);

/// <summary>
/// How one target reaches IGDB: which <c>external_game_source</c> to ask, with
/// which id, and by which route it was worked out.
/// </summary>
/// <param name="SourceId">IGDB's <c>external_game_source</c> id.</param>
/// <param name="Uid">The id to send under that source.</param>
/// <param name="Route">Human-readable provenance, for the run's log line only.</param>
public sealed record IgdbLookup(int SourceId, string Uid, string Route);

/// <summary>
/// The plan for one enrichment run: every target that has a way to reach IGDB,
/// and how.
/// </summary>
/// <param name="Lookups">Target → the IGDB question to ask about it.</param>
/// <param name="RouteCounts">How many targets each route accounted for. Reporting only.</param>
public sealed record EnrichmentLookupPlan(
    IReadOnlyDictionary<TargetKey, IgdbLookup> Lookups,
    IReadOnlyDictionary<string, int> RouteCounts)
{
    public static readonly EnrichmentLookupPlan Empty = new(
        new Dictionary<TargetKey, IgdbLookup>(),
        new Dictionary<string, int>(StringComparer.Ordinal));
}

/// <summary>
/// Works out, per store, how a release reaches IGDB — the step that did not
/// exist while enrichment only ever asked about Steam.
///
/// <para><b>The bug this class is the answer to.</b> The enrichment pass asked
/// the repository for Steam targets and asked IGDB with Steam's
/// <c>external_game_source</c>, both hardcoded. Every Epic and GOG release in
/// the library was therefore invisible to it — measured on the author's real
/// library as 67 Epic and 14 GOG works with zero <c>igdb_id</c>, zero covers,
/// zero years and zero summaries between them. Not a low number: exactly zero,
/// which is what a question nobody asked looks like.</para>
///
/// <para><b>Three routes, and the reason each store gets the one it gets</b>
/// (all measured, see <c>docs/spikes/epic-gog-local-files.md</c> section 19–20
/// and re-verified live against the author's library while fixing this):</para>
/// <list type="bullet">
///   <item><b>Steam → source 1.</b> The original path, 865 of 946 matched.</item>
///   <item><b>GOG → source 5.</b> IGDB stores the bare GOG product id verbatim,
///     so this is the same hard join with a different source id and no other
///     machinery at all: 13 of 14 matched on the first try. The single miss is
///     "The Witcher 3 REDkit", a modding toolkit rather than a game.</item>
///   <item><b>Epic → gamesdb → Steam appid → source 1.</b> Epic has no direct
///     route. IGDB's source-26 uids are Epic store <i>offer</i> ids and CMS
///     <i>page</i> ids; the launcher writes <c>CatalogItemId</c>, a third id
///     space. Measured 0 of 67. So the Epic id is translated to its
///     <c>AppName</c> from the local catalog, resolved through GOG's
///     cross-store identity graph, and the Steam appid that comes back goes
///     down the route that already works for 946 titles: 62 of 67 resolved.</item>
/// </list>
///
/// <para><b>Why the Epic hop is not "just fuzzy matching with extra steps".</b>
/// Every link is an exact identifier published by the service that owns it —
/// <c>catalogItemId → AppName</c> from Epic's own catalog file,
/// <c>epic/AppName → game_id → steam/appid</c> from GOG's graph,
/// <c>appid → IGDB game</c> from <c>external_games</c>. No title is normalised
/// and no similarity is scored, which is what §5.3's non-negotiable is about.
/// The one thing it inherits is that gamesdb resolves <i>games</i>, not
/// <i>editions</i>: an Epic "Gold Edition" can land on the base game's IGDB
/// record. That is the right granularity for the Work columns this pass writes
/// (title, year, summary, cover) and the wrong one for a Release, so this class
/// produces metadata lookups and never a merge — §9's pitfall 5 stands
/// untouched.</para>
///
/// <para><b>Nothing here writes anything, and silence is never an answer.</b> A
/// target with no route is simply absent from <see cref="EnrichmentLookupPlan.Lookups"/>,
/// and the caller must leave its columns exactly as it found them. Epic with no
/// launcher on disk, gamesdb unreachable, and a title that genuinely has no
/// cross-store twin are all indistinguishable here — deliberately, because the
/// only safe reading of all three is "learned nothing this run".</para>
/// </summary>
public sealed class EnrichmentLookupPlanner
{
    /// <summary>Route labels. Reporting only — nothing branches on these.</summary>
    public const string SteamDirectRoute = "steam→igdb";
    public const string GogDirectRoute = "gog→igdb";
    public const string EpicBridgedRoute = "epic→gamesdb→steam→igdb";

    private readonly IGameIdentityGraph? _identity;
    private readonly IReadOnlyList<IStoreArtifactAliasSource> _aliasSources;
    private readonly IgdbOptions _igdbOptions;
    private readonly ILogger<EnrichmentLookupPlanner> _logger;

    public EnrichmentLookupPlanner(
        IgdbOptions igdbOptions,
        IEnumerable<IStoreArtifactAliasSource>? aliasSources = null,
        IGameIdentityGraph? identity = null,
        ILogger<EnrichmentLookupPlanner>? logger = null)
    {
        _igdbOptions = igdbOptions;
        _aliasSources = aliasSources?.ToArray() ?? [];
        _identity = identity;
        _logger = logger ?? NullLogger<EnrichmentLookupPlanner>.Instance;
    }

    public async Task<EnrichmentLookupPlan> PlanAsync(
        IReadOnlyList<EnrichmentTarget> targets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            return EnrichmentLookupPlan.Empty;
        }

        var lookups = new Dictionary<TargetKey, IgdbLookup>();
        var routes = new Dictionary<string, int>(StringComparer.Ordinal);

        // Direct routes first. They cost nothing but a dictionary write, and
        // they are the only ones that work with no network at all beyond IGDB
        // itself.
        var epic = new List<EnrichmentTarget>();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            if (string.Equals(target.Provider, ExternalIdProviders.Epic, StringComparison.Ordinal))
            {
                epic.Add(target);
                continue;
            }

            if (_igdbOptions.ExternalGameSourceIdFor(target.Provider) is not { } sourceId)
            {
                continue;
            }

            var route = target.Provider == ExternalIdProviders.Gog ? GogDirectRoute : SteamDirectRoute;
            Record(lookups, routes, target, new IgdbLookup(sourceId, target.ProviderId, route));
        }

        if (epic.Count > 0)
        {
            await PlanEpicAsync(epic, lookups, routes, ct).ConfigureAwait(false);
        }

        return new EnrichmentLookupPlan(lookups, routes);
    }

    /// <summary>
    /// The two-hop Epic route. Both hops are optional and both fail soft: no
    /// alias source (no launcher on this machine), no identity graph
    /// (unregistered), a title with no <c>AppName</c>, and a title with no Steam
    /// twin all end the same way — the target has no lookup, and the caller
    /// leaves its row alone.
    /// </summary>
    private async Task PlanEpicAsync(
        List<EnrichmentTarget> targets,
        Dictionary<TargetKey, IgdbLookup> lookups,
        Dictionary<string, int> routes,
        CancellationToken ct)
    {
        if (_identity is null)
        {
            _logger.LogDebug(
                "No identity graph registered; {Count} Epic works keep their metadata as-is.", targets.Count);
            return;
        }

        var aliases = await ReadEpicAliasesAsync(ct).ConfigureAwait(false);
        if (aliases.Count == 0)
        {
            // Distinct from "these titles have no alias": Epic's catalog is a
            // local file, and its absence says nothing about the library.
            _logger.LogInformation(
                "No Epic artifact aliases available (launcher not installed, or its catalog unreadable); "
                + "{Count} Epic works cannot be looked up this run and keep whatever metadata they have.",
                targets.Count);
            return;
        }

        var steamSource = _igdbOptions.SteamExternalGameSourceId;
        var gogSource = _igdbOptions.GogExternalGameSourceId;
        var bridged = 0;
        var noAlias = 0;
        var noTwin = 0;

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            if (!aliases.TryGetValue(target.ProviderId, out var appName))
            {
                noAlias++;
                continue;
            }

            GamesDbGame? game;
            try
            {
                game = await _identity
                    .ResolveAsync(GamesDbPlatforms.Epic, appName, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The client soft-fails internally, so reaching here means
                // something unforeseen. §5.1: it still must not abort the pass.
                _logger.LogWarning(ex, "Identity lookup for Epic artifact failed; continuing.");
                continue;
            }

            if (game is null)
            {
                noTwin++;
                continue;
            }

            // Steam first because that is the route with 946 titles of evidence
            // behind it; GOG second for the handful of Epic titles that reached
            // GOG but never Steam. An Epic exclusive has neither, which is a
            // fact about the game and not a failure.
            //
            // Numeric only, and that is not defensive programming for its own
            // sake: the graph is crowd-shaped and carries junk. Fez lists BOTH
            // steam/224760 and steam/steam_224760 — the release key pasted into
            // the id field — with no guaranteed order between them. IGDB would
            // simply miss on the malformed one, so the cost of taking it is a
            // title that stays blank for no visible reason.
            if (FirstNumeric(game.IdsOn(GamesDbPlatforms.Steam)) is { } appId)
            {
                Record(lookups, routes, target, new IgdbLookup(steamSource, appId, EpicBridgedRoute));
                bridged++;
            }
            else if (FirstNumeric(game.IdsOn(GamesDbPlatforms.Gog)) is { } gogId)
            {
                Record(lookups, routes, target, new IgdbLookup(gogSource, gogId, EpicBridgedRoute));
                bridged++;
            }
            else
            {
                noTwin++;
            }
        }

        _logger.LogInformation(
            "Epic identity: {Bridged} of {Total} works bridged to a store id IGDB indexes "
            + "({NoAlias} had no AppName on disk, {NoTwin} have no cross-store release).",
            bridged, targets.Count, noAlias, noTwin);
    }

    /// <summary>
    /// The merged Epic alias map. Several sources may be registered; the first
    /// to supply an id wins, and one throwing must not silence the rest.
    /// </summary>
    private async Task<Dictionary<string, string>> ReadEpicAliasesAsync(CancellationToken ct)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _aliasSources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                foreach (var (id, alias) in await source
                             .GetAliasesAsync(ExternalIdProviders.Epic, ct).ConfigureAwait(false))
                {
                    merged.TryAdd(id, alias);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Alias source {Source} failed; continuing with the others.", source.GetType().Name);
            }
        }

        return merged;
    }

    /// <summary>
    /// The first all-digits id, or null. Both stores IGDB indexes use numeric
    /// ids — Steam appids and GOG product ids alike — so this is a shape test
    /// with real content, not a guess.
    /// </summary>
    private static string? FirstNumeric(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            if (id.Length > 0 && id.All(char.IsAsciiDigit))
            {
                return id;
            }
        }

        return null;
    }

    private static void Record(
        Dictionary<TargetKey, IgdbLookup> lookups,
        Dictionary<string, int> routes,
        EnrichmentTarget target,
        IgdbLookup lookup)
    {
        if (!lookups.TryAdd(new TargetKey(target.Provider, target.ProviderId), lookup))
        {
            return;
        }

        routes[lookup.Route] = routes.GetValueOrDefault(lookup.Route) + 1;
    }
}
