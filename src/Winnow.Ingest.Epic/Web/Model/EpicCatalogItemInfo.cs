using System.Text.Json.Serialization;
using Winnow.Core.Queries;

namespace Winnow.Ingest.Epic.Web.Model;

/// <summary>
/// What Epic's catalog service says about one owned catalog item — the two facts
/// the library service does not carry, plus the ids that route to everything
/// else.
///
/// <para><b>Why this type exists at all.</b>
/// <c>/library/api/public/items</c> returns entitlements: <c>namespace</c>,
/// <c>catalogItemId</c>, <c>appName</c>, <c>acquisitionDate</c>, and nothing
/// else. No title and no categories. So the API half of Epic ingest could
/// neither name what it found nor tell a game from an Unreal Engine build, and
/// on the author's library it contributed 29 ownership rows that rendered as
/// <c>App 16a66a9f5630407d923429470bd5c967</c>. The catalog service answers both
/// questions in one call, keyed by exactly the two ids the library service
/// already handed over.</para>
///
/// <para><b>The same shape <c>catcache.bin</c> stores.</b> The local catalog file
/// is the launcher's cache of these very entries, so <see cref="Title"/>,
/// <see cref="Categories"/> and <see cref="AppName"/> are the same fields
/// <see cref="EpicCatalogEntry"/> reads, reached over the network for the items
/// the local file has never held. One difference is worth knowing and is not a
/// bug on either side: <c>catcache.bin</c> stores trademark symbols
/// transliterated to a literal <c>?</c> (<c>epic-gog-local-files.md</c> trap 3),
/// while the service returns the real character — this library's
/// <c>LEGO® Fortnite: Odyssey</c> arrives with a genuine U+00AE. Neither is
/// "corrected" anywhere; both are stored as sent.</para>
/// </summary>
/// <param name="CatalogItemId">The key. Same value as the library service's <c>catalogItemId</c>.</param>
/// <param name="Namespace">Epic sandbox/namespace id, echoed back by the service.</param>
/// <param name="Title">
/// The human title, or null when the entry carried none. <b>Null is "this source
/// has no name for it"</b> and must leave any stored name alone — the local
/// reader is authoritative for what it knows.
/// </param>
/// <param name="Categories">
/// <c>categories[].path</c> in the storefront's own order. Empty when the entry
/// carried no categories, which is "cannot say", never "not a game".
/// </param>
/// <param name="AppName">
/// <c>releaseInfo[0].appId</c> — Epic's per-artifact codename. Never a title
/// ("Bluebird" is Fez), and carried for one reason: it is the id
/// <c>gamesdb.gog.com</c> keys Epic releases on, and therefore the first hop of
/// the only route an Epic title has to IGDB.
/// </param>
/// <param name="MainGameCatalogItemId">
/// The parent catalog item id when this entry is DLC, else null. Read from
/// <c>mainGameItem</c>, falling back to the first element of
/// <c>mainGameItemList</c> — the live response carries both spellings, and
/// which one appears varies by entry.
/// </param>
public sealed record EpicCatalogItemInfo(
    string CatalogItemId,
    string? Namespace,
    string? Title,
    IReadOnlyList<string> Categories,
    string? AppName,
    string? MainGameCatalogItemId)
{
    /// <summary>
    /// Whether the one shared rule admits this as a game, or null when the entry
    /// carried no categories to judge it by.
    ///
    /// <para>Deliberately delegates to <see cref="EpicGameFilter"/> rather than
    /// restating the rule: the local scan uses that predicate to decide what
    /// never becomes a candidate, and the library view uses it to decide what
    /// leaves the games grid. A second copy here is how the two would drift.</para>
    /// </summary>
    [JsonIgnore]
    public bool? IsGame => Categories.Count == 0 ? null : EpicGameFilter.IsGame(Categories);

    /// <summary>
    /// Whether this entry is DLC — a non-empty parent id, the only marker that
    /// works (<c>epic-gog-local-files.md</c> section 4).
    ///
    /// <para><b>Reported, and deliberately not acted on by the non-game
    /// filter.</b> Categories cannot tell DLC from a base game — "Borderlands 3
    /// Bounty of Blood" carries <c>application, games, applications</c> — and
    /// hiding on this flag would also have hidden LEGO Fortnite: Odyssey, which
    /// carries a <c>mainGameItem</c> pointing at Fortnite and 408 minutes of the
    /// user's time. DLC visibility is a separate product question from "is this a
    /// game", and this property exists so that question can be asked later
    /// without a second round trip.</para>
    /// </summary>
    [JsonIgnore]
    public bool IsDlc => EpicGameFilter.IsDlc(MainGameCatalogItemId);

    /// <summary>
    /// The categories in the form migration 0009 stores, or null when there are
    /// none — so that "the service did not classify this" reaches the writer as
    /// a null it will COALESCE away, not as an empty string that would satisfy
    /// "column is filled" forever.
    /// </summary>
    [JsonIgnore]
    public string? CategoriesValue
        => Categories.Count == 0 ? null : EpicGameFilter.Join(Categories);
}
