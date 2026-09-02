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

    /// <summary>Every valid kind, for validation loops.</summary>
    public static readonly IReadOnlyList<string> All = [SameGame, ExpansionOf];
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
