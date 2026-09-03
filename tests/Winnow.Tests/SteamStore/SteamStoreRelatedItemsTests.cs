using Winnow.Core.Identity;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Winnow.Enrich.Steam.Storage;
using Xunit;

namespace Winnow.Tests.SteamStore;

/// <summary>
/// The Steam half of TASK-70.10, and the reason it could
/// ship before the IGDB half: <c>type</c> and <c>related_items</c> arrive with
/// the <c>GetItems</c> query Winnow has always sent — neither has an
/// <c>include_</c> flag — so 954 bodies already in <c>metadata_cache</c> carry
/// the parent pointers, and recovering them costs no HTTP request at all.
///
/// <para>Every fixture here is verbatim from the author's own cache. Nothing in
/// this file reaches the network, and one test proves it by asserting that the
/// handler recorded no request while the pairs came out.</para>
/// </summary>
public sealed class SteamStoreRelatedItemsTests
{
    /// <summary>Answers 404 to everything, so a network read is a visible failure rather than a fixture.</summary>
    private static SteamStoreTestHost OfflineHost(IStoreMetadataCache cache)
        => new(
            (_, _) => throw new InvalidOperationException(
                "This test must not reach the network; the answer is supposed to be in the cache."),
            cache: cache);

    private static async Task<IStoreMetadataCache> CacheOfAsync(params string[] appIds)
    {
        var cache = new InMemoryStoreMetadataCache();
        foreach (var appId in appIds)
        {
            await cache.SetAsync(
                SteamStoreClient.CacheProvider,
                SteamStoreClient.AppCacheKey(appId),
                StoreFixtures.RelatedItemJson(appId),
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        return cache;
    }

    /// <summary>
    /// The headline: the parent pointers come back out of bodies written months
    /// ago, with the network unreachable and the cache long past its TTL.
    /// </summary>
    [Fact]
    public async Task Cached_bodies_yield_their_parent_pointers_with_no_request()
    {
        var cache = await CacheOfAsync("65900", "1875460", "100", "224580", "400430", "42910");
        using var host = OfflineHost(cache);

        var items = await host.Client.GetCachedItemsAsync(
            ["65900", "1875460", "100", "224580", "400430", "42910"]);

        Assert.Empty(host.Handler.Requests);

        // Sid Meier's Civilization V: Demo, type 1, parent 8930.
        Assert.Equal(SteamStoreItemTypes.Demo, items["65900"].StoreType);
        Assert.Equal("8930", items["65900"].Related.ParentAppId);

        // Midnight Ghost Hunt Playtest, type 12, parent 915810.
        Assert.Equal(SteamStoreItemTypes.BetaOrPlaytest, items["1875460"].StoreType);
        Assert.Equal("915810", items["1875460"].Related.ParentAppId);

        // Counter-Strike: Condition Zero Deleted Scenes, type 14, parent 80 —
        // which is Condition Zero, the answer IGDB gives and the one the title
        // heuristic got wrong by filing it under Counter-Strike.
        Assert.Equal(SteamStoreItemTypes.Retired, items["100"].StoreType);
        Assert.Equal("80", items["100"].Related.ParentAppId);

        // Arma II: DayZ Mod, type 2, parent 33930 — Operation Arrowhead, not
        // Arma 2, which is again the parent the heuristic missed.
        Assert.Equal(SteamStoreItemTypes.Mod, items["224580"].StoreType);
        Assert.Equal("33930", items["224580"].Related.ParentAppId);

        // The Vanishing of Ethan Carter Redux, type 4, parent 258520.
        Assert.Equal(SteamStoreItemTypes.Dlc, items["400430"].StoreType);
        Assert.Equal("258520", items["400430"].Related.ParentAppId);

        // Magicka names its own demo: related_items is bidirectional.
        Assert.Equal(["73050"], items["42910"].Related.DemoAppIds);
    }

    /// <summary>
    /// The downward arrays. A base game names its demos and playtests, and both
    /// encodings of the demo pointer — the object array and the flat appid
    /// array — are read, because a body may carry either.
    /// </summary>
    [Fact]
    public async Task A_base_game_names_its_own_demos_and_playtests()
    {
        var cache = await CacheOfAsync("418370", "3107230", "8930");
        using var host = OfflineHost(cache);

        var items = await host.Client.GetCachedItemsAsync(["418370", "3107230", "8930"]);

        // Resident Evil 7 Biohazard carries the demo under BOTH demos and
        // standalone_demos, and under both the object and the flat encoding.
        Assert.Equal(["530620"], items["418370"].Related.DemoAppIds);
        Assert.Equal(["530620"], items["418370"].Related.StandaloneDemoAppIds);

        // Pantheon: Rise of the Fallen names its public test realm.
        Assert.Equal(["4709660"], items["3107230"].Related.PlaytestAppIds);

        Assert.Equal(["65900"], items["8930"].Related.DemoAppIds);
    }

    /// <summary>
    /// Two of the author's 49 parent pointers name the app itself. That is not
    /// a relation, and letting it through would give those works a storefront
    /// opinion about their own relations — which is the one thing that silences
    /// the title heuristic.
    /// </summary>
    [Fact]
    public async Task An_app_that_names_itself_as_its_parent_has_no_parent()
    {
        var cache = await CacheOfAsync("3900");
        using var host = OfflineHost(cache);

        var item = (await host.Client.GetCachedItemsAsync(["3900"]))["3900"];

        Assert.Null(item.Related.ParentAppId);
        Assert.True(item.Related.IsEmpty);
        Assert.Null(StorefrontRelation.Read(new StorefrontFacts
        {
            SteamStoreType = item.StoreType,
            SteamParentAppId = item.Related.ParentAppId,
        }));
    }

    /// <summary>
    /// THE TYPE-DEPENDENT MEANING, which is the correction the diagnosis found.
    /// On a type 1 or 12 the parent is the game the sample belongs to, so the
    /// claim is variant_of. On a type 14 it is the app that REPLACED this one,
    /// so the claim is same_game — a child relation would file a game under its
    /// own replacement. Three of the author's pairs are exactly that shape and
    /// point at works with the same title.
    /// </summary>
    [Fact]
    public async Task The_meaning_of_a_parent_appid_depends_on_the_type()
    {
        var cache = await CacheOfAsync("65900", "1875460", "34450", "224580", "400430");
        using var host = OfflineHost(cache);

        var items = await host.Client.GetCachedItemsAsync(
            ["65900", "1875460", "34450", "224580", "400430"]);

        Assert.Equal(
            (IdentityLinkKinds.VariantOf, RelationLabels.Demo, "8930"),
            Claim(items["65900"]));

        Assert.Equal(
            (IdentityLinkKinds.VariantOf, RelationLabels.Playtest, "915810"),
            Claim(items["1875460"]));

        // Sid Meier's Civilization IV: Warlords, the retail-era appid, pointing
        // at the Steam-era appid of a work with the SAME TITLE.
        Assert.Equal(
            (IdentityLinkKinds.SameGame, RelationLabels.Superseded, "3990"),
            Claim(items["34450"]));

        // A mod keeps its label and claims no kind: Enderal and tModLoader are
        // games you play, and whether DayZ Mod belongs under Operation
        // Arrowhead is the user's call, not this code's.
        Assert.Equal((null, RelationLabels.Mod, "33930"), Claim(items["224580"]));

        Assert.Equal(
            (IdentityLinkKinds.ExpansionOf, RelationLabels.Dlc, "258520"),
            Claim(items["400430"]));
    }

    /// <summary>
    /// The whole point restated as a negative: an appid the cache has never
    /// held produces nothing, and still no request. A cache-only read is not a
    /// fetch with a cache in front of it.
    /// </summary>
    [Fact]
    public async Task An_uncached_appid_yields_nothing_and_still_asks_nobody()
    {
        var cache = await CacheOfAsync("65900");
        using var host = OfflineHost(cache);

        var items = await host.Client.GetCachedItemsAsync(["65900", "999999"]);

        Assert.Empty(host.Handler.Requests);
        Assert.Equal(["65900"], items.Keys.Order(StringComparer.Ordinal));
    }

    private static (string? Kind, string Label, string? Parent) Claim(SteamStoreItem item)
    {
        var claim = StorefrontRelation.Read(new StorefrontFacts
        {
            SteamStoreType = item.StoreType,
            SteamParentAppId = item.Related.ParentAppId,
        });

        Assert.NotNull(claim);
        return (claim.Kind, claim.Label, claim.SteamParentAppId);
    }
}
