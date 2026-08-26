namespace Hoard.Ingest.Epic;

/// <summary>
/// The two classification rules that separate a playable Epic title from the
/// engine builds, marketplace assets, tools and cosmetic entitlements that share
/// the launcher's catalog (docs/spikes/epic-gog-local-files.md section 4).
///
/// <para>Both rules read the same vocabulary from two places: a manifest's
/// <c>AppCategories</c> array and a catalog entry's <c>categories[].path</c>.
/// They were verified to be the same list for the same title.</para>
/// </summary>
public static class EpicGameFilter
{
    /// <summary>Category marking a store product rather than an engine or asset.</summary>
    public const string GamesCategory = "games";

    /// <summary>Category marking something the launcher can install and run.</summary>
    public const string ApplicationsCategory = "applications";

    /// <summary>
    /// <b>Is this a game?</b> The categories must contain BOTH <c>games</c> and
    /// <c>applications</c>.
    ///
    /// <para>Measured over all 297 catalog entries on a real account: this admits
    /// all 73 game entries and rejects Unreal Engine (<c>engines</c>,
    /// <c>engines/ue5</c>), Twinmotion and other tools (<c>applications</c> +
    /// <c>software</c>, no <c>games</c>), Fab/marketplace assets
    /// (<c>assets</c>, <c>asset-format/…</c>, <c>type/asset</c>) and — the single
    /// largest group at 114 entries — the cosmetic and entitlement-only add-ons
    /// that carry only <c>audience</c> and <c>public</c>. Skipping this filter
    /// fills the library with junk.</para>
    /// </summary>
    public static bool IsGame(IReadOnlyCollection<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var games = false;
        var applications = false;
        foreach (var category in categories)
        {
            games |= string.Equals(category, GamesCategory, StringComparison.OrdinalIgnoreCase);
            applications |= string.Equals(category, ApplicationsCategory, StringComparison.OrdinalIgnoreCase);
        }

        return games && applications;
    }

    /// <summary>
    /// <b>Is this DLC?</b> The parent catalog item id is non-empty. Nothing else
    /// works.
    ///
    /// <para>Two traps live here. First, categories cannot tell: the Borderlands 3
    /// DLC "Bounty of Blood" carries <c>application, games, applications</c> and
    /// so looks exactly like a base game. Second, <c>dlcItemList</c> on the parent
    /// is <c>[]</c> on all 297 entries including base games that demonstrably have
    /// DLC — resolve parentage bottom-up from the child, never top-down from a
    /// list. Note also that <c>MainGame*</c> arrives as an <i>empty string</i> on
    /// a base game rather than as a missing key, so this tests for emptiness, not
    /// for absence.</para>
    /// </summary>
    public static bool IsDlc(string? mainGameCatalogItemId)
        => !string.IsNullOrWhiteSpace(mainGameCatalogItemId);
}
