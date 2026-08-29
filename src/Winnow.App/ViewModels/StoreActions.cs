using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

/// <summary>
/// Epic's composite launch key: <c>namespace:catalogItemId:artifactId</c>.
/// All three parts are validated against a strict ASCII character class before
/// interpolation into a URI.
/// </summary>
public readonly record struct EpicLaunchKey
{
    private EpicLaunchKey(string ns, string catalogItemId, string artifactId)
    {
        Namespace = ns;
        CatalogItemId = catalogItemId;
        ArtifactId = artifactId;
    }

    public string Namespace { get; }

    public string CatalogItemId { get; }

    public string ArtifactId { get; }

    /// <summary>
    /// The key as the launcher's URL wants it: the three parts joined by a
    /// percent-encoded colon, so the whole composite is ONE path segment.
    /// A literal <c>:</c> here would be legal in a path but is not what Epic
    /// writes, and the shape below is copied from a URL Epic generated itself.
    /// </summary>
    public string PathSegment => $"{Namespace}%3A{CatalogItemId}%3A{ArtifactId}";

    /// <summary>Null unless all three parts are present and are ids we recognise.</summary>
    public static EpicLaunchKey? Create(string? ns, string? catalogItemId, string? artifactId)
        => IsIdLike(ns) && IsIdLike(catalogItemId) && IsIdLike(artifactId)
            ? new EpicLaunchKey(ns!, catalogItemId!, artifactId!)
            : null;

    /// <summary>
    /// ASCII letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, 1–64 characters.
    /// Every observed namespace (32-hex, <c>fn</c>, <c>catnip</c>) and every
    /// observed artifact id (<c>Bluebird</c>, <c>CatnipDLC3</c>, 32-hex) fits;
    /// nothing that fits needs escaping, which is why there is none.
    /// </summary>
    private static bool IsIdLike(string? value)
    {
        if (value is not { Length: > 0 and <= 64 })
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Launch and store URIs for each store (§10.3). All URIs were verified by
/// measurement against the installed launchers, not from documentation.
/// Steam: <c>steam://run|install/appid</c>. Epic: launch only (no install action
/// exists in the binary). GOG: <c>goggalaxy://launchGame|installationScreen</c>.
/// Returns null when no honest action can be offered.
/// </summary>
public static class StoreActions
{
    /// <summary>
    /// Returns the primary Play/Install action for a tile, or null.
    /// <paramref name="installed"/> is three-valued: true/false/null (unknown).
    /// Unknown yields no action rather than a misleading button (§10.3).
    /// </summary>
    public static GameLink? PrimaryFor(
        string store,
        bool? installed,
        string? steamAppId,
        string? gogProductId,
        EpicLaunchKey? epicKey)
    {
        if (installed is not { } onDisk)
        {
            return null;
        }

        return store switch
        {
            ExternalIdProviders.Steam => SteamPrimary(steamAppId, onDisk),
            ExternalIdProviders.Gog => GogPrimary(gogProductId, onDisk),
            ExternalIdProviders.Epic => EpicPrimary(epicKey, onDisk),
            _ => null,
        };
    }

    /// <summary>
    /// Store page, patch notes, launcher shortcuts — everything beside the
    /// primary action. Empty is a normal answer.
    /// </summary>
    public static IReadOnlyList<GameLink> LinksFor(
        string store, string? steamAppId, string? gogProductId)
    {
        var links = new List<GameLink>(2);

        switch (store)
        {
            case ExternalIdProviders.Steam when GameLink.IsSteamAppId(steamAppId):
                Add(links, GameLink.Create(
                    "Store page",
                    $"https://store.steampowered.com/app/{steamAppId}/"));
                Add(links, GameLink.Create(
                    "All patch notes",
                    $"https://store.steampowered.com/news/app/{steamAppId}"));
                break;

            case ExternalIdProviders.Gog when IsGogProductId(gogProductId):
                // The one URI in this file that was watched working end to end.
                // It is named for what it does — it opens Galaxy on the game's
                // page — rather than for what the user might do next from there.
                Add(links, GameLink.Create(
                    "Show in GOG Galaxy",
                    $"{GameLink.GogScheme}://openGameView/{GogReleaseKey(gogProductId!)}",
                    "Open this game's page in GOG Galaxy"));
                break;
        }

        return links;

        static void Add(List<GameLink> into, GameLink? link)
        {
            if (link is not null)
            {
                into.Add(link);
            }
        }
    }

    /// <summary>
    /// A GOG product id as stored in <c>external_ids</c>: plain digits. Observed
    /// values run from <c>1</c> to ten digits; the cap is generous and the point
    /// is the character class, not the length.
    /// </summary>
    public static bool IsGogProductId(string? productId)
        => productId is { Length: > 0 and <= 12 } && productId.All(char.IsAsciiDigit);

    /// <summary>
    /// Galaxy's "game release key" for a GOG-native product: <c>gog_&lt;id&gt;</c>.
    /// The GOG ingest reads this exact key out of Galaxy's own database and
    /// splits the product id off it, so this reassembles what was taken apart.
    /// </summary>
    private static string GogReleaseKey(string productId) => $"gog_{productId}";

    private static GameLink? SteamPrimary(string? appId, bool installed)
    {
        if (!GameLink.IsSteamAppId(appId))
        {
            return null;
        }

        return installed
            ? GameLink.Create(
                "Play",
                $"{GameLink.SteamScheme}://run/{appId}",
                "Launch through Steam",
                GameLinkKind.Play)
            : GameLink.Create(
                "Install",
                $"{GameLink.SteamScheme}://install/{appId}",
                "Start the download in Steam",
                GameLinkKind.Install);
    }

    private static GameLink? GogPrimary(string? productId, bool installed)
    {
        if (!IsGogProductId(productId))
        {
            return null;
        }

        return installed
            ? GameLink.Create(
                "Play",
                $"{GameLink.GogScheme}://launchGame/{GogReleaseKey(productId!)}",
                "Launch through GOG Galaxy",
                GameLinkKind.Play)
            : GameLink.Create(
                "Install",
                $"{GameLink.GogScheme}://installationScreen/{productId}",
                "Open this game's install screen in GOG Galaxy",
                GameLinkKind.Install);
    }

    /// <summary>
    /// Epic has a verified launch and no verified install, so an uninstalled
    /// Epic title gets nothing. See this class's remarks for what was looked at
    /// and what was not there.
    /// </summary>
    private static GameLink? EpicPrimary(EpicLaunchKey? key, bool installed)
        => installed && key is { } launch
            ? GameLink.Create(
                "Play",
                $"{GameLink.EpicScheme}://apps/{launch.PathSegment}?action=launch&silent=true",
                "Launch through the Epic Games Launcher",
                GameLinkKind.Play)
            : null;
}
