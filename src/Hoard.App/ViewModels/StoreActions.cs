using Hoard.Core.Domain;

namespace Hoard.App.ViewModels;

/// <summary>
/// Epic's composite launch key: <c>namespace : catalogItemId : artifactId</c>.
///
/// <para>Epic addresses a title by all three, never by one. The catalog item id
/// is the stable identity and the only part <c>external_ids</c> stores; the
/// namespace qualifies it and the artifact id ("AppName") names the specific
/// release — a codename such as <c>Bluebird</c>, never a title.</para>
///
/// <para>Every part is validated here rather than trusted, for
/// <see cref="GameLink"/>'s reason: these three strings arrive from a cached
/// network payload, and a URL is not a place to interpolate an unchecked
/// string. The character class is deliberately narrower than "escape it" —
/// every value this app has ever seen for these fields is 32-hex or a short
/// word, so an id outside that set is a surprise, and the honest response to a
/// surprise is no link rather than a link built out of it.</para>
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
/// How each store is reached, and the evidence for it.
///
/// <para>§10.3 gives the rule this exists to generalise: the primary affordance
/// is <b>named for which one it is</b>, <c>Play</c> or <c>Install</c>, because a
/// button reading "Play" on an uninstalled 60GB game promises something the next
/// hour will not deliver. Until now that rule was implemented for Steam only and
/// the method returned early without an appid, so 99 Epic titles and 14 GOG
/// titles got no primary action and no links at all.</para>
///
/// <para><b>Nothing here came from a blog post.</b> The design doc's §10 says to
/// resolve launcher questions empirically because the widely-circulated answers
/// are out of date, and every URI below carries what was actually measured on a
/// machine with all three launchers installed. Where a measurement could not be
/// made, the method returns <c>null</c> and the tile draws no button: a button
/// that silently does nothing is worse than an absent one, and is the exact
/// failure §10.3 already forbids for a missing appid.</para>
///
/// <list type="bullet">
///   <item><b>Steam.</b> <c>steam://run/&lt;appid&gt;</c> and
///     <c>steam://install/&lt;appid&gt;</c>, unchanged, and the shape Steam's own
///     Start Menu shortcuts use.</item>
///   <item><b>Epic — launch.</b>
///     <c>com.epicgames.launcher://apps/&lt;ns&gt;%3A&lt;cid&gt;%3A&lt;artifact&gt;?action=launch&amp;silent=true</c>.
///     Taken from a shortcut <i>the Epic Games Launcher wrote itself</i> (Fez, on
///     the author's desktop), whose catalog item id matches this database's
///     <c>external_ids</c> row for Fez exactly; and cross-checked against the
///     literals <c>com.epicgames.launcher://apps/</c> and
///     <c>?action=launch&amp;silent=true</c> inside
///     <c>EpicGamesLauncher.exe</c>.</item>
///   <item><b>Epic — install. There is none, so there is no button.</b> The
///     launcher's binary contains no <c>action=install</c> at all: the only
///     actions it carries are <c>launch</c>, <c>installer</c>, <c>updatecheck</c>
///     and <c>verify</c>, and its only other URL routes are <c>settings</c>,
///     <c>enterprise</c>, <c>twinmotion</c>, <c>ue/marketplace/items/</c> and
///     <c>social/notification/</c> — no store route, and nothing that takes a
///     catalog id to an install. An uninstalled Epic title therefore gets no
///     primary action, which is the same answer §10.3 already gives a Steam
///     release with no appid.</item>
///   <item><b>GOG.</b> <c>goggalaxy://launchGame/gog_&lt;productId&gt;</c> and
///     <c>goggalaxy://installationScreen/&lt;productId&gt;</c>. Galaxy dispatches
///     <c>goggalaxy://&lt;command&gt;/&lt;argument&gt;</c> against a fixed command
///     table — <c>launchGame</c>, <c>installGame</c>, <c>installDlc</c>,
///     <c>focusGame</c>, <c>installationScreen</c>, <c>refreshGame</c>,
///     <c>openStoreUrl</c>, <c>openGameView</c> — read out of
///     <c>GalaxyClient.exe</c> beside its own "No handler for protocol command"
///     and "Handling protocol command" messages. Each argument type is attested
///     by the client's own validation text: "Launch game view command failed to
///     convert '{}' to a GRK" for <c>launchGame</c> (a GRK is
///     <c>gog_&lt;productId&gt;</c>, which is exactly the key the GOG ingest
///     splits its provider id out of), and "Product ID for the installation
///     screen command cannot be empty" for <c>installationScreen</c>.</item>
/// </list>
///
/// <para><b>The end-to-end proof, and its limit.</b> The scheme, the dispatch
/// and the GRK grammar were confirmed by firing
/// <c>goggalaxy://openGameView/gog_&lt;bogus id&gt;</c> at the running client and
/// reading its log echo the command back, parse the GRK and note the product was
/// not installed — with a product id nobody owns, so nothing could start.
/// Repeating it as <c>goggalaxy://opengameview/...</c> behaved identically, which
/// is the reason the lowercasing <see cref="System.Uri"/> does to the authority
/// is harmless here. The two commands that would actually start something were
/// NOT fired, because the brief for this change forbids launching or installing
/// anything; their command names and argument types rest on the client's own
/// strings above.</para>
///
/// <para><b>Secondary links.</b> Steam keeps its store page and patch-notes hub,
/// both built from the appid. GOG additionally gets <c>Show in GOG Galaxy</c> —
/// the one URI here that was watched working end to end — so even a GOG title
/// whose install command turned out to be inert still has a route into the
/// launcher that says exactly what it does. Epic gets none: a store URL needs a
/// product slug, and nothing in this database holds one.</para>
/// </summary>
public static class StoreActions
{
    /// <summary>
    /// The one filled affordance for a tile, or null when this app cannot name
    /// one honestly.
    ///
    /// <para><b><paramref name="installed"/> is three-valued and the third value
    /// is not a quieter "no".</b> <c>true</c> is "a source looked and it is on
    /// disk", <c>false</c> is "a source looked and it is not", and <c>null</c> is
    /// "nothing looked" — which is neither, and must never be folded into
    /// <c>false</c>. Folding it once already cost this project the entire
    /// library's install state, and it would cost this button its honesty: an
    /// "Install" on a game that is already on disk sends the user to a download
    /// they do not need, and it is the same lie as a "Play" on one that is
    /// not.</para>
    ///
    /// <para>So an unknown install state renders <b>no primary action at
    /// all</b>. That is not a shrug — it is the rule §10.3 already applies to a
    /// release with no appid, applied to a release with no answer: this button
    /// is a promise about the next sixty seconds, and a promise nothing is
    /// backing does not get made. The back of the tile still carries Add to list
    /// and Details, so the card is never actionless.</para>
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
            ? GameLink.Create("Play", $"{GameLink.SteamScheme}://run/{appId}", "Launch through Steam")
            : GameLink.Create("Install", $"{GameLink.SteamScheme}://install/{appId}", "Start the download in Steam");
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
                "Launch through GOG Galaxy")
            : GameLink.Create(
                "Install",
                $"{GameLink.GogScheme}://installationScreen/{productId}",
                "Open this game's install screen in GOG Galaxy");
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
                "Launch through the Epic Games Launcher")
            : null;
}
