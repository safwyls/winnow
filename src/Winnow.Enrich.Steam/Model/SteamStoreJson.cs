using System.Globalization;
using System.Text.Json;

namespace Winnow.Enrich.Steam.Model;

/// <summary>
/// Everything that knows the wire shape of the two undocumented store-frontend
/// endpoints, kept in one file so the blast radius of a Valve-side change is a
/// single type — and so the fixture contract test has one place to point at.
///
/// <para>These endpoints appear in no documentation and in no
/// <c>GetSupportedAPIList</c> response. Every reader here therefore returns
/// null/empty on anything it does not recognise instead of throwing: a shape
/// change must degrade to "no data", never to an exception in an enrichment
/// pass (§5.1).</para>
/// </summary>
internal static class SteamStoreJson
{
    /// <summary>
    /// Store JSON is snake_case, and Steam mixes numeric and string encodings for
    /// numbers within the same object (<c>final_price_in_cents</c> arrives as a
    /// string while <c>weight</c> arrives as a number), so string-to-number
    /// reading is on everywhere.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary><c>success</c> value meaning "this item exists on the store".</summary>
    private const int SuccessOk = 1;

    /// <summary>
    /// Builds the <c>input_json</c> value for a <c>GetItems</c> batch — exactly
    /// the query the spike verified live, including the <c>data_request</c>
    /// fields it named. The extra blocks (<c>basic_info</c>, <c>release</c>,
    /// <c>platforms</c>, <c>assets</c>) cost about a kilobyte per app and land in
    /// the cache verbatim, which is the cheap half of "fetch once, decide later".
    /// </summary>
    internal static string BuildGetItemsQuery(IReadOnlyList<string> appIds, SteamStoreOptions options)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("ids");
            foreach (var appId in appIds)
            {
                writer.WriteStartObject();
                writer.WriteNumber("appid", long.Parse(appId, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartObject("context");
            writer.WriteString("language", options.Language);
            writer.WriteString("country_code", options.CountryCode);
            writer.WriteNumber("steam_realm", options.SteamRealm);
            writer.WriteEndObject();

            writer.WriteStartObject("data_request");
            writer.WriteNumber("include_tag_count", options.TagCount);
            writer.WriteBoolean("include_basic_info", true);
            writer.WriteBoolean("include_assets", true);
            writer.WriteBoolean("include_release", true);
            writer.WriteBoolean("include_platforms", true);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The <c>input_json</c> value for the tag vocabulary.</summary>
    internal static string BuildTagListQuery(SteamStoreOptions options)
        => JsonSerializer.Serialize(new { language = options.Language }, Options);

    /// <summary>
    /// The <c>input_json</c> value for the store-category vocabulary. Same shape
    /// as the tag list: a language and nothing else.
    /// </summary>
    internal static string BuildStoreCategoriesQuery(SteamStoreOptions options)
        => JsonSerializer.Serialize(new { language = options.Language }, Options);

    /// <summary>
    /// Splits a <c>GetItems</c> body into <c>id</c> → the raw JSON of that store
    /// item, so each app can be cached and re-parsed independently of the batch
    /// it arrived in.
    ///
    /// <para>Keyed on <c>id</c>, never <c>appid</c>: an item Steam cannot serve
    /// comes back as <c>{"id":760,"appid":0,"success":15,…}</c>, so <c>appid</c>
    /// is not a usable correlation key. Position is not one either — the spike
    /// warns never to assume 1:1 request/response alignment.</para>
    /// </summary>
    /// <returns>Null when the envelope is not the shape this client understands.</returns>
    internal static IReadOnlyDictionary<string, string>? TryReadStoreItems(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("store_items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && TryReadId(id) is { } key)
                {
                    raw[key] = item.GetRawText();
                }
            }

            return raw;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Projects one cached store item onto <see cref="SteamStoreItem"/>. The
    /// cache-hit path and the fresh-response path both go through here, so a
    /// warm read and a cold read cannot disagree.
    /// </summary>
    /// <returns>
    /// Null when Steam answered but has nothing to offer for this appid —
    /// <c>success</c> other than 1, or no usable name. That is a real answer and
    /// may be cached as a miss; it is not a failure.
    /// </returns>
    internal static SteamStoreItem? TryParseItem(string appId, string rawItemJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawItemJson);
            var item = document.RootElement;
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!item.TryGetProperty("success", out var success)
                || success.ValueKind != JsonValueKind.Number
                || success.GetInt32() != SuccessOk)
            {
                return null;
            }

            if (!item.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString()))
            {
                return null;
            }

            return new SteamStoreItem(appId, name.GetString()!, ReadTags(item))
            {
                Categories = ReadCategories(item),
                StoreType = ReadStoreType(item),
                Related = ReadRelatedItems(item),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads <c>tags</c> in the order Steam returned it and turns position into
    /// <see cref="SteamStoreTag.Rank"/>. The array is already sorted by
    /// descending <c>weight</c>; ordering is re-applied here anyway so rank
    /// stays correct if that ever stops being true.
    /// </summary>
    private static IReadOnlyList<SteamStoreTag> ReadTags(JsonElement item)
    {
        if (!item.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            // Perfectly normal: an app with no user tags yet, and the shape a
            // non-store item comes back with.
            return SteamStoreItem.NoTags;
        }

        var weighted = new List<(long TagId, long Weight, int Position)>(tags.GetArrayLength());
        var position = 0;
        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.Object
                || !tag.TryGetProperty("tagid", out var tagId)
                || TryReadInt64(tagId) is not { } id)
            {
                continue;
            }

            var weight = tag.TryGetProperty("weight", out var w) ? TryReadInt64(w) ?? 0 : 0;
            weighted.Add((id, weight, position++));
        }

        if (weighted.Count == 0)
        {
            return SteamStoreItem.NoTags;
        }

        return weighted
            .OrderByDescending(t => t.Weight)
            .ThenBy(t => t.Position)
            .Select((t, index) => new SteamStoreTag(t.TagId, index + 1))
            .ToArray();
    }

    /// <summary>
    /// Reads the <c>categories</c> block — Valve's own answer to "how is this
    /// played, what does it support, what can you play it with".
    ///
    /// <para>No <c>data_request</c> flag turns this on: it arrives with the query
    /// <see cref="BuildGetItemsQuery"/> has always sent, which means every store
    /// body already in <c>metadata_cache</c> carries it and re-reading them costs
    /// nothing. Verified live 2026-08-25.</para>
    ///
    /// <para>An app with no block at all is normal, not an error — free tools and
    /// delisted apps commonly have none, and an app with only
    /// <c>supported_player_categoryids</c> is the single commonest shape in the
    /// author's library.</para>
    /// </summary>
    private static SteamStoreCategories ReadCategories(JsonElement item)
    {
        if (!item.TryGetProperty("categories", out var categories)
            || categories.ValueKind != JsonValueKind.Object)
        {
            return SteamStoreCategories.None;
        }

        var players = ReadCategoryIds(categories, "supported_player_categoryids");
        var features = ReadCategoryIds(categories, "feature_categoryids");
        var controllers = ReadCategoryIds(categories, "controller_categoryids");

        return players.Count == 0 && features.Count == 0 && controllers.Count == 0
            ? SteamStoreCategories.None
            : new SteamStoreCategories(players, features, controllers);
    }

    /// <summary>
    /// Reads <c>StoreItem.type</c>, the numeric kind enum. Arrives with the
    /// query <see cref="BuildGetItemsQuery"/> has always sent, so every cached
    /// body already carries it. Absent on some items, which is null and not
    /// zero; zero is a real value meaning game.
    /// </summary>
    private static int? ReadStoreType(JsonElement item)
        => item.TryGetProperty("type", out var type) && TryReadInt64(type) is { } value
           && value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : null;

    /// <summary>
    /// Reads <c>related_items</c> per Valve's <c>webui/common.proto</c>
    /// <c>StoreItem_RelatedItems</c>. Upward pointers (<c>parent_appid</c>,
    /// <c>dlc_parent_appids</c>) and downward arrays (<c>demos</c>,
    /// <c>standalone_demos</c>, <c>playtests</c>) both. The demo and playtest
    /// arrays hold objects carrying appid plus label and show_above_purchase;
    /// only the appid is taken. The two flat <c>demo_appid</c> /
    /// <c>standalone_demo_appid</c> arrays the proto also defines are read
    /// alongside the object arrays, because a body may carry either encoding.
    /// </summary>
    private static SteamStoreRelatedItems ReadRelatedItems(JsonElement item)
    {
        if (!item.TryGetProperty("related_items", out var related)
            || related.ValueKind != JsonValueKind.Object)
        {
            return SteamStoreRelatedItems.None;
        }

        // An app that names ITSELF as its parent is saying nothing. Two of the
        // author's 49 parent pointers are exactly that (appid 3900 Civilization
        // IV and appid 6980 Thief: Deadly Shadows). Letting a self-reference
        // through would give those works a storefront opinion about their own
        // relations, which is the one thing that silences the title heuristic.
        var ownAppId = item.TryGetProperty("appid", out var own) ? TryReadInt64(own) : null;

        var parent = related.TryGetProperty("parent_appid", out var parentAppId)
                     && TryReadInt64(parentAppId) is { } id and > 0
                     && id != ownAppId
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;

        var demos = ReadAppIds(related, "demos", "demo_appid");
        var standaloneDemos = ReadAppIds(related, "standalone_demos", "standalone_demo_appid");
        var playtests = ReadAppIds(related, "playtests", null);
        var dlcParents = ReadAppIds(related, null, "dlc_parent_appids");

        return parent is null
               && demos.Count == 0
               && standaloneDemos.Count == 0
               && playtests.Count == 0
               && dlcParents.Count == 0
            ? SteamStoreRelatedItems.None
            : new SteamStoreRelatedItems(parent, demos, standaloneDemos, playtests, dlcParents);
    }

    /// <summary>
    /// Reads the union of an object array carrying appid fields and a flat
    /// array of appid values, either of which may be absent. Order is preserved
    /// and duplicates are dropped.
    /// </summary>
    private static IReadOnlyList<string> ReadAppIds(
        JsonElement related, string? objectArray, string? flatArray)
    {
        List<string>? ids = null;

        if (objectArray is not null
            && related.TryGetProperty(objectArray, out var objects)
            && objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in objects.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("appid", out var appId)
                    && TryReadInt64(appId) is { } id and > 0)
                {
                    Add(ref ids, id);
                }
            }
        }

