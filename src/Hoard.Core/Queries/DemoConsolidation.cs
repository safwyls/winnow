using Hoard.Core.Matching;

namespace Hoard.Core.Queries;

/// <summary>
/// One owned release, as <see cref="DemoConsolidation"/> needs to see it: the
/// title to classify and the two facts that can veto a binding.
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
}

/// <summary>
/// Decides which owned demos are redundant because the user also owns the full
/// game — the read-time half of "one tile per game, not one tile per demo".
///
/// <para><b>Derived, never stored.</b> This is a pure function of the titles
/// currently in the library, run inside the derived-bucket query (§6.1) exactly
/// like every bucket. Nothing is written, nothing is deleted, no release is
/// re-parented: the demo keeps its ownership, its play records, its snapshots
/// and its history, and every one of them stays queryable. Remove the base game
/// from the library and the next read simply finds no base for the demo, so the
/// demo comes back. That reversibility is the whole reason this is a query and
/// not a column (§6.1, and the charter's "stored derived values rot").</para>
///
/// <para><b>Why not re-parent the demo Release onto the base Work.</b> The
/// four-layer model (§5.3) would carry it — a demo really is another Release of
/// the same Work — but writing that link is an auto-merge on a title heuristic,
/// which is the one thing §5.3 forbids outright and §9 ranks as the second most
/// likely way this project fails. §5.3 permits auto-merge only on a hard
/// external-id join, and IGDB publishes no such id for Steam demo appids (see
/// below). A stored re-parent would also be one-way: nothing would un-merge the
/// Work when the user later removed the base game. So the Work-level grouping
/// is computed on read instead of written down — the same relationship, minus
/// the destructive write.</para>
///
/// <para><b>Playtime is never merged.</b> A demo is its own Steam appid with
/// its own minutes. Consolidation suppresses a <i>tile</i>; it does not add,
/// blend or transfer a single minute, which would be §6.2's "two facts, not one
/// average" all over again. The suppressed demo's numbers stay exactly where
/// they were, on its own ownership.</para>
///
/// <para><b>Detection: title marker, and why IGDB cannot do this job.</b>
/// Verified against the live IGDB v4 API (August 2026):</para>
/// <list type="bullet">
///   <item>The <c>game_types</c> endpoint has fifteen values — Main Game, DLC,
///     Expansion, Bundle, Standalone Expansion, Mod, Episode, Season, Remake,
///     Remaster, Expanded Game, Port, Fork, Pack/Addon, Update. <b>None of them
///     is "Demo".</b> The demos IGDB does track (<c>Need for Speed: Most Wanted
///     Demo</c>, <c>Platinum Demo: Final Fantasy XV</c>, <c>Resident Evil 7
///     Teaser: Beginning Hour</c>) are filed as game_type 4, Standalone
///     Expansion — the same value as <c>Far Cry 3: Blood Dragon</c>. Suppressing
///     on that field would hide standalone expansions, which are whole games the
///     user owns.</item>
///   <item><c>parent_game</c> exists and is populated on those entries, but it
///     is only reachable once the release has an IGDB id, and Steam demo appids
///     do not have one: <c>external_games</c> filtered to the Steam source
///     returns nothing for 107110 (Bastion Demo), 73050 (Magicka Demo), 1458040
///     (Tales of Arise Demo) or 35020 (Batman: Arkham Asylum Demo). On the
///     author's real library not one of the demo works carries an
///     <c>igdb_id</c>, so an IGDB-first rule would fire on none of them.</item>
/// </list>
/// <para>So the marker is the title, and precision is bought elsewhere — with a
/// second gate rather than a longer word list.</para>
///
/// <para><b>The two gates.</b> A demo is suppressed only when both hold:</para>
/// <list type="number">
///   <item>the normalised title's <b>last token</b> is <c>demo</c>, and
///     something is left in front of it. Tokenising first is what protects real
///     titles: <c>Demonologist</c>, <c>Demon's Souls</c> and <c>Demolition
///     Derby</c> never produce a bare <c>demo</c> token, and a title that is
///     nothing but the word is not a demo of anything.</item>
///   <item>the remainder is an <b>exact</b> match — never a similarity score —
///     for another owned, non-demo release. Equality is over
///     <see cref="TitleNormalizer"/>'s comparable core plus its rebuild-edition
///     markers, which is what makes <c>Portal Demo</c> fail to bind to
///     <c>Portal 2</c> (cores <c>portal</c> vs <c>portal 2</c>) and
///     <c>Bastion Demo</c> fail to bind to <c>Bastion Remastered</c> (§9 pitfall
///     5: a rebuild is a different Release). Bundle markers are deliberately not
///     part of the key — <c>Batman: Arkham Asylum - Game of the Year Edition</c>
///     is the full game, so the demo folds into it.</item>
/// </list>
/// <para>Both gates must pass, which is why a mis-classified title is nearly
/// harmless: a real game whose name happens to end in "Demo" is only hidden if
/// the library <i>also</i> holds a differently-named release that normalises to
/// exactly its title minus that word. Precision over recall (§5.3): a demo left
/// visible is a cosmetic miss, a hidden game the user owns is a lie about their
/// library.</para>
/// </summary>
public static class DemoConsolidation
{
    /// <summary>The single normalised token that marks a demo.</summary>
    private const string Marker = "demo";

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

