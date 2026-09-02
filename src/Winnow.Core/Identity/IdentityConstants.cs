namespace Winnow.Core.Identity;

/// <summary>Valid <see cref="IdentityLink.Kind"/> values (CHECK-constrained in the schema).</summary>
public static class IdentityLinkKinds
{
    /// <summary>
    /// The two releases are the same game sold twice (Steam Prey and Epic Prey).
    /// Changes IDENTITY: the child contributes no additional title to the library
    /// count; its playtime is the same game being played; its releases nest under
    /// the parent per section 6.2; a feed dismissal of one side suppresses the other;
    /// the sweep must never propose the pair again.
    /// </summary>
    public const string SameGame = "same_game";

    /// <summary>
    /// The child is an expansion, DLC or standalone pack of the parent (Civilization
    /// IV and Beyond the Sword). Changes PRESENTATION ONLY: the child is a separate
    /// product, separately owned and separately played. No count, playtime, bucket
    /// or recommendation is folded. Summing 30h of Civilization IV with 120h of
    /// Beyond the Sword produces a number no source reported about either, and
    /// collapsing an unplayed expansion into a played parent destroys probably the
    /// best recommendation the app can make.
    /// </summary>
    public const string ExpansionOf = "expansion_of";

    /// <summary>
    /// The child is a sample or test build of the parent: a demo, a beta, a
    /// playtest, a staging or experimental branch. Changes COUNTS conditionally:
    /// it does not count as a title while its parent is owned, and it does count
    /// when it is the only thing owned. Playtime never rolls up, but the
    /// variant's own hours stay visible on the parent's modal, because "you
    /// played forty minutes of the demo and never bought it" is the app's
    /// premise rather than noise. This is
    /// <see cref="Queries.DemoConsolidation"/>'s read-time rule made into a
    /// stored fact with a storefront source behind it (migration 0021).
    /// </summary>
    public const string VariantOf = "variant_of";

    /// <summary>Every valid kind, for validation loops.</summary>
    public static readonly IReadOnlyList<string> All = [SameGame, ExpansionOf, VariantOf];
}

/// <summary>
/// The vocabulary <see cref="IdentityLink.RelationLabel"/> carries: the source's
/// own word for the relation, so a card can say "Demo" or "Remaster" or
/// "Standalone expansion" while only three kinds exist. Kinds are defined by the
/// numbers they change and cost a table rebuild each (migration 0021); labels are
/// vocabulary and cost nothing. IGDB has fifteen game_type names today and will
/// add more.
/// </summary>
public static class RelationLabels
{
    /// <summary>IGDB game_type <c>dlc_addon</c>. Kind: expansion_of.</summary>
    public const string Dlc = "dlc";

    /// <summary>IGDB game_type <c>expansion</c>. Kind: expansion_of.</summary>
    public const string Expansion = "expansion";

    /// <summary>IGDB game_type <c>standalone_expansion</c>. Kind: expansion_of.</summary>
    public const string StandaloneExpansion = "standalone expansion";

    /// <summary>IGDB game_type <c>episode</c>. Kind: expansion_of.</summary>
    public const string Episode = "episode";

    /// <summary>IGDB game_type <c>season</c>. Kind: expansion_of.</summary>
    public const string Season = "season";

    /// <summary>IGDB game_type <c>pack</c>. Kind: expansion_of.</summary>
    public const string Pack = "pack";

    /// <summary>IGDB game_type <c>expanded_game</c>. Numerically identical to expansion_of, semantically an edition.</summary>
    public const string ExpandedGame = "expanded game";

    /// <summary>IGDB game_type <c>remaster</c>. Claims no kind and refutes an expansion: a remaster is the same game built again, adding nothing to group.</summary>
    public const string Remaster = "remaster";

    /// <summary>IGDB game_type <c>remake</c>. Claims no kind and refutes an expansion: a remake is the same game built again, adding nothing to group.</summary>
    public const string Remake = "remake";

    /// <summary>IGDB game_type <c>port</c>. Claims no kind and refutes an expansion: a port is the same game built again, adding nothing to group.</summary>
    public const string Port = "port";

    /// <summary>IGDB game_type <c>fork</c>. Numerically identical to expansion_of, semantically an edition.</summary>
    public const string Fork = "fork";

    /// <summary>IGDB game_type <c>bundle</c>. Recorded with no kind: a bundle contains games, it is not a child of one.</summary>
    public const string Bundle = "bundle";

    /// <summary>IGDB game_type <c>update</c>. Recorded with no kind: a patch entity, not a product anyone owns separately.</summary>
    public const string Update = "update";

    /// <summary>
    /// IGDB game_type <c>mod</c>, or Steam store type 2. Recorded, never
    /// auto-folded: Enderal and tModLoader are games you play, not add-ons,
    /// and whether DayZ Mod belongs under Operation Arrowhead is the user's
    /// call, not this code's.
    /// </summary>
    public const string Mod = "mod";

    /// <summary>Steam store type 1, or PICS <c>common.type</c> Demo. Kind: variant_of.</summary>
    public const string Demo = "demo";

    /// <summary>PICS <c>common.type</c> Beta. Kind: variant_of. Verified 2026-09-02: three works in the author's database carry this value.</summary>
    public const string Beta = "beta";

    /// <summary>Steam store type 12 (beta/playtest build). Kind: variant_of.</summary>
    public const string Playtest = "playtest";

    /// <summary>
    /// Steam store type 14 (retired). The parent appid on a retired app names
    /// the app that REPLACED it, so the claim is same_game, not a child
    /// relation. Three of the author's pairs are exactly this shape.
    /// </summary>
    public const string Superseded = "superseded";

    /// <summary>
    /// IGDB game_type <c>main_game</c>. Not a relation but the absence of one,
    /// and the refutation that kills nine of the measured sequel false positives
    /// on its own. A main_game with no parent_game extends nothing, whatever
    /// its title happens to start with.
    /// </summary>
    public const string MainGame = "main game";

    /// <summary>
    /// A parent pointer with no type to explain it. The relation is real enough
    /// to refute a heuristic that names a different base and not specific enough
    /// to name a kind.
    /// </summary>
    public const string Related = "related";
}

/// <summary>Valid <see cref="IdentityLink.Source"/> values (CHECK-constrained in the schema).</summary>
public static class IdentityLinkSources
{
    /// <summary>A person answered a question. A user's answer cannot be re-derived.</summary>
    public const string User = "user";

    /// <summary>A shared external identifier joined without review (SS5.3 step 1). Can be re-derived from enrichment data.</summary>
    public const string HardId = "hard_id";
}

/// <summary>Valid <see cref="IdentityAct.Kind"/> values (CHECK-constrained in the schema).</summary>
public static class IdentityActKinds
{
    /// <summary>One or more children linked to a parent.</summary>
    public const string Link = "link";

    /// <summary>A prior act retracted, restoring every child it displaced.</summary>
    public const string Unlink = "unlink";
}
