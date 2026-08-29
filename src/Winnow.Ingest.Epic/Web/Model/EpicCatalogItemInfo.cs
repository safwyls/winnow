using System.Text.Json.Serialization;
using Winnow.Core.Queries;

namespace Winnow.Ingest.Epic.Web.Model;

/// <summary>
/// What Epic's catalog service says about one owned catalog item: title,
/// categories, and routing ids the library service does not carry.
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
    /// <summary>Whether the entry's categories pass <see cref="EpicGameFilter"/>, or null if no categories.</summary>
    [JsonIgnore]
    public bool? IsGame => Categories.Count == 0 ? null : EpicGameFilter.IsGame(Categories);

    /// <summary>Whether this entry is DLC (has a non-empty parent catalog item id).</summary>
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
