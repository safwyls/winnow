namespace Winnow.Enrich.Steam.Model;

/// <summary>
/// One user-defined store tag on an app, as ranked by Steam. Only rank is
/// stored; weight is a per-app normalisation unsuitable for cross-app comparison.
/// </summary>
/// <param name="TagId">Steam's tag id. Resolve to a name via <see cref="SteamTagVocabulary"/>.</param>
/// <param name="Rank">1-based position in Steam's ordering; 1 is the app's top tag.</param>
public sealed record SteamStoreTag(long TagId, int Rank);

/// <summary>
/// What Winnow takes from one <c>IStoreBrowseService/GetItems</c> store item.
///
/// <para>Narrow on purpose. The endpoint also returns short description,
/// developers, publishers, release date, platform/Deck compatibility and asset
/// filenames, and all of that is cached verbatim — but nothing is exposed until
/// something needs it. M1 needs <see cref="Name"/>, to replace the ~600
/// <c>App &lt;appid&gt;</c> placeholders carrying
/// <c>works.name_is_provisional = 1</c>.</para>
/// </summary>
/// <param name="AppId">The Steam appid that was requested.</param>
/// <param name="Name">The store's name for the app. Never empty — an item without one is a miss.</param>
/// <param name="Tags">Steam's top tags in rank order (Steam publishes at most 20).</param>
public sealed record SteamStoreItem(string AppId, string Name, IReadOnlyList<SteamStoreTag> Tags)
{
    /// <summary>Shared empty tag list, so a tagless item allocates nothing.</summary>
    public static readonly IReadOnlyList<SteamStoreTag> NoTags = [];

    /// <summary>
    /// Valve's storefront classification (player modes, features, controller
    /// support). Init property so pre-existing cached bodies still project.
    /// </summary>
    public SteamStoreCategories Categories { get; init; } = SteamStoreCategories.None;
}

/// <summary>
/// The three category lists <c>IStoreBrowseService/GetItems</c> returns on
/// <c>categories</c>. Ids only — resolve them through
/// <see cref="SteamStoreCategoryVocabulary"/>.
///
/// <para>Split the way Valve splits them, and the split is worth keeping: the
/// three lists map onto three different columns of a filter panel (how it is
/// played, what it supports, what you can play it with), and flattening them
/// would mean re-deriving the distinction from the vocabulary on every read.</para>
/// </summary>
/// <param name="PlayerCategoryIds">
/// <c>supported_player_categoryids</c> — single-player, co-op, PvP, MMO,
/// split-screen. Folded onto Winnow's game-mode vocabulary; note that four
/// distinct ids all mean co-op.
/// </param>
/// <param name="FeatureCategoryIds">
/// <c>feature_categoryids</c> — Steam Achievements, Trading Cards, Cloud,
/// Workshop, Remote Play, and (since Valve added them) the accessibility
/// features.
/// </param>
/// <param name="ControllerCategoryIds">
/// <c>controller_categoryids</c> — full or partial controller support, DualSense,
/// Steam Input.
/// </param>
public sealed record SteamStoreCategories(
    IReadOnlyList<int> PlayerCategoryIds,
    IReadOnlyList<int> FeatureCategoryIds,
    IReadOnlyList<int> ControllerCategoryIds)
{
    /// <summary>What an app with no <c>categories</c> block has. Common and normal.</summary>
    public static readonly SteamStoreCategories None = new([], [], []);

    /// <summary>True when Steam said nothing at all about this app's categories.</summary>
    public bool IsEmpty
        => PlayerCategoryIds.Count == 0
           && FeatureCategoryIds.Count == 0
           && ControllerCategoryIds.Count == 0;
}

/// <summary>
/// Steam's category vocabulary from <c>IStoreBrowseService/GetStoreCategories</c>.
/// The only way to turn a category id into a display name. Duplicate display
/// names and unresolved localization tokens are upstream; see migration 0007.
/// </summary>
/// <param name="Names">categoryid → display name.</param>
public sealed record SteamStoreCategoryVocabulary(IReadOnlyDictionary<int, string> Names)
{
    /// <summary>The vocabulary Winnow has when the endpoint gave it nothing.</summary>
    public static readonly SteamStoreCategoryVocabulary Empty = new(new Dictionary<int, string>());

    /// <summary>The display name for a category id, or null when this snapshot has none.</summary>
    public string? NameFor(int categoryId) => Names.GetValueOrDefault(categoryId);
}

/// <summary>
/// The store's whole tag vocabulary from <c>IStoreService/GetTagList</c> — the
/// only way to turn a <see cref="SteamStoreTag.TagId"/> into a display name.
///
/// <para>Fetched and cached, and nothing is built on it yet: the spike verified
/// the tag pipeline works, and the decision on record is that it stays verified
/// rather than becoming a feature.</para>
/// </summary>
/// <param name="VersionHash">
/// Steam's stamp on this snapshot of the vocabulary (e.g. <c>711684454</c>).
/// A caller that wants to know whether the list moved compares this rather than
/// diffing 446 entries.
/// </param>
/// <param name="Names">tagid → display name, in the requested language.</param>
public sealed record SteamTagVocabulary(string VersionHash, IReadOnlyDictionary<long, string> Names)
{
    /// <summary>The vocabulary Winnow has when the endpoint gave it nothing.</summary>
    public static readonly SteamTagVocabulary Empty =
        new(string.Empty, new Dictionary<long, string>());

    /// <summary>The display name for a tag id, or null when this snapshot has none.</summary>
    public string? NameFor(long tagId) => Names.GetValueOrDefault(tagId);
}
