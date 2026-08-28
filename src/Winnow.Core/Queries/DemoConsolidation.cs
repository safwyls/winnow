using Winnow.Core.Matching;

namespace Winnow.Core.Queries;

/// <summary>
/// One owned release, as <see cref="DemoConsolidation"/> needs to see it: the
/// title to classify, Valve's own classification of it, and the two facts that
/// can veto a binding.
/// </summary>
/// <remarks>
/// Only releases the user actually owns are ever passed in. That is not an
/// optimisation — it is the rule. Consolidation hides a demo because the full
/// game is <i>in the library</i>; a base release with no ownership produces no
/// tile, so hiding the demo behind it would hide the only copy the user has.
/// </remarks>
public sealed record DemoConsolidationEntry
{
    /// <summary><c>releases.id</c>.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>
    /// The title to classify: <c>releases.name</c>, falling back to the work
    /// name when the release row is blank — the same choice
    /// <see cref="ReleaseIdentity.MatchTitle"/> makes, and for the same reason.
    /// The release is the layer that carries the edition.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// True for a machine-minted placeholder (<c>App 1203620</c>). Excluded
    /// from both sides: a placeholder is evidence about nothing.
    /// </summary>
    public bool NameIsProvisional { get; init; }

    /// <summary><c>works.first_release_year</c>, or null when unenriched.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary>
    /// <c>works.steam_app_type</c> — Valve's <c>common.type</c> for the appid
    /// (migration 0006), or null when nothing has read it.
    ///
    /// <para>Null is "not known", never "not a demo": several appids in the
    /// author's library answer <c>_missing_token</c> with no <c>common</c>
    /// object at all, and those rows fall back to the title gate.</para>
    /// </summary>
    public string? SteamAppType { get; init; }
}

