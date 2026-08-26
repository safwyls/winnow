using System.Text;

namespace Hoard.Core.Queries;

/// <summary>
/// The descriptor vocabularies the library filter asks about (migration 0007).
///
/// <para>Each kind is one provider's taxonomy passed through unchanged, with a
/// single exception: <see cref="GameMode"/>, which both providers describe in
/// different words and which Hoard therefore normalises
/// (<see cref="GameModes"/>).</para>
///
/// <para>Facets are stored FACTS, not derived values. §6.1's rule that buckets
/// must stay queries is about values that change when Hoard changes its mind — a
/// threshold moves and every stored bucket rots. "Elden Ring is tagged
/// Souls-like" is not that kind of value: it changes when Valve's users change
/// it, and the cure is a re-read of <c>metadata_cache</c>, which is what the
/// backfill does.</para>
/// </summary>
public static class FacetKinds
{
    /// <summary>IGDB genre. A fact about the Work — stored on <c>work_facets</c>.</summary>
    public const string Genre = "genre";

    /// <summary>IGDB theme. A fact about the Work.</summary>
    public const string Theme = "theme";

    /// <summary>IGDB player perspective (first person, third person, …). A fact about the Work.</summary>
    public const string PlayerPerspective = "player_perspective";

    /// <summary>
    /// How the game is played. The one kind Hoard normalises rather than passes
    /// through, because IGDB's <c>game_modes</c> and Steam's player categories
    /// both answer it in incompatible words — see <see cref="GameModes"/>.
    ///
    /// <para>Written at both layers: from IGDB onto the Work, from Steam onto the
    /// Release. A reader unions them.</para>
    /// </summary>
    public const string GameMode = "game_mode";

    /// <summary>
    /// A Steam user tag. A fact about ONE appid, so it lives on
    /// <c>release_facets</c> — Skyrim and Skyrim Special Edition are separately
    /// tagged by separately-voting users, and merging them would be §6.2's
    /// forbidden blend wearing different clothes.
    ///
    /// <para>Carries a <c>rank</c>; see <see cref="FacetAssignment.Rank"/>.</para>
    /// </summary>
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
/// Hoard's own game-mode vocabulary, and the only facet key that is a string
/// rather than an id.
///
/// <para><b>Why a string.</b> Every other facet kind is one provider's list, so
/// a row's identity can be a local id minted from the provider's name. Game
/// modes come from two providers at once — IGDB's <c>game_modes</c> and Steam's
/// <c>categories.supported_player_categoryids</c> — and neither vocabulary can be
/// the key without silently making the other second-class. So the key is a slug
/// Hoard owns, seeded by migration 0007 with fixed ids, and
/// <see cref="LibraryFilter.GameModes"/> matches on it.</para>
///
/// <para>Steam names four different categories that all mean co-op (Co-op,
/// Online Co-op, LAN Co-op, Shared/Split Screen Co-op) and four that all mean
/// multiplayer; normalising is not a simplification here, it is the only way to
/// render one checkbox that means one thing.</para>
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

    /// <summary>
    /// What a mode's checkbox says — the same six names migration 0007 seeded.
    ///
    /// <para>Written out rather than derived from the slug, because a slug is a
    /// lossy form of a name and reversing it would be a guess: "mmo" does not
    /// become "MMO" by any general rule, and "co_operative" does not become
    /// "Co-op" by one either. That second pair is exactly why
    /// <see cref="FacetAssignment.Slug"/> exists — the display name here folds to
    /// <c>co_op</c>, which is NOT this vocabulary's key.</para>
    /// </summary>
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

    /// <summary>
    /// IGDB game-mode names, folded onto the vocabulary above. Matched on the
    /// slugified name rather than the raw string so casing and punctuation drift
    /// ("Co-operative", "Co-Operative") cannot silently drop a mode.
    /// </summary>
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
    /// Steam player-category ids, folded onto the vocabulary above. Verified
    /// against <c>IStoreBrowseService/GetStoreCategories</c> live on 2026-08-25:
    /// these are every category the endpoint reports with <c>type = 1</c>.
    ///
    /// <para>One id can mean two modes. "Shared/Split Screen Co-op" (39) is both
    /// co-op and split screen, and "Shared/Split Screen PvP" (37) is both
    /// multiplayer and split screen — which is why the value is a list and the
    /// caller unions rather than assigns.</para>
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

