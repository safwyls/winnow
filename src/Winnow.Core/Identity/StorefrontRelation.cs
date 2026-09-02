namespace Winnow.Core.Identity;

/// <summary>
/// Which storefront made a claim about a work's relation to another. Recorded
/// so a wrong answer can be traced to the source that gave it.
/// </summary>
public static class StorefrontRelationSources
{
    /// <summary><c>IStoreBrowseService/GetItems</c>: <c>StoreItem.type</c> and <c>related_items</c>.</summary>
    public const string SteamStore = "steam_store";

    /// <summary>The steamcmd.net PICS mirror: <c>common.type</c> and <c>common.parent</c>.</summary>
    public const string SteamPics = "steam_pics";

    /// <summary>IGDB: <c>games.game_type</c>, <c>parent_game</c> and <c>version_parent</c>.</summary>
    public const string Igdb = "igdb";
}

/// <summary>
/// The raw storefront facts stored on one work (migration 0022, and
/// steam_app_type from 0006). Observations only. No kind is stored, because
/// parent_appid's meaning depends on the type and freezing that mapping into
/// the database would need a migration to correct it.
/// </summary>
public sealed record StorefrontFacts
{
    /// <summary><c>works.steam_store_type</c> (migration 0022). Valve's numeric <c>StoreItem.type</c>; null is "not known", 0 is a real value meaning game.</summary>
    public int? SteamStoreType { get; init; }

    /// <summary><c>works.steam_parent_app_id</c> (migration 0022). <c>related_items.parent_appid</c> or PICS <c>common.parent</c>.</summary>
    public string? SteamParentAppId { get; init; }

    /// <summary><c>works.steam_app_type</c> (migration 0006). PICS <c>common.type</c>, read case-insensitively.</summary>
    public string? SteamAppType { get; init; }

    /// <summary><c>works.igdb_game_type</c> (migration 0022). The <c>games.game_type</c> label from /v4/game_types.</summary>
    public string? IgdbGameType { get; init; }

    /// <summary><c>works.igdb_parent_id</c> (migration 0022). IGDB <c>games.parent_game</c>.</summary>
    public long? IgdbParentId { get; init; }

    /// <summary><c>works.igdb_version_parent_id</c> (migration 0022). IGDB <c>games.version_parent</c>.</summary>
    public long? IgdbVersionParentId { get; init; }

    /// <summary>The facts about a work no source has probed. The norm for an unenriched library.</summary>
    public static StorefrontFacts None { get; } = new();

    /// <summary>True when no source has said anything at all about this work's relations.</summary>
    public bool IsEmpty
        => SteamStoreType is null
           && string.IsNullOrWhiteSpace(SteamParentAppId)
           && string.IsNullOrWhiteSpace(SteamAppType)
           && string.IsNullOrWhiteSpace(IgdbGameType)
           && IgdbParentId is null
           && IgdbVersionParentId is null;
}

/// <summary>
/// What one storefront claims about one work's relation to another. Produced by
/// <see cref="StorefrontRelation.Read"/>; nothing here writes a link. A claim
/// with a <see cref="Kind"/> is a proposal the user may still refuse (the
/// never-auto-merge rule holds); a claim with a null <see cref="Kind"/> records
/// the source's word without asking for a link, and a
/// <see cref="RefutesExtension"/> claim asks for nothing at all, it only stops
/// the title heuristic from guessing.
/// </summary>
/// <param name="Source">One of <see cref="StorefrontRelationSources"/>.</param>
/// <param name="Label">One of <see cref="RelationLabels"/> — the source's own word.</param>
/// <param name="Kind">One of <see cref="IdentityLinkKinds"/>, or null when the label is recorded but no link is claimed.</param>
/// <param name="SteamParentAppId">The parent as a Steam appid, when the claim came from a Steam source.</param>
/// <param name="IgdbParentId">The parent as an IGDB game id, when the claim came from IGDB.</param>
/// <param name="RefutesExtension">True when the source states this work extends nothing — IGDB main_game with no parent.</param>
public sealed record StorefrontClaim(
    string Source,
    string Label,
    string? Kind,
    string? SteamParentAppId = null,
    long? IgdbParentId = null,
    bool RefutesExtension = false)
{
    /// <summary>True when the claim names a parent in either encoding (Steam appid or IGDB game id).</summary>
    public bool HasParent => SteamParentAppId is not null || IgdbParentId is not null;
}

