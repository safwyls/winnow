using System.Text;

namespace Winnow.Core.Queries;

/// <summary>
/// Facet kind constants for the descriptor vocabularies (migration 0007).
/// Each kind passes through one provider's taxonomy unchanged, except
/// <see cref="GameMode"/> which Winnow normalises across IGDB and Steam.
/// </summary>
public static class FacetKinds
{
    /// <summary>IGDB genre. A fact about the Work — stored on <c>work_facets</c>.</summary>
    public const string Genre = "genre";

    /// <summary>IGDB theme. A fact about the Work.</summary>
    public const string Theme = "theme";

    /// <summary>IGDB player perspective (first person, third person, …). A fact about the Work.</summary>
    public const string PlayerPerspective = "player_perspective";

    /// <summary>How the game is played. Normalised across IGDB and Steam via <see cref="GameModes"/>.</summary>
    public const string GameMode = "game_mode";

    /// <summary>A Steam user tag. Per-release (on <c>release_facets</c>). Carries a rank.</summary>
    public const string Tag = "tag";

    /// <summary>
    /// A Steam storefront feature: achievements, trading cards, cloud saves,
    /// workshop, Remote Play. A fact about one appid.
    /// </summary>
    public const string Feature = "feature";

    /// <summary>Steam controller support: full, partial, DualSense, Steam Input. A fact about one appid.</summary>
    public const string Controller = "controller";

    /// <summary>Every kind, in the order a filter panel reads best.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Genre, Theme, Tag, GameMode, PlayerPerspective, Feature, Controller];
}

/// <summary>
/// Winnow's normalised game-mode vocabulary. Keyed by slug (not id) because
/// IGDB and Steam both contribute modes in incompatible vocabularies.
/// </summary>
public static class GameModes
{
    public const string SinglePlayer = "single_player";
    public const string Multiplayer = "multiplayer";
    public const string CoOperative = "co_operative";
    public const string SplitScreen = "split_screen";
    public const string Mmo = "mmo";
    public const string BattleRoyale = "battle_royale";

    /// <summary>The whole vocabulary, in the order migration 0007 seeded it.</summary>
    public static IReadOnlyList<string> All { get; } =
        [SinglePlayer, Multiplayer, CoOperative, SplitScreen, Mmo, BattleRoyale];

    /// <summary>Display name for a game-mode slug.</summary>
    public static string DisplayName(string slug) => slug switch
    {
        SinglePlayer => "Single-player",
        Multiplayer => "Multiplayer",
        CoOperative => "Co-op",
        SplitScreen => "Split screen",
        Mmo => "MMO",
        BattleRoyale => "Battle royale",
        _ => slug,
    };

    /// <summary>One assignment of a game mode, keyed by slug and labelled by name.</summary>
    public static FacetAssignment Assignment(string slug)
        => new(FacetKinds.GameMode, DisplayName(slug), Slug: slug);

    /// <summary>IGDB game-mode names mapped to Winnow slugs. Matched via slugified name.</summary>
    private static readonly Dictionary<string, string> FromIgdb = new(StringComparer.Ordinal)
    {
        ["single_player"] = SinglePlayer,
        ["singleplayer"] = SinglePlayer,
        ["multiplayer"] = Multiplayer,
        ["co_operative"] = CoOperative,
        ["cooperative"] = CoOperative,
        ["co_op"] = CoOperative,
        ["split_screen"] = SplitScreen,
        ["massively_multiplayer_online_mmo"] = Mmo,
        ["mmo"] = Mmo,
        ["battle_royale"] = BattleRoyale,
    };

    /// <summary>
    /// Steam player-category ids mapped to Winnow game-mode slugs.
    /// One id can map to multiple modes (e.g. "Shared/Split Screen Co-op" is both co-op and split screen).
    /// </summary>
    private static readonly Dictionary<int, string[]> FromSteamCategory = new()
    {
        [2] = [SinglePlayer],                   // Single-player
        [1] = [Multiplayer],                    // Multi-player
        [9] = [CoOperative],                    // Co-op
        [38] = [CoOperative],                   // Online Co-op
        [48] = [CoOperative],                   // LAN Co-op
        [39] = [CoOperative, SplitScreen],      // Shared/Split Screen Co-op
        [24] = [SplitScreen],                   // Shared/Split Screen
        [37] = [Multiplayer, SplitScreen],      // Shared/Split Screen PvP
        [27] = [Multiplayer],                   // Cross-Platform Multiplayer
        [36] = [Multiplayer],                   // Online PvP
        [47] = [Multiplayer],                   // LAN PvP
        [49] = [Multiplayer],                   // PvP
        [20] = [Mmo],                           // MMO
    };

    /// <summary>The mode an IGDB game-mode name means, or null when it means none of ours.</summary>
    public static string? FromIgdbName(string? name)
    {
        var slug = Facet.Slugify(name);
        return slug.Length != 0 && FromIgdb.TryGetValue(slug, out var mode) ? mode : null;
    }

    /// <summary>The game modes a Steam player-category id maps to. Empty for unknown ids.</summary>
    public static IReadOnlyList<string> FromSteamPlayerCategory(int categoryId)
        => FromSteamCategory.TryGetValue(categoryId, out var modes) ? modes : [];
}

