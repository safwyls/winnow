using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// Materialises filter-panel descriptors (migration 0007) from cached IGDB and
/// Steam metadata. IGDB supplies work-level facets (genres, themes, perspectives);
/// Steam supplies release-level facets (tags, categories). Game modes come from
/// both, normalised onto <see cref="GameModes"/>. Cache-first, so warm re-runs
/// cost zero requests.
/// </summary>
public sealed class FacetSyncService
{
    private readonly ILibraryQueryRepository _libraryQueries;
    private readonly IFacetRepository _facets;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly ILogger<FacetSyncService> _logger;

    public FacetSyncService(
        ILibraryQueryRepository libraryQueries,
        IFacetRepository facets,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        ILogger<FacetSyncService> logger)
    {
        _libraryQueries = libraryQueries;
        _facets = facets;
        _igdb = igdb;
        _steamStore = steamStore;
        _logger = logger;
    }

    /// <summary>Re-derives descriptors from caches and stores what changed. Idempotent: a warm re-run touches no rows.</summary>
    public async Task<FacetSyncReport> SyncAsync(CancellationToken ct = default)
    {
        var targets = await _libraryQueries.GetFacetTargetsAsync(ct);
        if (targets.Count == 0)
        {
            return FacetSyncReport.Empty;
        }

        var igdbGames = await ReadIgdbAsync(targets, ct);
        var steam = await ReadSteamAsync(targets, ct);

        var worksWritten = 0;
        var releasesWritten = 0;
        var rowsWritten = 0;

        // A Work with two releases appears twice in `targets`; its work-level
        // descriptors are identical both times, so writing them twice would be
        // one wasted comparison per extra release. Cheap to avoid, and it keeps
        // the report's "works written" honest.
        var workDone = new HashSet<long>();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            if (target.IgdbId is { } igdbId
                && igdbGames.TryGetValue(igdbId, out var game)
                && workDone.Add(target.WorkId))
            {
                var written = await _facets.SetWorkFacetsAsync(
                    target.WorkId, WorkFacets(game), ct);
                if (written > 0)
                {
                    worksWritten++;
                    rowsWritten += written;
                }
            }

            if (target.SteamAppId is { } appId
                && steam.Items.TryGetValue(appId, out var item))
            {
                var written = await _facets.SetReleaseFacetsAsync(
                    target.ReleaseId, ReleaseFacets(item, steam), ct);
                if (written > 0)
                {
                    releasesWritten++;
                    rowsWritten += written;
                }
            }
        }

        var report = new FacetSyncReport(
            ReleasesExamined: targets.Count,
            IgdbGamesRead: igdbGames.Count,
            SteamItemsRead: steam.Items.Count,
            WorksWritten: worksWritten,
            ReleasesWritten: releasesWritten,
            RowsWritten: rowsWritten);

        _logger.LogInformation(
            "Facet sync: {Releases} releases examined, {Igdb} IGDB games and {Steam} store items read, "
            + "{Works} works and {Written} releases updated ({Rows} rows).",
            report.ReleasesExamined, report.IgdbGamesRead, report.SteamItemsRead,
            report.WorksWritten, report.ReleasesWritten, report.RowsWritten);