/// <summary>
/// Turns the stored storefront facts into a claim. Pure, deterministic, no IO,
/// and the only place the type-to-kind mapping lives, so correcting it is a
/// code change rather than a migration.
///
/// <para>Steam and IGDB are complementary, not redundant, and the precedence
/// order says so. Steam is authoritative for demos, betas, playtests and mods
/// and silent on expansions: every genuine standalone expansion in the measured
/// library is type 0 with no parent appid. IGDB is the reverse: it types
/// expansions precisely and does not model demos, betas or playtests at all. A
/// Steam variant claim outranks anything IGDB says, and IGDB decides everything
/// Steam is silent about.</para>
/// </summary>
public static class StorefrontRelation
{
    /// <summary>Steam store type 0. An ordinary game. Verified: 841 of 954 cached bodies.</summary>
    public const int SteamTypeGame = 0;

    /// <summary>Steam store type 1. A demo. Verified: 31 of 954 cached bodies.</summary>
    public const int SteamTypeDemo = 1;

    /// <summary>Steam store type 2. A mod. Verified: 5 of 954 cached bodies.</summary>
    public const int SteamTypeMod = 2;

    /// <summary>Steam store type 4. DLC. Verified: 1 of 954 cached bodies.</summary>
    public const int SteamTypeDlc = 4;

    /// <summary>Steam store type 12. A beta or playtest build. Verified: 7 of 954 cached bodies.</summary>
    public const int SteamTypeBetaOrPlaytest = 12;

    /// <summary>
    /// Steam store type 14. Retired. Seen only on delisted or superseded apps,
    /// where parent_appid names the app that REPLACED this one. That is a
    /// same_game claim, not a child relation; reading it as a child relation
    /// would file a game under its own replacement. Verified: 12 of 954 cached
    /// bodies.
    /// </summary>
    public const int SteamTypeRetired = 14;

    /// <summary>IGDB <c>main_game</c>, exactly as /v4/game_types spells it.</summary>
    public const string IgdbMainGame = "main_game";

    private static readonly Dictionary<string, (string Kind, string Label)> IgdbTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Products you bought that depend on a base. They count as titles
            // and their playtime does not roll up (the user's decision 3 on
            // TASK-70).
            ["dlc_addon"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Dlc),
            ["expansion"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Expansion),
            ["standalone_expansion"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.StandaloneExpansion),
            ["episode"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Episode),
            ["season"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Season),
            ["pack"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Pack),

