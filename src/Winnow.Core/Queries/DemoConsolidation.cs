using Winnow.Core.Matching;

namespace Winnow.Core.Queries;

/// <summary>
/// One owned release for <see cref="DemoConsolidation"/>: title, Valve's type,
/// and the fields that can veto a binding. Only owned releases are passed in.
/// </summary>
public sealed record DemoConsolidationEntry
{
    /// <summary><c>releases.id</c>.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>The title to classify: release name, falling back to work name.</summary>
    public required string Title { get; init; }

    /// <summary>True for a machine-minted placeholder. Excluded from consolidation.</summary>
    public bool NameIsProvisional { get; init; }

    /// <summary><c>works.first_release_year</c>, or null when unenriched.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary>Valve's <c>common.type</c> (migration 0006), or null when unknown. Null falls back to the title gate.</summary>
    public string? SteamAppType { get; init; }
}

/// <summary>
/// Hides demos, betas and playtests whose full game is also owned. Derived at
/// read time (never stored), run inside the bucket query. Nothing is deleted or
/// re-parented; playtime is never merged.
///
/// <para>Two gates: (1) Is this a variant? Valve's <c>common.type = Demo</c>
/// wins outright; <c>Game</c> vetoes only the demo marker (Valve has no beta
/// type); unknown type falls back to title markers. (2) Does an owned non-variant
/// base with matching normalised core + rebuild editions exist? Exact key match,
/// never similarity.</para>
/// </summary>
public static class DemoConsolidation
{
    /// <summary>Separates core from edition markers in a key. Cannot occur in a normalised title.</summary>
    private const char KeyDelimiter = '';

    /// <summary>Max year difference for two releases to be considered the same game.</summary>
    private const int YearTolerance = 1;

    /// <summary>Valve's <c>common.type</c> for a demo. Compared case-insensitively.</summary>
    private const string DemoType = "demo";

    /// <summary>Valve's <c>common.type</c> for an ordinary game. Vetoes only the demo title marker.</summary>
    private const string GameType = "game";

    /// <summary>The <c>demo</c> marker, separate because it is the only one the Game type can veto.</summary>
    private static readonly string[][] DemoMarkerPhrases =
    [
        ["demo"],
    ];

    /// <summary>
    /// Trailing token runs marking a pre-release or limited handout. Matched longest-first,
    /// suffix-only, with at least one token remaining in front. Bare <c>test</c>, <c>trial</c>
    /// and <c>prologue</c> are deliberately absent (they name standalone releases too often).
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

    /// <summary>True when the title ends with a <c>demo</c> marker (gate one only, ignores ownership).</summary>
    public static bool IsDemoTitle(string? title)
        => TryReadMarker(TitleNormalizer.Normalize(title), DemoMarkerPhrases, out _);

    /// <summary>True when the title carries any variant marker (demo, beta, playtest, etc.). Superset of <see cref="IsDemoTitle"/>.</summary>
    public static bool IsVariantTitle(string? title)
    {
        var normalized = TitleNormalizer.Normalize(title);
        return TryReadMarker(normalized, DemoMarkerPhrases, out _)
               || TryReadMarker(normalized, PrereleaseMarkerPhrases, out _);
    }

    /// <summary>
    /// Which variant word the title carries, as one of
    /// <see cref="Identity.RelationLabels"/>, or null when it carries none.
    /// This is what lets a proposal about "Civilization V: Demo" say Demo
    /// rather than Expansion. Demo outranks the pre-release markers, matching
    /// gate one's own precedence; a playtest marker is distinguished from a
    /// beta marker because a card that says Beta about a playtest is saying
    /// something Valve did not.
    /// </summary>
    public static string? VariantLabel(string? title)
    {
        var normalized = TitleNormalizer.Normalize(title);
        if (TryReadMarker(normalized, DemoMarkerPhrases, out _))
        {
            return Identity.RelationLabels.Demo;
        }

        if (!TryReadMarker(normalized, PrereleaseMarkerPhrases, out _))
        {
            return null;
        }

        foreach (var token in normalized.Tokens)
        {
            if (string.Equals(token, "playtest", StringComparison.Ordinal))
            {
                return Identity.RelationLabels.Playtest;
            }
        }

        return Identity.RelationLabels.Beta;
    }

    /// <summary>Maps each redundant variant release to the owned base that supersedes it.</summary>
    /// <param name="owned">All owned releases. Order does not matter (ties broken by lowest release id).</param>
    public static IReadOnlyDictionary<long, long> Consolidate(
        IEnumerable<DemoConsolidationEntry> owned)
    {
        ArgumentNullException.ThrowIfNull(owned);

        // Two passes: classify all releases before matching variants.
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

            // ParsedYear from the title outranks the work's enriched year.
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

            // Lowest id among year-compatible candidates; choice only needs to be stable.
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
    /// Gate one: decides whether this entry is a variant and returns the base key.
    /// Demo type is accepted outright; Game type vetoes only the demo marker (Valve has no beta type).
    /// </summary>
    private static bool TryReadBaseKey(NormalizedTitle title, string? appType, out string baseKey)
    {
        baseKey = string.Empty;

        var typedDemo = string.Equals(appType?.Trim(), DemoType, StringComparison.OrdinalIgnoreCase);
        var typedGame = string.Equals(appType?.Trim(), GameType, StringComparison.OrdinalIgnoreCase);

        // Non-game types (Tool, Application, Config, Music) are not variants.
        if (!string.IsNullOrWhiteSpace(appType) && !typedDemo && !typedGame)
        {
            return false;
        }

        if (TryReadMarker(title, DemoMarkerPhrases, out var demoRemainder))
        {
            if (typedGame)
            {
                // Valve typed it Game, not Demo -- believe the storefront.
                return false;
            }

            baseKey = demoRemainder;
            return true;
        }

        if (TryReadMarker(title, PrereleaseMarkerPhrases, out var prereleaseRemainder))
        {
            // Valve has no beta/playtest type, so Game does not contradict the title.
            baseKey = prereleaseRemainder;
            return true;
        }

        if (typedDemo)
        {
            // Typed Demo with no title marker -- use the whole title as key.
            baseKey = Key(title.Tokens, title.RebuildEditions);
            return true;
        }

        return false;
    }

    /// <summary>Matches the longest marker phrase at the end of the title and returns the base key.</summary>
    private static bool TryReadMarker(NormalizedTitle title, string[][] phrases, out string baseKey)
    {
        baseKey = string.Empty;

        var tokens = title.Tokens;
        var longest = -1;

        foreach (var phrase in phrases)
        {
            // The marker alone is a title, not a handout -- something must remain in front.
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
    /// Equality key: core tokens + rebuild-edition markers (rebuild = different build,
    /// so demo of original is not superseded by remaster). Bundle markers excluded.
    /// </summary>
    private static string Key(IEnumerable<string> coreTokens, IReadOnlyList<string> rebuildEditions)
        => string.Join(' ', coreTokens) + KeyDelimiter + string.Join(KeyDelimiter, rebuildEditions);

    /// <summary>Null year never vetoes. Two known years more than <see cref="YearTolerance"/> apart indicate different games.</summary>
    private static bool YearsAgree(int? variantYear, int? baseYear)
        => variantYear is null || baseYear is null
           || Math.Abs(variantYear.Value - baseYear.Value) <= YearTolerance;

    private readonly record struct Candidate(long ReleaseId, string Key, int? Year);
}