    /// <summary>
    /// The modes a Steam player-category id means. Empty for an id this build
    /// has never seen — Valve can add one at any time, and an unknown category
    /// is silence, never a guess.
    /// </summary>
    public static IReadOnlyList<string> FromSteamPlayerCategory(int categoryId)
        => FromSteamCategory.TryGetValue(categoryId, out var modes) ? modes : [];
}

/// <summary>
/// One row of the <c>facets</c> vocabulary: a descriptor the library can be
/// filtered on.
/// </summary>
/// <param name="Id">
/// The surrogate key (migration 0007). One integer namespace across every kind,
/// so a filter carries a flat set of ids and a reader tests membership with one
/// lookup. Stable: the backfill only ever inserts, so a saved filter keeps
/// meaning what it meant.
/// </param>
/// <param name="Kind">One of <see cref="FacetKinds"/>.</param>
/// <param name="Slug">
/// The normalised name, and the natural key within a kind. For
/// <see cref="FacetKinds.GameMode"/> this is also the value
/// <see cref="LibraryFilter.GameModes"/> matches on.
/// </param>
/// <param name="Name">What the checkbox says, verbatim from the provider.</param>
public sealed record Facet(long Id, string Kind, string Slug, string Name)
{
    /// <summary>
    /// Lower-cases, folds every run of non-alphanumerics to a single underscore
    /// and trims the ends — "Shared/Split Screen Co-op" becomes
    /// <c>shared_split_screen_co_op</c>.
    ///
    /// <para>This is the vocabulary's natural key, so it must be the SAME
    /// function everywhere: the backfill mints rows with it, migration 0007
    /// seeded the game-mode slugs to match it, and
    /// <see cref="GameModes.FromIgdbName"/> looks up through it. Culture-
    /// invariant on purpose — a Turkish locale lower-casing 'I' to 'ı' would
    /// mint a second row for a genre that already exists.</para>
    ///
    /// <para>Diacritics are preserved rather than stripped: "Pokémon" is a real
    /// name and folding it to "pokemon" would be a guess about equivalence that
    /// nothing here needs to make.</para>
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

/// <summary>
/// One descriptor attached to one thing, as the backfill writes it.
/// </summary>
/// <param name="Kind">One of <see cref="FacetKinds"/>.</param>
/// <param name="Name">
/// The provider's display name. The repository slugifies it and mints the
/// <c>facets</c> row if this is the first time the library has seen it.
/// </param>
/// <param name="Rank">
/// 1-based position in the provider's own ordering, or null for an unordered
/// kind.
///
/// <para><b>Rank, never weight.</b> <c>docs/spikes/steam-store-tags.md</c>
/// measured Steam's <c>weight</c> against the store page's raw vote counts and
/// found a constant per-app ratio with identical rank order: it is a per-app
/// normalisation, comparable within an app and meaningless across apps. Only the
/// order survives into storage.</para>
/// </param>
/// <param name="Slug">
/// The vocabulary key, when the caller owns it. Null for every kind whose
/// vocabulary is a provider's, where the key IS the folded name.
///
/// <para>Needed for exactly one kind, and needed there absolutely.
/// <see cref="FacetKinds.GameMode"/> is a CLOSED vocabulary that migration 0007
/// seeded with fixed ids, so an assignment has to land on the seeded row rather
/// than mint a second one beside it — and the display name does not always fold
/// to the seeded slug ("Co-op" folds to <c>co_op</c>, not <c>co_operative</c>).
/// Deriving the key from the label works right up until someone rewords a label,
/// at which point the library silently grows a duplicate checkbox and every
/// saved filter pointing at the old row goes quiet. An explicit key removes the
/// whole class.</para>
/// </param>
public sealed record FacetAssignment(string Kind, string Name, int? Rank = null, string? Slug = null)
{
    /// <summary>The key this assignment stores under: its own slug, or the folded name.</summary>
    public string Key => string.IsNullOrWhiteSpace(Slug) ? Facet.Slugify(Name) : Slug;
}

/// <summary>
/// Every facet on one release, flattened for in-memory filtering.
///
/// <para>Facet ids from BOTH layers are unioned here — the Work's genres and
/// themes alongside the Release's store tags — because the caller is asking
/// "what is true of this tile", and the two tables exist to keep the facts at the
/// right layer, not to make the reader do a join per row.</para>
/// </summary>
/// <param name="ReleaseId">The release these descriptors belong to.</param>
/// <param name="FacetIds">
/// Every facet id true of this release, across every kind. Ids are unique across
/// kinds, so a single set is enough to answer a genre question and a tag question
/// with the same lookup.
/// </param>
/// <param name="GameModes">
/// Game-mode slugs, kept separately because <see cref="LibraryFilter.GameModes"/>
/// matches on the slug rather than the id — see <see cref="Queries.GameModes"/>
/// for why that kind alone is string-keyed.
/// </param>
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

/// <summary>
/// Everything the filter panel needs, read in one pass: the per-release facet
/// sets to filter with, and the vocabulary to render.
///
/// <para>Sized for the whole library on purpose. <c>LibraryViewModel</c> filters
/// in memory because the library is a few hundred kilobytes of projection and
/// re-querying SQLite per keystroke buys nothing; this is the facet half of that
/// same projection and it follows the same rule. The author's 926-title library
/// produces well under a hundred kilobytes here.</para>
/// </summary>
public sealed class FacetSnapshot
{
    /// <summary>The whole vocabulary, including facets nothing currently carries.</summary>
    public required IReadOnlyList<Facet> Facets { get; init; }