/// <summary>
/// Decides which owned entries are redundant because they are a demo, beta or
/// playtest of a game the user also owns — the read-time half of "one tile per
/// game, not one tile per build the publisher handed out".
///
/// <para><b>Derived, never stored.</b> This is a pure function of what is
/// currently in the library, run inside the derived-bucket query (§6.1) exactly
/// like every bucket. Nothing is written, nothing is deleted, no release is
/// re-parented: the demo keeps its ownership, its play records, its snapshots
/// and its history, and every one of them stays queryable. Remove the base game
/// from the library and the next read simply finds no base for it, so it comes
/// back. That reversibility is the whole reason this is a query and not a
/// column (§6.1, and the charter's "stored derived values rot").</para>
///
/// <para><b>Why not re-parent the demo Release onto the base Work.</b> The
/// four-layer model (§5.3) would carry it — a demo really is another Release of
/// the same Work — but writing that link is an auto-merge on a title heuristic,
/// which is the one thing §5.3 forbids outright and §9 ranks as the second most
/// likely way this project fails. §5.3 permits auto-merge only on a hard
/// external-id join. A stored re-parent would also be one-way: nothing would
/// un-merge the Work when the user later removed the base game. So the
/// Work-level grouping is computed on read instead of written down — the same
/// relationship, minus the destructive write.</para>
///
/// <para><b>Playtime is never merged.</b> A demo is its own Steam appid with
/// its own minutes. Consolidation suppresses a <i>tile</i>; it does not add,
/// blend or transfer a single minute, which would be §6.2's "two facts, not one
/// average" all over again. The suppressed entry's numbers stay exactly where
/// they were, on its own ownership.</para>
///
/// <para><b>Two gates, and both must pass.</b> Gate one asks "is this entry a
/// variant handout rather than a game?"; gate two asks "does the library
/// contain the game it is a handout OF?". A mis-classification at gate one is
/// therefore nearly harmless: a real game wrongly called a demo is only hidden
/// if the library <i>also</i> holds a differently-named release that normalises
/// to exactly its title minus the marker. Precision over recall (§5.3): a demo
/// left visible is a cosmetic miss, a hidden game the user owns is a lie about
/// their library.</para>
///
/// <para><b>Gate one — the type, then the title.</b> Two signals, combined in
/// the one way the measured data supports. Both were checked against
/// <c>api.steamcmd.net/v1/info/{appid}</c> for 85 appids of the author's real
/// library on 2026-08-24 (see migration 0006 for the full distribution):</para>
/// <list type="number">
///   <item><b><c>common.type</c> is Valve's own classification and wins where
///     it speaks.</b> <c>Demo</c> marks the entry a variant whatever the title
///     says — which is the only way to catch <c>FINAL FANTASY XIV Online Free
///     Trial</c> (typed <c>Demo</c>, parent 39210) and <c>Wild Terra 2: New
///     Lands - Free Weekend</c> (typed <c>Demo</c>, parent 1134700), neither of
///     which carries a <c>demo</c> token. Comparison is case-insensitive
///     because the service's casing is not stable: Bastion answers <c>game</c>,
///     Monster Hunter Wilds answers <c>Game</c>.</item>
///   <item><b>But <c>Game</c> is not a denial, because Valve has no beta or
///     playtest type.</b> Measured: <c>Call of Duty: WWII - PC Open Beta</c>,
///     <c>PUBG: Test Server</c>, <c>New World Public Test Realm</c> and
///     <c>Gatewalkers (Alpha)</c> are all typed <c>Game</c>. So a <c>Game</c>
///     type vetoes only the <c>demo</c> marker — where Valve demonstrably would
///     have said <c>Demo</c> and did not — and says nothing about the beta,
///     playtest and test markers, which it has no vocabulary to express.</item>
///   <item><b>Unknown type falls back to the title alone</b>, and that fallback
///     is not an edge case. The two entries that prompted this — <c>Monster
///     Hunter Wilds Beta test</c> (3065170) and <c>BitCraft Online Playtest</c>
///     (3562740) — both answer HTTP 200 with <c>"_missing_token": true</c> and
///     no <c>common</c> object at all, as do 8510, 854040 and 1883690.
///     <c>appdetails</c> refuses the same appids. Without a Steam Web API key
///     the title is all there is.</item>
///   <item><b>Non-game types other than <c>Demo</c> are left visible.</b>
///     <c>Tool</c>, <c>Application</c>, <c>Config</c>, <c>Music</c> and the rest
///     are not variants of an owned game — a tile for <c>SteamVR Performance
///     Test</c> or <c>Palworld Dedicated Server</c> is arguably clutter, but
///     hiding it is a different product decision (a "non-game entries" filter),
///     not a consolidation, and §5.3 says the safe default is to show
///     it.</item>
/// </list>
///
/// <para><b>Gate two — an exact key match against an owned base.</b> Never a
/// similarity score. Equality is over <see cref="TitleNormalizer"/>'s
/// comparable core plus its rebuild-edition markers, which is what makes
/// <c>Portal Demo</c> fail to bind to <c>Portal 2</c> (cores <c>portal</c> vs
/// <c>portal 2</c>) and <c>Bastion Demo</c> fail to bind to <c>Bastion
/// Remastered</c> (§9 pitfall 5: a rebuild is a different Release). Bundle
/// markers are deliberately not part of the key — <c>Batman: Arkham Asylum -
/// Game of the Year Edition</c> is the full game, so the demo folds into it.
/// The base must itself be a non-variant: a demo never supersedes a beta, and a
/// beta never supersedes a demo.</para>
///
/// <para><b>Why IGDB cannot do gate one.</b> Verified against the live IGDB v4
/// API (August 2026): the <c>game_types</c> endpoint has fifteen values and
/// none of them is "Demo" — the demos IGDB does track are filed as game_type 4,
/// Standalone Expansion, the same value as <c>Far Cry 3: Blood Dragon</c>.
/// <c>parent_game</c> exists but is only reachable once the release has an IGDB
/// id, and Steam demo appids do not have one: <c>external_games</c> filtered to
/// the Steam source returns nothing for 107110, 73050, 1458040 or 35020, and on
/// the author's real library not one demo work carries an <c>igdb_id</c>. That
/// is why the classification comes from Steam's own PICS data instead.</para>
/// </summary>
public static class DemoConsolidation
{
    /// <summary>
    /// Separates the core from the edition markers inside a key. U+001F cannot
    /// occur in a normalised title — normalisation turns every
    /// non-alphanumeric character into a space — so no title can forge the
    /// key of another.
    /// </summary>
    private const char KeyDelimiter = '\u001F';

