namespace Hoard.Ingest.Epic;

/// <summary>
/// One <c>Data\ThirPartyManagedApps\&lt;ns&gt;_&lt;catalogId&gt;_&lt;appName&gt;.json</c>
/// file (docs/spikes/epic-gog-local-files.md section 7) — an Epic-owned title
/// that installs and runs through a different launcher, observed here as
/// <c>UbisoftConnect</c> for Watch Dogs and For Honor.
///
/// <para><b>These titles never get a <c>.item</c> manifest.</b> An Epic reader
/// that walks only <c>Manifests\</c> misses them entirely. They do appear in
/// <c>catcache.bin</c>, so ownership is already covered; this file exists to
/// resolve their <i>install</i> state, by naming the other launcher's own
/// registry record.</para>
///
/// <para>Note the key casing differs from the <c>.item</c> manifest:
/// <c>CatalogID</c> (capital D) here versus <c>CatalogItemId</c> there, and
/// <c>Namespace</c> versus <c>CatalogNamespace</c>.</para>
/// </summary>
/// <param name="CatalogItemId">From the <c>CatalogID</c> key; joins to a catalog entry's <c>id</c>.</param>
/// <param name="CatalogNamespace">From the <c>Namespace</c> key.</param>
/// <param name="AppName">Epic's codename for the artifact.</param>
/// <param name="Title">The human title — this file does carry one.</param>
/// <param name="Provider">Delivering launcher, e.g. <c>UbisoftConnect</c>.</param>
/// <param name="RegistryPath">
/// The provider's install key, e.g.
/// <c>SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\274</c>. Rooted at HKLM.
/// </param>
/// <param name="RegistryKey">Value name under <paramref name="RegistryPath"/> holding the install directory.</param>
/// <param name="GameId">The provider's own id for the title.</param>
/// <param name="FilePath">Absolute path of the file this was read from.</param>
public sealed record EpicThirdPartyApp(
    string CatalogItemId,
    string CatalogNamespace,
    string AppName,
    string Title,
    string Provider,
    string RegistryPath,
    string RegistryKey,
    string GameId,
    string FilePath);