        return report;
    }

    /// <summary>Loads IGDB games from cache. Returns empty on any failure so the Steam half still runs.</summary>
    private async Task<IReadOnlyDictionary<long, IgdbGame>> ReadIgdbAsync(
        IReadOnlyList<FacetTarget> targets, CancellationToken ct)
    {
        var ids = targets
            .Select(t => t.IgdbId)
            .OfType<long>()
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<long, IgdbGame>();
        }

        try
        {
            var games = await _igdb.GetGamesAsync(ids, ct: ct);
            return games.ToDictionary(g => g.IgdbId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "IGDB unavailable for facet sync; genres and themes stay as they were.");
            return new Dictionary<long, IgdbGame>();
        }
    }

    /// <summary>Loads Steam store items and both vocabularies (tags, categories). Skips the Steam half entirely if either vocabulary is empty.</summary>
    private async Task<SteamFacetSource> ReadSteamAsync(
        IReadOnlyList<FacetTarget> targets, CancellationToken ct)
    {
        var appIds = targets
            .Select(t => t.SteamAppId)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (appIds.Length == 0)
        {
            return SteamFacetSource.None;
        }

        var tags = await _steamStore.GetTagListAsync(ct: ct);
        var categories = await _steamStore.GetStoreCategoriesAsync(ct: ct);

        if (tags.Names.Count == 0 || categories.Names.Count == 0)
        {
            _logger.LogInformation(
                "Steam tag or category vocabulary unavailable; store descriptors are left untouched "
                + "this run rather than written with unresolvable ids.");
            return SteamFacetSource.None;
        }

        var items = await _steamStore.GetItemsAsync(appIds, ct: ct);
        return new SteamFacetSource(items, tags, categories);
    }

    /// <summary>Extracts work-level descriptors from an IGDB game. Unknown game modes are dropped (closed vocabulary).</summary>
    private static List<FacetAssignment> WorkFacets(IgdbGame game)
    {
        var genres = OrEmpty(game.Genres);
        var themes = OrEmpty(game.Themes);
        var perspectives = OrEmpty(game.PlayerPerspectives);
        var modes = OrEmpty(game.GameModes);

        var facets = new List<FacetAssignment>(
            genres.Count + themes.Count + perspectives.Count + modes.Count);

        facets.AddRange(genres.Select(g => new FacetAssignment(FacetKinds.Genre, g)));
        facets.AddRange(themes.Select(t => new FacetAssignment(FacetKinds.Theme, t)));
        facets.AddRange(perspectives.Select(p => new FacetAssignment(FacetKinds.PlayerPerspective, p)));

        foreach (var mode in modes)
        {
            if (GameModes.FromIgdbName(mode) is { } slug)
            {
                facets.Add(GameModes.Assignment(slug));
            }
        }

        return facets;
    }

    /// <summary>Returns the list or empty. The nullable parameter handles cached payloads written before the field existed.</summary>
    private static IReadOnlyList<string> OrEmpty(IReadOnlyList<string>? values) => values ?? [];

    /// <summary>Extracts release-level descriptors from a Steam store item. Tags keep their rank; player categories map to game modes.</summary>
    private static List<FacetAssignment> ReleaseFacets(SteamStoreItem item, SteamFacetSource source)
    {
        var facets = new List<FacetAssignment>();

        foreach (var tag in item.Tags)
        {
            if (source.Tags.NameFor(tag.TagId) is { Length: > 0 } name)
            {
                facets.Add(new FacetAssignment(FacetKinds.Tag, name, tag.Rank));
            }
        }

        foreach (var categoryId in item.Categories.PlayerCategoryIds)
        {
            // One id can mean two modes — "Shared/Split Screen Co-op" is both —
            // so this unions rather than assigns.
            foreach (var slug in GameModes.FromSteamPlayerCategory(categoryId))
            {
                facets.Add(GameModes.Assignment(slug));
            }
        }

        AddCategories(facets, FacetKinds.Feature, item.Categories.FeatureCategoryIds, source);
        AddCategories(facets, FacetKinds.Controller, item.Categories.ControllerCategoryIds, source);

        return facets;
    }

    private static void AddCategories(
        List<FacetAssignment> facets,
        string kind,
        IReadOnlyList<int> categoryIds,
        SteamFacetSource source)
    {
        foreach (var categoryId in categoryIds)
        {
            if (source.Categories.NameFor(categoryId) is { Length: > 0 } name)
            {
                facets.Add(new FacetAssignment(kind, name));
            }
        }
    }

    /// <summary>The Steam half's three inputs, carried together so they cannot be used half-populated.</summary>
    private sealed record SteamFacetSource(
        IReadOnlyDictionary<string, SteamStoreItem> Items,
        SteamTagVocabulary Tags,
        SteamStoreCategoryVocabulary Categories)
    {
        public static SteamFacetSource None { get; } = new(
            new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal),
            SteamTagVocabulary.Empty,
            SteamStoreCategoryVocabulary.Empty);
    }
}

/// <summary>What one facet sync did, for the startup log and for tests to assert on.</summary>
/// <param name="ReleasesExamined">Rows the target query returned.</param>
/// <param name="IgdbGamesRead">IGDB games available (cache plus any fetch).</param>
/// <param name="SteamItemsRead">Store items available (cache plus any fetch).</param>
/// <param name="WorksWritten">Works whose descriptor set actually changed.</param>
/// <param name="ReleasesWritten">Releases whose descriptor set actually changed.</param>
/// <param name="RowsWritten">
/// Assignment rows inserted or deleted. <b>Zero on a warm re-run</b>, which is
/// how the idempotence test states its claim.
/// </param>
public sealed record FacetSyncReport(
    int ReleasesExamined,
    int IgdbGamesRead,
    int SteamItemsRead,
    int WorksWritten,
    int ReleasesWritten,
    int RowsWritten)
{
    public static FacetSyncReport Empty { get; } = new(0, 0, 0, 0, 0, 0);
}
