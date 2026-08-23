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
