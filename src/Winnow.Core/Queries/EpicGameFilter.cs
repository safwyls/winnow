namespace Winnow.Core.Queries;

/// <summary>
/// The two classification rules that separate a playable Epic title from the
/// engine builds, marketplace assets, tools and cosmetic entitlements that share
/// the launcher's catalog (docs/spikes/epic-gog-local-files.md section 4).
///
/// <para>Both rules read the same vocabulary from three places, all verified to
/// be the same list for the same title: a manifest's <c>AppCategories</c> array,
/// a <c>catcache.bin</c> entry's <c>categories[].path</c>, and — added
/// 2026-08-26 — the same <c>categories[].path</c> on Epic's authenticated
/// <c>catalog/api/shared/namespace/{ns}/bulk/items</c> response.</para>
///
/// <para><b>Why this lives in <c>Winnow.Core</c> rather than in the Epic ingest
/// module.</b> It used to sit beside the readers, which was fine while its only
/// caller was the local scan that drops non-games before they ever become
/// candidates. It now has a second caller that cannot drop anything: the library
/// view's non-game filter (<see cref="NonGameEntries"/>), which decides whether
/// an ownership row the user genuinely holds belongs in the games grid. Those two
/// callers must agree by construction. Restating "games + applications" a second
/// time in the query layer would be the parallel notion of "not a game" that this
/// codebase must not grow — one rule, two callers, no copy.</para>
/// </summary>
public static class EpicGameFilter
{
    /// <summary>Category marking a store product rather than an engine or asset.</summary>
    public const string GamesCategory = "games";

    /// <summary>Category marking something the launcher can install and run.</summary>
    public const string ApplicationsCategory = "applications";

    /// <summary>
    /// The separator Epic itself uses when it flattens a category list to a
    /// string: the <c>.item</c> manifest's <c>TechnicalType</c> is
    /// <c>"public,games,applications"</c> — comma-joined, no spaces. Winnow stores
    /// the list the same way (migration 0009) rather than inventing a
    /// representation, and no observed category path contains a comma.
    /// </summary>
    public const char CategorySeparator = ',';

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
    ///
    /// <para>Re-measured 2026-08-26 against the authenticated catalog service,
    /// over the 29 entitlements the API had contributed to the library that the
    /// local <c>catcache.bin</c> could not name: it admits 3 (an ARK map pack, a
    /// Borderlands 3 campaign add-on, and LEGO Fortnite: Odyssey — which has 408
    /// minutes of recorded play) and rejects 26 (Unreal Engine 4.0 and two Chaos
    /// preview builds, the Infinity Blade asset packs, Soul: Cave, the Kite open-world
    /// demo, the Action RPG sample project, two <c>hidden</c> Fortnite content
    /// entitlements and a Civilization VI DLC).</para>
    /// </summary>
    public static bool IsGame(IReadOnlyCollection<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var games = false;
        var applications = false;
        foreach (var category in categories)
        {
            games |= string.Equals(category?.Trim(), GamesCategory, StringComparison.OrdinalIgnoreCase);
            applications |= string.Equals(category?.Trim(), ApplicationsCategory, StringComparison.OrdinalIgnoreCase);
        }

        return games && applications;
    }

    /// <summary>
    /// The same question asked of the stored, comma-joined form (migration
    /// 0009).
    ///
    /// <para><b>Three-valued, and it has to be.</b> <c>null</c> means <i>nobody
    /// has read this item's categories</i> — an Epic work named from
    /// <c>catcache.bin</c> before the column existed, or one the catalog service
    /// declined to answer for. That is not evidence either way, and the caller
    /// must not turn it into one. Only a non-empty list is an answer.</para>
    /// </summary>
    public static bool? IsGame(string? commaJoinedCategories)
        => Split(commaJoinedCategories) is { Count: > 0 } categories ? IsGame(categories) : null;

    /// <summary>
    /// Splits the stored form back into paths, dropping blanks. Never null; an
    /// unreadable or absent value yields an empty list, which
    /// <see cref="IsGame(string?)"/> reports as "cannot say".
    /// </summary>
    public static IReadOnlyList<string> Split(string? commaJoinedCategories)
        => string.IsNullOrWhiteSpace(commaJoinedCategories)
            ? []
            : commaJoinedCategories
                .Split(CategorySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Joins category paths into the stored form. Order is the storefront's own,
    /// preserved rather than sorted, so the column keeps saying what Epic said.
    /// </summary>
    public static string Join(IEnumerable<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        return string.Join(
            CategorySeparator,
            categories
                .Where(static c => !string.IsNullOrWhiteSpace(c))
                .Select(static c => c.Trim().Replace(CategorySeparator, ' ')));
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