    /// <summary>
    /// How far apart two known release years may be and still be one game.
    /// ±1 because storefront and IGDB years disagree across a new year's eve
    /// and across regional releases — the same tolerance §5.3 gives the
    /// soft matcher's year signal.
    /// </summary>
    private const int YearTolerance = 1;

    /// <summary>Valve's <c>common.type</c> for a demo. Compared case-insensitively.</summary>
    private const string DemoType = "demo";

    /// <summary>
    /// Valve's <c>common.type</c> for the ordinary case. Named so the one thing
    /// it is allowed to veto — a <c>demo</c> marker Valve would have typed
    /// <c>Demo</c> — reads as a decision rather than a string literal.
    /// </summary>
    private const string GameType = "game";

    /// <summary>
    /// The <c>demo</c> marker, kept separate from the rest because it is the
    /// only one whose absence from <c>common.type</c> is informative.
    /// </summary>
    private static readonly string[][] DemoMarkerPhrases =
    [
        ["demo"],
    ];

    /// <summary>
    /// Trailing token runs marking a pre-release or limited handout, matched
    /// longest-first and only at the END of a normalised title with at least
    /// one token left in front.
    ///
    /// <para><b>Suffix-only, and tokenised, for the same reason the demo marker
    /// always was.</b> Tokenising is what protects real titles:
    /// <c>Demonologist</c>, <c>Demon's Souls</c> and <c>Alpha Protocol</c> never
    /// produce a bare marker token in trailing position, and a title that is
    /// nothing but the marker is not a handout of anything. A LEADING marker is
    /// left alone on purpose — <c>Demo Disc: Spectral Mall</c> and <c>Platinum
    /// Demo: Final Fantasy XV</c> are standalone releases, not a suffix on a
    /// base title.</para>
    ///
    /// <para><b>Every phrase here was read off a real Steam entry</b>, not
    /// imagined: <c>Monster Hunter Wilds Beta test</c>, <c>BitCraft Online
    /// Playtest</c>, <c>Call of Duty: WWII - PC Open Beta</c>, <c>PUBG: Test
    /// Server</c>, <c>New World Public Test Realm</c>, <c>Gatewalkers
    /// (Alpha)</c>, <c>FINAL FANTASY XIV Online Free Trial</c>, <c>Wild Terra 2:
    /// New Lands - Free Weekend</c>. Bare <c>test</c> is deliberately ABSENT —
    /// <c>The Turing Test</c> is a game — and so is bare <c>trial</c> and bare
    /// <c>prologue</c>, both of which name standalone releases far too often to
    /// strip on sight.</para>
    /// </summary>
    private static readonly string[][] PrereleaseMarkerPhrases =
    [
        ["beta"],
        ["beta", "test"],
        ["open", "beta"],
        ["open", "beta", "test"],
        ["closed", "beta"],
        ["closed", "beta", "test"],
        ["public", "beta"],
        ["playtest"],
        ["play", "test"],
        ["alpha"],
        ["alpha", "test"],
        ["closed", "alpha"],
        ["open", "alpha"],
        ["public", "test"],
        ["public", "test", "realm"],
        ["test", "realm"],
        ["test", "server"],
        ["network", "test"],
        ["technical", "test"],
        ["free", "trial"],
        ["free", "weekend"],
    ];

    /// <summary>
    /// True when the title carries a demo marker — i.e. it normalises to at
    /// least one token followed by <c>demo</c>. Says nothing about whether the
    /// base game is owned; that is the second gate.
    /// </summary>
    public static bool IsDemoTitle(string? title)
        => TryReadMarker(TitleNormalizer.Normalize(title), DemoMarkerPhrases, out _);

