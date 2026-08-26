using System.Globalization;
using System.Text.Json;

namespace Hoard.Tests.SteamStore;

/// <summary>
/// The verbatim store responses captured for
/// <c>docs/spikes/steam-store-tags.md</c> (see
/// tests/fixtures/steam-store/README.md), plus generators that answer a request
/// for arbitrary appids in the same shape.
///
/// <para>The generators read the <c>input_json</c> they are answering, so a
/// response only ever contains items the request actually asked for — which is
/// what makes the batching assertions meaningful rather than circular.</para>
/// </summary>
internal static class StoreFixtures
{
    /// <summary>Elden Ring, Dota 2, TF2, and appid 760 (a non-store app), as captured.</summary>
    internal const string EldenRingAppId = "1245620";

    internal const string DotaAppId = "570";

    internal const string TeamFortressAppId = "440";

    /// <summary>Steam Screenshots: exists as an appid, is not a store item. The graceful-failure case.</summary>
    internal const string NonStoreAppId = "760";

    /// <summary>tagid → <c>Souls-like</c>; Elden Ring's rank-1 tag and one §4.3 names by example.</summary>
    internal const long SoulsLikeTagId = 29482;

    /// <summary>tagid → <c>Roguelike Deckbuilder</c>; the other tag §4.3 names by example.</summary>
    internal const long RoguelikeDeckbuilderTagId = 1091588;

    private static string PathOf(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "steam-store", fileName);

    /// <summary>The real <c>IStoreBrowseService/GetItems</c> response, byte for byte.</summary>
    internal static string GetItemsResponse() => File.ReadAllText(PathOf("getitems-v1.json"));

    /// <summary>The real <c>IStoreService/GetTagList</c> response, byte for byte.</summary>
    internal static string TagListResponse() => File.ReadAllText(PathOf("gettaglist-v1.json"));

    /// <summary>
    /// The real <c>IStoreBrowseService/GetStoreCategories</c> response, byte for
    /// byte — the vocabulary the category ids in <c>getitems-v1.json</c> resolve
    /// through.
    /// </summary>
    internal static string StoreCategoriesResponse() => File.ReadAllText(PathOf("getstorecategories-v1.json"));

    /// <summary>
    /// One captured store item's raw JSON — the exact bytes
    /// <see cref="Hoard.Enrich.Steam.SteamStoreClient"/> writes into
    /// <c>metadata_cache</c> for that appid, and therefore the exact bytes a
    /// cache-only read parses back.
    /// </summary>
    internal static string CapturedItemJson(string appId)
    {
        var id = long.Parse(appId, CultureInfo.InvariantCulture);
        using var document = JsonDocument.Parse(GetItemsResponse());
        return document.RootElement.GetProperty("response").GetProperty("store_items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt64() == id)
            .GetRawText();
    }

    /// <summary>Steam Achievements. A feature category (<c>type: 2</c>).</summary>
    internal const int AchievementsCategoryId = 22;

    /// <summary>Full controller support. A controller category (<c>type: 3</c>).</summary>
    internal const int FullControllerCategoryId = 28;

    /// <summary>Single-player. A player category (<c>type: 1</c>), and Elden Ring's first.</summary>
    internal const int SinglePlayerCategoryId = 2;

    /// <summary>
    /// Two of Valve's duplicate display names: 55 (wired) and 56 (Bluetooth) are
    /// both "DualShock Controller Support". Anything keyed on the NAME collapses
    /// them, which is what migration 0007 does on purpose.
    /// </summary>
    internal const int Ps4ControllerCategoryId = 55;

    internal const int Ps4ControllerBluetoothCategoryId = 56;

    /// <summary>
    /// A <c>GetItems</c> response for whatever <paramref name="request"/> asked
    /// for, shaped like the captured one: <c>id</c> mirrors the request,
    /// <c>tags</c> carry descending weights, and an appid in
    /// <paramref name="nonStoreAppIds"/> comes back the way 760 really does —
    /// <c>success: 15</c>, <c>appid: 0</c>, empty name, no tags.
    /// </summary>
    internal static string GetItemsFor(
        RecordedStoreRequest request, ISet<string>? nonStoreAppIds = null, int tagCount = 20)
    {
        var items = request.RequestedAppIds.Select(appId =>
            nonStoreAppIds is not null && nonStoreAppIds.Contains(appId)
                ? NonStoreItem(appId)
                : StoreItem(appId, tagCount));

        return Envelope(new { store_items = items });
    }

    /// <summary>
    /// The tag ids this fixture puts on an app, in the order Steam would return
    /// them (highest weight first), so a test can assert rank without hard-coding
    /// the generator's arithmetic.
    /// </summary>
    internal static IReadOnlyList<long> ExpectedTagIds(string appId, int tagCount = 20)
        => Enumerable.Range(0, tagCount)
            .Select(i => long.Parse(appId, CultureInfo.InvariantCulture) * 100 + i)
            .ToArray();

    internal static string ExpectedName(string appId) => "Store Title " + appId;

    private static object StoreItem(string appId, int tagCount)
    {
        var tagIds = ExpectedTagIds(appId, tagCount);
        var id = long.Parse(appId, CultureInfo.InvariantCulture);

        return new
        {
            item_type = 0,
            id,
            success = 1,
            visible = true,
            name = ExpectedName(appId),
            store_url_path = $"app/{appId}/Store_Title_{appId}",
            appid = id,

            // Descending, as Steam returns them: the first entry is rank 1.
            tags = tagIds.Select((tagId, index) => new { tagid = tagId, weight = 1000 - (index * 10) }),
            tagids = tagIds,
        };
    }

    /// <summary>
    /// The captured shape of a non-store app. Note <c>appid: 0</c> alongside a
    /// real <c>id</c> — correlating on <c>appid</c> would lose the request.
    /// </summary>
    private static object NonStoreItem(string appId) => new
    {
        item_type = 0,
        id = long.Parse(appId, CultureInfo.InvariantCulture),
        success = 15,
        visible = false,
        name = string.Empty,
        store_url_path = "app/0/",
        appid = 0,
    };

    /// <summary>A tag vocabulary covering everything <see cref="GetItemsFor"/> can emit.</summary>
    internal static string TagListFor(IEnumerable<string> appIds, string versionHash = "711684454", int tagCount = 20)
    {
        var tags = appIds
            .SelectMany(appId => ExpectedTagIds(appId, tagCount))
            .Distinct()
            .Select(tagId => new { tagid = tagId, name = "Tag " + tagId.ToString(CultureInfo.InvariantCulture) });

        return Envelope(new { version_hash = versionHash, tags });
    }

    internal static string Envelope(object response)
        => JsonSerializer.Serialize(new { response }, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new();
}
