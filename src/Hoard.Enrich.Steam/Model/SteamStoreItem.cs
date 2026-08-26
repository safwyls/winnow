namespace Hoard.Enrich.Steam.Model;

/// <summary>
/// One user-defined store tag on an app, as ranked by Steam.
///
/// <para><b>Rank, not weight, is the stored signal.</b> The spike measured
/// <c>weight</c> against the store page's raw vote counts for the same app and
/// found a constant per-app ratio (7.032–7.037 across all 20 tags) with
/// byte-identical rank order: it is a per-app normalisation, comparable
/// <i>within</i> an app and meaningless <i>across</i> apps. A number that looks
/// cross-comparable but is not is worse than no number, so it does not appear
/// here. The raw weights survive verbatim in the cached response body
/// (<c>metadata_cache</c>) if a future feature ever needs them.</para>
/// </summary>
/// <param name="TagId">Steam's tag id. Resolve to a name via <see cref="SteamTagVocabulary"/>.</param>
/// <param name="Rank">1-based position in Steam's ordering; 1 is the app's top tag.</param>
public sealed record SteamStoreTag(long TagId, int Rank);

/// <summary>
/// What Hoard takes from one <c>IStoreBrowseService/GetItems</c> store item.
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
    /// Valve's own storefront classification of this app: how many people can
    /// play it, which Steam features it uses, what controllers it supports.
    ///
    /// <para><b>Already in the response, and already in the cache.</b> Verified
    /// live on 2026-08-25 against the exact <c>data_request</c> the spike
    /// established and this client has always sent: <c>categories</c> comes back
    /// as a sibling of <c>tags</c> with no extra flag, no extra request and no
    /// key. Every app body <c>metadata_cache</c> already holds contains it —
    /// re-reading them is a local parse, not a fetch.</para>
    ///
    /// <para>An init property rather than a positional parameter so that a
    /// cached body written before anything read this field still projects.</para>
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
/// split-screen. Folded onto Hoard's game-mode vocabulary; note that four
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
/// Steam's category vocabulary from <c>IStoreBrowseService/GetStoreCategories</c>
/// — the only way to turn a category id into a display name, and the exact
/// counterpart of <see cref="SteamTagVocabulary"/>.
///
/// <para><b>Verified live on 2026-08-25</b>, resolving the open question the tag
/// spike left: keyless, one request, 16 KB, 72 categories, and it needs no
/// <c>data_request</c> flag because the ids were already arriving. This is the
/// whole "Features" and "Hardware support" vocabulary of the reference filter
/// panel for one HTTP GET a month.</para>
///
/// <para><b>Duplicate display names are Valve's, not a bug here.</b> Ids 55 and
/// 56 are both "DualShock Controller Support" (wired and Bluetooth), 57 and 58
/// are both "DualSense Controller Support", and 30 and 51 are both "Steam
/// Workshop" (global and Steam China). Anything keying on the NAME collapses
/// them, which is the right answer for a checkbox list — migration 0007 does
/// exactly that and says so.</para>
///
/// <para><b>Some names are unresolved localization tokens.</b> Three categories
/// answer with <c>display_name</c> values like
/// <c>#category_playable_at_your_own_pace</c>; the endpoint simply failed to
/// localize them. <see cref="NameFor"/> falls back to <c>internal_name</c>, which
/// for those three is a perfectly good English phrase ("Playable at Your Own
/// Pace").</para>
/// </summary>
/// <param name="Names">categoryid → display name.</param>
public sealed record SteamStoreCategoryVocabulary(IReadOnlyDictionary<int, string> Names)
{
    /// <summary>The vocabulary Hoard has when the endpoint gave it nothing.</summary>
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
    /// <summary>The vocabulary Hoard has when the endpoint gave it nothing.</summary>
    public static readonly SteamTagVocabulary Empty =
        new(string.Empty, new Dictionary<long, string>());

    /// <summary>The display name for a tag id, or null when this snapshot has none.</summary>
    public string? NameFor(long tagId) => Names.GetValueOrDefault(tagId);
}
