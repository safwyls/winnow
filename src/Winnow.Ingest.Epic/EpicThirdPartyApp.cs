namespace Winnow.Ingest.Epic;

/// <summary>
/// A third-party-managed Epic title that installs through another launcher
/// (e.g. Ubisoft Connect). No <c>.item</c> manifest; install state comes from the
/// delivering launcher's registry key.
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
