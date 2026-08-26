using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Model;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Steam.Model;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

/// <summary>
/// Materialises the descriptors the library filter panel needs (migration 0007)
/// out of metadata Hoard has already fetched.
///
/// <para><b>This pass is a re-read, not a re-fetch.</b> Both sources it uses are
/// cache-first by construction — <see cref="IIgdbClient.GetGamesAsync"/> and
/// <see cref="ISteamStoreClient.GetItemsAsync"/> consult
/// <c>metadata_cache</c> before the network and record misses as misses — so on
/// the author's library the IGDB half costs zero requests for all 865 cached
/// games, and the Steam half costs requests only for appids the store has never
/// been asked about. Migration 0005 predicted exactly this: the data was kept
/// verbatim so that a later feature could build its table "without spending a
/// single request".</para>
///
/// <para><b>Where the descriptors come from, and why each one is where it
/// is.</b></para>
/// <list type="bullet">
///   <item><b>IGDB → the Work.</b> Genres, themes and player perspectives are
///     facts about the game: Skyrim is an RPG whichever edition is owned.</item>
///   <item><b>Steam → the Release.</b> Store tags and storefront categories
///     belong to one appid. Skyrim and Skyrim Special Edition are separate apps
///     with separately-voted tags, and folding them together would be §6.2's
///     forbidden blend in different clothes.</item>
///   <item><b>Game modes → both.</b> The one descriptor both sources answer, in
///     incompatible words, normalised onto <see cref="GameModes"/> so the filter
///     asks it once.</item>
/// </list>
///
/// <para>§5.1: this composes enrichment clients and repositories, exactly as
/// <see cref="EnrichmentSyncService"/> does, and the UI never calls it. It must
/// never block a user-facing path and it never throws at its caller: every
/// failure degrades to "fewer facets this run", which the next run fixes.</para>
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

    /// <summary>
    /// Re-derives every release's descriptors from the caches and stores what
    /// changed.
    ///
    /// <para><b>Idempotent, and free on a second run.</b> Two mechanisms, and
    /// they cover different costs. Nothing is re-FETCHED because both clients
    /// read the cache first. Nothing is re-WRITTEN because
    /// <see cref="IFacetRepository.SetWorkFacetsAsync"/> compares before it
    /// writes and reports zero when the stored set already matched — so a warm
    /// re-run touches no rows at all, rather than rewriting ten thousand of them
    /// to arrive where it started.</para>
    /// </summary>
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

    /// <summary>
    /// The IGDB games behind the library's works, from the cache wherever
    /// possible.
    ///
    /// <para>Returns empty rather than throwing on any failure — IGDB being
    /// unconfigured is the ordinary case on a fresh machine, not an error, and
    /// the Steam half of this pass stands on its own.</para>
    /// </summary>
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

    /// <summary>
    /// The store items behind the library's Steam releases, plus the two
    /// vocabularies their ids resolve through.
    ///
    /// <para><b>Both vocabularies or neither.</b> Tag ids and category ids are
    /// meaningless without their name maps, and a release write replaces that
    /// release's whole descriptor set — so writing with half a vocabulary in hand
    /// would silently DELETE the other half's stored facets. When either
    /// vocabulary comes back empty the Steam half is skipped entirely and the
    /// next run picks it up. Both maps prefer a stale cached snapshot to an empty
    /// one, so this only happens on a machine that has never reached the store at
    /// all — which is also a machine with nothing to lose.</para>
    /// </summary>
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

    /// <summary>
    /// What IGDB says about a game, as work-level descriptors.
    ///
    /// <para>Game modes are folded onto Hoard's vocabulary and everything else is
    /// passed through by name. An IGDB mode this build has no slug for is
    /// dropped rather than minted as its own facet: the game-mode vocabulary is
    /// closed by design, and a stray seventh checkbox appearing because IGDB
    /// added a value is worse than not offering it until someone decides what it
    /// means.</para>
    /// </summary>
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

    /// <summary>
    /// Reads a list off a cached <see cref="IgdbGame"/> that may not have one.
    ///
    /// <para>The type declares these lists as non-nullable, but the values come
    /// from JSON on disk rather than from the constructor: a payload written
    /// before a field existed simply has no property for it, and the
    /// deserializer supplies the default — <c>null</c> — for a positional
    /// parameter it cannot fill. Since surviving exactly that case is the reason
    /// the cache shape was left alone, the reader has to be able to survive it
    /// too. The nullable parameter is what keeps this from being flagged as a
    /// redundant check.</para>
    /// </summary>
    private static IReadOnlyList<string> OrEmpty(IReadOnlyList<string>? values) => values ?? [];

    /// <summary>
    /// What the Steam store says about one appid, as release-level descriptors.
    ///
    /// <para>Tags keep their rank — the only part of Steam's <c>weight</c> that
    /// means anything across apps (see <c>docs/spikes/steam-store-tags.md</c>).
    /// Player categories become game modes; feature and controller categories
    /// become their own kinds. An id the vocabulary cannot name is skipped, not
    /// invented: an unnamed checkbox is not a filter.</para>
    /// </summary>
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
