namespace Hoard.Core.Queries;

/// <summary>
/// Which Steam app types are not games, for the library view's "show non-game
/// entries" filter (<see cref="BucketThresholds.ShowNonGameEntries"/>).
///
/// <para><b>What this is for.</b> A Steam library is not only games: it also
/// carries dedicated servers, SDKs and level editors, soundtracks, videos and
/// the occasional hardware entry. Those are real things the user owns, but they
/// are not what this application is about, so the library view hides them by
/// default and offers a toggle to show them. Four entries in the author's
/// library are typed <c>Tool</c> — <c>SteamVR Performance Test</c>,
/// <c>Skyrim Creation Kit</c>, <c>Eco Server</c>, <c>Palworld Dedicated
/// Server</c> — and every one of them is clutter beside 600 games.</para>
///
/// <para><b>Derived, never stored.</b> Like the buckets and like demo
/// consolidation, this is applied inside the §6.1 read query. Nothing is
/// written, nothing is deleted, no ownership is touched: flipping the setting
/// changes what the next read returns and needs no re-sync, and every hidden
/// row stays fully reachable through every other repository.</para>
///
/// <para><b>NULL is "not known", never "not a game"</b> — migration 0006's
/// central warning, and the one way this filter could do real damage. Most of
/// the library has never been probed, and five appids in the author's library
/// answer <c>_missing_token</c> with no <c>common</c> object at all. An unknown
/// type is therefore always visible: hiding hundreds of real games because
/// nobody read their type would be the worst possible failure of this
/// feature.</para>
///
/// <para><b>Demos are emphatically not in this set.</b> Thirty-three entries in
/// the measured library are typed <c>Demo</c>, and they are already handled —
/// correctly and reversibly — by <see cref="DemoConsolidation"/>: hidden when
/// the full game is owned, visible when they are the only copy the user has. A
/// solitary demo IS a game the user can play, and this filter must never take a
/// second, blunter position on it.</para>
///
/// <para><b>Precision over recall (§5.3).</b> The set below is a closed list of
/// values that cannot describe a game under any reading, not a "not
/// <c>Game</c>" complement. Valve's vocabulary is undocumented and grows without
/// notice, so an unrecognised value — a type this build has never seen — stays
/// visible exactly like an unknown one. A tool left on screen is cosmetic
/// clutter; a game hidden from the user's own library is a lie about what they
/// own.</para>
/// </summary>
public static class NonGameEntries
{
    /// <summary>
    /// The app types hidden when <see cref="BucketThresholds.ShowNonGameEntries"/>
    /// is off. Compared case-insensitively after trimming, because the
    /// service's casing is not stable (migration 0006: Bastion answers
    /// <c>game</c>, Monster Hunter Wilds answers <c>Game</c>) and the stored
    /// value is deliberately verbatim.
    ///
    /// <para>Adding a value is the whole extension mechanism: append the
    /// lower-case string here and the next read honours it, with no migration
    /// and no re-sync, because nothing about this decision is stored.</para>
    ///
    /// <para>Why each one is here:</para>
    /// <list type="bullet">
    ///   <item><c>tool</c> — dedicated servers, SDKs, editors, benchmarks. All
    ///     four measured occurrences in the author's library are of this
    ///     kind.</item>
    ///   <item><c>application</c> — non-game software that happens to ship on
    ///     Steam.</item>
    ///   <item><c>config</c> — Valve's internal configuration depots, which are
    ///     not a product at all.</item>
    ///   <item><c>music</c> — soundtrack appids, which appear beside their game
    ///     as a second tile.</item>
    ///   <item><c>video</c>, <c>movie</c>, <c>episode</c>, <c>series</c>,
    ///     <c>media</c> — Steam's video catalogue. Films and documentaries are
    ///     watched, not played.</item>
    ///   <item><c>hardware</c> — Steam Deck, Index, Steam Link and friends,
    ///     which land in the library as owned entries.</item>
    /// </list>
    ///
    /// <para>Deliberately ABSENT, and each for a reason:</para>
    /// <list type="bullet">
    ///   <item><c>demo</c> — see the class remarks. Consolidation owns
    ///     demos.</item>
    ///   <item><c>dlc</c> — not a game either, but the measured library
    ///     contains none as an owned tile, so hiding it would be a guess about
    ///     a case nobody has seen. It is the first candidate if one ever shows
    ///     up.</item>
    ///   <item><c>mod</c> — a standalone Source mod is something the user
    ///     plays.</item>
    ///   <item><c>beta</c> — Valve publishes no such type (migration 0006
    ///     measured every beta in the library answering <c>Game</c>), and
    ///     pre-release handouts are consolidation's job regardless.</item>
    ///   <item>Anything else Valve invents — unrecognised means visible.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool",
        "application",
        "config",
        "music",
        "video",
        "movie",
        "episode",
        "series",
        "media",
        "hardware",
    };

    /// <summary>
    /// The hidden types, for anything that wants to name them — a tooltip on
    /// the toggle, a test, a diagnostic. Ordinal-insensitive membership is
    /// <see cref="IsNonGame"/>'s job; this is the list, not the predicate.
    /// </summary>
    public static IReadOnlyCollection<string> HiddenTypes { get; } = [.. Hidden.Order(StringComparer.Ordinal)];

    /// <summary>
    /// True when <paramref name="steamAppType"/> is a type this application
    /// hides by default.
    ///
    /// <para>False for null, empty and whitespace — the unprobed and the
    /// unreadable are "not known", and the safe answer to "not known" is to
    /// show the row. False, too, for any value not in
    /// <see cref="HiddenTypes"/>, including one Valve has not invented yet.</para>
    ///
    /// <para>Trimmed before comparison so a stray space in a stored value
    /// cannot flip the answer, and compared with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> because the casing of the
    /// stored value is Valve's and is not stable.</para>
    /// </summary>
    public static bool IsNonGame(string? steamAppType)
        => !string.IsNullOrWhiteSpace(steamAppType) && Hidden.Contains(steamAppType.Trim());
}