    /// <summary>
    /// True when the title carries any variant marker — demo, beta, playtest,
    /// test realm, free weekend. A superset of <see cref="IsDemoTitle"/>.
    ///
    /// <para>Used by the enrichment pass to decide which appids are worth
    /// asking steamcmd.net about for their <c>common.type</c>: the type only
    /// ever changes an outcome for entries this class is going to reason about,
    /// so the rest are never asked.</para>
    /// </summary>
    public static bool IsVariantTitle(string? title)
    {
        var normalized = TitleNormalizer.Normalize(title);
        return TryReadMarker(normalized, DemoMarkerPhrases, out _)
               || TryReadMarker(normalized, PrereleaseMarkerPhrases, out _);
    }

    /// <summary>
    /// Maps each redundant variant release to the owned release that supersedes
    /// it. A variant with no owned base is absent from the result and stays
    /// fully visible; so is every ordinary release.
    /// </summary>
    /// <param name="owned">
    /// Every release the user owns at least one copy of, with its title and
    /// Valve's type for it. Order is irrelevant to the outcome — the base of an
    /// ambiguous key is chosen by lowest release id, so repeated runs over the
    /// same library return the same map (the pass is a pure function, and
    /// therefore idempotent across syncs by construction).
    /// </param>
    public static IReadOnlyDictionary<long, long> Consolidate(
        IEnumerable<DemoConsolidationEntry> owned)
    {
        ArgumentNullException.ThrowIfNull(owned);

        // Two passes over one materialised list: every release has to be
        // classified before any variant can be matched, because the base of the
        // first demo may be the last row read.
        var bases = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        var variants = new List<Candidate>();

        foreach (var entry in owned)
        {
            if (entry.NameIsProvisional)
            {
                continue;
            }

            var normalized = TitleNormalizer.Normalize(entry.Title);
            if (normalized.IsEmpty)
            {
                continue;
            }

            // ParsedYear first: a year IN the title ("Prey (2006)") is a
            // deliberate disambiguation by whoever wrote it, and outranks the
            // work's enriched year.
            var year = normalized.ParsedYear ?? entry.FirstReleaseYear;

            if (TryReadBaseKey(normalized, entry.SteamAppType, out var baseKey))
            {
                variants.Add(new Candidate(entry.ReleaseId, baseKey, year));
                continue;
            }

            var key = Key(normalized.Tokens, normalized.RebuildEditions);
            if (!bases.TryGetValue(key, out var list))
            {
                bases[key] = list = [];
            }

            list.Add(new Candidate(entry.ReleaseId, key, year));
        }

        if (variants.Count == 0 || bases.Count == 0)
        {
            return new Dictionary<long, long>();
        }

        var consolidated = new Dictionary<long, long>(variants.Count);
        foreach (var variant in variants)
        {
            if (!bases.TryGetValue(variant.Key, out var candidates))
            {
                continue;
            }

            // Lowest id among the year-compatible candidates. A library can
            // hold two releases that normalise alike (a game and its GOTY
            // edition); either is proof the user owns the full game, so the
            // choice only has to be stable, not clever.
            long? chosen = null;
            foreach (var candidate in candidates)
            {
                if (!YearsAgree(variant.Year, candidate.Year))
                {
                    continue;
                }

                if (chosen is null || candidate.ReleaseId < chosen)
                {
                    chosen = candidate.ReleaseId;
                }
            }

            if (chosen is not null)
            {
                consolidated[variant.ReleaseId] = chosen.Value;
            }
        }

        return consolidated;
    }

