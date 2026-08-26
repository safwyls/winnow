namespace Hoard.Ingest.Epic;

/// <summary>
/// One entry from <c>Data\Catalog\catcache.bin</c> — the launcher's entitlement
/// catalog, i.e. <b>the owned library, installed or not</b>
/// (docs/spikes/epic-gog-local-files.md section 6).
/// </summary>
/// <param name="CatalogItemId">Catalog item id; matches a manifest's <c>CatalogItemId</c>.</param>
/// <param name="CatalogNamespace">Catalog namespace; matches a manifest's <c>CatalogNamespace</c>.</param>
/// <param name="Title">The human title.</param>
/// <param name="Developer">Developer string as the catalog reports it.</param>
/// <param name="AppName">
/// <c>releaseInfo[0].appId</c>, which is the manifest's <c>AppName</c>. Empty when
/// the entry carries no release info. Taken defensively from index 0: every one of
/// the 73 observed game entries had exactly one element, but nothing guarantees it.
/// </param>
/// <param name="Categories">
/// <c>categories[].path</c> — the same vocabulary a manifest's <c>AppCategories</c>
/// uses. See <see cref="EpicGameFilter.IsGame"/>.
/// </param>
/// <param name="MainGameCatalogItemId">
/// <c>mainGameItem.id</c>: non-empty means this entry is DLC. Empty string on a
/// base game, never absent.
/// </param>
/// <param name="MainGameNamespace"><c>mainGameItem.namespace</c>, same convention.</param>
/// <param name="ThirdPartyManagedProvider">
/// <c>customAttributes.ThirdPartyManagedProvider</c> — non-empty (e.g.
/// <c>UbisoftConnect</c>) for an Epic-owned title that installs through another
/// launcher and therefore never gets a <c>.item</c> manifest.
/// </param>
/// <param name="RegistryPath">
/// <c>customAttributes.RegistryPath</c>. The third-party launcher's own install
/// record, which is the only place a third-party-managed title's install state
/// can be read from.
/// </param>
/// <param name="RegistryKey"><c>customAttributes.RegistryKey</c> — the value name under <paramref name="RegistryPath"/>.</param>
/// <param name="CoverImageUrl">
/// URL of the portrait cover (<c>DieselGameBoxTall</c>) when the entry has one.
/// Free cover art with no network call at ingest time; unused by v1's ingest but
/// carried because throwing it away means fetching it again later.
/// </param>
public sealed record EpicCatalogEntry(
    string CatalogItemId,
    string CatalogNamespace,
    string Title,
    string Developer,
    string AppName,
    IReadOnlyList<string> Categories,
    string MainGameCatalogItemId,
    string MainGameNamespace,
    string ThirdPartyManagedProvider,
    string RegistryPath,
    string RegistryKey,
    string? CoverImageUrl)
{
    /// <summary>True when <see cref="EpicGameFilter.IsGame"/> admits this entry's categories.</summary>
    public bool IsGame => EpicGameFilter.IsGame(Categories);

    /// <summary>
    /// True when this entry is DLC. Note that <c>dlcItemList</c> on the parent is
    /// <c>[]</c> on every observed entry and must never be used for this.
    /// </summary>
    public bool IsDlc => EpicGameFilter.IsDlc(MainGameCatalogItemId);

    /// <summary>True when this title installs through another launcher.</summary>
    public bool IsThirdPartyManaged => ThirdPartyManagedProvider.Length > 0;
}