    /// <summary>
    /// One entry per release that carries at least one facet. A release with no
    /// cached metadata is simply absent — it is NOT missing from the library, and
    /// an empty filter must still match it (see
    /// <see cref="LibraryFilter.IsEmpty"/>).
    /// </summary>
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

    /// <summary>
    /// The vocabulary with a count beside each entry, counted over exactly the
    /// releases named — normally the ones the grid is currently showing.
    ///
    /// <para><b>Counted over the caller's set, deliberately.</b> The bucket query
    /// drops a demo whose full game is also owned and (by default) everything
    /// Valve typed as a tool or a soundtrack, so a count taken over every row in
    /// the database would be a number the grid can never show. This is the same
    /// rule <see cref="BucketThresholds.ShowNonGameEntries"/> is documented
    /// under: the rail must not report a total the grid does not display, and a
    /// checkbox saying "RPG 41" beside 38 visible tiles is that bug in
    /// miniature.</para>
    ///
    /// <para>Facets carried by nothing in the set are omitted rather than
    /// returned as zero: a filter panel full of empty checkboxes is noise, and
    /// the vocabulary is still available in full on
    /// <see cref="Facets"/>.</para>
    /// </summary>
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

    /// <summary>
    /// Kind → its column position, so the panel's columns keep a fixed order
    /// whatever order the vocabulary happens to come back in.
    /// </summary>
    private static readonly Dictionary<string, int> KindOrdering =
        FacetKinds.All
            .Select((kind, index) => (kind, index))
            .ToDictionary(x => x.kind, x => x.index, StringComparer.Ordinal);

    private static int KindOrder(string kind)
        => KindOrdering.TryGetValue(kind, out var order) ? order : int.MaxValue;
}

/// <summary>
/// One release, with the external ids its descriptors can be looked up by — the
/// input to the facet backfill.
///
/// <para>Carries BOTH layers because the descriptors live at both: the IGDB id
/// belongs to the Work and yields genres and themes, the Steam appid belongs to
/// the Release and yields store tags and storefront categories. One row per
/// release, so a Work with two releases is asked about once per storefront
/// listing and its work-level facets are written once.</para>
///
/// <para><b>Properties, not positional parameters.</b> SQLite reports every
/// INTEGER column as <c>Int64</c>, so Dapper cannot bind a constructor taking a
/// <c>long?</c> alongside them and refuses to materialise a positional record —
/// the same reason <see cref="ReleaseIdentity"/> and
/// <see cref="EnrichmentTarget"/> are shaped this way.</para>
/// </summary>
public sealed record FacetTarget
{
    /// <summary>The work IGDB descriptors are written against.</summary>
    public required long WorkId { get; init; }

    /// <summary>The release Steam descriptors are written against.</summary>
    public required long ReleaseId { get; init; }

    /// <summary><c>works.igdb_id</c>, or null when the work was never resolved.</summary>
    public long? IgdbId { get; init; }

    /// <summary>
    /// The release's Steam appid from <c>external_ids</c>, or null for a release
    /// that is not a Steam one.
    /// </summary>
    public string? SteamAppId { get; init; }
}