/// <summary>One row of the <c>facets</c> vocabulary table.</summary>
/// <param name="Id">Surrogate key (migration 0007), shared namespace across all kinds.</param>
/// <param name="Kind">One of <see cref="FacetKinds"/>.</param>
/// <param name="Slug">Normalised name and natural key within a kind.</param>
/// <param name="Name">Display name, verbatim from the provider.</param>
public sealed record Facet(long Id, string Kind, string Slug, string Name)
{
    /// <summary>
    /// Lowercases and folds non-alphanumeric runs to underscores. Culture-invariant.
    /// Diacritics are preserved.
    /// </summary>
    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSeparator = false;
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }
}

/// <summary>One descriptor attached to one thing, as the backfill writes it.</summary>
/// <param name="Kind">One of <see cref="FacetKinds"/>.</param>
/// <param name="Name">Provider's display name. The repository slugifies it and upserts.</param>
/// <param name="Rank">1-based position in the provider's ordering, or null for unordered kinds.</param>
/// <param name="Slug">Explicit vocabulary key, or null to derive from name. Required for <see cref="FacetKinds.GameMode"/>.</param>
public sealed record FacetAssignment(string Kind, string Name, int? Rank = null, string? Slug = null)
{
    /// <summary>The key this assignment stores under: its own slug, or the folded name.</summary>
    public string Key => string.IsNullOrWhiteSpace(Slug) ? Facet.Slugify(Name) : Slug;
}

/// <summary>Every facet on one release, unioning work-level and release-level descriptors.</summary>
/// <param name="ReleaseId">The release these descriptors belong to.</param>
/// <param name="FacetIds">All facet ids true of this release, across every kind.</param>
/// <param name="GameModes">Game-mode slugs (string-keyed, unlike other facets).</param>
public sealed record ReleaseFacets(
    long ReleaseId,
    IReadOnlyList<long> FacetIds,
    IReadOnlyList<string> GameModes)
{
    /// <summary>The row for a release nothing is known about — no facets, not absent.</summary>
    public static ReleaseFacets Empty(long releaseId) => new(releaseId, [], []);
}

/// <summary>
/// A facet with a count, ready to render as one checkbox in the filter panel.
/// </summary>
/// <param name="Facet">The vocabulary row.</param>
/// <param name="ReleaseCount">How many releases in the counted set carry it.</param>
public sealed record FacetCount(Facet Facet, int ReleaseCount);

/// <summary>The full facet vocabulary and per-release facet sets, for in-memory filtering.</summary>
public sealed class FacetSnapshot
{
    /// <summary>The whole vocabulary, including facets nothing currently carries.</summary>
    public required IReadOnlyList<Facet> Facets { get; init; }

    /// <summary>One entry per release with at least one facet. Absent releases still match an empty filter.</summary>
    public required IReadOnlyList<ReleaseFacets> Releases { get; init; }

    /// <summary>Nothing materialised yet — the shape a fresh database returns.</summary>
    public static FacetSnapshot Empty { get; } = new() { Facets = [], Releases = [] };

    /// <summary>
    /// Facet id → its row, for the lookups the view does per tile.
    /// </summary>
    public IReadOnlyDictionary<long, Facet> ById => _byId ??= Facets.ToDictionary(f => f.Id);

    /// <summary>
    /// Release id → its facets, for the same reason.
    /// </summary>
    public IReadOnlyDictionary<long, ReleaseFacets> ByRelease
        => _byRelease ??= Releases.ToDictionary(r => r.ReleaseId);

    private Dictionary<long, Facet>? _byId;
    private Dictionary<long, ReleaseFacets>? _byRelease;

    /// <summary>Counts each facet over the given releases. Omits facets with zero matches.</summary>
    public IReadOnlyList<FacetCount> CountsFor(IEnumerable<long> releaseIds)
    {
        var counts = new Dictionary<long, int>();
        foreach (var releaseId in releaseIds.Distinct())
        {
            if (!ByRelease.TryGetValue(releaseId, out var facets))
            {
                continue;
            }

            foreach (var facetId in facets.FacetIds)
            {
                counts[facetId] = counts.GetValueOrDefault(facetId) + 1;
            }
        }

        var result = new List<FacetCount>(counts.Count);
        foreach (var facet in Facets)
        {
            if (counts.TryGetValue(facet.Id, out var count))
            {
                result.Add(new FacetCount(facet, count));
            }
        }

        // Kind first so the panel's columns stay in a fixed order, then
        // commonest first within a kind — the order the reference storefront
        // uses, and the one that puts the useful checkbox at the top.
        return result
            .OrderBy(c => KindOrder(c.Facet.Kind))
            .ThenByDescending(c => c.ReleaseCount)
            .ThenBy(c => c.Facet.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Kind to column-position mapping for stable panel ordering.</summary>
    private static readonly Dictionary<string, int> KindOrdering =
        FacetKinds.All
            .Select((kind, index) => (kind, index))
            .ToDictionary(x => x.kind, x => x.index, StringComparer.Ordinal);

    private static int KindOrder(string kind)
        => KindOrdering.TryGetValue(kind, out var order) ? order : int.MaxValue;
}

/// <summary>
/// A release with its external lookup ids (IGDB, Steam), as input to the facet backfill.
/// Uses init properties (not positional params) for Dapper Int64 compatibility.
/// </summary>
public sealed record FacetTarget
{
    /// <summary>The work IGDB descriptors are written against.</summary>
    public required long WorkId { get; init; }

    /// <summary>The release Steam descriptors are written against.</summary>
    public required long ReleaseId { get; init; }

    /// <summary><c>works.igdb_id</c>, or null when the work was never resolved.</summary>
    public long? IgdbId { get; init; }

    /// <summary>Steam appid from <c>external_ids</c>, or null.</summary>
    public string? SteamAppId { get; init; }
}