    /// <summary>
    /// Gate one. Decides whether this entry is a variant handout and, if so,
    /// what key its base game would have. False for everything else.
    ///
    /// <para>The combination rule, and why it is asymmetric: a
    /// <see cref="DemoType"/> classification is accepted outright because Valve
    /// published it, while a <see cref="GameType"/> classification is allowed to
    /// veto ONLY the <c>demo</c> marker. Valve types demos <c>Demo</c>, so a
    /// <c>Game</c>-typed "… Demo" is evidence of a real game with an awkward
    /// name; Valve has no beta or playtest type at all, so the same <c>Game</c>
    /// beside "… Open Beta" is evidence of nothing and must not silently switch
    /// this feature off for every beta in the library.</para>
    /// </summary>
    private static bool TryReadBaseKey(NormalizedTitle title, string? appType, out string baseKey)
    {
        baseKey = string.Empty;

        var typedDemo = string.Equals(appType?.Trim(), DemoType, StringComparison.OrdinalIgnoreCase);
        var typedGame = string.Equals(appType?.Trim(), GameType, StringComparison.OrdinalIgnoreCase);

        // A type Valve gave that is neither Game nor Demo — Tool, Application,
        // Config, Music. Not a variant of anything the user owns, so it is left
        // visible rather than folded into a game it merely accompanies.
        if (!string.IsNullOrWhiteSpace(appType) && !typedDemo && !typedGame)
        {
            return false;
        }

        if (TryReadMarker(title, DemoMarkerPhrases, out var demoRemainder))
        {
            if (typedGame)
            {
                // Valve had the vocabulary to call this a Demo and called it a
                // Game instead. Believe the storefront over the title.
                return false;
            }

            baseKey = demoRemainder;
            return true;
        }

        if (TryReadMarker(title, PrereleaseMarkerPhrases, out var prereleaseRemainder))
        {
            // Reached whether the type is Game or unknown: Valve publishes no
            // beta/playtest type, so neither answer contradicts the title.
            baseKey = prereleaseRemainder;
            return true;
        }

        if (typedDemo)
        {
            // Typed Demo with no marker in the title — "FINAL FANTASY XIV Online
            // Free Trial" before that phrase was known, or a demo Steam simply
            // named after the game. The whole title is the key, so gate two
            // still has to find an owned, identically-named non-variant before
            // anything is hidden.
            baseKey = Key(title.Tokens, title.RebuildEditions);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Matches the longest marker phrase that ends the title, and returns the
    /// key its base game would have.
    ///
    /// <para>Longest-first so <c>beta test</c> beats bare <c>beta</c> and
    /// <c>public test realm</c> beats <c>test realm</c> — otherwise
    /// <c>New World Public Test Realm</c> would look for a base game called
    /// "New World Public".</para>
    /// </summary>
    private static bool TryReadMarker(NormalizedTitle title, string[][] phrases, out string baseKey)
    {
        baseKey = string.Empty;

        var tokens = title.Tokens;
        var longest = -1;

        foreach (var phrase in phrases)
        {
            // Count > phrase.Length: the marker alone IS a title ("Demo",
            // "Playtest"), not a handout of anything, so something must be left
            // in front of it.
            if (phrase.Length <= longest || tokens.Count <= phrase.Length)
            {
                continue;
            }

            var at = tokens.Count - phrase.Length;
            var hit = true;
            for (var i = 0; i < phrase.Length; i++)
            {
                if (!string.Equals(tokens[at + i], phrase[i], StringComparison.Ordinal))
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
            {
                longest = phrase.Length;
            }
        }

        if (longest < 0)
        {
            return false;
        }

        baseKey = Key(tokens.Take(tokens.Count - longest), title.RebuildEditions);
        return true;
    }

    /// <summary>
    /// The equality key: comparable core, then the rebuild-edition markers.
    ///
    /// <para>Rebuild markers are IN the key because they mark a different build
    /// with a different achievement set (§9 pitfall 5) — a demo of the original
    /// is not superseded by owning the remaster. Bundle markers (GOTY,
    /// Complete, Deluxe) are deliberately OUT: they are the same build plus
    /// content, so owning the GOTY edition is owning the game.</para>
    /// </summary>
    private static string Key(IEnumerable<string> coreTokens, IReadOnlyList<string> rebuildEditions)
        => string.Join(' ', coreTokens) + KeyDelimiter + string.Join(KeyDelimiter, rebuildEditions);

    /// <summary>
    /// Absent evidence never vetoes: a variant with no known year (the normal
    /// case — IGDB does not carry Steam demo appids, so nothing enriches them)
    /// binds on the title alone. Two KNOWN years more than a year apart are
    /// evidence of two different games, which is the Prey (2006) / Prey (2017)
    /// case §5.3 names.
    /// </summary>
    private static bool YearsAgree(int? variantYear, int? baseYear)
        => variantYear is null || baseYear is null
           || Math.Abs(variantYear.Value - baseYear.Value) <= YearTolerance;

    private readonly record struct Candidate(long ReleaseId, string Key, int? Year);
}