        if (flatArray is not null
            && related.TryGetProperty(flatArray, out var flat)
            && flat.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in flat.EnumerateArray())
            {
                if (TryReadInt64(element) is { } id and > 0)
                {
                    Add(ref ids, id);
                }
            }
        }

        return ids is null ? [] : ids;

        static void Add(ref List<string>? ids, long id)
        {
            var text = id.ToString(CultureInfo.InvariantCulture);
            ids ??= [];
            if (!ids.Contains(text, StringComparer.Ordinal))
            {
                ids.Add(text);
            }
        }
    }

    private static IReadOnlyList<int> ReadCategoryIds(JsonElement categories, string property)
    {
        if (!categories.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<int>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            // Category ids are small, but they are read through the same
            // string-or-number path as everything else here: Steam mixes the two
            // encodings within one object and has no obligation to be consistent
            // about which fields it mixes.
            if (TryReadInt64(element) is { } id and >= int.MinValue and <= int.MaxValue)
            {
                ids.Add((int)id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Reads a <c>GetStoreCategories</c> body into the categoryid → name map.
    ///
    /// <para>Falls back to <c>internal_name</c> when <c>display_name</c> is an
    /// unresolved localization token. Three categories answer with one today
    /// (<c>#category_playable_at_your_own_pace</c> and friends) and rendering a
    /// checkbox labelled with a hash and an underscore string would be worse than
    /// rendering Valve's internal wording, which reads fine.</para>
    /// </summary>
    /// <returns>Null when the envelope is not the shape this client understands.</returns>
    internal static SteamStoreCategoryVocabulary? TryReadStoreCategories(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("categories", out var categories)
                || categories.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var names = new Dictionary<int, string>();
            foreach (var category in categories.EnumerateArray())
            {
                if (category.ValueKind != JsonValueKind.Object
                    || !category.TryGetProperty("categoryid", out var id)
                    || TryReadInt64(id) is not { } categoryId
                    || categoryId is < int.MinValue or > int.MaxValue)
                {
                    continue;
                }

                if (DisplayName(category) is { Length: > 0 } name)
                {
                    names[(int)categoryId] = name;
                }
            }

            // A vocabulary with no words is a shape change wearing a 200, not a
            // real answer — the same reading TryReadTagList takes, for the same
            // reason: refusing it keeps an empty map out of the cache.
            return names.Count == 0 ? null : new SteamStoreCategoryVocabulary(names);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? DisplayName(JsonElement category)
    {
        var display = category.TryGetProperty("display_name", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith('#'))
        {
            return display;
        }

        var internalName = category.TryGetProperty("internal_name", out var i) && i.ValueKind == JsonValueKind.String
            ? i.GetString()
            : null;

        return string.IsNullOrWhiteSpace(internalName) ? display : internalName;
    }

    /// <summary>
    /// Reads a <c>GetTagList</c> body into the tagid → name map.
    /// </summary>
    /// <returns>Null when the envelope is not the shape this client understands.</returns>
    internal static SteamTagVocabulary? TryReadTagList(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("tags", out var tags)
                || tags.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var names = new Dictionary<long, string>();
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.Object
                    && tag.TryGetProperty("tagid", out var tagId)
                    && TryReadInt64(tagId) is { } id
                    && tag.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() is { Length: > 0 } text)
                {
                    names[id] = text;
                }
            }

            if (names.Count == 0)
            {
                // A vocabulary with no words is a shape change wearing a 200,
                // not a real answer. Refusing it keeps an empty map out of the
                // cache for the next 30 days.
                return null;
            }

            var versionHash = response.TryGetProperty("version_hash", out var hash)
                && hash.ValueKind == JsonValueKind.String
                    ? hash.GetString() ?? string.Empty
                    : string.Empty;

            return new SteamTagVocabulary(versionHash, names);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The <c>id</c> of a store item as its cache key, whatever encoding it arrived in.</summary>
    private static string? TryReadId(JsonElement element)
        => TryReadInt64(element)?.ToString(CultureInfo.InvariantCulture);

    private static long? TryReadInt64(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt64(out var value) ? value : null,
        JsonValueKind.String => long.TryParse(
            element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
        _ => null,
    };
}
