using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.Igdb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.App.Services;

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
/// Plans how each store's releases reach IGDB for enrichment.
/// Steam and GOG use direct <c>external_game_source</c> lookups.
/// Epic has no IGDB-indexed id, so it bridges through GOG's cross-store
/// identity graph to obtain a Steam appid. Targets with no route are
/// omitted from the plan and left unchanged by the caller.
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

    /// <summary>Plans the two-hop Epic route (Epic alias -> gamesdb -> Steam/GOG appid). Both hops fail soft.</summary>
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

    /// <summary>Merges Epic aliases from all registered sources. First value per id wins; individual source failures are swallowed.</summary>
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

    /// <summary>Returns the first all-digits id, or null. Steam appids and GOG product ids are both numeric.</summary>
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