            // Editions. Numerically identical to expansion_of and semantically
            // not expansions, so they take that kind and their own label.
            ["expanded_game"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.ExpandedGame),
            ["remaster"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Remaster),
            ["remake"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Remake),
            ["port"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Port),
            ["fork"] = (IdentityLinkKinds.ExpansionOf, RelationLabels.Fork),
        };

    private static readonly Dictionary<string, string> IgdbLabelOnlyTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Recorded, never folded. Whether a mod belongs under its base game
            // is an open question for the user, not a decision this code makes.
            ["mod"] = RelationLabels.Mod,

            // A bundle contains games; it is not a child of one.
            ["bundle"] = RelationLabels.Bundle,

            // A patch entity, not a product anyone owns separately.
            ["update"] = RelationLabels.Update,
        };

    /// <summary>
    /// The claim these facts support, or null when every source is silent.
    /// Silence is the condition the title heuristic is allowed to fill.
    /// </summary>
    public static StorefrontClaim? Read(StorefrontFacts? facts)
    {
        if (facts is null || facts.IsEmpty)
        {
            return null;
        }

        var parentAppId = string.IsNullOrWhiteSpace(facts.SteamParentAppId)
            ? null
            : facts.SteamParentAppId.Trim();

        // 1. Steam's variant types, which IGDB cannot contradict because IGDB
        //    does not model demos, betas or playtests at all.
        if (facts.SteamStoreType == SteamTypeDemo)
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamStore,
                RelationLabels.Demo,
                IdentityLinkKinds.VariantOf,
                SteamParentAppId: parentAppId);
        }

        if (facts.SteamStoreType == SteamTypeBetaOrPlaytest)
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamStore,
                RelationLabels.Playtest,
                IdentityLinkKinds.VariantOf,
                SteamParentAppId: parentAppId);
        }

        // 2. Retired. The parent names the app that replaced this one, which is
        //    one game listed twice rather than a child of anything.
        if (facts.SteamStoreType == SteamTypeRetired)
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamStore,
                RelationLabels.Superseded,
                parentAppId is null ? null : IdentityLinkKinds.SameGame,
                SteamParentAppId: parentAppId);
        }

        // 3. The PICS mirror, for an app the store never answered about. 0006's
        //    note that "Valve has no beta/playtest type" no longer holds: the
        //    measured database carries works typed literally Beta.
        var picsType = facts.SteamAppType?.Trim();
        if (IsPicsType(picsType, "demo"))
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamPics,
                RelationLabels.Demo,
                IdentityLinkKinds.VariantOf,
                SteamParentAppId: parentAppId);
        }

        if (IsPicsType(picsType, "beta"))
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamPics,
                RelationLabels.Beta,
                IdentityLinkKinds.VariantOf,
                SteamParentAppId: parentAppId);
        }

        // 4. IGDB, which owns everything above the variant line.
        var igdbParent = facts.IgdbParentId ?? facts.IgdbVersionParentId;
        var igdbType = facts.IgdbGameType?.Trim();

        if (!string.IsNullOrEmpty(igdbType))
        {
            if (IgdbTypes.TryGetValue(igdbType, out var mapped))
            {
                return new StorefrontClaim(
                    StorefrontRelationSources.Igdb,
                    mapped.Label,
                    igdbParent is null ? null : mapped.Kind,
                    IgdbParentId: igdbParent);
            }

            if (IgdbLabelOnlyTypes.TryGetValue(igdbType, out var label))
            {
                return new StorefrontClaim(
                    StorefrontRelationSources.Igdb,
                    label,
                    Kind: null,
                    IgdbParentId: igdbParent);
            }

            if (string.Equals(igdbType, IgdbMainGame, StringComparison.OrdinalIgnoreCase))
            {
                // The refutation. A main game with no parent extends nothing,
                // whatever its title happens to start with.
                return new StorefrontClaim(
                    StorefrontRelationSources.Igdb,
                    RelationLabels.MainGame,
                    Kind: null,
                    IgdbParentId: igdbParent,
                    RefutesExtension: igdbParent is null);
            }

            // A type name this build has never seen. Recorded verbatim so it
            // shows up as itself rather than as silence, and claiming no kind
            // for it, because guessing the numbers a new IGDB type changes is
            // exactly the mistake this design is avoiding.
            return new StorefrontClaim(
                StorefrontRelationSources.Igdb, igdbType, Kind: null, IgdbParentId: igdbParent);
        }

        // 5. Steam DLC, below IGDB because IGDB distinguishes DLC from an
        //    expansion and Steam does not.
        if (facts.SteamStoreType == SteamTypeDlc)
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamStore,
                RelationLabels.Dlc,
                parentAppId is null ? null : IdentityLinkKinds.ExpansionOf,
                SteamParentAppId: parentAppId);
        }

        if (facts.SteamStoreType == SteamTypeMod || IsPicsType(picsType, "mod"))
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamStore,
                RelationLabels.Mod,
                Kind: null,
                SteamParentAppId: parentAppId);
        }

        // 6. A parent pointer with no type to explain it. Real enough to refute
        //    a heuristic that names a different base, not specific enough to
        //    propose a kind.
        if (parentAppId is not null)
        {
            return new StorefrontClaim(
                StorefrontRelationSources.SteamStore,
                RelationLabels.Related,
                Kind: null,
                SteamParentAppId: parentAppId);
        }

        if (igdbParent is not null)
        {
            return new StorefrontClaim(
                StorefrontRelationSources.Igdb,
                RelationLabels.Related,
                Kind: null,
                IgdbParentId: igdbParent);
        }

        // Type 0 with no parent, a tool, a piece of music: Steam has classified
        // the app and said nothing about any relation. That is silence, and it
        // is deliberately not treated as speech — Steam is documented-silent on
        // expansions, so reading a plain type 0 as an opinion would mute the
        // title heuristic over the entire Steam library.
        return null;
    }

    private static bool IsPicsType(string? picsType, string expected)
        => !string.IsNullOrEmpty(picsType)
           && string.Equals(picsType, expected, StringComparison.OrdinalIgnoreCase);
}