    /// <summary>
    /// True when the title carries the demo marker — i.e. it normalises to at
    /// least one token followed by <c>demo</c>. Says nothing about whether the
    /// base game is owned; that is the second gate.
    /// </summary>
    public static bool IsDemoTitle(string? title)
        => TryReadDemoKey(TitleNormalizer.Normalize(title), out _);

    /// <summary>
    /// Maps each redundant demo release to the owned release that supersedes it.
    /// A demo with no owned base is absent from the result and stays fully
    /// visible; so is every non-demo release.
    /// </summary>
    /// <param name="owned">
    /// Every release the user owns at least one copy of, with its title.
    /// Order is irrelevant to the outcome — the base of an ambiguous key is
    /// chosen by lowest release id, so repeated runs over the same library
    /// return the same map (the pass is a pure function, and therefore
    /// idempotent across syncs by construction).
    /// </param>
    public static IReadOnlyDictionary<long, long> Consolidate(
        IEnumerable<DemoConsolidationEntry> owned)
    {
        ArgumentNullException.ThrowIfNull(owned);

        // Two passes over one materialised list: every release has to be
        // classified before any demo can be matched, because the base of the
        // first demo may be the last row read.
        var bases = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        var demos = new List<Candidate>();

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

            if (TryReadDemoKey(normalized, out var baseKey))
            {
                demos.Add(new Candidate(entry.ReleaseId, baseKey, year));
                continue;
            }

            var key = Key(normalized.Tokens, normalized.RebuildEditions);
            if (!bases.TryGetValue(key, out var list))
            {
                bases[key] = list = [];
            }

            list.Add(new Candidate(entry.ReleaseId, key, year));
        }

        if (demos.Count == 0 || bases.Count == 0)
        {
            return new Dictionary<long, long>();
        }

        var consolidated = new Dictionary<long, long>(demos.Count);
        foreach (var demo in demos)
        {
            if (!bases.TryGetValue(demo.Key, out var candidates))
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
                if (!YearsAgree(demo.Year, candidate.Year))
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
                consolidated[demo.ReleaseId] = chosen.Value;
            }
        }

        return consolidated;
    }

    /// <summary>
    /// Reads the demo marker off a normalised title and returns the key its
    /// base game would have. False for everything else.
    /// </summary>
    private static bool TryReadDemoKey(NormalizedTitle title, out string baseKey)
    {
        baseKey = string.Empty;

        var tokens = title.Tokens;

        // Count > 1: "Demo" alone is a title, not a demo of anything. The
        // marker must also be LAST — Steam suffixes it ("Bastion Demo",
        // "Cronos: The New Dawn - Demo", "Sid Meier's Civilization V: Demo",
        // all of which normalise to the same trailing token). A leading
        // "Demo Disc" or "Platinum Demo: …" is left alone on purpose: those are
        // compilations and standalone releases, not a suffix on a base title.
        if (tokens.Count < 2 || !string.Equals(tokens[^1], Marker, StringComparison.Ordinal))
        {
            return false;
        }

        baseKey = Key(tokens.Take(tokens.Count - 1), title.RebuildEditions);
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
    /// Absent evidence never vetoes: a demo with no known year (the normal
    /// case — IGDB does not carry Steam demo appids, so nothing enriches them)
    /// binds on the title alone. Two KNOWN years more than a year apart are
    /// evidence of two different games, which is the Prey (2006) / Prey (2017)
    /// case §5.3 names.
    /// </summary>
    private static bool YearsAgree(int? demoYear, int? baseYear)
        => demoYear is null || baseYear is null
           || Math.Abs(demoYear.Value - baseYear.Value) <= YearTolerance;

    private readonly record struct Candidate(long ReleaseId, string Key, int? Year);
}
